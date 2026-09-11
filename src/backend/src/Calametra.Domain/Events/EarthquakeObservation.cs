using Calametra.Domain.Abstractions;
using Calametra.Domain.Seismology;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Events;

/// <summary>
/// What one agency reports about one earthquake.
/// </summary>
/// <remarks>
/// A single earthquake produces several of these. The 10 February 2017 Surigao
/// event has a PHIVOLCS observation (Ms 6.7, 10 km) and a USGS observation
/// (Mww 6.5, 15 km, external id <c>us20008ixa</c>). Both are correct readings from
/// different networks using different methods.
/// <para>
/// Calametra therefore never reduces an event to one magnitude. The UI shows every
/// observation side by side with its agency and scale, and explains why they differ.
/// </para>
/// <para>
/// <b>On the name.</b> This type is deliberately <i>not</i> called
/// <c>EventObservation</c>. Every member below is specific to seismology —
/// <see cref="Epicenter"/>, <see cref="MagnitudeValue"/>, <see cref="MagnitudeScale"/>,
/// <see cref="DepthKilometres"/>, <see cref="DepthQuality"/> — and none of them means
/// anything for a landslide polygon or a cyclone track point. A generic name would have
/// invited the next hazard to reuse this table and carry five columns it can never
/// populate. Other hazards get their own observation type; what they share is
/// <see cref="HazardEvent"/>, which holds only what is genuinely common.
/// </para>
/// <para>
/// <b>On the shape of the magnitude and depth members.</b> Each is stored as mapped
/// primitives (<see cref="MagnitudeValue"/> / <see cref="MagnitudeScale"/>,
/// <see cref="DepthKilometres"/> / <see cref="DepthQuality"/>) and also exposed as a
/// rich value object (<see cref="Magnitude"/>, <see cref="Depth"/>). The primitives
/// exist so query handlers can filter and sort in SQL — a computed value object
/// cannot be translated by EF Core, and pushing magnitude filtering into memory
/// would defeat the point of a database. The value objects exist so domain logic and
/// callers work with a magnitude that cannot be separated from its scale. Writes go
/// through the value object, so the two cannot drift.
/// </para>
/// </remarks>
public sealed class EarthquakeObservation : Entity
{
    private EarthquakeObservation()
    {
    }

    private EarthquakeObservation(
        Guid id,
        Guid eventId,
        Guid dataSourceId,
        string externalEventId,
        DateTimeOffset observedAt,
        Point epicenter,
        DateTimeOffset retrievedAt)
        : base(id)
    {
        HazardEventId = eventId;
        DataSourceId = dataSourceId;
        ExternalEventId = externalEventId;
        ObservedAt = observedAt;
        RetrievedAt = retrievedAt;
        ApplyPosition(epicenter);
    }

    public Guid HazardEventId { get; private set; }

    /// <summary>Which agency produced this reading. Never empty: unattributed data is not stored.</summary>
    public Guid DataSourceId { get; private set; }

    /// <summary>The reporting source's own identifier, e.g. USGS <c>us20008ixa</c>.</summary>
    public string ExternalEventId { get; private set; } = string.Empty;

    /// <summary>Origin time as this source determined it. Sources may differ by seconds.</summary>
    public DateTimeOffset ObservedAt { get; private set; }

    /// <summary>Epicentre as this source located it.</summary>
    public Point Epicenter { get; private set; } = default!;

    // ---- Mapped primitives: queryable in SQL --------------------------------

    /// <summary>
    /// Epicentre latitude in decimal degrees.
    /// </summary>
    /// <remarks>
    /// Stored as its own column rather than read from <see cref="Epicenter"/> in a
    /// projection. PostGIS coordinate accessors (<c>ST_Y</c>, <c>ST_X</c>) are
    /// defined for <c>geometry</c> only, and Calametra stores positions as
    /// <c>geography</c> so that distance predicates return metres. Projecting
    /// <c>Epicenter.Y</c> therefore fails at the database with
    /// <c>42883: function st_y(geography) does not exist</c>.
    /// <para>
    /// Spatial predicates — <c>ST_DWithin</c>, <c>ST_Distance</c> — are unaffected
    /// and still run against the geography column and its GiST index. These columns
    /// exist purely so a result row can carry coordinates without a cast.
    /// </para>
    /// </remarks>
    public double Latitude { get; private set; }

