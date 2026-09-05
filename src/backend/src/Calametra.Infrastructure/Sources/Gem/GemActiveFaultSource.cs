using System.Text.Json;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace Calametra.Infrastructure.Sources.Gem;

/// <summary>
/// Reads mapped active faults from the GEM Global Active Faults Database.
/// </summary>
/// <remarks>
/// <para>
/// Used because it is openly licensed (CC BY-SA 4.0) and therefore storable,
/// which the PHIVOLCS service is not. Verified against the live dataset on
/// 2026-09-04: 13,696 features worldwide, 116 in the <c>philippines</c>
/// catalogue, 19 intersecting the CARAGA bounding box — including the Surigao,
/// Offshore Surigao, Lianga, Agusan Marsh, Caraga River, Central Mindanao and
/// Esperanza faults.
/// </para>
/// <para>
/// <b>Resolution caveat, which must be surfaced to users.</b> GEM is a regional
/// compilation: 19 principal named faults across CARAGA, against the PHIVOLCS
/// service returning over 1,000 trace segments for the same box. GEM is
/// sufficient to answer "which major fault system is near this event" and is not
/// a substitute for detailed local mapping. That limitation is recorded in the
/// data source's coverage notes rather than left for a reader to discover.
/// </para>
/// <para>
/// <b>Licence caveat.</b> CC BY-SA 4.0 is share-alike. Attribution is mandatory,
/// and any derived fault dataset Calametra publishes inherits the same licence.
/// This differs materially from the PHIVOLCS terms and is why the two are kept as
/// separate <c>DataSource</c> rows rather than merged into one fault layer.
/// </para>
/// </remarks>
internal sealed class GemActiveFaultSource(
    HttpClient httpClient,
    IOptions<GemOptions> options,
    ILogger<GemActiveFaultSource> logger)
    : IActiveFaultSource
{
    /// <summary>Attribute keys promoted to their own columns; the rest are kept as JSON.</summary>
    private const string CatalogIdKey = "catalog_id";
    private const string NameKey = "name";
    private const string SlipTypeKey = "slip_type";

    private static readonly Error FetchFailed = new(
        ErrorType.Failure,
        "gem.fetch_failed",
        "The GEM active fault dataset could not be retrieved.");

    public string SourceSlug => GemOptions.Slug;

    public async Task<Result<IReadOnlyList<CatalogFault>>> FetchAsync(
        ActiveFaultQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            using var response = await httpClient.GetAsync(
                options.Value.DatasetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                GemLog.UnexpectedStatus(logger, (int)response.StatusCode);

                return Result<IReadOnlyList<CatalogFault>>.Failure(FetchFailed);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // Streamed rather than buffered into a string: the document is ~10 MB
            // and materialising it twice is avoidable waste in a worker process.
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var faults = ExtractFaults(document, query);

            GemLog.Imported(logger, faults.Count);

            return Result<IReadOnlyList<CatalogFault>>.Success(faults);
        }
        catch (HttpRequestException exception)
        {
            GemLog.RequestFailed(logger, exception);

            return Result<IReadOnlyList<CatalogFault>>.Failure(FetchFailed);
        }
        catch (JsonException exception)
        {
            GemLog.MalformedPayload(logger, exception);

            return Result<IReadOnlyList<CatalogFault>>.Failure(FetchFailed);
        }
    }

    /// <summary>
    /// Filters the worldwide collection to features touching the requested box.
    /// </summary>
    /// <remarks>
    /// A fault is kept when any vertex falls inside the box. Deliberately
    /// inclusive: a fault that merely passes through the region is exactly the
    /// kind of feature that matters seismically, and clipping traces at the
    /// boundary would draw faults that appear to stop at an administrative line.
    /// </remarks>
    private static List<CatalogFault> ExtractFaults(JsonDocument document, ActiveFaultQuery query)
    {
        var results = new List<CatalogFault>();

        if (!document.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry)
                || !geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var vertices = ReadVertices(coordinates);

            if (vertices.Count < 2 || !Intersects(vertices, query))
            {
                continue;
            }

            var attributes = ReadAttributes(feature);

            if (!attributes.TryGetValue(CatalogIdKey, out var externalId))
            {
                continue;
            }

            attributes.TryGetValue(NameKey, out var name);
            attributes.TryGetValue(SlipTypeKey, out var slipType);

            results.Add(new CatalogFault
            {
                ExternalId = externalId,
                Name = name,
                SlipType = slipType,
                Trace = Wgs84.LineString(vertices),
                Attributes = attributes,
            });
        }

        return results;
    }

    private static List<Point> ReadVertices(JsonElement coordinates)
    {
        var vertices = new List<Point>();

        foreach (var pair in coordinates.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
            {
                continue;
            }

            // GeoJSON order is [longitude, latitude]; Wgs84.Point takes latitude first.
            var longitude = pair[0].GetDouble();
            var latitude = pair[1].GetDouble();

            if (Math.Abs(latitude) > 90d || Math.Abs(longitude) > 180d)
            {
                continue;
            }

            vertices.Add(Wgs84.Point(latitude, longitude));
        }

        return vertices;
    }

    private static bool Intersects(List<Point> vertices, ActiveFaultQuery query) =>
        vertices.Exists(vertex =>
            vertex.Y >= query.MinLatitude
            && vertex.Y <= query.MaxLatitude
            && vertex.X >= query.MinLongitude
            && vertex.X <= query.MaxLongitude);

    private static Dictionary<string, string> ReadAttributes(JsonElement feature)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!feature.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return attributes;
        }

        foreach (var property in properties.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => property.Value.ToString(),
            };

            // GEM writes the literal string "None" for absent values.
            if (!string.IsNullOrWhiteSpace(value)
                && !value.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                attributes[property.Name] = value;
            }
        }

        return attributes;
    }
}

internal static partial class GemLog
{
    [LoggerMessage(
        EventId = 8000,
        Level = LogLevel.Information,
        Message = "GEM dataset yielded {FaultCount} fault traces within the study area")]
    public static partial void Imported(ILogger logger, int faultCount);

    [LoggerMessage(
        EventId = 8001,
        Level = LogLevel.Warning,
        Message = "GEM dataset request returned HTTP {StatusCode}")]
    public static partial void UnexpectedStatus(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Error, Message = "GEM dataset request failed")]
    public static partial void RequestFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Error, Message = "GEM dataset payload was malformed")]
    public static partial void MalformedPayload(ILogger logger, Exception exception);
}
