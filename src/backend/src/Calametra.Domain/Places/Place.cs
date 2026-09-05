using Calametra.Domain.Abstractions;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Places;

/// <summary>Philippine administrative levels, coarsest first.</summary>
public enum PlaceKind
{
    Unknown = 0,
    Region = 1,
    Province = 2,
    City = 3,
    Municipality = 4,
    Barangay = 5,
}

public static class PlaceErrors
{
    public static readonly Error NotFound =
        new(ErrorType.NotFound, "place.not_found", "The place was not found.");

    public static readonly Error NameRequired =
        new(ErrorType.Validation, "place.name_required", "A place requires a name.");

    public static readonly Error UnknownKind =
        new(ErrorType.Validation, "place.unknown_kind", "A place requires a known administrative level.");
}

/// <summary>
/// A named administrative location, used as the anchor for "what happened here?".
/// </summary>
/// <remarks>
/// Population is stored as a precomputed figure rather than derived on demand.
/// WorldPop publishes raster GeoTIFFs, and running zonal statistics against a
/// raster per user request would be slow and would drag raster handling into the
/// query path. Instead the raster is reduced once, offline, to a population figure
/// per place, after which exposure questions become an ordinary spatial join.
/// The figure carries its own source and vintage so it can be attributed.
/// </remarks>
public sealed class Place : AuditableEntity
{
    private Place()
    {
    }

    private Place(
        Guid id,
        string name,
        PlaceKind kind,
        Point centroid,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Name = name;
        Kind = kind;
        Centroid = centroid;
    }

    public string Name { get; private set; } = string.Empty;

    public PlaceKind Kind { get; private set; }

    /// <summary>Philippine Standard Geographic Code, where known.</summary>
    public string? PsgcCode { get; private set; }

    /// <summary>Parent administrative unit, forming the region → province → municipality chain.</summary>
    public Guid? ParentPlaceId { get; private set; }

    /// <summary>Representative point, used for map fly-to and radius searches.</summary>
    public Point Centroid { get; private set; } = default!;

    /// <summary>
    /// Administrative boundary, where boundary data is available. Null for places
    /// known only as a point.
    /// </summary>
    public MultiPolygon? Boundary { get; private set; }

    /// <summary>Precomputed population estimate. Exposure information, never a damage prediction.</summary>
    public long? PopulationEstimate { get; private set; }

    /// <summary>Which source the population figure came from.</summary>
    public Guid? PopulationDataSourceId { get; private set; }

    /// <summary>Vintage of the population figure.</summary>
    public DateTimeOffset? PopulationAsOf { get; private set; }

    public static Result<Place> Create(
        string name,
        PlaceKind kind,
        Point centroid,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Place>.Failure(PlaceErrors.NameRequired);
        }

        if (kind == PlaceKind.Unknown)
        {
            return Result<Place>.Failure(PlaceErrors.UnknownKind);
        }

        return Result<Place>.Success(new Place(Guid.CreateVersion7(), name.Trim(), kind, centroid, now));
    }

    public Place WithHierarchy(string? psgcCode, Guid? parentPlaceId)
    {
        PsgcCode = psgcCode;
        ParentPlaceId = parentPlaceId;
        return this;
    }

    public Place WithBoundary(MultiPolygon? boundary)
    {
        Boundary = boundary;
        return this;
    }

    public void SetPopulation(long estimate, Guid dataSourceId, DateTimeOffset asOf, DateTimeOffset now)
    {
        PopulationEstimate = estimate;
        PopulationDataSourceId = dataSourceId;
        PopulationAsOf = asOf;
        Touch(now);
    }
}
