using Calametra.Domain.Administrative;
using NetTopologySuite.Geometries;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// One administrative boundary as an upstream served it, with the tags that identify it.
/// </summary>
/// <remarks>
/// Deliberately carries the tags rather than a resolved unit. Deciding which canonical unit a relation
/// belongs to is a matching decision, and ADR-005 keeps matching in the application layer where the
/// confirmed crosswalk lives — an adapter that resolved identity itself would be able to attach a
/// polygon to a unit without any of the crosswalk's evidence rules applying.
/// </remarks>
public sealed record BoundaryFeature
{
    public required long OsmRelationId { get; init; }

    /// <summary>The <c>ref</c> tag as observed, whatever edition of the PSGC code it holds.</summary>
    public string? RefTag { get; init; }

    public string? Name { get; init; }

    public int AdminLevel { get; init; }

    /// <summary>Assembled outer and inner rings. Valid, or repaired and flagged.</summary>
    public required MultiPolygon Geometry { get; init; }

    public double AreaSquareKm { get; init; }

    public bool WasRepaired { get; init; }

    public string? RepairNote { get; init; }
}

/// <summary>
/// What one boundary read produced, including what it failed to produce.
/// </summary>
/// <param name="ExtractVersion">
/// The upstream's own statement of the data vintage — for Overpass, <c>timestamp_osm_base</c>. Recorded
/// because ADR-005 D7 requires a containment figure to be able to name the boundary edition behind it,
/// and "when we ran the query" is a weaker claim than "what the data was as of".
/// </param>
/// <param name="Unassembled">
/// Relations that were returned but whose rings could not be closed into a polygon. Reported rather than
/// dropped: an incomplete boundary in OSM is a fact about the map, and a unit silently missing from a
/// coverage count is the failure this platform exists to refuse.
/// </param>
public sealed record BoundarySnapshot
{
    public required IReadOnlyList<BoundaryFeature> Features { get; init; }

    public required DateTimeOffset ExtractedAt { get; init; }

    public string? ExtractVersion { get; init; }

    public required IReadOnlyList<UnassembledRelation> Unassembled { get; init; }

    /// <summary>Chunks the adapter asked for and did not get, so partial coverage is never silent.</summary>
    public required IReadOnlyList<string> FailedChunks { get; init; }

    /// <summary>
    /// How this geometry reached the platform. Declared for a file, never inferred.
    /// </summary>
    public required BoundaryProvenance Provenance { get; init; }

    /// <summary>What a person would cite, e.g. "Geofabrik Philippines extract, 2026-09-13".</summary>
    public required string Label { get; init; }

    /// <summary>The path or endpoint read.</summary>
    public required string AccessRoute { get; init; }

    public string? OriginalFileName { get; init; }

    /// <summary>SHA-256 of the original bytes, taken before parsing.</summary>
    public string? FileSha256 { get; init; }

    public long? FileSizeBytes { get; init; }

    /// <summary>The date the upstream states the extract represents, not the date it was downloaded.</summary>
    public DateOnly? Vintage { get; init; }

    public string? AcquisitionNote { get; init; }
}

public sealed record UnassembledRelation(long OsmRelationId, string? RefTag, string? Name, string Reason);

/// <summary>
/// Reads administrative boundary geometry for the Philippines.
/// </summary>
public interface ILguBoundarySource
{
    /// <summary>
    /// Fetches boundaries at one OSM administrative level.
    /// </summary>
    /// <param name="adminLevel">
    /// 6 for cities and municipalities in the Philippines. Not 8 — see
    /// <c>OpenStreetMapOptions.CityMunicipalityAdminLevel</c>.
    /// </param>
    /// <param name="chunks">
    /// Bounding boxes to fetch in sequence. A national boundary query is too large for one Overpass
    /// request, and the alternative to chunking is a single call that times out and yields nothing.
    /// </param>
    Task<BoundarySnapshot> ReadAsync(
        int adminLevel,
        IReadOnlyList<BoundaryChunk> chunks,
        CancellationToken cancellationToken);
}

/// <param name="Label">Names the chunk in logs and in the failure list, so a gap can be re-fetched.</param>
public sealed record BoundaryChunk(
    string Label,
    double South,
    double West,
    double North,
    double East);
