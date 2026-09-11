using System.Globalization;
using System.Text.Json;
using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Hazards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.Phivolcs;

/// <summary>
/// Serves PHIVOLCS hazard layers by proxying the publisher's own WMS service.
/// </summary>
/// <remarks>
/// <para>
/// Why proxy instead of storing geometry. The PHIVOLCS public ArcGIS services expose
/// twelve hazard layers, but the layer-level <c>query</c> operation returns HTTP 400
/// "The requested capability is not supported" for every variant, and
/// <c>generatekml</c> is likewise disabled. What works is <c>export</c>, WMS
/// <c>GetMap</c>, and <c>identify</c>. That access pattern is a deliberate publishing
/// decision: rendering is offered, bulk data is not. Public reachability is not a
/// licence to copy and re-serve, so V1 renders the publisher's own imagery and stores
/// nothing.
/// </para>
/// <para>
/// Why proxy through the API rather than letting the browser call PHIVOLCS directly.
/// Three reasons: the upstream service sends no CORS headers, so a direct browser
/// request fails; attribution and caching need to be applied in one place; and routing
/// through the API means upstream request volume is ours to shape.
/// </para>
/// <para>
/// <b>Why tiles are cached in memory.</b> This is how upstream volume is actually
/// controlled, and the lesson was learned the hard way. Rate limiting the client was
/// tried first and was the wrong lever: MapLibre requests one tile per viewport tile,
/// so a single map view is twenty to forty requests and a few pans is hundreds. A limit
/// sized for user actions returns HTTP 429 to our own map while doing nothing about the
/// upstream load, because the requests that do get through still each reach PHIVOLCS.
/// </para>
/// <para>
/// Caching inverts that. A tile is fetched from PHIVOLCS once and then served from
/// memory to every subsequent request from any user, so upstream sees one request per
/// distinct tile per cache lifetime regardless of how many people are panning. The
/// client is free to render as fluidly as it likes, and the agency is genuinely
/// protected rather than protected-by-proxy-of-degrading-us.
/// </para>
/// <para>
/// On the coordinate system. The capabilities document advertises only EPSG:4326 and
/// CRS:84, yet the service was verified to serve EPSG:3857 <c>GetMap</c> requests
/// correctly. MapLibre needs Web Mercator, so 3857 is what is requested. This is the
/// mirror image of the disabled <c>query</c> operation, which is advertised but absent —
/// the metadata for this service is unreliable in both directions, so behaviour was
/// established by probing.
/// </para>
/// </remarks>
internal sealed class PhivolcsHazardMapService(
    HttpClient httpClient,
    IApplicationDbContext context,
    IMemoryCache tileCache,
    IOptions<PhivolcsOptions> options,
    ILogger<PhivolcsHazardMapService> logger)
    : IHazardMapService
{
    private static readonly Error UpstreamUnavailable = new(
        ErrorType.Failure,
        "hazard_map.upstream_unavailable",
        "The official hazard map service could not be reached.");

    private static readonly Error NotRemotelyDelivered = new(
        ErrorType.Validation,
        "hazard_map.not_remotely_delivered",
        "This layer is stored locally and is not served through the map proxy.");

    /// <summary>
    /// Attributes worth showing a reader, in the order they should appear.
    /// </summary>
    /// <remarks>
    /// A curated list rather than everything the service returns, because an ArcGIS
    /// <c>identify</c> response carries the layer's full column set — including its
    /// editing audit trail. Passing that through verbatim showed users
    /// <c>OBJECTID</c>, <c>GLOBALID</c>, <c>SHAPE</c>, <c>ST_LENGTH(SHAPE)</c>,
    /// <c>CREATOR: KLPAPIONA</c> and <c>EDITOR: SDE</c> — database housekeeping that
    /// says nothing about the fault and buries the fields that do.
    /// <para>
    /// This does not conflict with presenting the publisher's terminology unaltered.
    /// The values and the meanings stay exactly as PHIVOLCS records them; the labels
    /// only expand abbreviations, so <c>fname</c> becomes "Fault system" and never
    /// something reinterpreted. What is dropped is internal plumbing that was never a
    /// statement about the fault.
    /// </para>
    /// </remarks>
    private static readonly (string Key, string Label)[] PresentableAttributes =
    [
        ("fname", "Fault system"),
        ("segname", "Segment"),
        ("Fault Category", "Category"),
        ("Trace type", "Trace type"),
        ("Mechanism", "Mechanism"),
        ("datemapped", "Year mapped"),
        ("Mapping Scale", "Mapping scale"),
        ("Project", "Mapping project"),
        ("Mappers", "Mapped by"),
        ("Other Information", "Notes"),
    ];

    private static readonly Error FeatureInfoUnsupported = new(
        ErrorType.Validation,
        "hazard_map.feature_info_unsupported",
        "This layer's source does not support feature inspection.");

    public async Task<Result<HazardMapImage>> GetTileAsync(
        HazardTileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var layer = await FindLayerAsync(request.HazardLayerId, cancellationToken);

        if (layer is null)
        {
            return Result<HazardMapImage>.Failure(HazardLayerErrors.NotFound);
        }

        if (layer.DeliveryMode != LayerDeliveryMode.RemoteWms)
        {
            return Result<HazardMapImage>.Failure(NotRemotelyDelivered);
        }

        var cacheKey = TileCacheKey(request);

        if (tileCache.TryGetValue(cacheKey, out HazardMapImage? cached) && cached is not null)
        {
            return Result<HazardMapImage>.Success(cached);
        }

        var fetched = await FetchTileAsync(layer, request, cancellationToken);

        if (fetched.IsSuccess)
        {
            // Hazard layers and fault traces change on a timescale of years, so a long
            // cache lifetime is both safe and the polite way to consume someone else's
            // map service. Size is set in bytes against a bounded cache so a wide
            // browsing session cannot grow the process without limit.
            tileCache.Set(
                cacheKey,
                fetched.Value,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.Value.TileCacheDuration,
                    Size = fetched.Value.Content.Length,
                });
        }

        return fetched;
    }

    /// <summary>
    /// Cache key for a tile request.
    /// </summary>
    /// <remarks>
    /// The bounding box is part of the key verbatim. MapLibre requests tiles on a fixed
    /// grid, so the same view produces byte-identical box strings and the cache hits
    /// reliably rather than near-missing on floating-point formatting.
    /// </remarks>
    private static string TileCacheKey(HazardTileRequest request) =>
        $"tile:{request.HazardLayerId}:{request.BoundingBox3857}:{request.Width}x{request.Height}";

    private async Task<Result<HazardMapImage>> FetchTileAsync(
        HazardLayerDefinition layer,
        HazardTileRequest request,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildGetMapUri(layer, request);

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                PhivolcsLog.UpstreamStatus(logger, (int)response.StatusCode, requestUri);

                return Result<HazardMapImage>.Failure(UpstreamUnavailable);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";

            // A WMS error is returned as XML with a 200 status, so the content type has
            // to be checked rather than the status code alone.
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                PhivolcsLog.UnexpectedContentType(logger, contentType, requestUri);

                return Result<HazardMapImage>.Failure(UpstreamUnavailable);
            }

            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            return Result<HazardMapImage>.Success(new HazardMapImage(content, contentType));
        }
        catch (HttpRequestException exception)
        {
            PhivolcsLog.RequestFailed(logger, exception, requestUri);

            return Result<HazardMapImage>.Failure(UpstreamUnavailable);
        }
    }

    public async Task<Result<IReadOnlyList<HazardFeatureAttributes>>> IdentifyAsync(
        HazardIdentifyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var layer = await FindLayerAsync(request.HazardLayerId, cancellationToken);

        if (layer is null)
        {
            return Result<IReadOnlyList<HazardFeatureAttributes>>.Failure(HazardLayerErrors.NotFound);
        }

        if (!layer.SupportsFeatureInfo || string.IsNullOrWhiteSpace(layer.FeatureInfoEndpoint))
        {
            return Result<IReadOnlyList<HazardFeatureAttributes>>.Failure(FeatureInfoUnsupported);
        }

        var requestUri = BuildIdentifyUri(layer, request);

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                PhivolcsLog.UpstreamStatus(logger, (int)response.StatusCode, requestUri);

                return Result<IReadOnlyList<HazardFeatureAttributes>>.Failure(UpstreamUnavailable);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var features = ParseIdentifyResults(stream, layer.DisplayName);

            return Result<IReadOnlyList<HazardFeatureAttributes>>.Success(features);
        }
        catch (HttpRequestException exception)
        {
            PhivolcsLog.RequestFailed(logger, exception, requestUri);

            return Result<IReadOnlyList<HazardFeatureAttributes>>.Failure(UpstreamUnavailable);
        }
        catch (JsonException exception)
        {
            PhivolcsLog.MalformedFeatureInfo(logger, exception, requestUri);

            return Result<IReadOnlyList<HazardFeatureAttributes>>.Failure(UpstreamUnavailable);
        }
    }

    private Task<HazardLayerDefinition?> FindLayerAsync(Guid layerId, CancellationToken cancellationToken) =>
        context.HazardLayers.AsNoTracking().FirstOrDefaultAsync(layer => layer.Id == layerId, cancellationToken);

    private static string BuildGetMapUri(HazardLayerDefinition layer, HazardTileRequest request) =>
        $"{layer.WmsEndpoint}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap"
        + $"&LAYERS={Uri.EscapeDataString(layer.WmsLayerName!)}&STYLES="
        + "&CRS=EPSG:3857"
        + $"&BBOX={Uri.EscapeDataString(request.BoundingBox3857)}"
        + $"&WIDTH={request.Width}&HEIGHT={request.Height}"
        + "&FORMAT=image/png32&TRANSPARENT=TRUE";

    /// <summary>
    /// Builds an ArcGIS REST <c>identify</c> request for a clicked position.
    /// </summary>
    /// <remarks>
    /// Uses ArcGIS <c>identify</c> rather than WMS <c>GetFeatureInfo</c> because on
    /// this service GetFeatureInfo returns an empty collection for every request —
    /// verified against windows from 0.04° up to a full degree — while
    /// <c>identify</c> returns the fault system, segment name, mapping year and
    /// project. The WMS capabilities document advertises
    /// <c>application/geojson</c> for GetFeatureInfo regardless, which is another
    /// instance of this service's metadata not describing its behaviour.
    /// <para>
    /// <c>tolerance</c> is in screen pixels and is evaluated against the synthetic
    /// <c>mapExtent</c>/<c>imageDisplay</c> pair below. Six pixels against a 0.6°
    /// window is roughly 400 m on the ground, which is a forgiving but not absurd
    /// target for clicking a line.
    /// </remarks>
    private static string BuildIdentifyUri(HazardLayerDefinition layer, HazardIdentifyRequest request)
    {
        const int viewportPixels = 600;
        const double halfSpanDegrees = 0.3d;

        var geometry = Uri.EscapeDataString(
            $"{{\"x\":{Number(request.Longitude)},\"y\":{Number(request.Latitude)},"
            + "\"spatialReference\":{\"wkid\":4326}}");

        var mapExtent = string.Join(
            ',',
            Number(request.Longitude - halfSpanDegrees),
            Number(request.Latitude - halfSpanDegrees),
            Number(request.Longitude + halfSpanDegrees),
            Number(request.Latitude + halfSpanDegrees));

        return $"{layer.FeatureInfoEndpoint}?geometry={geometry}"
            + "&geometryType=esriGeometryPoint&sr=4326"
            + $"&layers={Uri.EscapeDataString("all:" + layer.WmsLayerName)}"
            + $"&tolerance={Math.Clamp(request.TolerancePixels, 1, 24)}"
            + $"&mapExtent={Uri.EscapeDataString(mapExtent)}"
            + $"&imageDisplay={viewportPixels},{viewportPixels},96"
            + "&returnGeometry=false&f=json";

        static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads an ArcGIS <c>identify</c> response into attribute dictionaries.
    /// </summary>
    /// <remarks>
    /// Attributes are kept as the publisher's own key/value pairs. For a PHIVOLCS
    /// fault these include the fault system, the segment name, the year mapped and
    /// the mapping project. Calametra shows them verbatim rather than remapping them
    /// into fields of its own, because renaming an authority's terminology would
    /// misrepresent it.
    /// </remarks>
    private static List<HazardFeatureAttributes> ParseIdentifyResults(Stream stream, string layerName)
    {
        using var document = JsonDocument.Parse(stream);

        var features = new List<HazardFeatureAttributes>();

        if (!document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return features;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("attributes", out var properties)
                || properties.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in properties.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.ToString(),
                };

                // ArcGIS emits the literal string "Null" for absent attributes.
                if (!string.IsNullOrWhiteSpace(value)
                    && !value.Equals("Null", StringComparison.OrdinalIgnoreCase))
                {
                    raw[property.Name] = value;
                }
            }

            if (raw.Count == 0)
            {
                continue;
            }

            // Curated and ordered. An insertion-ordered dictionary preserves the
            // sequence declared in PresentableAttributes, so the reader gets fault
            // system before segment before mapping detail rather than whatever order
            // the service happened to serialise.
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (key, label) in PresentableAttributes)
            {
                if (raw.TryGetValue(key, out var value))
                {
                    attributes[label] = value;
                }
            }

            if (attributes.Count == 0)
            {
                continue;
            }

            var displayValue = result.TryGetProperty("value", out var valueElement)
                && valueElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(valueElement.GetString())
                    ? valueElement.GetString()
                    : FirstAvailable(raw, "segname", "Segment", "fname", "name");

            features.Add(new HazardFeatureAttributes
            {
                LayerName = layerName,
                DisplayValue = displayValue,
                Attributes = attributes,
            });
        }

        return features;

        static string? FirstAvailable(Dictionary<string, string> attributes, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (attributes.TryGetValue(key, out var value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}

internal static partial class PhivolcsLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "Hazard map service returned HTTP {StatusCode} for {RequestUri}")]
    public static partial void UpstreamStatus(ILogger logger, int statusCode, string requestUri);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Hazard map service returned unexpected content type {ContentType} for {RequestUri}")]
    public static partial void UnexpectedContentType(ILogger logger, string contentType, string requestUri);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Hazard map request to {RequestUri} failed")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string requestUri);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Error,
        Message = "Hazard map service returned malformed feature info for {RequestUri}")]
    public static partial void MalformedFeatureInfo(ILogger logger, Exception exception, string requestUri);
}
