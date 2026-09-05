using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Events;

public static class EventErrors
{
    public static readonly Error NotFound =
        new(ErrorType.NotFound, "event.not_found", "The event was not found.");

    public static readonly Error UnknownType =
        new(ErrorType.Validation, "event.unknown_type", "An event requires a known hazard type.");

    public static readonly Error ObservationSourceRequired =
        new(
            ErrorType.Validation,
            "event.observation_source_required",
            "Every observation must name the data source that reported it.");

    public static readonly Error ExternalIdRequired =
        new(
            ErrorType.Validation,
            "event.external_id_required",
            "Every observation must carry the reporting source's own identifier so it can be reconciled.");

    public static readonly Error DuplicateObservation =
        new(
            ErrorType.Conflict,
            "event.duplicate_observation",
            "This source has already reported an observation for this event.");

    public static readonly Error ObservationNotFound =
        new(ErrorType.NotFound, "event.observation_not_found", "The observation does not belong to this event.");

    public static readonly Error TrackPointsNotApplicable =
        new(
            ErrorType.Validation,
            "event.track_points_not_applicable",
            "Track points describe events that move through time, such as tropical cyclones.");

    public static readonly Error NotComparable =
        new(
            ErrorType.Validation,
            "event.not_comparable",
            "These events cannot be compared numerically because their magnitudes use different scale families.");
}
