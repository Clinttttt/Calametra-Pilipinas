using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Confirms every correspondence whose evidence the register itself supplied, as a named dated batch.
/// </summary>
/// <remarks>
/// <para>
/// The form ADR-005 D4 permits: "a named, dated batch that records which register edition and which sources
/// were consulted". The proposition a reviewer is held to here is one sentence — <em>where both editions
/// publish the same nine-digit code for a unit, a confirmed pairing on that code identifies it</em> — and it
/// is right or wrong for the whole class.
/// </para>
/// <para>
/// It carries no threshold and no similarity. Rows the bridges disagreed about, and rows whose two editions
/// name the unit differently, are refused by the domain and stay proposed for individual review. The batch
/// cannot widen what a single confirmation may do, which is why every row goes through
/// <c>LguEditionCorrespondence.Confirm</c> rather than being updated in bulk.
/// </para>
/// </remarks>
public static class ConfirmLguEditionCorrespondenceClass
{
    /// <param name="ExpectedCount">
    /// How many rows the reviewer believes they are settling. A mismatch aborts untouched, so a population
    /// that changed after the queue was read cannot be confirmed behind their back.
    /// </param>
    public sealed record Command(
        string ReviewedBy,
        string SourcesConsulted,
        int ExpectedCount) : ICommand<CorrespondenceConfirmationSummary>;

    public sealed record CorrespondenceConfirmationSummary(
        string EditionLabel,
        string ReviewedBy,
        DateTimeOffset ReviewedAt,
        int Confirmed,
        int Refused,
        IReadOnlyList<string> RefusalReasons);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, CorrespondenceConfirmationSummary>
    {
        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "edition_correspondence.no_current_edition",
            "No current register edition is held, so there is nothing to confirm against.");

        private static readonly Error SourcesRequired = new(
            ErrorType.Validation,
            "edition_correspondence.sources_required",
            "ADR-005 D4 requires a batch to record which sources were consulted.");

        public async Task<Result<CorrespondenceConfirmationSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.ReviewedBy))
            {
                return Result<CorrespondenceConfirmationSummary>.Failure(
                    LguEditionCorrespondenceErrors.ReviewerRequired);
            }

            if (string.IsNullOrWhiteSpace(request.SourcesConsulted))
            {
                return Result<CorrespondenceConfirmationSummary>.Failure(SourcesRequired);
            }

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<CorrespondenceConfirmationSummary>.Failure(NoCurrentEdition);
            }

            if (!edition.IsCitableAsAuthority)
            {
                return Result<CorrespondenceConfirmationSummary>.Failure(
                    LguCodeLinkErrors.EditionNotCitable);
            }

            // The class is defined by what the evidence supports, not by a score: proposals with a register
            // bridge whose names agree. Everything else is individual work by construction.
            var candidates = await review.LguEditionCorrespondences
                .Where(item => item.Status == LguLinkStatus.Proposed
                    && item.ProposedAgainstEditionId == edition.Id
                    && item.NamesAgree)
                .ToListAsync(cancellationToken);

            if (candidates.Count != request.ExpectedCount)
            {
                return Result<CorrespondenceConfirmationSummary>.Failure(new Error(
                    ErrorType.Conflict,
                    "edition_correspondence.count_mismatch",
                    $"The batch states {request.ExpectedCount} rows but {candidates.Count} are proposed with "
                    + "agreeing names against the current edition. Nothing was confirmed."));
            }

            var now = timeProvider.GetUtcNow();
            var confirmed = 0;
            var refusals = new List<string>();

            foreach (var correspondence in candidates)
            {
                var result = correspondence.Confirm(
                    LguLinkEvidence.EditionCorrespondence,
                    request.ReviewedBy,
                    request.SourcesConsulted,
                    edition,
                    now);

                if (result.IsFailure)
                {
                    refusals.Add(
                        $"{correspondence.LegacyCanonicalPsgcCode}: {result.Error!.Code}");

                    continue;
                }

                confirmed++;
            }

            await review.SaveChangesAsync(cancellationToken);

            CorrespondenceLog.ClassConfirmed(logger, request.ReviewedBy, confirmed, refusals.Count);

            return Result<CorrespondenceConfirmationSummary>.Success(
                new CorrespondenceConfirmationSummary(
                    edition.Label,
                    request.ReviewedBy,
                    now,
                    confirmed,
                    refusals.Count,
                    refusals));
        }
    }
}

