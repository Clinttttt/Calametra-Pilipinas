using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Presents the outstanding pairings so a reviewer can see what they would be deciding.
/// </summary>
/// <remarks>
/// <para>
/// <b>The queue exists because "1,729 proposals" is not reviewable information.</b> A reviewer asked to
/// approve that number has no basis for a decision; a reviewer shown that 1,728 of them are the PSA's own
/// published correspondence and one is a digit re-slicing whose names disagree has two decisions to make,
/// one of which is a policy and the other of which is a judgement about a specific place.
/// </para>
/// <para>
/// So the queue groups by evidence class and reports each class's size, and then lists individually
/// everything a class decision must not cover — the re-slicings whose names disagree, which ADR-005 D4
/// singles out because it is the rule that fails in the capital.
/// </para>
/// <para>
/// A query, not a command. Reads proposals and decides nothing.
/// </para>
/// </remarks>
public static class GetLguReviewQueue
{
    public sealed record Query : IQuery<ReviewQueue>;

    /// <param name="Evidence">The class the matcher assigned.</param>
    /// <param name="Count">How many proposals carry it.</param>
    /// <param name="BatchConfirmable">
    /// Whether one attributed decision may settle the whole class. False for manual review, which needs a
    /// written reason per row, and false where names disagree.
    /// </param>
    /// <param name="WhyItCanBeBatched">Stated in the report, so the reviewer judges the basis rather than trusting the flag.</param>
    public sealed record EvidenceClass(
        string Evidence,
        int Count,
        bool BatchConfirmable,
        string WhyItCanBeBatched);

    /// <param name="CanonicalCode">The ten-digit code of the unit.</param>
    /// <param name="HistoricalCode">The nine-digit code proposed for it.</param>
    /// <param name="UnitName">What the register calls it.</param>
    /// <param name="DirectoryName">What the gazetteer calls it, where a row was matched.</param>
    /// <param name="Basis">What the matcher recorded as its reason.</param>
    public sealed record IndividualReview(
        Guid LinkId,
        string CanonicalCode,
        string HistoricalCode,
        string UnitName,
        string? DirectoryName,
        string Evidence,
        bool NamesAgree,
        string Basis);

    /// <param name="UnitsWithoutProposal">
    /// Units the matcher could not pair at all. These need an accepted exception, not a review.
    /// </param>
    /// <param name="DirectoryRowsWithoutProposal">Gazetteer rows the matcher could not pair.</param>
    public sealed record ReviewQueue(
        bool AnyCurrentEdition,
        string? EditionLabel,
        bool EditionMayCertify,
        Guid? EditionId,
        int Proposed,
        int Confirmed,
        int Rejected,
        IReadOnlyList<EvidenceClass> Classes,
        IReadOnlyList<IndividualReview> RequiringIndividualReview,
        IReadOnlyList<UnpairedUnit> UnitsWithoutProposal,
        IReadOnlyList<UnpairedDirectoryRow> DirectoryRowsWithoutProposal,
        int UnitsAlreadyExcepted,
        int DirectoryRowsAlreadyExcepted);

    public sealed record UnpairedUnit(Guid LguId, string CanonicalCode, string Name, string Level);

