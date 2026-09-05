using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Seismology;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.Usgs;

/// <summary>
/// Reads the USGS ComCat earthquake catalogue via the FDSN event web service.
/// </summary>
/// <remarks>
/// The adapter's real job is not fetching JSON — it is translating USGS reporting
/// conventions into Calametra's explicit quality types, so that no handler downstream
/// has to know anything about how the USGS reports data.
/// </remarks>
internal sealed class UsgsEarthquakeCatalogSource(
    HttpClient httpClient,
    IOptions<UsgsOptions> options,
    ILogger<UsgsEarthquakeCatalogSource> logger)
    : IEarthquakeCatalogSource
{
    /// <summary>
    /// Depths the NEIC assigns by convention when depth cannot be resolved from the
    /// available phase data.
    /// </summary>
    /// <remarks>
    /// Verified against the live catalogue for the Philippine bounding box.
    /// <para>
    /// Modern era (2015-01-01 to 2026-09-01, M4.0+, 8,715 events): 2,529 events
    /// report exactly 10.00 km and 608 exactly 35.00 km, while the next most common
    /// exact value has 11 events. That cliff is what identifies them as placeholders.
    /// </para>
    /// <para>
    /// Historical era (1900-01-01 to 1990-01-01, M4.0+, 4,328 events) uses different
    /// conventions and needs them all: 1,054 events at exactly 33.00 km (24.4%),
    /// 228 at 15.00 km, 163 at 35.00 km and 57 at 10.00 km — roughly 35% of the
    /// historical record. 33 km was the long-standing NEIC "normal depth" assumption
    /// for a crustal event, and 15 km is the fixed depth used by the historical
    /// re-analysis that produced most pre-1960 magnitudes. Every one of the 26
    /// M7.5+ events before 1950 is recorded at exactly 15 km.
    /// </para>
    /// <para>
    /// This test is the only way to detect them. The <c>depthError</c> field is
    /// populated for these events, so error-based filtering does not work.
    /// </para>
    /// <para>
    /// Values below the threshold of clear convention are deliberately excluded.
    /// 25 km (69 events) and 60 km (41 events) also recur, but not at a rate that
    /// separates them from genuine measurements, and wrongly flagging a real depth
    /// is worse than missing a placeholder.
    /// </para>
    /// </remarks>
    private static readonly double[] NeicAssignedDepthsKm = [10d, 15d, 33d, 35d];

    private static readonly Error FetchFailed = new(
        ErrorType.Failure,
        "usgs.fetch_failed",
        "The USGS earthquake catalogue could not be reached.");

    public string SourceSlug => UsgsOptions.Slug;

    public async Task<Result<IReadOnlyList<CatalogEarthquake>>> FetchAsync(
        EarthquakeCatalogQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requestUri = BuildRequestUri(query);

        try
        {
            var payload = await httpClient.GetFromJsonAsync<UsgsFeatureCollection>(
                requestUri,
                cancellationToken);

            if (payload?.Features is null)
            {
                UsgsLog.EmptyPayload(logger, requestUri);

                return Result<IReadOnlyList<CatalogEarthquake>>.Success([]);
            }

            var events = payload.Features
                .Select(TryMap)
                .OfType<CatalogEarthquake>()
                .ToList();

            UsgsLog.Fetched(logger, events.Count, payload.Features.Count);

            return Result<IReadOnlyList<CatalogEarthquake>>.Success(events);
        }
        catch (HttpRequestException exception)
        {
            UsgsLog.RequestFailed(logger, exception, requestUri);

            return Result<IReadOnlyList<CatalogEarthquake>>.Failure(FetchFailed);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            UsgsLog.RequestTimedOut(logger, exception, requestUri);

            return Result<IReadOnlyList<CatalogEarthquake>>.Failure(FetchFailed);
        }
        catch (JsonException exception)
        {
            // A payload we cannot parse is an upstream data problem, not a bug to
            // crash on. Returning a failed Result lets a backfill log the window and
            // carry on rather than losing the whole run.
            UsgsLog.MalformedPayload(logger, exception, requestUri);

            return Result<IReadOnlyList<CatalogEarthquake>>.Failure(FetchFailed);
        }
    }

    private string BuildRequestUri(EarthquakeCatalogQuery query)
    {
        var parameters = new List<string>
        {
            "format=geojson",
            $"starttime={Iso(query.From)}",
            $"endtime={Iso(query.To)}",
            $"minlatitude={Number(query.MinLatitude)}",
            $"maxlatitude={Number(query.MaxLatitude)}",
            $"minlongitude={Number(query.MinLongitude)}",
            $"maxlongitude={Number(query.MaxLongitude)}",
            "orderby=time-asc",
        };

        if (query.MinMagnitude is { } minMagnitude)
        {
            parameters.Add($"minmagnitude={Number(minMagnitude)}");
        }

        return $"{options.Value.QueryPath}?{string.Join('&', parameters)}";

        static string Iso(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private CatalogEarthquake? TryMap(UsgsFeature feature)
    {
        // GeoJSON coordinate order is [longitude, latitude, depth]. Longitude and
        // latitude are required; depth is not, and is null for some historical events.
        if (feature.Geometry?.Coordinates is not { Count: >= 2 } coordinates
            || coordinates[0] is not { } longitude
            || coordinates[1] is not { } latitude
            || feature.Properties?.Time is not { } epochMilliseconds
            || string.IsNullOrWhiteSpace(feature.Id))
        {
            UsgsLog.SkippedFeature(logger, feature.Id ?? "(no id)");

            return null;
        }

        var properties = feature.Properties;
        var reportedDepth = coordinates.Count >= 3 ? coordinates[2] : null;

        return new CatalogEarthquake
        {
            ExternalId = feature.Id,
            OccurredAt = DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds),
            Longitude = longitude,
            Latitude = latitude,
            Depth = InterpretDepth(reportedDepth),
            Magnitude = properties.Magnitude is { } magnitude
                ? new MagnitudeReading(magnitude, MagnitudeTypeExtensions.Parse(properties.MagnitudeType))
                : null,
            PlaceDescription = properties.Place,
            SourceUrl = properties.Url,
        };
    }

    /// <summary>
    /// Classifies a reported depth as measured, agency-assigned, or absent.
    /// </summary>
    /// <remarks>
    /// Exact equality is the correct test for the assigned values, not a tolerance. A
    /// genuinely measured depth is reported with decimals (46.378 km); the assigned
    /// values are reported as whole numbers precisely because they were not measured.
    /// A tolerance would misclassify real shallow events near 10 km.
    /// <para>
    /// A null depth becomes <see cref="DepthQuality.Unknown"/> rather than being
    /// treated as zero or skipped. It is a meaningfully different statement from an
    /// assigned default: the agency did not resolve the depth at all.
    /// </para>
    /// </remarks>
    private static DepthReading InterpretDepth(double? depthKm)
    {
        if (depthKm is not { } depth)
        {
            return DepthReading.Unknown;
        }

        return Array.Exists(NeicAssignedDepthsKm, assigned => depth.Equals(assigned))
            ? DepthReading.OperatorAssigned(depth)
            : DepthReading.Constrained(depth);
    }

    // ---- Wire format. Internal to this adapter; never leaves the assembly. ----

    private sealed record UsgsFeatureCollection
    {
        [JsonPropertyName("features")]
        public IReadOnlyList<UsgsFeature>? Features { get; init; }
    }

    private sealed record UsgsFeature
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("properties")]
        public UsgsProperties? Properties { get; init; }

        [JsonPropertyName("geometry")]
        public UsgsGeometry? Geometry { get; init; }
    }

    private sealed record UsgsProperties
    {
        [JsonPropertyName("mag")]
        public double? Magnitude { get; init; }

        [JsonPropertyName("magType")]
        public string? MagnitudeType { get; init; }

        [JsonPropertyName("place")]
        public string? Place { get; init; }

        /// <summary>Origin time in Unix epoch milliseconds.</summary>
        [JsonPropertyName("time")]
        public long? Time { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }

    private sealed record UsgsGeometry
    {
        /// <summary>
        /// GeoJSON order: longitude, latitude, depth in kilometres.
        /// </summary>
        /// <remarks>
        /// Elements are nullable because the USGS reports depth as <c>null</c> for some
        /// historical events — the location was determined but the depth was never
        /// resolved at all, which is different from being assigned a default. Declaring
        /// this as <c>double</c> throws
        /// <c>JsonException: Cannot get the value of a token type 'Null' as a number</c>
        /// on the first such event, which for the Philippine box is in 1900.
        /// </remarks>
        [JsonPropertyName("coordinates")]
        public IReadOnlyList<double?>? Coordinates { get; init; }
    }
}

internal static partial class UsgsLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "USGS returned {FeatureCount} features, mapped {MappedCount}")]
    public static partial void Fetched(ILogger logger, int mappedCount, int featureCount);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "USGS returned no payload for {RequestUri}")]
    public static partial void EmptyPayload(ILogger logger, string requestUri);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "USGS request to {RequestUri} failed")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string requestUri);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error, Message = "USGS request to {RequestUri} timed out")]
    public static partial void RequestTimedOut(ILogger logger, Exception exception, string requestUri);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Skipped USGS feature {ExternalId}: no usable position or origin time")]
    public static partial void SkippedFeature(ILogger logger, string externalId);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Error,
        Message = "USGS payload from {RequestUri} could not be parsed")]
    public static partial void MalformedPayload(ILogger logger, Exception exception, string requestUri);
}
