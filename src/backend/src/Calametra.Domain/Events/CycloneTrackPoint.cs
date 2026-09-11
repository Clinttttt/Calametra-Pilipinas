using Calametra.Domain.Abstractions;
using Calametra.Domain.Meteorology;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Events;

/// <summary>
/// One agency's fix on a tropical cyclone at one moment.
/// </summary>
/// <remarks>
/// <para>
/// A cyclone is not a point in time the way an earthquake is; it is a path. Each fix records
/// where the centre was, how strong it was and who said so, and a storm accumulates hundreds
/// of them at three- or six-hourly intervals.
/// </para>
/// <para>
/// <b>On the name.</b> Called <c>CycloneTrackPoint</c> rather than <c>EventTrackPoint</c> for
/// the same reason <see cref="EarthquakeObservation"/> is not <c>EventObservation</c>: every
/// member here is meteorological. Wind, central pressure, landfall and distance to land mean
/// nothing for a landslide footprint or an eruption. A generic name would invite the next
/// moving-hazard type to share this table and inherit columns it cannot populate.
/// </para>
/// <para>
/// <b>Wind carries its averaging period.</b> The value is a <see cref="WindReading"/>, not a
/// bare number, because agencies average sustained wind over different intervals — JTWC over
/// one minute, JMA and Hong Kong over ten, CMA over two. Yamaneko was simultaneously 30, 35,
/// 34 and 25 knots to four agencies. A single "wind speed" column would have quietly asserted
/// that those are the same measurement.
/// </para>
/// <para>
/// Pressure needs no such qualifier: minimum central pressure in millibars is the same
/// quantity however it is estimated, which is why it is stored as a plain value. That
/// asymmetry is deliberate and is the point of qualifying wind rather than qualifying
/// everything by reflex.
/// </para>
/// </remarks>
public sealed class CycloneTrackPoint : Entity
{
    private CycloneTrackPoint()
    {
    }

    private CycloneTrackPoint(
        Guid id,
        Guid eventId,
        Guid dataSourceId,
        DateTimeOffset capturedAt,
        Point position)
        : base(id)
    {
        HazardEventId = eventId;
        DataSourceId = dataSourceId;
        CapturedAt = capturedAt;
        Position = position;
    }

    public Guid HazardEventId { get; private set; }

    /// <summary>Which agency produced this fix. Never empty: unattributed data is not stored.</summary>
    public Guid DataSourceId { get; private set; }

