using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Seismology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// Monthly event counts across the archive, for the timeline's density histogram.
/// </summary>
/// <remarks>
/// <para>
/// Aggregated in the database rather than by counting the events the client already
/// holds, for two reasons. The client only holds what it fetched, so a filtered or
/// paged view would produce a histogram that disagrees with the archive. And the
/// histogram has to be available before the events are, so the timeline can render
/// immediately instead of waiting on eight page requests.
/// </para>
/// <para>
/// The histogram is not decoration. Seismicity is extremely uneven — the December
/// 2023 Mindanao sequence alone contributes a large share of a single year — so a
/// plain linear scrubber gives the user no indication that most years are quiet and
/// a few weeks are not. Without the histogram, scrubbing feels broken.
/// </para>
/// </remarks>
public static class GetEarthquakeActivity
{
    public sealed record Query : IQuery<ActivityResponse>
    {
        public DateTimeOffset? From { get; init; }

        public DateTimeOffset? To { get; init; }

        /// <summary>
        /// Restricts the histogram to one magnitude scale family, so it matches a
        /// map filtered the same way.
        /// </summary>
        public MagnitudeScaleFamily? ScaleFamily { get; init; }

        public double? MinMagnitude { get; init; }
    }

    /// <summary>Counts per month, plus the extent the timeline should span.</summary>
    public sealed record ActivityResponse(
        DateTimeOffset? FirstEventAt,
        DateTimeOffset? LastEventAt,
        int TotalCount,
        int PeakMonthlyCount,
        IReadOnlyList<ActivityBucket> Buckets);

    /// <summary>
    /// One month of activity.
    /// </summary>
    /// <param name="PeriodStart">First instant of the month, UTC.</param>
    /// <param name="Count">Events with an origin time in this month.</param>
    /// <param name="MaxMagnitude">
    /// Largest magnitude in the month, or null when none was reported. Lets the
    /// histogram mark months containing a major event, which a count alone hides: a
    /// quiet month with one M7 matters more than a busy month of M4s.
    /// </param>
    public sealed record ActivityBucket(
        DateTimeOffset PeriodStart,
        int Count,
        double? MaxMagnitude);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() =>
            RuleFor(query => query.To)
                .GreaterThan(query => query.From!.Value)
                .When(query => query.From.HasValue && query.To.HasValue)
                .WithMessage("'To' must be later than 'From'.");
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, ActivityResponse>
    {
        public async Task<Result<ActivityResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var query =
                from hazardEvent in context.HazardEvents.AsNoTracking()
                join observation in context.EventObservations.AsNoTracking()
                    on hazardEvent.PreferredObservationId equals observation.Id
                where hazardEvent.Type == HazardEventType.Earthquake
                select new { hazardEvent.CanonicalOccurredAt, observation.MagnitudeValue, observation.MagnitudeScale };

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

            if (request.ScaleFamily is { } family)
            {
                var scales = Enum.GetValues<MagnitudeType>()
                    .Where(scale => scale.Family() == family)
                    .ToArray();

                query = query.Where(row => scales.Contains(row.MagnitudeScale));
            }

            // Grouped on year and month rather than a date_trunc call: Npgsql
            // translates the component properties, so this stays a single aggregate
            // query instead of pulling 8,700 rows back to count them here.
            var grouped = await query
                .GroupBy(row => new { row.CanonicalOccurredAt.Year, row.CanonicalOccurredAt.Month })
                .Select(group => new
                {
                    group.Key.Year,
                    group.Key.Month,
                    Count = group.Count(),
                    MaxMagnitude = group.Max(row => row.MagnitudeValue),
                })
                .ToListAsync(cancellationToken);

            if (grouped.Count == 0)
            {
                return Result<ActivityResponse>.Success(
                    new ActivityResponse(null, null, 0, 0, []));
            }

            var ordered = grouped
                .OrderBy(bucket => bucket.Year)
                .ThenBy(bucket => bucket.Month)
                .ToList();

            var first = ordered[0];
            var last = ordered[^1];

            // Months with no events are filled in as zeroes. A histogram that simply
            // omits empty months would compress the time axis and misrepresent the
            // gaps, which are exactly what makes the distribution interesting.
            var buckets = FillMissingMonths(ordered.Select(bucket =>
                new ActivityBucket(
                    new DateTimeOffset(bucket.Year, bucket.Month, 1, 0, 0, 0, TimeSpan.Zero),
                    bucket.Count,
                    bucket.MaxMagnitude)));

            return Result<ActivityResponse>.Success(new ActivityResponse(
                new DateTimeOffset(first.Year, first.Month, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(last.Year, last.Month, 1, 0, 0, 0, TimeSpan.Zero)
                    .AddMonths(1)
                    .AddTicks(-1),
                buckets.Sum(bucket => bucket.Count),
                buckets.Count == 0 ? 0 : buckets.Max(bucket => bucket.Count),
                buckets));
        }

        /// <summary>Inserts zero-count buckets for months absent from the results.</summary>
        private static List<ActivityBucket> FillMissingMonths(IEnumerable<ActivityBucket> present)
        {
            var source = present.ToList();

            if (source.Count == 0)
            {
                return source;
            }

            var filled = new List<ActivityBucket>(source.Count);
            var cursor = source[0].PeriodStart;
            var end = source[^1].PeriodStart;
            var index = 0;

            while (cursor <= end)
            {
                if (index < source.Count && source[index].PeriodStart == cursor)
                {
                    filled.Add(source[index]);
                    index++;
                }
                else
                {
                    filled.Add(new ActivityBucket(cursor, 0, null));
                }

                cursor = cursor.AddMonths(1);
            }

            return filled;
        }
    }
}
