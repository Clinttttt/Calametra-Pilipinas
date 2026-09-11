using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// The primary read path: filters the earthquake archive by time, magnitude, depth
/// and geography.
/// </summary>
/// <remarks>
/// Backs the Time Machine, the historical explorer, the place-history panel and the
/// cross-section, which are the same query with different parameters bound. Keeping
/// them one slice avoids four handlers that drift apart.
/// </remarks>
public static class SearchEarthquakes
{
    public sealed record Query : IQuery<PaginatedList<EarthquakeSummaryResponse>>
    {
        /// <summary>Inclusive lower bound on origin time.</summary>
        public DateTimeOffset? From { get; init; }

        /// <summary>Exclusive upper bound on origin time.</summary>
        public DateTimeOffset? To { get; init; }

        public double? MinMagnitude { get; init; }

        public double? MaxMagnitude { get; init; }

        public double? MinDepthKm { get; init; }

        public double? MaxDepthKm { get; init; }

        /// <summary>
        /// Restricts results to one magnitude scale family. Necessary whenever
        /// magnitudes are being compared or ranked, because the catalogue mixes
        /// body-wave and moment magnitudes.
        /// </summary>
        public MagnitudeScaleFamily? ScaleFamily { get; init; }

        /// <summary>
        /// When false, events whose depth was fixed to an agency default are
        /// excluded. Defaults to true so plain listings stay complete; the
        /// cross-section sets it false.
        /// </summary>
        public bool IncludeAssignedDepths { get; init; } = true;

        /// <summary>Centre of a radius search. Requires <see cref="RadiusKm"/>.</summary>
        public double? CentreLatitude { get; init; }

        public double? CentreLongitude { get; init; }

        /// <summary>Radius in kilometres around the centre point.</summary>
        public double? RadiusKm { get; init; }

        public int Page { get; init; } = 1;

        public int PageSize { get; init; } = 100;
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThan(0);

            RuleFor(query => query.PageSize)
                .InclusiveBetween(1, 1_000)
                .WithMessage("PageSize must be between 1 and 1000.");

            RuleFor(query => query.To)
                .GreaterThan(query => query.From!.Value)
                .When(query => query.From.HasValue && query.To.HasValue)
                .WithMessage("'To' must be later than 'From'.");

            RuleFor(query => query.MaxMagnitude)
                .GreaterThanOrEqualTo(query => query.MinMagnitude!.Value)
                .When(query => query.MinMagnitude.HasValue && query.MaxMagnitude.HasValue);

            RuleFor(query => query.MaxDepthKm)
                .GreaterThanOrEqualTo(query => query.MinDepthKm!.Value)
                .When(query => query.MinDepthKm.HasValue && query.MaxDepthKm.HasValue);

            RuleFor(query => query.CentreLatitude).InclusiveBetween(-90d, 90d).When(HasAnyRadiusField);
            RuleFor(query => query.CentreLongitude).InclusiveBetween(-180d, 180d).When(HasAnyRadiusField);
            RuleFor(query => query.RadiusKm).GreaterThan(0d).LessThanOrEqualTo(2_000d).When(HasAnyRadiusField);

            // A radius search needs all three parts or none. Silently ignoring a
            // half-specified radius would hand back results the caller did not ask for.
            RuleFor(query => query)
                .Must(query => !HasAnyRadiusField(query)
                    || (query.CentreLatitude.HasValue
                        && query.CentreLongitude.HasValue
                        && query.RadiusKm.HasValue))
                .WithMessage(
                    "A radius search requires CentreLatitude, CentreLongitude and RadiusKm together.");
        }

