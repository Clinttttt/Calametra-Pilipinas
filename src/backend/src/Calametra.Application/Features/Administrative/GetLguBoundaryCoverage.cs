using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Measures how much of the country actually has an outline attached, and to what.
/// </summary>
/// <remarks>
/// <para>
/// <b>The question is not "how many polygons did we store".</b> It is whether every city and municipality
/// the active register holds has an outline attached to <em>it</em> — which is a different number, because
/// a polygon can be stored against the wrong unit, stored twice, or stored for a unit the register no
/// longer contains. So coverage is counted from the register outwards, unit by unit, rather than from the
/// geometry table inwards.
/// </para>
/// <para>
/// Reported per level, because a national percentage hides the shape of the gap. 96% coverage that is
/// complete in Luzon and absent in Tawi-Tawi is a different fact from 96% scattered evenly, and only the
/// first would make the interface confidently wrong about a specific region.
/// </para>
/// <para>
/// Repaired outlines are counted separately and never folded into "stored". A boundary that needed a
/// zero-width buffer to become valid is usable and is also a place where this platform guessed, and the
/// count is the honest way to keep that visible.
/// </para>
/// </remarks>
public static class GetLguBoundaryCoverage
{
    public sealed record Query : IQuery<BoundaryCoverageReport>;

    /// <param name="Level">City or Municipality.</param>
    /// <param name="UnitsInRegister">The population that ought to have an outline.</param>
    /// <param name="UnitsWithBoundary">Units carrying an outline in force.</param>
    /// <param name="Repaired">Of those, how many needed repair to be valid.</param>
    public sealed record LevelCoverage(
        string Level,
        int UnitsInRegister,
        int UnitsWithBoundary,
        int Repaired)
    {
        public double CoverageRatio => UnitsInRegister == 0
            ? 0d
            : (double)UnitsWithBoundary / UnitsInRegister;
    }

    public sealed record MissingUnit(string CanonicalCode, string Name, string Level, bool IsExcepted);

