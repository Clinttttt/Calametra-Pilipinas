using Calametra.Domain.Abstractions;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>Rendered map imagery returned by an upstream hazard map service.</summary>
public sealed record HazardMapImage(byte[] Content, string ContentType);

/// <summary>A request for a rendered hazard tile in Web Mercator.</summary>
/// <remarks>
/// <para>
/// Web Mercator (EPSG:3857) is used because that is what MapLibre requires for a
/// raster source. The PHIVOLCS WMS capabilities document advertises only EPSG:4326
/// and CRS:84, but the service was verified on 2026-09-04 to serve EPSG:3857
/// GetMap requests correctly. Trusting the capabilities document would have ruled
/// out the whole approach; trusting only the advertised operations would also have
/// been wrong in the other direction, since the same service advertises a Query
/// capability that is disabled. Probe, do not read.
/// </para>
/// <para>
/// <b>Two addressing modes, because two publishers behave differently.</b> A bounding box asks
/// the service to render, which is the only option PHIVOLCS offers. Where a publisher exposes a
/// pre-rendered cache — DOST-MGB does, at 24 zoom levels on the standard Web Mercator grid —
/// the tile is addressed by <see cref="Zoom"/>, <see cref="Column"/> and <see cref="Row"/>
/// instead. That is not a micro-optimisation: measured on 2026-09-11, MGB renders a 256 px
/// susceptibility tile in 18.8-19.4 seconds and serves the same tile from its cache in 60-120
/// milliseconds. Rendering would have made the layer unusable for panning and would have put a
/// 19-second render on the agency's server for every tile a reader crossed.
/// </para>
/// </remarks>
public sealed record HazardTileRequest
{
    public required Guid HazardLayerId { get; init; }

    /// <summary>Bounding box in EPSG:3857 metres, as <c>minX,minY,maxX,maxY</c>.</summary>
    /// <remarks>Ignored when a cached tile address is supplied.</remarks>
    public string? BoundingBox3857 { get; init; }

    public int Width { get; init; } = 256;

    public int Height { get; init; } = 256;

    /// <summary>Zoom level of a cached tile, or null to render from a bounding box.</summary>
    public int? Zoom { get; init; }

    /// <summary>Column (x) of a cached tile.</summary>
    public int? Column { get; init; }

    /// <summary>Row (y) of a cached tile.</summary>
    public int? Row { get; init; }

    /// <summary>True when this request addresses a pre-rendered tile rather than a render.</summary>
    public bool IsCachedTileRequest => Zoom is not null && Column is not null && Row is not null;
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
