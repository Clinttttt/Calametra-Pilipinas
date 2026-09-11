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

    public static readonly Error SourceRequired =
        new(ErrorType.Validation, "place.source_required", "A place requires the gazetteer it came from.");
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
        Guid dataSourceId,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Name = name;
        Kind = kind;
        Centroid = centroid;
        DataSourceId = dataSourceId;

        // Derived here so the two can never disagree with the geometry, which is the failure mode
        // a duplicated coordinate invites.
        Latitude = centroid.Y;
        Longitude = centroid.X;
    }

    public string Name { get; private set; } = string.Empty;

    public PlaceKind Kind { get; private set; }

    /// <summary>
    /// The gazetteer this place came from.
    /// </summary>
    /// <remarks>
    /// Required, for the same reason every reading carries its agency. A place name and a
    /// coordinate are somebody's published claim about where a boundary lies and what the unit
    /// is called, and the platform has already had to record that two gazetteers disagree about
    /// the code for the same province. Kept separate from
    /// <see cref="PopulationDataSourceId"/> because the point and the population figure can
    /// legitimately come from different publishers.
    /// </remarks>
    public Guid DataSourceId { get; private set; }

    /// <summary>Philippine Standard Geographic Code, where known.</summary>
    public string? PsgcCode { get; private set; }

    /// <summary>Parent administrative unit, forming the region → province → municipality chain.</summary>
    public Guid? ParentPlaceId { get; private set; }

    /// <summary>Representative point, used for map fly-to and radius searches.</summary>
    public Point Centroid { get; private set; } = default!;

    /// <summary>
    /// Latitude of <see cref="Centroid"/>, materialised as a column.
    /// </summary>
    /// <remarks>
    /// Duplicated deliberately, matching <c>EarthquakeObservation</c> and <c>CycloneTrackPoint</c>,
    /// and for two reasons that both bite in practice. The column is <c>geography</c>, and PostGIS
    /// defines <c>ST_Y</c> and <c>ST_X</c> on <c>geometry</c> only — so projecting
    /// <c>Centroid.Y</c> in a query compiles and then fails at the database. And naming an
    /// epicentre after the nearest of 1,647 municipalities means reading the whole gazetteer per
    /// request: as geometry that is 1,647 point objects to parse, which measured at roughly 55 ms;
    /// as two doubles it is a flat projection.
    /// <para>
    /// Set from the geometry in the constructor, so the two cannot drift.
    /// </para>
    /// </remarks>
    public double Latitude { get; private set; }

    /// <summary>Longitude of <see cref="Centroid"/>. See <see cref="Latitude"/>.</summary>
    public double Longitude { get; private set; }

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
        Guid dataSourceId,
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

        if (dataSourceId == Guid.Empty)
        {
            return Result<Place>.Failure(PlaceErrors.SourceRequired);
        }

        return Result<Place>.Success(
            new Place(Guid.CreateVersion7(), name.Trim(), kind, centroid, dataSourceId, now));
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