    /// <param name="TotalAreaSquareKm">
    /// Summed area of the outlines in force. A sanity figure rather than a claim about the country: the
    /// Philippine land area is about 300,000 km², so a total far from that says the geometry is wrong in a
    /// way no count of rows would reveal.
    /// </param>
    /// <param name="SmallestAreaSquareKm">
    /// The smallest outline stored. A near-zero minimum is the signature of a collapsed ring that passed
    /// validity but encloses nothing meaningful.
    /// </param>
    public sealed record BoundaryCoverageReport(
        bool AnyBoundariesHeld,
        string? SourceSlug,
        string? Attribution,
        DateTimeOffset? ExtractedAt,
        string? ExtractVersion,
        int BoundariesInForce,
        int BoundariesSuperseded,
        int BoundariesRepaired,
        IReadOnlyList<LevelCoverage> Levels,
        IReadOnlyList<MissingUnit> MissingUnits,
        IReadOnlyList<string> UnitsWithMultipleVersions,
        double TotalAreaSquareKm,
        double SmallestAreaSquareKm,
        double LargestAreaSquareKm,
        string? LargestUnitName,
        bool SufficientForInteraction,
        string SufficiencyVerdict);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IApplicationDbContext analytics) : IQueryHandler<Query, BoundaryCoverageReport>
    {
        /// <summary>
        /// Coverage below which the map interaction should not be built.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 98% of cities and municipalities. Not 100%, because ADR-005 D5 already establishes that an
        /// unpaired unit is valid rather than broken and the same reasoning applies to geometry: OSM is a
        /// volunteer map and a handful of municipalities will be unmapped or mapped without a code at any
        /// given moment.
        /// </para>
        /// <para>
        /// Not lower either, and the reason is what the interaction promises. Click-the-land identification
        /// implies that clicking land yields a municipality; every gap is a place where the map silently
        /// answers nothing, and a reader cannot tell that from having clicked the sea. Two percent is about
        /// thirty municipalities — small enough to enumerate in a caveat, which is the test of whether a
        /// gap is acceptable.
        /// </para>
        /// </remarks>
        private const double SufficientCoverageRatio = 0.98;

        public async Task<Result<BoundaryCoverageReport>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<BoundaryCoverageReport>.Success(new BoundaryCoverageReport(
                    false, null, null, null, null, 0, 0, 0, [], [], [], 0d, 0d, 0d, null, false,
                    "No register edition is held, so there is no population to measure coverage against."));
            }

            var units = await review.Lgus
                .Where(lgu => lgu.RegisterEditionId == edition.Id
                    && (lgu.Level == LguLevel.City || lgu.Level == LguLevel.Municipality))
                .Select(lgu => new { lgu.Id, lgu.CanonicalPsgcCode, lgu.Name, lgu.Level })
                .ToListAsync(cancellationToken);

            var boundaries = await review.LguBoundaries
                .Select(boundary => new
                {
                    boundary.LguId,
                    boundary.ValidTo,
                    boundary.WasRepaired,
                    boundary.AreaSquareKm,
                    boundary.CanonicalPsgcCode,
                    boundary.OsmName,
                    boundary.ExtractedAt,
                    boundary.ExtractVersion,
                    boundary.SourceId,
                })
                .ToListAsync(cancellationToken);

            var inForce = boundaries.Where(boundary => boundary.ValidTo == null).ToList();
            var superseded = boundaries.Count - inForce.Count;

            var inForceByLgu = inForce
                .GroupBy(boundary => boundary.LguId)
                .ToDictionary(group => group.Key, group => group.First());

            var exceptedUnitIds = await review.LguCrosswalkExceptions
                .Where(item => item.RegisterEditionId == edition.Id && item.LguId != null)
                .Select(item => item.LguId!.Value)
                .ToListAsync(cancellationToken);

            var excepted = exceptedUnitIds.ToHashSet();

            var levels = new List<LevelCoverage>();

            foreach (var level in new[] { LguLevel.City, LguLevel.Municipality })
            {
                var inLevel = units.FindAll(unit => unit.Level == level);
                var covered = inLevel.FindAll(unit => inForceByLgu.ContainsKey(unit.Id));

                levels.Add(new LevelCoverage(
                    level.ToString(),
                    inLevel.Count,
                    covered.Count,
                    covered.Count(unit => inForceByLgu[unit.Id].WasRepaired)));
            }

            var missing = units
                .Where(unit => !inForceByLgu.ContainsKey(unit.Id))
                .Select(unit => new MissingUnit(
                    unit.CanonicalPsgcCode,
                    unit.Name,
                    unit.Level.ToString(),
                    excepted.Contains(unit.Id)))
                .OrderBy(unit => unit.CanonicalCode, StringComparer.Ordinal)
                .ToList();

            // More than one row in force for one unit would mean the partial unique index is missing or a
            // write bypassed it. Checked rather than assumed, because the whole version model depends on
            // exactly one outline being current.
            var duplicated = inForce
                .GroupBy(boundary => boundary.LguId)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.First().CanonicalPsgcCode}: {group.Count()} rows in force")
                .ToList();

            var totalArea = inForce.Sum(boundary => boundary.AreaSquareKm);
            var smallest = inForce.Count == 0 ? 0d : inForce.Min(boundary => boundary.AreaSquareKm);
            var largest = inForce.Count == 0 ? 0d : inForce.Max(boundary => boundary.AreaSquareKm);
            var largestUnit = inForce.Count == 0
                ? null
                : inForce.OrderByDescending(boundary => boundary.AreaSquareKm).First().OsmName;

            var localGovernmentUnits = levels.Sum(level => level.UnitsInRegister);
            var localGovernmentCovered = levels.Sum(level => level.UnitsWithBoundary);
            var ratio = localGovernmentUnits == 0
                ? 0d
                : (double)localGovernmentCovered / localGovernmentUnits;

            var sufficient = ratio >= SufficientCoverageRatio && duplicated.Count == 0;

            var verdict = localGovernmentUnits == 0
                ? "The register holds no cities or municipalities."
                : duplicated.Count > 0
                    ? $"{duplicated.Count} unit(s) hold more than one outline in force. The version model "
                        + "requires exactly one, so this must be resolved before the interaction is built."
                    : sufficient
                        ? $"{localGovernmentCovered} of {localGovernmentUnits} cities and municipalities "
                            + $"carry an outline ({ratio:P1}). Sufficient: the remaining gap is small "
                            + "enough to name in a caveat, which is the test of whether a gap is "
                            + "acceptable in a click-the-land interface."
                        : $"{localGovernmentCovered} of {localGovernmentUnits} cities and municipalities "
                            + $"carry an outline ({ratio:P1}), short of the {SufficientCoverageRatio:P0} "
                            + "this platform requires. Every gap is a place where clicking land would "
                            + "silently answer nothing, and a reader cannot tell that from having clicked "
                            + "the sea.";

            var sourceId = inForce.Count == 0 ? (Guid?)null : inForce[0].SourceId;

            var source = sourceId is null
                ? null
                : await analytics.DataSources
                    .FirstOrDefaultAsync(candidate => candidate.Id == sourceId, cancellationToken);

            return Result<BoundaryCoverageReport>.Success(new BoundaryCoverageReport(
                inForce.Count > 0,
                source?.Slug,
                source?.Attribution,
                inForce.Count == 0 ? null : inForce.Max(boundary => boundary.ExtractedAt),
                inForce.Count == 0 ? null : inForce[0].ExtractVersion,
                inForce.Count,
                superseded,
                inForce.Count(boundary => boundary.WasRepaired),
                levels,
                missing,
                duplicated,
                totalArea,
                smallest,
                largest,
                largestUnit,
                sufficient,
                verdict));
        }
    }
}