    /// <summary>Epicentre longitude in decimal degrees.</summary>
    public double Longitude { get; private set; }

    /// <summary>Magnitude value as reported. Null when the source reported none.</summary>
    public double? MagnitudeValue { get; private set; }

    /// <summary>Scale the magnitude was measured on. Never inferred.</summary>
    public MagnitudeType MagnitudeScale { get; private set; } = MagnitudeType.Unknown;

    /// <summary>Hypocentre depth in kilometres as reported.</summary>
    public double? DepthKilometres { get; private set; }

    /// <summary>
    /// Whether the reported depth was measured or fixed to an agency default.
    /// Filterable so the cross-section can exclude assigned depths in SQL.
    /// </summary>
    public DepthQuality DepthQuality { get; private set; } = DepthQuality.Unknown;

    // ---- Rich value objects: for domain logic and callers -------------------

    /// <summary>Magnitude together with its scale, or null when none was reported.</summary>
    public MagnitudeReading? Magnitude => MagnitudeValue is { } value
        ? new MagnitudeReading(value, MagnitudeScale)
        : null;

    /// <summary>Depth together with how much it can be trusted.</summary>
    public DepthReading Depth => new(DepthKilometres, DepthQuality);

    /// <summary>
    /// Increments when the source revises the solution. Agencies routinely refine
    /// origin, depth and magnitude for days after an event.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>When Calametra fetched this reading.</summary>
    public DateTimeOffset RetrievedAt { get; private set; }

    /// <summary>Deep link to the source's own page for this event.</summary>
    public string? SourceUrl { get; private set; }

    internal static Result<EarthquakeObservation> Create(
        Guid eventId,
        Guid dataSourceId,
        string externalEventId,
        DateTimeOffset observedAt,
        Point epicenter,
        DepthReading depth,
        MagnitudeReading? magnitude,
        DateTimeOffset retrievedAt,
        string? sourceUrl)
    {
        if (dataSourceId == Guid.Empty)
        {
            return Result<EarthquakeObservation>.Failure(EventErrors.ObservationSourceRequired);
        }

        if (string.IsNullOrWhiteSpace(externalEventId))
        {
            return Result<EarthquakeObservation>.Failure(EventErrors.ExternalIdRequired);
        }

        var observation = new EarthquakeObservation(
            Guid.CreateVersion7(),
            eventId,
            dataSourceId,
            externalEventId.Trim(),
            observedAt,
            epicenter,
            retrievedAt)
        {
            SourceUrl = sourceUrl,
        };

        observation.ApplyReadings(depth, magnitude);

        return Result<EarthquakeObservation>.Success(observation);
    }

    /// <summary>Applies a revised solution from the reporting agency.</summary>
    public void Revise(
        DateTimeOffset observedAt,
        Point epicenter,
        DepthReading depth,
        MagnitudeReading? magnitude,
        DateTimeOffset retrievedAt)
    {
        ObservedAt = observedAt;
        RetrievedAt = retrievedAt;
        ApplyPosition(epicenter);
        ApplyReadings(depth, magnitude);
        Revision++;
    }

    /// <summary>
    /// The single place position is written, so the geography column and the
    /// coordinate columns cannot disagree.
    /// </summary>
    private void ApplyPosition(Point epicenter)
    {
        ArgumentNullException.ThrowIfNull(epicenter);

        Epicenter = epicenter;

        // NTS stores longitude in X and latitude in Y.
        Latitude = epicenter.Y;
        Longitude = epicenter.X;
    }

    /// <summary>
    /// The single place the primitives are written, so they cannot diverge from the
    /// value objects that read them.
    /// </summary>
    private void ApplyReadings(DepthReading depth, MagnitudeReading? magnitude)
    {
        DepthKilometres = depth.Kilometres;
        DepthQuality = depth.Quality;

        MagnitudeValue = magnitude?.Value;
        MagnitudeScale = magnitude?.Type ?? MagnitudeType.Unknown;
    }
}
