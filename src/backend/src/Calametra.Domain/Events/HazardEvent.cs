using Calametra.Domain.Abstractions;
using Calametra.Domain.Meteorology;
using Calametra.Domain.Seismology;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Events;

/// <summary>
/// A real-world hazard phenomenon: the thing that happened, independent of who
/// measured it.
/// </summary>
/// <remarks>
/// The split between this aggregate and <see cref="EarthquakeObservation"/> is the
/// central modelling decision in Calametra.
/// <code>
///                    HazardEvent
///               (one real earthquake)
///                         |
///          +--------------+--------------+
///          |                             |
///   EarthquakeObservation        EarthquakeObservation
///     PHIVOLCS                        USGS
///     Ms 6.7, 10 km                   Mww 6.5, 15 km
/// </code>
/// The canonical fields on this aggregate exist only so the map has something to
/// place and the timeline has something to sort by. They are a display choice,
/// not a truth claim, and every panel that shows a number must show the
/// observation it came from.
/// <para>
/// The observation type is named for its hazard, not generically, because everything it
/// carries is seismological — epicentre, magnitude, scale, hypocentre depth. When a second
/// hazard family arrives it gets its own observation type rather than inheriting columns it
/// can never populate. This aggregate is the shared part: identity, canonical time and
/// place, and the fact that several agencies described the same real-world occurrence.
/// </para>
/// </remarks>
public sealed class HazardEvent : AuditableEntity
{
    private readonly List<EarthquakeObservation> _observations = [];
    private readonly List<CycloneTrackPoint> _trackPoints = [];

    private HazardEvent()
    {
    }

    private HazardEvent(
        Guid id,
        HazardEventType type,
        DateTimeOffset canonicalOccurredAt,
        Point canonicalEpicenter,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Type = type;
        CanonicalOccurredAt = canonicalOccurredAt;
        CanonicalEpicenter = canonicalEpicenter;
    }

    public HazardEventType Type { get; private set; }

    /// <summary>
    /// The name the event is known by, where it has one.
    /// </summary>
    /// <remarks>
    /// Null for most earthquakes, which are identified by time and place rather than a name.
    /// Populated for cyclones, which are named by the agencies that track them, and available
    /// for any later hazard that is named — a volcano's eruption, or a historically titled
    /// earthquake.
    /// <para>
    /// Kept on the shared aggregate rather than on an observation type, because unlike
    /// magnitude or wind a name carries no unit, no scale and no comparability rule. It is one
    /// string meaning the same thing for every hazard, which is the test for whether something
    /// belongs here rather than in a hazard-specific table.
    /// </para>
    /// <para>
    /// For Philippine cyclones this holds the <b>international</b> name only. PAGASA assigns a
    /// separate local name — Haiyan was Yolanda — and the upstream archive does not carry it.
    /// Storing only one name is a known limitation, not a claim that only one exists.
    /// </para>
    /// </remarks>
    public string? Name { get; private set; }

    /// <summary>
    /// The name a national authority assigned, where it differs from the international one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PAGASA names every tropical cyclone that enters the Philippine Area of Responsibility
    /// from its own rotating list, independently of the international name assigned by the
    /// Japan Meteorological Agency. Haiyan was Yolanda here; Goni was Rolly; Rai was Odette.
    /// For a Philippine audience the local name is usually the recognisable one, and in public
    /// memory it is often the *only* one.
    /// </para>
    /// <para>
    /// Held separately rather than folded into <see cref="Name"/> because the two come from
    /// different naming authorities. Concatenating them would make it impossible to say which
    /// body assigned which, and the platform's rule is that no value is shown without its
    /// source.
    /// </para>
    /// <para>
    /// Null where no mapping is held, which is the common case. The crosswalk is curated rather
    /// than ingested: PAGASA publishes its name lists as documents rather than as data, and the
    /// mapping from international to local name requires per-storm knowledge that no machine-
    /// readable source provides.
    /// </para>
    /// </remarks>
    public string? LocalName { get; private set; }

    /// <summary>
    /// Records the national authority's name for this event.
    /// </summary>
    /// <remarks>
    /// Separate from creation because the crosswalk is applied after ingestion: the storm has to
    /// exist before it can be matched, and the mapping is maintained independently of the
    /// archive the storm came from.
    /// </remarks>
    public void AssignLocalName(string localName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(localName))
        {
            return;
        }