        private static bool HasAnyRadiusField(Query query) =>
            query.CentreLatitude.HasValue || query.CentreLongitude.HasValue || query.RadiusKm.HasValue;
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, PaginatedList<EarthquakeSummaryResponse>>
    {
        public async Task<Result<PaginatedList<EarthquakeSummaryResponse>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            // Joined to the preferred observation because magnitude and depth belong
            // to an agency reading, never to the event itself.
            var query =
                from hazardEvent in context.HazardEvents.AsNoTracking()
                join observation in context.EarthquakeObservations.AsNoTracking()
                    on hazardEvent.PreferredObservationId equals observation.Id
                join source in context.DataSources.AsNoTracking()
                    on observation.DataSourceId equals source.Id
                where hazardEvent.Type == HazardEventType.Earthquake
                select new { HazardEvent = hazardEvent, Observation = observation, Source = source };

            if (request.From is { } from)
            {
                query = query.Where(row => row.HazardEvent.CanonicalOccurredAt >= from);
            }

            if (request.To is { } to)
            {
                query = query.Where(row => row.HazardEvent.CanonicalOccurredAt < to);
            }

            if (request.MinMagnitude is { } minMagnitude)
            {
                query = query.Where(row => row.Observation.MagnitudeValue >= minMagnitude);
            }

            if (request.MaxMagnitude is { } maxMagnitude)
            {
                query = query.Where(row => row.Observation.MagnitudeValue <= maxMagnitude);
            }

            if (request.MinDepthKm is { } minDepth)
            {
                query = query.Where(row => row.Observation.DepthKilometres >= minDepth);
            }

            if (request.MaxDepthKm is { } maxDepth)
            {
                query = query.Where(row => row.Observation.DepthKilometres <= maxDepth);
            }

            if (!request.IncludeAssignedDepths)
            {
                query = query.Where(row => row.Observation.DepthQuality == DepthQuality.Constrained);
            }

            if (request.ScaleFamily is { } family)
            {
                var scales = ScalesInFamily(family);
                query = query.Where(row => scales.Contains(row.Observation.MagnitudeScale));
            }

            if (request is { CentreLatitude: { } latitude, CentreLongitude: { } longitude, RadiusKm: { } radiusKm })
            {
                // Epicentre columns are PostGIS geography, so Distance returns metres
                // and this translates to ST_Distance without a projection step.
                var centre = Wgs84.Point(latitude, longitude);
                var radiusMetres = radiusKm * 1_000d;

                query = query.Where(row => row.Observation.Epicenter.Distance(centre) <= radiusMetres);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            if (totalCount == 0)
            {
                return Result<PaginatedList<EarthquakeSummaryResponse>>.Success(
                    PaginatedList<EarthquakeSummaryResponse>.Empty(request.Page, request.PageSize));
            }

            var rows = await query
                .OrderByDescending(row => row.HazardEvent.CanonicalOccurredAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(row => new
                {
                    row.HazardEvent.Id,
                    row.HazardEvent.CanonicalOccurredAt,
                    // Read from the mapped coordinate columns, not from the geography
                    // column: PostGIS ST_X / ST_Y accept geometry only.
                    row.Observation.Latitude,
                    row.Observation.Longitude,
                    row.Observation.MagnitudeValue,
                    row.Observation.MagnitudeScale,
                    row.Observation.DepthKilometres,
                    row.Observation.DepthQuality,
                    row.Source.Agency,
                    row.Source.Slug,
                    ObservationCount = row.HazardEvent.Observations.Count,
                })
                .ToListAsync(cancellationToken);

            var items = rows
                .Select(row => new EarthquakeSummaryResponse(
                    row.Id,
                    row.CanonicalOccurredAt,
                    row.Latitude,
                    row.Longitude,
                    MagnitudeResponse.From(row.MagnitudeValue, row.MagnitudeScale),
                    DepthResponse.From(row.DepthKilometres, row.DepthQuality),
                    row.Agency,
                    row.Slug,
                    row.ObservationCount > 1,
                    // Detecting genuine disagreement needs every reading loaded, which
                    // is a detail-view concern. The list flags only that more than one
                    // agency reported, so the client knows a comparison exists.
                    HasMagnitudeDisagreement: false))
                .ToList();

            return Result<PaginatedList<EarthquakeSummaryResponse>>.Success(
                PaginatedList<EarthquakeSummaryResponse>.Create(
                    items,
                    totalCount,
                    request.Page,
                    request.PageSize));
        }

        /// <summary>
        /// Expands a scale family into the concrete scales it contains, so the filter
        /// runs in SQL against the mapped column rather than in memory.
        /// </summary>
        private static MagnitudeType[] ScalesInFamily(MagnitudeScaleFamily family) =>
            Enum.GetValues<MagnitudeType>()
                .Where(scale => scale.Family() == family)
                .ToArray();
    }
}
