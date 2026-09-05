using Calametra.Domain.Abstractions;
using NetTopologySuite.Geometries;

namespace Calametra.Domain.Events;

/// <summary>
/// One position of an event that moves through time.
/// </summary>
/// <remarks>
/// Designed against the NOAA IBTrACS schema, which reports cyclone positions at
/// roughly three-hour intervals with wind, pressure, distance to land and landfall
/// flags. Storing each fix as a row rather than baking a single LineString means
/// the timeline can scrub to any instant, and the derived track line stays a
/// projection rather than the source of truth.
/// </remarks>
public sealed class EventTrackPoint : Entity
{
    private EventTrackPoint()
    {
    }

    private EventTrackPoint(
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

    public Guid DataSourceId { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public Point Position { get; private set; } = default!;

    /// <summary>Maximum sustained wind in knots, as reported.</summary>
    public int? MaxSustainedWindKnots { get; private set; }

    /// <summary>Minimum central pressure in millibars, as reported.</summary>
    public int? MinimumPressureMillibars { get; private set; }

    /// <summary>Storm nature or intensity classification, verbatim from the source.</summary>
    public string? Classification { get; private set; }

    /// <summary>Distance to the nearest land in kilometres, where the source provides it.</summary>
    public double? DistanceToLandKm { get; private set; }

    public bool IsLandfall { get; private set; }

    internal static EventTrackPoint Create(
        Guid eventId,
        Guid dataSourceId,
        DateTimeOffset capturedAt,
        Point position,
        int? maxSustainedWindKnots,
        int? minimumPressureMillibars,
        string? classification,
        double? distanceToLandKm,
        bool isLandfall) =>
        new(Guid.CreateVersion7(), eventId, dataSourceId, capturedAt, position)
        {
            MaxSustainedWindKnots = maxSustainedWindKnots,
            MinimumPressureMillibars = minimumPressureMillibars,
            Classification = classification,
            DistanceToLandKm = distanceToLandKm,
            IsLandfall = isLandfall,
        };
}
