using Calametra.Domain.Administrative;
using NetTopologySuite.Geometries;

namespace Calametra.Application.Abstractions.Sources;

/// <param name="CanonicalCode">The ten-digit PSGC code, normalised from the publisher's own format.</param>
/// <param name="PublishedCode">The code exactly as published, kept as the evidence for the normalisation.</param>
public sealed record CanonicalBoundaryFeature
{
    public required string CanonicalCode { get; init; }

    public required string PublishedCode { get; init; }

    public string? Name { get; init; }

    public required MultiPolygon Geometry { get; init; }

    public double AreaSquareKm { get; init; }

    public bool WasRepaired { get; init; }

    public string? RepairNote { get; init; }
}

/// <summary>
/// One dated read of a register-keyed, land-only administrative boundary set.
/// </summary>
public sealed record CanonicalBoundarySnapshot
{
    public required IReadOnlyList<CanonicalBoundaryFeature> Features { get; init; }

    /// <summary>Features the reader could not use, with the reason each.</summary>
    public required IReadOnlyList<string> Rejected { get; init; }

    public required DateTimeOffset ExtractedAt { get; init; }

    public required BoundaryProvenance Provenance { get; init; }

    public required string Label { get; init; }

    public required string AccessRoute { get; init; }

    public required string OriginalFileName { get; init; }

    public required string FileSha256 { get; init; }

    public long FileSizeBytes { get; init; }

    public DateOnly? Vintage { get; init; }

    public string? AcquisitionNote { get; init; }
}

/// <summary>
/// Reads the canonical on-land administrative geometry, keyed by the PSGC code the publisher carries.
/// </summary>
/// <remarks>
/// A separate port from <see cref="ILguBoundarySource"/>, which reads OSM. ADR-005 D2a forbids treating the
/// two as one spatial concept — OSM outlines extend to municipal waters and these stop at the coast — and
/// two ports make that a structural fact rather than a convention someone has to remember.
/// </remarks>
public interface ICanonicalBoundarySource
{
    /// <summary>Slug of the registered <c>DataSource</c> this reader belongs to.</summary>
    string SourceSlug { get; }

    Task<CanonicalBoundarySnapshot> ReadAsync(CancellationToken cancellationToken);
}