        LocalName = localName.Trim();
        Touch(now);
    }

    /// <summary>
    /// Origin time used for timeline ordering. Taken from the preferred
    /// observation; sources typically agree to within a second or two.
    /// </summary>
    public DateTimeOffset CanonicalOccurredAt { get; private set; }

    /// <summary>Position used for map placement and spatial indexing.</summary>
    public Point CanonicalEpicenter { get; private set; } = default!;

    /// <summary>
    /// The observation shown by default. Null until one is nominated, which is an
    /// application-layer decision (typically: prefer the Philippine authority, then
    /// prefer a moment-magnitude solution).
    /// </summary>
    public Guid? PreferredObservationId { get; private set; }

    public IReadOnlyCollection<EarthquakeObservation> Observations => _observations.AsReadOnly();

    public IReadOnlyCollection<CycloneTrackPoint> TrackPoints => _trackPoints.AsReadOnly();

    public EarthquakeObservation? PreferredObservation =>
        PreferredObservationId is { } id
            ? _observations.SingleOrDefault(observation => observation.Id == id)
            : _observations.Count == 1 ? _observations[0] : null;

    /// <summary>
    /// True when more than one agency has reported this event, which means the
    /// UI must render a comparison rather than a single figure.
    /// </summary>
    public bool HasMultipleObservations => _observations.Count > 1;

    /// <summary>
    /// True when reporting agencies disagree on magnitude beyond rounding, or
    /// report on scales that are not comparable. Drives the "why do these differ?"
    /// disclosure in the event panel.
    /// </summary>
    public bool HasMagnitudeDisagreement
    {
        get
        {
            var readings = _observations
                .Select(observation => observation.Magnitude)
                .OfType<MagnitudeReading>()
                .ToList();

            if (readings.Count < 2)
            {
                return false;
            }

            for (var i = 0; i < readings.Count - 1; i++)
            {
                for (var j = i + 1; j < readings.Count; j++)
                {
                    var difference = readings[i].DifferenceFrom(readings[j]);

                    // Null means the scales are not comparable, which is itself a
                    // disagreement worth disclosing.
                    if (difference is null || difference > 0.05d)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public static Result<HazardEvent> Create(
        HazardEventType type,
        DateTimeOffset occurredAt,
        Point epicenter,
        DateTimeOffset now,
        string? name = null)
    {
        if (type == HazardEventType.Unknown)
        {
            return Result<HazardEvent>.Failure(EventErrors.UnknownType);
        }

        var hazardEvent = new HazardEvent(Guid.CreateVersion7(), type, occurredAt, epicenter, now)
        {
            // Optional and trimmed to null, so an upstream blank never becomes an empty name
            // that renders as a gap where a name should be.
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
        };

        return Result<HazardEvent>.Success(hazardEvent);
    }

    /// <summary>
    /// Records what one agency reported. One observation per source per event; a
    /// repeat report from the same source is a revision, not a second observation.
    /// </summary>
    public Result<EarthquakeObservation> AddObservation(
        Guid dataSourceId,
        string externalEventId,
        DateTimeOffset observedAt,
        Point epicenter,
        DepthReading depth,
        MagnitudeReading? magnitude,
        DateTimeOffset now,
        string? sourceUrl = null)
    {
        if (_observations.Any(observation => observation.DataSourceId == dataSourceId))
        {
            return Result<EarthquakeObservation>.Failure(EventErrors.DuplicateObservation);
        }

        var creation = EarthquakeObservation.Create(
            Id,
            dataSourceId,
            externalEventId,
            observedAt,
            epicenter,
            depth,
            magnitude,
            now,
            sourceUrl);

        if (creation.IsFailure)
        {
            return creation;
        }

        var created = creation.Value;
        _observations.Add(created);

        // First observation establishes the canonical view until one is nominated.
        if (_observations.Count == 1)
        {
            PreferredObservationId = created.Id;
            CanonicalOccurredAt = observedAt;
            CanonicalEpicenter = epicenter;
        }

        Touch(now);

        return Result<EarthquakeObservation>.Success(created);
    }

    /// <summary>
    /// Nominates which agency's reading the map and timeline follow. Realigns the
    /// canonical fields so ordering and placement stay consistent with what is shown.
    /// </summary>
    public Result SetPreferredObservation(Guid observationId, DateTimeOffset now)
    {
        var observation = _observations.SingleOrDefault(candidate => candidate.Id == observationId);

        if (observation is null)
        {
            return Result.Failure(EventErrors.ObservationNotFound);
        }

        PreferredObservationId = observation.Id;
        CanonicalOccurredAt = observation.ObservedAt;
        CanonicalEpicenter = observation.Epicenter;
        Touch(now);

        return Result.Success();
    }

    /// <summary>
    /// Records one agency's fix on a cyclone's centre.
    /// </summary>
    /// <remarks>
    /// Wind arrives as a <see cref="WindReading"/> rather than a bare number of knots, so a
    /// caller cannot record a speed without stating the interval it was averaged over. The
    /// archive holds four agencies reporting the same storm at 30, 35, 34 and 25 knots over
    /// three different intervals; a plain <c>int?</c> would have flattened that into a single
    /// unqualified figure.
    /// </remarks>
    public Result AddTrackPoint(
        Guid dataSourceId,
        string externalStormId,
        DateTimeOffset capturedAt,
        Point position,
        DateTimeOffset now,
        WindReading? wind = null,
        int? minimumPressureMillibars = null,
        string? classification = null,
        double? distanceToLandKm = null,
        bool isLandfall = false,
        double? radiusOfMaximumWindNm = null,
        double? radiusOutermostIsobarNm = null,
        WindField galeField = default,
        WindField stormField = default,
        WindField hurricaneField = default)
    {
        if (Type != HazardEventType.TropicalCyclone)
        {
            return Result.Failure(EventErrors.TrackPointsNotApplicable);
        }

        if (string.IsNullOrWhiteSpace(externalStormId))
        {
            return Result.Failure(EventErrors.ExternalIdRequired);
        }

        _trackPoints.Add(CycloneTrackPoint.Create(
            Id,
            dataSourceId,
            externalStormId,
            capturedAt,
            position,
            wind,
            minimumPressureMillibars,
            classification,
            distanceToLandKm,
            isLandfall,
            radiusOfMaximumWindNm,
            radiusOutermostIsobarNm,
            galeField,
            stormField,
            hurricaneField));

        Touch(now);

        return Result.Success();
    }

    /// <summary>The track as an ordered line, derived from stored fixes.</summary>
    public LineString TrackLine() => Wgs84Track(_trackPoints);

    private static LineString Wgs84Track(List<CycloneTrackPoint> points) =>
        Geospatial.Wgs84.LineString(points
            .OrderBy(point => point.CapturedAt)
            .Select(point => point.Position));
}