    /// <summary>
    /// The upstream archive's identifier for the storm this fix belongs to, e.g. IBTrACS
    /// <c>2013306N07162</c>.
    /// </summary>
    /// <remarks>
    /// Held here rather than on <see cref="HazardEvent"/> because that aggregate deliberately
    /// carries no external identity — provenance lives entirely on the observations, so the
    /// shared type stays free of any one source's identifier scheme.
    /// <para>
    /// Unlike <c>EarthquakeObservation.ExternalEventId</c>, which differs per agency because
    /// each network issues its own event id, this value is the same across every fix of a storm
    /// and every agency reporting it. It identifies the storm as the *archive* numbers it, which
    /// is what makes ingestion idempotent: a storm already present can be recognised without
    /// re-reading its track.
    /// </para>
    /// </remarks>
    public string ExternalStormId { get; private set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Centre position as this agency located it.</summary>
    public Point Position { get; private set; } = default!;

    // ---- Mapped primitives: queryable in SQL --------------------------------

    /// <summary>Centre latitude in decimal degrees.</summary>
    /// <remarks>
    /// Stored alongside the geography column for the same reason as on
    /// <see cref="EarthquakeObservation"/>: PostGIS coordinate accessors are defined for
    /// <c>geometry</c> only, so a projection over a <c>geography</c> column cannot read them.
    /// </remarks>
    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>Sustained wind in knots as reported, or null when this agency gave none.</summary>
    public double? WindSpeedKnots { get; private set; }

    /// <summary>The interval the wind was averaged over. Never inferred.</summary>
    public WindAveragingPeriod WindAveragingPeriod { get; private set; } = WindAveragingPeriod.Unknown;

    /// <summary>Minimum central pressure in millibars as reported.</summary>
    public int? MinimumPressureMillibars { get; private set; }

    /// <summary>Storm nature or intensity classification, verbatim from the source.</summary>
    public string? Classification { get; private set; }

    /// <summary>Distance to the nearest land in kilometres, where the source provides it.</summary>
    public double? DistanceToLandKm { get; private set; }

    /// <summary>Whether this fix is a landfall as flagged by the source.</summary>
    public bool IsLandfall { get; private set; }

    // ---- Wind field extent --------------------------------------------------

    /// <summary>
    /// Radius of maximum wind in nautical miles — the eyewall.
    /// </summary>
    /// <remarks>
    /// The distance from the centre to the strongest winds, which is the physical size of the eye
    /// wall rather than of the storm. Reported by JTWC for roughly 83% of fixes. Stored because it
    /// is the only figure that lets a cyclone be drawn at its real scale instead of at an
    /// arbitrary marker size.
    /// </remarks>
    public double? RadiusOfMaximumWindNm { get; private set; }

    /// <summary>
    /// Radius of the outermost closed isobar in nautical miles — the storm's outer edge.
    /// </summary>
    /// <remarks>
    /// The circulation's full extent, well beyond the damaging winds. Distinct from the gale
    /// radius and not a substitute for it.
    /// </remarks>
    public double? RadiusOutermostIsobarNm { get; private set; }

    /// <summary>Which geometry this agency used to describe the gale field.</summary>
    public WindFieldGeometry WindFieldGeometry { get; private set; } = WindFieldGeometry.None;

    /// <summary>
    /// The wind speed the gale radii are measured at. 34 kt for JTWC, 30 kt for JMA and KMA.
    /// </summary>
    /// <remarks>
    /// Stored alongside the radii for the same reason the averaging period is stored alongside the
    /// wind speed: a radius without its threshold looks comparable across agencies and is not.
    /// </remarks>
    public int GaleThresholdKnots { get; private set; }

    public double? GaleRadiusNorthEastNm { get; private set; }

    public double? GaleRadiusSouthEastNm { get; private set; }

    public double? GaleRadiusSouthWestNm { get; private set; }

    public double? GaleRadiusNorthWestNm { get; private set; }

    public double? GaleRadiusLongAxisNm { get; private set; }

    public double? GaleRadiusShortAxisNm { get; private set; }

    /// <summary>Bearing of the ellipse's long axis, degrees clockwise from north.</summary>
    public int? GaleRadiusBearingDegrees { get; private set; }

    // ---- Inner wind bands ---------------------------------------------------
    //
    // The 50 and 64 knot radii, which together with the 34 knot gale radius form the nested
    // bands used in tropical-cyclone wind-field products. Quadrant form only: JTWC is the sole
    // agency in this basin that publishes them, and it publishes quadrants.
    //
    // Held as their own columns rather than a child table because they describe the same fix and
    // are always read with it. A join would be an extra round trip for four numbers.

    /// <summary>Storm-force (50 kt) radius per quadrant, in nautical miles.</summary>
    public double? StormRadiusNorthEastNm { get; private set; }

    public double? StormRadiusSouthEastNm { get; private set; }

    public double? StormRadiusSouthWestNm { get; private set; }

    public double? StormRadiusNorthWestNm { get; private set; }

    /// <summary>Hurricane-force (64 kt) radius per quadrant, in nautical miles.</summary>
    public double? HurricaneRadiusNorthEastNm { get; private set; }

    public double? HurricaneRadiusSouthEastNm { get; private set; }

    public double? HurricaneRadiusSouthWestNm { get; private set; }

    public double? HurricaneRadiusNorthWestNm { get; private set; }

    // ---- Rich value object: for domain logic and callers --------------------

    /// <summary>Wind together with its averaging period, or null when none was reported.</summary>
    public WindReading? Wind => WindSpeedKnots is { } speed
        ? new WindReading(speed, WindAveragingPeriod)
        : null;

    /// <summary>
    /// The gale field as this agency described it, reassembled from its mapped columns.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, matching how <see cref="Wind"/> and
    /// <see cref="EarthquakeObservation.Magnitude"/> are handled: the primitives exist so the
    /// database can filter and project them, and the value object exists so callers cannot
    /// separate a radius from the threshold and geometry that give it meaning.
    /// </remarks>
    public WindField GaleField => WindFieldGeometry switch
    {
        Meteorology.WindFieldGeometry.Quadrants => WindField.FromQuadrants(
            GaleThresholdKnots,
            GaleRadiusNorthEastNm,
            GaleRadiusSouthEastNm,
            GaleRadiusSouthWestNm,
            GaleRadiusNorthWestNm),
        Meteorology.WindFieldGeometry.Ellipse => WindField.FromEllipse(
            GaleThresholdKnots,
            GaleRadiusLongAxisNm,
            GaleRadiusShortAxisNm,
            GaleRadiusBearingDegrees),
        _ => WindField.None,
    };

    internal static CycloneTrackPoint Create(
        Guid eventId,
        Guid dataSourceId,
        string externalStormId,
        DateTimeOffset capturedAt,
        Point position,
        WindReading? wind,
        int? minimumPressureMillibars,
        string? classification,
        double? distanceToLandKm,
        bool isLandfall,
        double? radiusOfMaximumWindNm = null,
        double? radiusOutermostIsobarNm = null,
        WindField galeField = default,
        WindField stormField = default,
        WindField hurricaneField = default)
    {
        ArgumentNullException.ThrowIfNull(position);

        var point = new CycloneTrackPoint(
            Guid.CreateVersion7(),
            eventId,
            dataSourceId,
            capturedAt,
            position)
        {
            ExternalStormId = externalStormId.Trim(),
            MinimumPressureMillibars = minimumPressureMillibars,
            Classification = classification,
            DistanceToLandKm = distanceToLandKm,
            IsLandfall = isLandfall,
            RadiusOfMaximumWindNm = radiusOfMaximumWindNm,
            RadiusOutermostIsobarNm = radiusOutermostIsobarNm,
        };

        point.ApplyPosition(position);
        point.ApplyWind(wind);
        point.ApplyGaleField(galeField);
        point.ApplyInnerBands(stormField, hurricaneField);

        return point;
    }

    /// <summary>
    /// The single place position is written, so the geography column and the coordinate
    /// columns cannot disagree.
    /// </summary>
    private void ApplyPosition(Point position)
    {
        Position = position;

        // NTS stores longitude in X and latitude in Y.
        Latitude = position.Y;
        Longitude = position.X;
    }

    /// <summary>
    /// The single place wind is written, so the speed and its averaging period cannot be set
    /// independently of one another.
    /// </summary>
    private void ApplyWind(WindReading? wind)
    {
        WindSpeedKnots = wind?.SpeedKnots;
        WindAveragingPeriod = wind?.Period ?? WindAveragingPeriod.Unknown;
    }

    /// <summary>
    /// The single place the gale field is written, so a radius cannot be stored without the
    /// threshold and geometry that make it meaningful.
    /// </summary>
    private void ApplyGaleField(WindField field)
    {
        WindFieldGeometry = field.Geometry;
        GaleThresholdKnots = field.ThresholdKnots;
        GaleRadiusNorthEastNm = field.NorthEastNm;
        GaleRadiusSouthEastNm = field.SouthEastNm;
        GaleRadiusSouthWestNm = field.SouthWestNm;
        GaleRadiusNorthWestNm = field.NorthWestNm;
        GaleRadiusLongAxisNm = field.LongAxisNm;
        GaleRadiusShortAxisNm = field.ShortAxisNm;
        GaleRadiusBearingDegrees = field.BearingDegrees;
    }

    /// <summary>
    /// The single place the inner bands are written.
    /// </summary>
    /// <remarks>
    /// Both are quadrant fields by construction — only JTWC publishes them in this basin — so the
    /// ellipse members are ignored rather than mapped. Passing an ellipse here would be a caller
    /// error, and silently storing its axes as quadrant radii would be worse than dropping them.
    /// </remarks>
    private void ApplyInnerBands(WindField storm, WindField hurricane)
    {
        StormRadiusNorthEastNm = storm.NorthEastNm;
        StormRadiusSouthEastNm = storm.SouthEastNm;
        StormRadiusSouthWestNm = storm.SouthWestNm;
        StormRadiusNorthWestNm = storm.NorthWestNm;

        HurricaneRadiusNorthEastNm = hurricane.NorthEastNm;
        HurricaneRadiusSouthEastNm = hurricane.SouthEastNm;
        HurricaneRadiusSouthWestNm = hurricane.SouthWestNm;
        HurricaneRadiusNorthWestNm = hurricane.NorthWestNm;
    }
}
