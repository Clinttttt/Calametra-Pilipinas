using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Cyclones.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using FluentValidation;

namespace Calametra.Application.Features.Cyclones;

/// <summary>
/// One cyclone's track, as each agency drew it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One track per agency, not one averaged track.</b> Agencies differ on where the centre was
/// as well as how strong it was, and a mean path would be a line no agency published. Grouping
/// by agency also lets the map draw them as separate lines, which makes the positional
/// disagreement visible instead of hiding it inside a single stroke.
/// </para>
/// <para>
/// Each agency's track states its averaging period once, at the track level, rather than
/// repeating it on every fix. The period is a property of the agency's method, so it cannot
/// vary along a track — and stating it per point would imply it might.
/// </para>
/// </remarks>
public static class GetCycloneTrack
{
    public sealed record Query : IQuery<CycloneTrackResponse>
    {
        public required Guid EventId { get; init; }
    }

    /// <param name="WindKnots">Null when this agency reported pressure but no wind.</param>
    /// <param name="IsLandfall">
    /// True when the storm crosses the coast between this fix and the next, which is how the
    /// source expresses landfall — a property of the interval, not of the instant.
    /// </param>
    /// <param name="EyewallRadiusNm">
    /// Radius of maximum wind. The physical size of the eyewall, which is what lets a cyclone be
    /// drawn at its real scale rather than at an arbitrary marker size.
    /// </param>
    /// <param name="GaleGeometry">
    /// <c>Quadrants</c>, <c>Ellipse</c> or <c>None</c>. Agencies describe the wind field with
    /// different shapes, so the client must branch rather than assume one.
    /// </param>
    /// <param name="GaleThresholdKnots">
    /// The wind speed the gale radii are measured at — 34 kt for JTWC, 30 kt for JMA and KMA. A
    /// radius shown without it looks comparable across agencies and is not.
    /// </param>
    /// <param name="GaleAsymmetryRatio">
    /// Widest quadrant divided by narrowest, where all four are reported. Measured values in this
    /// archive reach 37, which is why the field must never be drawn as a circle.
    /// </param>
    public sealed record CycloneFixResponse(
        DateTimeOffset CapturedAt,
        double Latitude,
        double Longitude,
        double? WindKnots,
        int? PressureMillibars,
        string? Classification,
        double? DistanceToLandKm,
        bool IsLandfall,
        double? EyewallRadiusNm,
        double? OuterRadiusNm,
        string GaleGeometry,
        int GaleThresholdKnots,
        double? GaleNorthEastNm,
        double? GaleSouthEastNm,
        double? GaleSouthWestNm,
        double? GaleNorthWestNm,
        double? GaleLongAxisNm,
        double? GaleShortAxisNm,
        int? GaleBearingDegrees,
        double? GaleAsymmetryRatio,
        double? StormNorthEastNm,
        double? StormSouthEastNm,
        double? StormSouthWestNm,
        double? StormNorthWestNm,
        double? HurricaneNorthEastNm,
        double? HurricaneSouthEastNm,
        double? HurricaneSouthWestNm,
        double? HurricaneNorthWestNm);

    /// <param name="AveragingPeriod">
    /// The interval this agency averages sustained wind over. Stated once per track because it
    /// is a property of the agency's method and cannot change along the path.
    /// </param>
    /// <param name="PeakWindKnots">
    /// This agency's own peak. Comparable only with peaks from agencies using the same period.
    /// </param>
    public sealed record AgencyTrackResponse(
        string Agency,
        string SourceSlug,
        string AveragingPeriod,
        double? PeakWindKnots,
        int? MinimumPressureMillibars,
        IReadOnlyList<CycloneFixResponse> Fixes);

    /// <param name="Tracks">One per agency that analysed the storm, most fixes first.</param>
    /// <param name="AgreementNote">
    /// Plain-language statement of how far the agencies diverge, generated from the readings
    /// present rather than a fixed disclaimer.
    /// </param>
    public sealed record CycloneTrackResponse(
        Guid Id,
        string? Name,
        string? LocalName,
        int Season,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        bool MadeLandfall,
        IReadOnlyList<AgencyTrackResponse> Tracks,
        string? AgreementNote);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.EventId).NotEmpty();
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, CycloneTrackResponse>
    {
        public async Task<Result<CycloneTrackResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var track = await CycloneTrackLoader.Load(context, request.EventId, cancellationToken);

            return track is null
                ? Result<CycloneTrackResponse>.Failure(EventErrors.NotFound)
                : Result<CycloneTrackResponse>.Success(track);
        }
    }
}
