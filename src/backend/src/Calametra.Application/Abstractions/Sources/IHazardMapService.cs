using Calametra.Domain.Abstractions;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>Rendered map imagery returned by an upstream hazard map service.</summary>
public sealed record HazardMapImage(byte[] Content, string ContentType);

/// <summary>A request for a rendered hazard tile in Web Mercator.</summary>
/// <remarks>
/// Web Mercator (EPSG:3857) is used because that is what MapLibre requires for a
/// raster source. The PHIVOLCS WMS capabilities document advertises only EPSG:4326
/// and CRS:84, but the service was verified on 2026-09-04 to serve EPSG:3857
/// GetMap requests correctly. Trusting the capabilities document would have ruled
/// out the whole approach; trusting only the advertised operations would also have
/// been wrong in the other direction, since the same service advertises a Query
/// capability that is disabled. Probe, do not read.
/// </remarks>
public sealed record HazardTileRequest
{
    public required Guid HazardLayerId { get; init; }

    /// <summary>Bounding box in EPSG:3857 metres, as <c>minX,minY,maxX,maxY</c>.</summary>
    public required string BoundingBox3857 { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}

/// <summary>A request for official attributes at a clicked position.</summary>
public sealed record HazardIdentifyRequest
{
    public required Guid HazardLayerId { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>Search tolerance in screen pixels.</summary>
    public int TolerancePixels { get; init; } = 5;
}

/// <summary>
/// Official attributes of one hazard feature, verbatim from the publisher.
/// </summary>
/// <remarks>
/// Attributes are carried as a dictionary rather than a typed model on purpose.
/// They are the publisher's words — for a PHIVOLCS fault these include the fault
/// system name, the segment name, the year mapped and the mapping project — and
/// Calametra displays them as received rather than reinterpreting them. Presenting
/// them as our own typed fields would imply an editorial role the platform must not
/// take.
/// </remarks>
public sealed record HazardFeatureAttributes
{
    public required string LayerName { get; init; }

    public string? DisplayValue { get; init; }

    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
}

/// <summary>
/// Port for an upstream hazard map service that Calametra proxies rather than copies.
/// </summary>
/// <remarks>
/// This port exists because of a licensing position, not a technical one. PHIVOLCS
/// publishes rendered imagery and per-feature inspection but disables bulk vector
/// query; public reachability is not permission to copy and re-serve. Proxying
/// display traffic keeps V1 shippable while the formal dataset request is pending,
/// and centralising it here means attribution, caching and rate limiting are applied
/// in exactly one place.
/// </remarks>
public interface IHazardMapService
{
    Task<Result<HazardMapImage>> GetTileAsync(
        HazardTileRequest request,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<HazardFeatureAttributes>>> IdentifyAsync(
        HazardIdentifyRequest request,
        CancellationToken cancellationToken);
}
