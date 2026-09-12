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
    /// Which source supplied <see cref="Centroid"/>, when it is not the source that named the
    /// place.
    /// </summary>
    /// <remarks>
    /// Null means the coordinate came with the place from <see cref="DataSourceId"/>. Set when a
    /// better-located point replaces it, so the interface can state whose coordinate a distance was
    /// measured from — the platform's rule that no value is shown without its source applies to a
    /// position as much as to a magnitude.
    /// </remarks>
    public Guid? CoordinateDataSourceId { get; private set; }

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

    /// <summary>
    /// Replaces the representative point with a better-located one, naming the source that
    /// supplied it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The coordinate carries its own source because it need not come from the source that named
    /// the place. The gazetteer behind the directory publishes administrative points that are
    /// frequently a table-rounded value some kilometres from the town: measured across this
    /// archive, 1,090 of 1,647 cities and municipalities carry a coordinate rounded to the nearest
    /// arc-minute, five are rounded to a quarter of a degree, and the mean distance to the mapped
    /// town centre is 5.2 km. That is the same reasoning as <see cref="SetPopulation"/> — a figure
    /// from elsewhere is attributed to elsewhere, rather than silently inheriting the row's source.
    /// </para>
    /// <para>
    /// Every consequence of the point moves with it. The epicentre naming, the radius search and
    /// the place context all measure from this coordinate, so a point that is 14 km from the town
    /// puts an earthquake 14 km wrong in every one of them.
    /// </para>
    /// </remarks>
    public void SetCoordinate(Point centroid, Guid dataSourceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(centroid);

        Centroid = centroid;
        Latitude = centroid.Y;
        Longitude = centroid.X;
        CoordinateDataSourceId = dataSourceId;
        Touch(now);
    }
}
