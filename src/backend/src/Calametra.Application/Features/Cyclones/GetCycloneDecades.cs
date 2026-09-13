using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Cyclones;

/// <summary>
/// Storm counts by decade, for comparing one era with another.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same completeness problem as the earthquake catalogue, with a different shape.</b> IBTrACS
/// reaches back to 1884 and this archive imports from 1945, but coverage is not uniform across that
/// span: the satellite era begins in the 1960s, and JTWC documents its own western North Pacific
/// best-track corrections only for 1950-2000, rating 1985-2000 as high quality. So a rise in storms
/// per decade is partly a record of what could be seen — and unlike the earthquake catalogue there is
/// no equivalent of the magnitude-6 floor to fall back on, because a storm's intensity is itself the
/// figure whose early values are least dependable.
/// </para>
/// <para>
/// The response therefore carries the landfalling count beside the total. That is the closest thing to
/// an era-comparable series available here: a storm that crossed the coast was recorded by the people
/// it crossed, whether or not a satellite saw its eye. It is offered as the better of two imperfect
/// series rather than as a solved problem, and the caveat travels with it.
/// </para>
/// </remarks>
public static class GetCycloneDecades
{
    public sealed record Query : IQuery<CycloneDecadesResponse>;

    /// <param name="Decades">Coarsest first.</param>
    /// <param name="ReliableFromSeason">
    /// The season from which intensity figures may be read with reasonable confidence, per JTWC's own
    /// assessment of its best-track record. Served rather than assumed by the client.
    /// </param>
    public sealed record CycloneDecadesResponse(
        IReadOnlyList<CycloneDecade> Decades,
        int ReliableFromSeason,
        int TotalCount);

    /// <param name="Decade">First year of the decade.</param>
    /// <param name="StormCount">Storms whose track this archive holds for the decade.</param>
    /// <param name="LandfallCount">
    /// Storms recorded as crossing land. The more era-comparable of the two counts.
    /// </param>
    /// <param name="NamedInPhilippinesCount">
    /// Storms carrying a PAGASA local name in this archive. A property of the platform's crosswalk
    /// rather than of the storms, and reported so a reader does not read it as a landfall figure.
    /// </param>
    /// <param name="StrongestKnots">
    /// Highest wind speed recorded in the decade, across every contributing agency and averaging
    /// period. Deliberately not compared with another decade's by this slice: the averaging periods
    /// differ between agencies, so the maximum can change publisher between decades.
    /// </param>
    public sealed record CycloneDecade(
        int Decade,
        int StormCount,
        int LandfallCount,
        int NamedInPhilippinesCount,
        double? StrongestKnots);

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, CycloneDecadesResponse>
    {
        /// <summary>
        /// The season from which JTWC rates its own western North Pacific best-track record as high
        /// quality. Earlier intensities exist and are weaker claims.
        /// </summary>
        private const int ReliableFromSeason = 1985;

        public async Task<Result<CycloneDecadesResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var storms =
                from hazardEvent in context.HazardEvents.AsNoTracking()
                where hazardEvent.Type == HazardEventType.TropicalCyclone
                select new
                {
                    hazardEvent.Id,
                    hazardEvent.CanonicalOccurredAt.Year,
                    hazardEvent.LocalName,
                };

            var landfalls = context.CycloneTrackPoints.AsNoTracking()
                .Where(point => point.IsLandfall)
                .Select(point => point.HazardEventId)
                .Distinct();

            var peaks = context.CycloneTrackPoints.AsNoTracking()
                .GroupBy(point => point.HazardEventId)
                .Select(group => new
                {
                    HazardEventId = group.Key,
                    Peak = group.Max(point => point.WindSpeedKnots),
                });

            var rows = await storms
                .GroupJoin(peaks, storm => storm.Id, peak => peak.HazardEventId, (storm, peak) => new
                {
                    storm.Year,
                    storm.Id,
                    storm.LocalName,
                    Peak = peak.Select(candidate => candidate.Peak).FirstOrDefault(),
                })
                .GroupBy(row => row.Year / 10 * 10)
                .Select(group => new
                {
                    Decade = group.Key,
                    StormCount = group.Count(),
                    LandfallCount = group.Count(row => landfalls.Contains(row.Id)),
                    NamedCount = group.Count(row => row.LocalName != null),
                    StrongestKnots = group.Max(row => row.Peak),
                })
                .OrderBy(group => group.Decade)
                .ToListAsync(cancellationToken);

            var decades = rows
                .Select(row => new CycloneDecade(
                    row.Decade,
                    row.StormCount,
                    row.LandfallCount,
                    row.NamedCount,
                    row.StrongestKnots))
                .ToList();

            return Result<CycloneDecadesResponse>.Success(new CycloneDecadesResponse(
                decades,
                ReliableFromSeason,
                decades.Sum(decade => decade.StormCount)));
        }
    }
}
