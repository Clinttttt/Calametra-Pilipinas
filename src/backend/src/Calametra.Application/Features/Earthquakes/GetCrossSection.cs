using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// Earthquakes along a vertical slice through the crust.
/// </summary>
/// <remarks>
/// <para>
/// The platform's signature depth feature. A map cannot show how deep earthquakes are or
/// how depth changes across a region; a cross-section can, and across the Philippine
/// trenches it reveals the subducting slab as a dipping plane of seismicity reaching
/// 667 km.
/// </para>
/// <para>
/// <b>Depth quality is load-bearing here more than anywhere else.</b> 11,790 of 27,241
/// events report a depth the agency assigned rather than measured — 33 km, 10 km, 35 km
/// or 15 km. Plotted as though measured, they draw four dead-flat horizontal lines
/// through 43% of the section, and those lines look exactly like real structure. Every
/// point therefore carries its quality, and the caller can exclude assigned depths
/// outright.
/// </para>
/// <para>
/// Split of work: PostGIS filters the corridor using the GiST index, reducing tens of
/// thousands of events to a few hundred; the projection then runs on that small set in
/// <c>CrossSectionProfile</c>, where it is unit tested. The alternative — pushing
/// <c>ST_LineLocatePoint</c> into raw SQL — would put the calculation somewhere it
/// cannot be verified.
/// </para>
/// </remarks>
public static class GetCrossSection
{
    public sealed record Query : IQuery<CrossSectionResponse>
    {
        public required double StartLatitude { get; init; }

        public required double StartLongitude { get; init; }

        public required double EndLatitude { get; init; }

        public required double EndLongitude { get; init; }

        /// <summary>
        /// Half-width of the corridor either side of the line, in kilometres.
        /// </summary>
        /// <remarks>
        /// A section with no width would catch almost nothing — epicentres do not land
        /// on a drawn line. 50 km is a conventional default for a regional slab profile:
        /// wide enough to gather a population, narrow enough that the events genuinely
        /// belong to the same structure.
        /// </remarks>
        public double CorridorKm { get; init; } = 50d;

        /// <summary>
        /// When false, events whose depth was assigned by the agency are excluded.
        /// </summary>
        /// <remarks>
        /// Defaults to false — the opposite of the map's default, deliberately. On a map
        /// an assigned depth still tells you an earthquake happened there; on a depth
        /// section it is a fabricated vertical position, and including it by default
        /// would put four false bands into the platform's flagship visualisation.
        /// </remarks>
        public bool IncludeAssignedDepths { get; init; }

        public double? MinMagnitude { get; init; }

        public DateTimeOffset? From { get; init; }

        public DateTimeOffset? To { get; init; }
    }

    /// <summary>The section, its extent, and the events on it.</summary>
    public sealed record CrossSectionResponse(
        double LengthKm,
        double CorridorKm,
        double MaxDepthKm,
        int TotalCount,
        int ExcludedAssignedDepthCount,
        IReadOnlyList<CrossSectionPoint> Points);