    public sealed record UnpairedDirectoryRow(Guid PlaceId, string PsgcCode, string Name, string Kind);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IApplicationDbContext analytics) : IQueryHandler<Query, ReviewQueue>
    {
        public async Task<Result<ReviewQueue>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<ReviewQueue>.Success(new ReviewQueue(
                    false, null, false, null, 0, 0, 0, [], [], [], [], 0, 0));
            }

            var units = await review.Lgus
                .Where(lgu => lgu.RegisterEditionId == edition.Id)
                .Select(lgu => new
                {
                    lgu.Id,
                    lgu.CanonicalPsgcCode,
                    lgu.Name,
                    lgu.Level,
                })
                .ToListAsync(cancellationToken);

            var unitById = units.ToDictionary(unit => unit.Id);

            var links = await review.LguCodeLinks
                .Where(link => link.Status != LguLinkStatus.Superseded)
                .Select(link => new
                {
                    link.Id,
                    link.LguId,
                    link.HistoricalPsgcCode,
                    link.PlaceId,
                    link.Status,
                    link.Evidence,
                    link.NamesAgree,
                    link.ProposalBasis,
                })
                .ToListAsync(cancellationToken);

            // Scoped to the active edition's units, the same way the readiness report is: a live link
            // against a unit the current publication no longer holds is not this edition's work.
            var live = links.Where(link => unitById.ContainsKey(link.LguId)).ToList();

            var proposals = live.Where(link => link.Status == LguLinkStatus.Proposed).ToList();

            var directoryRows = await analytics.Places
                .Where(place => place.PsgcCode != null && place.Kind != PlaceKind.Barangay)
                .Select(place => new
                {
                    place.Id,
                    place.PsgcCode,
                    place.Name,
                    place.Kind,
                })
                .ToListAsync(cancellationToken);

            var exceptions = await review.LguCrosswalkExceptions
                .Where(item => item.RegisterEditionId == edition.Id)
                .Select(item => new { item.Kind, item.LguId, item.PlaceId })
                .ToListAsync(cancellationToken);

            var exceptedUnitIds = exceptions
                .Where(item => item.Kind == CrosswalkExceptionKind.RegisterUnitHasNoHistoricalCode
                    && item.LguId is not null)
                .Select(item => item.LguId!.Value)
                .ToHashSet();

            var exceptedPlaceIds = exceptions
                .Where(item => item.Kind == CrosswalkExceptionKind.DirectoryRowHasNoRegisterUnit
                    && item.PlaceId is not null)
                .Select(item => item.PlaceId!.Value)
                .ToHashSet();

            var pairedUnitIds = live.Select(link => link.LguId).ToHashSet();
            var pairedPlaceIds = live
                .Where(link => link.PlaceId is not null)
                .Select(link => link.PlaceId!.Value)
                .ToHashSet();

            var classes = proposals
                .GroupBy(link => link.Evidence)
                .Select(group =>
                {
                    var evidence = group.Key;
                    var allNamesAgree = group.All(link => link.NamesAgree);

                    return new EvidenceClass(
                        evidence.ToString(),
                        group.Count(),
                        BatchConfirmable(evidence, allNamesAgree),
                        WhyItCanBeBatched(evidence, allNamesAgree));
                })
                .OrderBy(item => item.Evidence, StringComparer.Ordinal)
                .ToList();

            var directoryNameByPlaceId = directoryRows.ToDictionary(row => row.Id, row => row.Name);

            // Anything a class decision must not settle, listed one by one with both names visible. This
            // is the part a person actually has to read.
            var individual = proposals
                .Where(link => !CanBeSettledByClass(link.Evidence, link.NamesAgree))
                .Select(link => new IndividualReview(
                    link.Id,
                    unitById[link.LguId].CanonicalPsgcCode,
                    link.HistoricalPsgcCode,
                    unitById[link.LguId].Name,
                    link.PlaceId is null ? null : directoryNameByPlaceId.GetValueOrDefault(link.PlaceId.Value),
                    link.Evidence.ToString(),
                    link.NamesAgree,
                    link.ProposalBasis ?? "not recorded"))
                .OrderBy(item => item.CanonicalCode, StringComparer.Ordinal)
                .ToList();

            var unpairedUnits = units
                .Where(unit => !pairedUnitIds.Contains(unit.Id) && !exceptedUnitIds.Contains(unit.Id))
                .Select(unit => new UnpairedUnit(
                    unit.Id,
                    unit.CanonicalPsgcCode,
                    unit.Name,
                    unit.Level.ToString()))
                .OrderBy(unit => unit.CanonicalCode, StringComparer.Ordinal)
                .ToList();

            var unpairedRows = directoryRows
                .Where(row => !pairedPlaceIds.Contains(row.Id) && !exceptedPlaceIds.Contains(row.Id))
                .Select(row => new UnpairedDirectoryRow(
                    row.Id,
                    row.PsgcCode!,
                    row.Name,
                    row.Kind.ToString()))
                .OrderBy(row => row.PsgcCode, StringComparer.Ordinal)
                .ToList();

            return Result<ReviewQueue>.Success(new ReviewQueue(
                true,
                edition.Label,
                edition.IsCitableAsAuthority,
                edition.Id,
                proposals.Count,
                live.Count(link => link.Status == LguLinkStatus.Confirmed),
                live.Count(link => link.Status == LguLinkStatus.Rejected),
                classes,
                individual,
                unpairedUnits,
                unpairedRows,
                exceptedUnitIds.Count,
                exceptedPlaceIds.Count));
        }

        /// <summary>
        /// Whether one attributed decision may settle a whole evidence class.
        /// </summary>
        /// <remarks>
        /// ADR-005 D4 permits "a named, dated batch that records which register edition and which sources
        /// were consulted" and forbids "accept all proposals above threshold N". The difference is whether
        /// a person is adjudicating a stated kind of evidence or delegating the decision to a score.
        /// <c>RegisterMatch</c> is the PSA publishing both codes for one unit, so the batch decision is
        /// "the register's own correspondence is sufficient" — reviewable, and wrong or right as a whole.
        /// <c>ManualReview</c> can never be batched: it requires a written reason per row, which is by
        /// definition per row.
        /// </remarks>
        private static bool BatchConfirmable(LguLinkEvidence evidence, bool allNamesAgree) =>
            evidence switch
            {
                LguLinkEvidence.RegisterMatch => true,
                LguLinkEvidence.DigitReslice => allNamesAgree,
                _ => false,
            };

        private static bool CanBeSettledByClass(LguLinkEvidence evidence, bool namesAgree) =>
            evidence switch
            {
                LguLinkEvidence.RegisterMatch => true,
                LguLinkEvidence.DigitReslice => namesAgree,
                _ => false,
            };

        private static string WhyItCanBeBatched(LguLinkEvidence evidence, bool allNamesAgree) =>
            evidence switch
            {
                LguLinkEvidence.RegisterMatch =>
                    "The PSA publication states both codes for the same unit. Confirming the class is one "
                    + "judgement — that the register's own correspondence is sufficient evidence — and it "
                    + "is recorded against the edition and the sources consulted.",
                LguLinkEvidence.DigitReslice when allNamesAgree =>
                    "Every row re-slices AND the register's own name agrees, which is the only form D4 "
                    + "permits this evidence to be confirmed in.",
                LguLinkEvidence.DigitReslice =>
                    "Some rows re-slice but the names disagree. D4 refuses this evidence there, so these "
                    + "go to individual review and the class cannot be settled at once.",
                LguLinkEvidence.ManualReview =>
                    "Manual review requires a written reason per row, so it is per row by definition.",
                _ =>
                    "The evidence is not stated, so there is nothing to adjudicate as a class.",
            };
    }
}
