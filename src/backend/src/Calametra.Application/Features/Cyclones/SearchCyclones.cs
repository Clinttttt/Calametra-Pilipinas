using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Meteorology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Cyclones;

/// <summary>
/// Tropical cyclones whose track entered the Philippine area.
/// </summary>
/// <remarks>
/// <para>
/// <b>Peak intensity is reported per averaging period, never as one figure.</b> A naive
/// <c>MAX(wind_speed_knots)</c> across a storm's fixes would take the highest number any agency
/// published and present it as the storm's strength — silently preferring whichever agency uses
/// the shortest averaging interval. For Haiyan that would report 170 kt, JTWC's one-minute
/// value, while the regional specialised centre for this basin published 125 kt over ten
/// minutes. Both are correct; neither is the storm's strength.
/// </para>
/// <para>
/// So the response carries a peak per period, and the caller decides what to show. This is the
/// same refusal to collapse disagreement that <c>HazardEvent</c> applies to magnitude.
/// </para>
/// </remarks>
public static class SearchCyclones
{
    public sealed record Query : IQuery<IReadOnlyList<CycloneSummaryResponse>>
    {
        /// <summary>Restrict to one season. Null returns every season held.</summary>
        public int? Season { get; init; }

        /// <summary>
        /// Free-text match on the storm's name. Null or blank returns every storm.
        /// </summary>
        /// <remarks>
        /// Matched against the international name <em>and</em> the PAGASA local name, because a
        /// Philippine reader is at least as likely to search "Yolanda" as "Haiyan" — and for many
        /// storms the local name is the only one they have ever heard. Searching only the
        /// international name would make the archive unreachable by the name it is remembered by.
        /// </remarks>
        public string? Name { get; init; }

        /// <summary>
        /// Only storms that made landfall, as flagged by the source.
        /// </summary>
        public bool LandfallOnly { get; init; }

        /// <summary>
        /// How the list is ordered.
        /// </summary>
        /// <remarks>
        /// Defaults to intensity rather than recency, and the reason is a data-quality one. The
        /// most recent storms carry a single agency's provisional track — other agencies publish
        /// their reanalyses a season or more later — so a recency-ordered default puts the
        /// thinnest records first and buries every storm that has a multi-agency comparison or a
        /// PAGASA name. For a historical exploration platform that is the wrong end of the
        /// archive to open on.
        /// </remarks>
        public CycloneOrder Order { get; init; } = CycloneOrder.Intensity;

        public int Limit { get; init; } = 60;
    }

    /// <summary>How a cyclone list is ordered.</summary>
    public enum CycloneOrder
    {
        /// <summary>
        /// Most intense first, by lowest central pressure.
        /// </summary>
        /// <remarks>
        /// Pressure, not wind. Wind cannot order a list across agencies — a maximum taken over
        /// mixed averaging periods would rank whichever agency uses the shortest interval, so
        /// Haiyan would sort on JTWC's one-minute 170 kt while a storm analysed only by JMA
        /// sorted on a ten-minute figure. Minimum central pressure is the same quantity to every
        /// agency, which makes it the one intensity measure that can legitimately be compared
        /// across them.
        /// </remarks>
        Intensity = 0,

        /// <summary>Most recent first.</summary>
        Recent = 1,
    }

    /// <param name="Knots">Highest speed any agency using this period reported.</param>
    /// <param name="Period">The averaging interval, without which the figure is not comparable.</param>
    /// <param name="Agency">Which agency reported that peak.</param>
    public sealed record PeakIntensityResponse(double Knots, string Period, string Agency)
    {
        /// <summary>Speed in km/h, the unit PAGASA uses for public warnings.</summary>
        public double KilometresPerHour => Math.Round(Knots * 1.852d, 0);

        public string Display => $"{Knots:0} kt ({Period})";
    }

