using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Seismology;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// The catalogue's own history, decade by decade.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one figure on this platform most likely to be misread, computed rather than asserted.</b>
/// Events per decade rise roughly 285-fold across the archive, from 21 in the 1900s to nearly 6,000
/// in the 2020s. Read naively that says the Philippines has become more seismic. It says nothing of
/// the kind: it is a record of how many seismometers were running, and the way to see that is to put
/// the total beside the magnitude-6 rate, which is flat at about five to seven per year for a
/// century.
/// </para>
/// <para>
/// So the response deliberately carries both series, and the completeness ratio between them. A
/// caller cannot render the rising bars without also holding the figure that explains them.
/// </para>
/// <para>
/// The other two columns are the same argument applied to fields rather than counts: the share of
/// depths the agency assigned rather than measured is far higher in the historical record — the
/// 33 km convention dominates before 1990 — and the dominant magnitude scale changes underneath the
/// reader, from surface-wave in the early record to body-wave and moment today. Each is a reason not
/// to compare a figure from one decade with a figure from another.
/// </para>
/// </remarks>
public static class GetCatalogueCompleteness
{
    public sealed record Query : IQuery<CompletenessResponse>;

    /// <param name="Decades">Coarsest first, so the record reads forward.</param>
    /// <param name="ComparableMagnitudeFloor">
    /// The magnitude above which decades may be compared with each other. Served rather than
    /// hard-coded in the client, because it is a property of the catalogue.
    /// </param>
    /// <param name="ObservedAt">
    /// When the figures were computed. A completeness statement without a date invites being quoted
    /// after the archive has grown underneath it.
    /// </param>
    public sealed record CompletenessResponse(
        IReadOnlyList<DecadeSummary> Decades,
        double ComparableMagnitudeFloor,
        int TotalCount,
        DateTimeOffset ObservedAt);

    /// <param name="Decade">First year of the decade, e.g. 1970.</param>
    /// <param name="TotalCount">Readings catalogued in the decade, at any magnitude.</param>
    /// <param name="ComparableCount">Readings at or above the comparable floor.</param>
    /// <param name="ComparablePerYear">
    /// The era-comparable rate. This is the series to read across decades; the total is not.
    /// </param>
    /// <param name="AssignedDepthCount">
    /// Readings whose depth the agency fixed to a default rather than resolving it.
    /// </param>
    /// <param name="AssignedDepthShare">The same figure as a proportion, for the reader.</param>
    /// <param name="DominantScale">
    /// The magnitude scale most readings in the decade were measured on, as the catalogue names it.
    /// </param>
    /// <param name="StrongestMagnitude">
    /// The largest magnitude value recorded in the decade, with its scale, or null for an empty
    /// decade. Deliberately not compared across decades by this slice: the scale changes.
    /// </param>
    public sealed record DecadeSummary(
        int Decade,
        int TotalCount,
        int ComparableCount,
        double ComparablePerYear,
        int AssignedDepthCount,
        double AssignedDepthShare,
        string? DominantScale,
        double? StrongestMagnitude,
        string? StrongestScale);

    internal sealed class Handler(IApplicationDbContext context, TimeProvider timeProvider)
        : IQueryHandler<Query, CompletenessResponse>
    {
        /// <summary>
        /// The floor above which decades may be compared.
        /// </summary>
        /// <remarks>
        /// Magnitude 6.0, and measured rather than chosen: across this archive the M6.0+ rate holds at
        /// roughly 6 per year in the 1920s, 1970s, 1990s and 2020s alike, while the total rises
        /// 285-fold. Large earthquakes were always detected; small ones only became detectable.
        /// </remarks>
        private const double ComparableFloor = 6d;

        public async Task<Result<CompletenessResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var rows =
                from hazardEvent in context.HazardEvents.AsNoTracking()
                join observation in context.EarthquakeObservations.AsNoTracking()
                    on hazardEvent.PreferredObservationId equals observation.Id
                where hazardEvent.Type == HazardEventType.Earthquake
                select new
                {
                    hazardEvent.CanonicalOccurredAt.Year,
                    observation.MagnitudeValue,
                    observation.MagnitudeScale,
                    observation.DepthQuality,
                };

            // Aggregated in the database, decade by decade. Integer division on the year is what makes
            // this a single grouped query rather than 27,000 rows pulled back to be bucketed here.
            var grouped = await rows
                .GroupBy(row => row.Year / 10 * 10)
                .Select(group => new
                {
                    Decade = group.Key,
                    TotalCount = group.Count(),
                    ComparableCount = group.Count(row => row.MagnitudeValue >= ComparableFloor),
                    AssignedDepthCount = group.Count(row =>
                        row.DepthQuality == DepthQuality.OperatorAssigned),
                    StrongestMagnitude = group.Max(row => row.MagnitudeValue),
                })
                .OrderBy(group => group.Decade)
                .ToListAsync(cancellationToken);

            // The dominant scale and the scale of the strongest reading need a second pass: both are
            // per-decade arg-max queries, which SQL will do but which read far worse than counting the
            // scales once. The set is a few hundred rows — ten decades by a dozen scales.
            var scaleCounts = await rows
                .GroupBy(row => new { Decade = row.Year / 10 * 10, row.MagnitudeScale })
                .Select(group => new
                {
                    group.Key.Decade,
                    group.Key.MagnitudeScale,
                    Count = group.Count(),
                    Strongest = group.Max(row => row.MagnitudeValue),
                })
                .ToListAsync(cancellationToken);

            var decades = grouped
                .Select(decade =>
                {
                    var scales = scaleCounts.Where(scale => scale.Decade == decade.Decade).ToArray();

                    var dominant = scales
                        .OrderByDescending(scale => scale.Count)
                        .FirstOrDefault();

                    var strongest = scales
                        .Where(scale => scale.Strongest == decade.StrongestMagnitude)
                        .FirstOrDefault();

                    return new DecadeSummary(
                        decade.Decade,
                        decade.TotalCount,
                        decade.ComparableCount,
                        Math.Round(decade.ComparableCount / 10d, 1),
                        decade.AssignedDepthCount,
                        decade.TotalCount == 0
                            ? 0d
                            : Math.Round((double)decade.AssignedDepthCount / decade.TotalCount, 3),
                        dominant?.MagnitudeScale.ToString(),
                        decade.StrongestMagnitude,
                        strongest?.MagnitudeScale.ToString());
                })
                .ToList();

            return Result<CompletenessResponse>.Success(new CompletenessResponse(
                decades,
                ComparableFloor,
                decades.Sum(decade => decade.TotalCount),
                timeProvider.GetUtcNow()));
        }
    }
}
