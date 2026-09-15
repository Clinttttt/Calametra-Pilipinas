using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Confirms one evidence class as a named, dated batch. The form ADR-005 D4 permits.
/// </summary>
/// <remarks>
/// <para>
/// <b>D4 draws its line between adjudicating evidence and delegating to a score,</b> not between one row
/// and many: "A reviewer confirms rows individually <em>or in a named, dated batch that records which
/// register edition and which sources were consulted</em>. 'Accept all proposals above threshold N' is not
/// review; it is the algorithm establishing the crosswalk with a person's name on it."
/// </para>
/// <para>
/// So this command carries no threshold, no score and no similarity. It names one evidence class, and the
/// reviewer's decision is a single proposition they can be held to: <em>the PSA's own published
/// correspondence between the two editions is sufficient to establish a pairing.</em> That is either right
/// or wrong for the whole class, which is exactly why it can be decided once — and why the sources
/// consulted are recorded with it.
/// </para>
/// <para>
/// Four guards keep it a decision rather than a bulk update:
/// </para>
/// <list type="number">
/// <item>
/// The reviewer states how many rows they expect to settle. If the population has changed since they
/// looked at the queue, the batch aborts rather than confirming rows they never saw.
/// </item>
/// <item>
/// <c>ManualReview</c> is refused. It requires a written reason per row, so it is per row by definition.
/// </item>
/// <item>
/// Rows are confirmed one at a time through <c>LguCodeLink.Confirm</c>, so every domain invariant applies
/// individually — including the refusal of a re-slicing whose names disagree. The batch cannot widen what
/// a single confirmation is allowed to do.
/// </item>
/// <item>
/// The sources consulted are required text, stored on every row confirmed. A batch with nothing to say
/// about what was checked is not reviewable after the fact.
/// </item>
/// </list>
/// </remarks>
public static class ConfirmLguCodeLinkClass
{
    /// <param name="Evidence">The single class being adjudicated.</param>
    /// <param name="ReviewedBy">The person accountable. Recorded verbatim on every row.</param>
    /// <param name="SourcesConsulted">
    /// What was checked, in the reviewer's words. D4 requires a batch to record this; it is stored as each
    /// row's reason so the justification travels with the data rather than living in a log.
    /// </param>
    /// <param name="ExpectedCount">
    /// How many rows the reviewer believes they are settling. A mismatch aborts the batch untouched.
    /// </param>
    public sealed record Command(
        LguLinkEvidence Evidence,
        string ReviewedBy,
        string SourcesConsulted,
        int ExpectedCount) : ICommand<ClassConfirmationSummary>;

    /// <param name="Confirmed">Rows the domain accepted.</param>
    /// <param name="Refused">
    /// Rows the domain refused, with their reasons. Not an error: a class decision meeting a row the
    /// invariants forbid is the guard working, and the row stays proposed for individual review.
    /// </param>
    public sealed record ClassConfirmationSummary(
        string Evidence,
        string EditionLabel,
        string ReviewedBy,
        DateTimeOffset ReviewedAt,
        int Confirmed,
        int Refused,
        IReadOnlyList<string> RefusalReasons);

    internal sealed class Handler(
        ILguCrosswalkReviewContext context,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, ClassConfirmationSummary>
    {
        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "lgu_review.no_current_edition",
            "No current register edition is held, so there is nothing to confirm against.");

        private static readonly Error ManualReviewNotBatchable = new(
            ErrorType.Validation,
            "lgu_review.manual_review_not_batchable",
            "Manual review cannot be confirmed as a class. It requires a written reason per row, which is "
            + "per row by definition. Confirm these individually.");

        private static readonly Error EvidenceRequired = new(
            ErrorType.Validation,
            "lgu_review.evidence_required",
            "A class confirmation must name the evidence class it adjudicates.");

        private static readonly Error SourcesRequired = new(
            ErrorType.Validation,
            "lgu_review.sources_required",
            "ADR-005 D4 requires a batch to record which sources were consulted. A batch that states "
            + "nothing about what was checked cannot be reviewed after the fact.");

        public async Task<Result<ClassConfirmationSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Evidence == LguLinkEvidence.Unknown)
            {
                return Result<ClassConfirmationSummary>.Failure(EvidenceRequired);
            }

            if (request.Evidence == LguLinkEvidence.ManualReview)
            {
                return Result<ClassConfirmationSummary>.Failure(ManualReviewNotBatchable);
            }

            if (string.IsNullOrWhiteSpace(request.SourcesConsulted))
            {
                return Result<ClassConfirmationSummary>.Failure(SourcesRequired);
            }

            if (string.IsNullOrWhiteSpace(request.ReviewedBy))
            {
                return Result<ClassConfirmationSummary>.Failure(LguCodeLinkErrors.ReviewerRequired);
            }

            var edition = await context.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<ClassConfirmationSummary>.Failure(NoCurrentEdition);
            }

            // The domain refuses a non-PSA edition per row anyway. Checked here too so a reviewer pointed
            // at a mirror is told once, rather than receiving 1,700 identical refusals.
            if (!edition.IsCitableAsAuthority)
            {
                return Result<ClassConfirmationSummary>.Failure(LguCodeLinkErrors.EditionNotCitable);
            }

            var editionUnitIds = await context.Lgus
                .Where(lgu => lgu.RegisterEditionId == edition.Id)
                .Select(lgu => lgu.Id)
                .ToListAsync(cancellationToken);

            var scope = editionUnitIds.ToHashSet();

            var candidates = (await context.LguCodeLinks
                    .Where(link => link.Status == LguLinkStatus.Proposed
                        && link.Evidence == request.Evidence
                        && link.ProposedAgainstEditionId == edition.Id)
                    .ToListAsync(cancellationToken))
                .Where(link => scope.Contains(link.LguId))
                .ToList();

            // The reviewer's own count, checked before anything is written. The queue they read and the
            // rows they are settling must be the same population, or the batch is confirming work they
            // never saw.
            if (candidates.Count != request.ExpectedCount)
            {
                return Result<ClassConfirmationSummary>.Failure(new Error(
                    ErrorType.Conflict,
                    "lgu_review.count_mismatch",
                    $"The batch states {request.ExpectedCount} rows but {candidates.Count} are proposed on "
                    + $"this evidence against the current edition. Nothing was confirmed. Re-read the "
                    + "review queue: the population changed after it was presented."));
            }

            var now = timeProvider.GetUtcNow();
            var confirmed = 0;
            var refusals = new List<string>();

            foreach (var link in candidates)
            {
                // Per row, through the same entry point a single confirmation uses. A batch may not relax
                // an invariant — a re-slicing whose names disagree is refused here exactly as it would be
                // refused one at a time.
                var result = link.Confirm(
                    request.Evidence,
                    request.ReviewedBy,
                    request.SourcesConsulted,
                    edition,
                    now);

                if (result.IsFailure)
                {
                    refusals.Add($"{link.HistoricalPsgcCode}: {result.Error!.Code}");

                    continue;
                }

                confirmed++;
            }

            await context.SaveChangesAsync(cancellationToken);

            var evidence = request.Evidence.ToString();

            ReviewLog.ClassConfirmed(
                logger,
                evidence,
                confirmed,
                refusals.Count,
                request.ReviewedBy,
                edition.Label);

            return Result<ClassConfirmationSummary>.Success(new ClassConfirmationSummary(
                evidence,
                edition.Label,
                request.ReviewedBy,
                now,
                confirmed,
                refusals.Count,
                refusals));
        }
    }
}
