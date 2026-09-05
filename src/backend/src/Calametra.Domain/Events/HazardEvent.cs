using Calametra.Domain.Abstractions;
using Calametra.Domain.Seismology;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Events;

/// <summary>
/// A real-world hazard phenomenon: the thing that happened, independent of who
/// measured it.
/// </summary>
/// <remarks>
/// The split between this aggregate and <see cref="EventObservation"/> is the
/// central modelling decision in Calametra.
/// <code>
///                    HazardEvent
///               (one real earthquake)
///                         |
///          +--------------+--------------+
///          |                             |
///   EventObservation              EventObservation
///     PHIVOLCS                        USGS
///     Ms 6.7, 10 km                   Mww 6.5, 15 km
/// </code>
/// The canonical fields on this aggregate exist only so the map has something to
/// place and the timeline has something to sort by. They are a display choice,
/// not a truth claim, and every panel that shows a number must show the
/// observation it came from.
/// </remarks>
public sealed class HazardEvent : AuditableEntity
{
    private readonly List<EventObservation> _observations = [];
    private readonly List<EventTrackPoint> _trackPoints = [];

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

    public IReadOnlyCollection<EventObservation> Observations => _observations.AsReadOnly();

    public IReadOnlyCollection<EventTrackPoint> TrackPoints => _trackPoints.AsReadOnly();

    public EventObservation? PreferredObservation =>
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
        DateTimeOffset now)
    {
        if (type == HazardEventType.Unknown)
        {
            return Result<HazardEvent>.Failure(EventErrors.UnknownType);
        }

        var hazardEvent = new HazardEvent(Guid.CreateVersion7(), type, occurredAt, epicenter, now);

        return Result<HazardEvent>.Success(hazardEvent);
    }

    /// <summary>
    /// Records what one agency reported. One observation per source per event; a
    /// repeat report from the same source is a revision, not a second observation.
    /// </summary>
    public Result<EventObservation> AddObservation(
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
            return Result<EventObservation>.Failure(EventErrors.DuplicateObservation);
        }

        var creation = EventObservation.Create(
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

        return Result<EventObservation>.Success(created);
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

    public Result AddTrackPoint(
        Guid dataSourceId,
        DateTimeOffset capturedAt,
        Point position,
        DateTimeOffset now,
        int? maxSustainedWindKnots = null,
        int? minimumPressureMillibars = null,
        string? classification = null,
        double? distanceToLandKm = null,
        bool isLandfall = false)
    {
        if (Type != HazardEventType.TropicalCyclone)
        {
            return Result.Failure(EventErrors.TrackPointsNotApplicable);
        }

        _trackPoints.Add(EventTrackPoint.Create(
            Id,
            dataSourceId,
            capturedAt,
            position,
            maxSustainedWindKnots,
            minimumPressureMillibars,
            classification,
            distanceToLandKm,
            isLandfall));

        Touch(now);

        return Result.Success();
    }

    /// <summary>The track as an ordered line, derived from stored fixes.</summary>
    public LineString TrackLine() => Wgs84Track(_trackPoints);

    private static LineString Wgs84Track(List<EventTrackPoint> points) =>
        Geospatial.Wgs84.LineString(points
            .OrderBy(point => point.CapturedAt)
            .Select(point => point.Position));
}