    /// <param name="Name">
    /// International name, or null for a storm that never earned one. Philippine storms also
    /// carry a PAGASA local name, which the upstream archive does not hold.
    /// </param>
    /// <param name="MinimumPressureMillibars">
    /// Lowest central pressure any agency reported. Unlike wind this needs no qualifier: it is
    /// the same quantity to every agency, and for Haiyan all four reported 895 mb.
    /// </param>
    /// <param name="Peaks">One entry per averaging period present, strongest first.</param>
    public sealed record CycloneSummaryResponse(
        Guid Id,
        string? Name,
        string? LocalName,
        int Season,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        int? MinimumPressureMillibars,
        int AgencyCount,
        int FixCount,
        bool MadeLandfall,
        IReadOnlyList<PeakIntensityResponse> Peaks);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Limit).InclusiveBetween(1, 200);

            // IBTrACS begins in 1884; anything earlier is a typo rather than a query.
            RuleFor(query => query.Season)
                .InclusiveBetween(1884, 2100)
                .When(query => query.Season.HasValue);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, IReadOnlyList<CycloneSummaryResponse>>
    {
        public async Task<Result<IReadOnlyList<CycloneSummaryResponse>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var storms = context.HazardEvents.AsNoTracking()
                .Where(hazardEvent => hazardEvent.Type == HazardEventType.TropicalCyclone);

            if (request.Season is { } season)
            {
                // Season is the calendar year of onset in this basin, so it is derived from the
                // canonical time rather than stored twice.
                storms = storms.Where(hazardEvent => hazardEvent.CanonicalOccurredAt.Year == season);
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                // Upper-cased on both sides rather than with `ILike`. `ILike` is PostgreSQL's own
                // case-insensitive operator and would read more naturally, but it is an Npgsql
                // extension and this project is not allowed to reference the provider — the
                // architecture tests enforce that `Calametra.Application` stays provider-agnostic.
                // `ToUpper().Contains(...)` translates to `upper(x) LIKE '%…%'`, so the comparison
                // still happens in the database rather than over materialised rows.
                var term = request.Name.Trim().ToUpperInvariant();

                // CA1862 asks for the `StringComparison` overload of `Contains`. That advice is
                // correct for in-memory string work and wrong here: EF Core cannot translate the
                // culture-aware overloads to SQL and throws at query time if given one. The
                // expression below is translated, not executed in .NET, so the upper-casing is what
                // performs the case-insensitive match — in the database.
#pragma warning disable CA1862
                storms = storms.Where(hazardEvent =>
                    (hazardEvent.Name != null && hazardEvent.Name.ToUpper().Contains(term))
                    || (hazardEvent.LocalName != null
                        && hazardEvent.LocalName.ToUpper().Contains(term)));
#pragma warning restore CA1862
            }

            // Ordering happens in SQL against the track points, because the sort key —
            // lowest central pressure — lives on the children rather than on the event.
            var ranked = request.Order == CycloneOrder.Recent
                ? storms.OrderByDescending(hazardEvent => hazardEvent.CanonicalOccurredAt)
                : storms.OrderBy(hazardEvent =>
                    context.CycloneTrackPoints
                        .Where(trackPoint => trackPoint.HazardEventId == hazardEvent.Id
                            && trackPoint.MinimumPressureMillibars != null)
                        .Min(trackPoint => trackPoint.MinimumPressureMillibars)
                    // Storms with no pressure reading sort last rather than first, which is
                    // where a null would otherwise land in ascending order.
                    ?? int.MaxValue);

            var candidates = await ranked
                .Take(request.Limit)
                .Select(hazardEvent => new
                {
                    hazardEvent.Id,
                    hazardEvent.Name,
                    hazardEvent.LocalName,
                    hazardEvent.CanonicalOccurredAt,
                })
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return Result<IReadOnlyList<CycloneSummaryResponse>>.Success([]);
            }

            var ids = candidates.ConvertAll(candidate => candidate.Id);

            // Fixes for the whole page in one query. Per-storm queries would be one round trip
            // per row, and the aggregation below cannot be expressed in SQL anyway: picking the
            // peak *within* each averaging period needs the period grouping, and the agency name
            // that produced it.
            var fixes = await context.CycloneTrackPoints.AsNoTracking()
                .Where(trackPoint => ids.Contains(trackPoint.HazardEventId))
                .Join(
                    context.DataSources.AsNoTracking(),
                    trackPoint => trackPoint.DataSourceId,
                    dataSource => dataSource.Id,
                    (trackPoint, dataSource) => new
                    {
                        trackPoint.HazardEventId,
                        trackPoint.CapturedAt,
                        trackPoint.WindSpeedKnots,
                        trackPoint.WindAveragingPeriod,
                        trackPoint.MinimumPressureMillibars,
                        trackPoint.IsLandfall,
                        trackPoint.DataSourceId,
                        dataSource.Agency,
                    })
                .ToListAsync(cancellationToken);

            var byStorm = fixes.GroupBy(entry => entry.HazardEventId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var results = new List<CycloneSummaryResponse>(candidates.Count);

            foreach (var candidate in candidates)
            {
                if (!byStorm.TryGetValue(candidate.Id, out var stormFixes))
                {
                    continue;
                }

                // One peak per averaging period. Grouping first is what prevents a one-minute
                // reading from being compared against a ten-minute one.
                var peaks = stormFixes
                    .Where(entry => entry.WindSpeedKnots is not null
                        && entry.WindAveragingPeriod != WindAveragingPeriod.Unknown)
                    .GroupBy(entry => entry.WindAveragingPeriod)
                    .Select(group =>
                    {
                        var strongest = group.MaxBy(entry => entry.WindSpeedKnots)!;

                        return new PeakIntensityResponse(
                            strongest.WindSpeedKnots!.Value,
                            group.Key.Label(),
                            strongest.Agency);
                    })
                    .OrderByDescending(peak => peak.Knots)
                    .ToList();

                var madeLandfall = stormFixes.Exists(entry => entry.IsLandfall);

                if (request.LandfallOnly && !madeLandfall)
                {
                    continue;
                }

                var pressures = stormFixes
                    .Where(entry => entry.MinimumPressureMillibars is not null)
                    .Select(entry => entry.MinimumPressureMillibars!.Value)
                    .ToList();

                results.Add(new CycloneSummaryResponse(
                    candidate.Id,
                    candidate.Name,
                    candidate.LocalName,
                    candidate.CanonicalOccurredAt.Year,
                    stormFixes.Min(entry => entry.CapturedAt),
                    stormFixes.Max(entry => entry.CapturedAt),
                    pressures.Count == 0 ? null : pressures.Min(),
                    stormFixes.Select(entry => entry.DataSourceId).Distinct().Count(),
                    stormFixes.Count,
                    madeLandfall,
                    peaks));
            }

            return Result<IReadOnlyList<CycloneSummaryResponse>>.Success(results);
        }
    }
}