    /// <summary>
    /// One hypocentre positioned on the section.
    /// </summary>
    /// <param name="EventId">For opening the event's detail.</param>
    /// <param name="AlongKm">Distance from the section's start. The horizontal axis.</param>
    /// <param name="DepthKm">Hypocentre depth. The vertical axis.</param>
    /// <param name="OffsetKm">
    /// Perpendicular distance from the line. Reported so a reader can distinguish an
    /// event on the slice from one gathered at the corridor's edge — the plot can fade
    /// distant points rather than implying they sit where they are drawn.
    /// </param>
    /// <param name="Magnitude">Null when the source reported none.</param>
    /// <param name="MagnitudeScale">Never omitted: a magnitude without its scale is not comparable.</param>
    /// <param name="DepthMeasured">
    /// False when the agency assigned the depth. Drives distinct styling.
    /// </param>
    /// <param name="OccurredAt">For time filtering and for the tooltip.</param>
    public sealed record CrossSectionPoint(
        Guid EventId,
        double AlongKm,
        double DepthKm,
        double OffsetKm,
        double? Magnitude,
        string MagnitudeScale,
        bool DepthMeasured,
        DateTimeOffset OccurredAt);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.StartLatitude).InclusiveBetween(-90d, 90d);
            RuleFor(query => query.EndLatitude).InclusiveBetween(-90d, 90d);
            RuleFor(query => query.StartLongitude).InclusiveBetween(-180d, 180d);
            RuleFor(query => query.EndLongitude).InclusiveBetween(-180d, 180d);

            // Upper bound because the corridor is a spatial predicate against 27,000
            // rows: a 500 km corridor stops being a section and becomes a regional
            // query wearing a section's clothes.
            RuleFor(query => query.CorridorKm).InclusiveBetween(1d, 200d);

            RuleFor(query => query)
                .Must(query =>
                    Math.Abs(query.StartLatitude - query.EndLatitude) > 0.001d
                    || Math.Abs(query.StartLongitude - query.EndLongitude) > 0.001d)
                .WithMessage("A cross-section needs two distinct endpoints.");

            RuleFor(query => query.To)
                .GreaterThan(query => query.From!.Value)
                .When(query => query.From.HasValue && query.To.HasValue);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, CrossSectionResponse>
    {
        public async Task<Result<CrossSectionResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var profile = CrossSectionProfile.Create(
                request.StartLatitude,
                request.StartLongitude,
                request.EndLatitude,
                request.EndLongitude,
                request.CorridorKm);

            var corridorMetres = request.CorridorKm * 1_000d;

            var query =
                from hazardEvent in context.HazardEvents.AsNoTracking()
                join observation in context.EarthquakeObservations.AsNoTracking()
                    on hazardEvent.PreferredObservationId equals observation.Id
                where hazardEvent.Type == HazardEventType.Earthquake
                    && observation.DepthKilometres != null
                    // ST_DWithin against the GiST index on the geography column. This is
                    // what keeps the query fast enough to run on every redraw.
                    && observation.Epicenter.IsWithinDistance(profile.Line, corridorMetres)
                select new
                {
                    hazardEvent.Id,
                    hazardEvent.CanonicalOccurredAt,
                    observation.Epicenter,
                    observation.DepthKilometres,
                    observation.DepthQuality,
                    observation.MagnitudeValue,
                    observation.MagnitudeScale,
                };

            if (request.From is { } from)
            {
                query = query.Where(row => row.CanonicalOccurredAt >= from);
            }

            if (request.To is { } to)
            {
                query = query.Where(row => row.CanonicalOccurredAt < to);
            }

            if (request.MinMagnitude is { } minMagnitude)
            {
                query = query.Where(row => row.MagnitudeValue >= minMagnitude);
            }

            var rows = await query.ToListAsync(cancellationToken);

            // Counted before filtering so the response can state how many events were
            // set aside. A section that silently drops 43% of its data is misleading in
            // a subtler way than one that plots it.
            var assignedDepthCount = rows.Count(row => row.DepthQuality != DepthQuality.Constrained);

            var included = request.IncludeAssignedDepths
                ? rows
                : rows.Where(row => row.DepthQuality == DepthQuality.Constrained).ToList();

            var points = included
                .Select(row =>
                {
                    var projection = profile.Project(row.Epicenter);

                    return new CrossSectionPoint(
                        row.Id,
                        Math.Round(projection.AlongKm, 2),
                        row.DepthKilometres!.Value,
                        Math.Round(projection.OffsetKm, 2),
                        row.MagnitudeValue,
                        row.MagnitudeScale.Label(),
                        row.DepthQuality == DepthQuality.Constrained,
                        row.CanonicalOccurredAt);
                })
                .OrderBy(point => point.AlongKm)
                .ToList();

            // Axis scaled to the deepest event present, rounded up, so a shallow crustal
            // section is not squashed into the top of a 700 km axis.
            var maxDepth = points.Count == 0
                ? 0d
                : Math.Ceiling(points.Max(point => point.DepthKm) / 50d) * 50d;

            return Result<CrossSectionResponse>.Success(new CrossSectionResponse(
                Math.Round(profile.LengthKm, 1),
                request.CorridorKm,
                maxDepth,
                points.Count,
                request.IncludeAssignedDepths ? 0 : assignedDepthCount,
                points));
        }
    }
}
