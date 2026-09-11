using Calametra.Domain.Abstractions;
using Calametra.Domain.Seismology;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// A normalised earthquake record as reported by an upstream catalogue.
/// </summary>
/// <remarks>
/// Owned by this project, not by the provider. Adapters translate their own wire
/// format into this shape, which is why depth and magnitude arrive already wrapped
/// in their quality and scale types — the adapter is where source-specific
/// conventions (such as NEIC default depths) are recognised and recorded.
/// </remarks>
public sealed record CatalogEarthquake
{
    /// <summary>The source's own identifier, e.g. USGS <c>us20008ixa</c>.</summary>
    public required string ExternalId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required DepthReading Depth { get; init; }

    public MagnitudeReading? Magnitude { get; init; }

    /// <summary>The source's own place description, e.g. "11 km N of Mabua, Philippines".</summary>
    public string? PlaceDescription { get; init; }

    /// <summary>Deep link to the source's page for this event.</summary>
    public string? SourceUrl { get; init; }
}

/// <summary>A bounded request for catalogue records.</summary>
public sealed record EarthquakeCatalogQuery
{
    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    public required double MinLatitude { get; init; }

    public required double MaxLatitude { get; init; }

    public required double MinLongitude { get; init; }

    public required double MaxLongitude { get; init; }

    /// <summary>Optional magnitude floor. Omitted means "everything the source has".</summary>
    public double? MinMagnitude { get; init; }
}

/// <summary>
/// Port for an upstream earthquake catalogue.
/// </summary>
/// <remarks>
/// Declared here, by the consumer, in the consumer's vocabulary. Implemented in
/// Calametra.Infrastructure by one adapter per catalogue. Modelled as a port rather
/// than a concrete client because the platform is expected to gain a second
/// catalogue: USGS provides global coverage but holds no Philippine events below M3.5,
/// so a PHIVOLCS local-catalogue adapter is a planned addition and must slot in
/// without changing any handler.
/// </remarks>
public interface IEarthquakeCatalogSource
{
    /// <summary>Slug of the <c>DataSource</c> this adapter reads from, e.g. <c>usgs-comcat</c>.</summary>
    string SourceSlug { get; }

    Task<Result<IReadOnlyList<CatalogEarthquake>>> FetchAsync(
        EarthquakeCatalogQuery query,
        CancellationToken cancellationToken);
}