/// <summary>
/// Confirms one correspondence on manual review, with the written reason that is its whole justification.
/// </summary>
/// <remarks>
/// The individual half of the review, and the only route for a pairing the mechanical evidence did not
/// settle: the boundary set's irregular code for the City of Manila, and the Maguindanao municipalities the
/// PSA renumbered without publishing a nine-digit correspondence for either half. A person names the unit,
/// states why, and their reason is retained as the record of the decision.
/// </remarks>
public static class ConfirmLguEditionCorrespondence
{
    public sealed record Command(
        string LegacyCanonicalPsgcCode,
        string CurrentCanonicalPsgcCode,
        string ReviewedBy,
        string Reason) : ICommand;

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command>
    {
        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "edition_correspondence.no_current_edition",
            "No current register edition is held.");

        private static readonly Error TargetMismatch = new(
            ErrorType.Conflict,
            "edition_correspondence.target_mismatch",
            "The proposal names a different current unit. Reject it and propose the correct pairing rather "
            + "than confirming a row that says something else — a confirmation must be of what was reviewed.");

        private static readonly Error CurrentUnitNotFound = new(
            ErrorType.NotFound,
            "edition_correspondence.current_unit_not_found",
            "No unit in the current register edition carries that canonical code.");

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var now = timeProvider.GetUtcNow();

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result.Failure(NoCurrentEdition);
            }

            var legacy = request.LegacyCanonicalPsgcCode.Trim();
            var target = request.CurrentCanonicalPsgcCode.Trim();

            var correspondence = await review.LguEditionCorrespondences
                .FirstOrDefaultAsync(
                    item => item.LegacyCanonicalPsgcCode == legacy
                        && item.Status == LguLinkStatus.Proposed,
                    cancellationToken);

            if (correspondence is null)
            {
                // No proposal exists, and for these cases none can: the Maguindanao halves carry no
                // published nine-digit correspondence at all, so neither bridge reaches them. A person may
                // still assert the pairing — that is what ManualReview is — and the assertion is recorded as
                // theirs, attributed to them rather than to the matcher, before being confirmed.
                var asserted = await AssertAsync(legacy, target, request.ReviewedBy, edition.Id, now, cancellationToken);

                if (asserted.IsFailure)
                {
                    return Result.Failure(asserted.Error!);
                }

                correspondence = asserted.Value;
                review.LguEditionCorrespondences.Add(correspondence);
            }
            else if (!string.Equals(
                    correspondence.CurrentCanonicalPsgcCode,
                    target,
                    StringComparison.Ordinal))
            {
                return Result.Failure(TargetMismatch);
            }

            var result = correspondence.Confirm(
                LguLinkEvidence.ManualReview,
                request.ReviewedBy,
                request.Reason,
                edition,
                now);

            if (result.IsFailure)
            {
                return result;
            }

            await review.SaveChangesAsync(cancellationToken);

            CorrespondenceLog.SingleConfirmed(
                logger,
                correspondence.LegacyCanonicalPsgcCode,
                correspondence.CurrentCanonicalPsgcCode,
                nameof(LguLinkEvidence.ManualReview),
                request.ReviewedBy);

            return Result.Success();
        }

        /// <summary>
        /// Records a pairing a reviewer asserts, attributed to them rather than to the matcher.
        /// </summary>
        private async Task<Result<LguEditionCorrespondence>> AssertAsync(
            string legacy,
            string target,
            string reviewedBy,
            Guid editionId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var unit = await review.Lgus
                .FirstOrDefaultAsync(
                    candidate => candidate.CanonicalPsgcCode == target
                        && candidate.RegisterEditionId == editionId,
                    cancellationToken);

            if (unit is null)
            {
                return Result<LguEditionCorrespondence>.Failure(CurrentUnitNotFound);
            }

            return LguEditionCorrespondence.Propose(
                legacy,
                unit.Name,
                unit.Id,
                unit.CanonicalPsgcCode,
                $"asserted on manual review by {reviewedBy}; no published nine-digit correspondence exists "
                + "for either edition of this unit",
                namesAgree: false,
                hasRegisterBridge: false,
                reviewedBy,
                editionId,
                now);
        }
    }
}
