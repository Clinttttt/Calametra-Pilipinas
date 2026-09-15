using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Confirms a proposed pairing. The only path by which a crosswalk row becomes readable.
/// </summary>
/// <remarks>
/// <para>
/// <b>One row at a time, deliberately.</b> ADR-005 D4 forbids bulk promotion: "accept all proposals
/// above threshold N" is the algorithm establishing the crosswalk with a person's name on it. A reviewer
/// who confirms a thousand pairings does so as a thousand recorded decisions, each citing the edition it
/// was checked against.
/// </para>
/// <para>
/// The invariants are the domain's, not this handler's — <c>LguCodeLink.Confirm</c> refuses a missing
/// reviewer, a missing reason on manual review, a re-slicing whose names disagree, and any edition that
/// did not come from the PSA. This handler's job is to load the row and its cited edition and let the
/// entity decide.
/// </para>
/// </remarks>
public static class ConfirmLguCodeLink
{
    /// <param name="LinkId">The proposal being decided.</param>
    /// <param name="Evidence">What the reviewer actually checked. Re-stated, never inherited from the matcher.</param>
    /// <param name="ReviewedBy">The person accountable. Recorded verbatim.</param>
    /// <param name="Reason">Mandatory when the evidence is manual review.</param>
    /// <param name="RegisterEditionId">The edition confirmed against. Must be PSA-direct.</param>
    public sealed record Command(
        Guid LinkId,
        LguLinkEvidence Evidence,
        string ReviewedBy,
        string? Reason,
        Guid RegisterEditionId) : ICommand;

    internal sealed class Handler(
        ILguCrosswalkReviewContext context,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command>
    {
        private static readonly Error LinkNotFound = new(
            ErrorType.NotFound,
            "lgu_link.not_found",
            "The proposed pairing was not found.");

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var link = await context.LguCodeLinks
                .FirstOrDefaultAsync(candidate => candidate.Id == request.LinkId, cancellationToken);

            if (link is null)
            {
                return Result.Failure(LinkNotFound);
            }

            var edition = await context.PsgcRegisterEditions
                .FirstOrDefaultAsync(candidate => candidate.Id == request.RegisterEditionId, cancellationToken);

            if (edition is null)
            {
                return Result.Failure(PsgcRegisterEditionErrors.NotFound);
            }

            var confirmed = link.Confirm(
                request.Evidence,
                request.ReviewedBy,
                request.Reason,
                edition,
                timeProvider.GetUtcNow());

            if (confirmed.IsFailure)
            {
                return confirmed;
            }

            await context.SaveChangesAsync(cancellationToken);

            // Hoisted rather than inlined: the analyser flags an enum ToString inside a log call, since
            // it would be evaluated even when the level is disabled.
            var evidence = request.Evidence.ToString();

            ReviewLog.Confirmed(logger, link.HistoricalPsgcCode, evidence, request.ReviewedBy);

            return Result.Success();
        }
    }
}

/// <summary>
/// Refuses a proposed pairing, with the reason that makes the rejection rate explainable.
/// </summary>
/// <remarks>
/// Rejections are retained rather than deleted, because ADR-005 requires the proportion of proposals a
/// reviewer refused to be recorded before the crosswalk is accepted: if a matcher proposes 1,600
/// pairings and 40 are wrong, that number is the justification for the review gate existing.
/// </remarks>
public static class RejectLguCodeLink
{
    public sealed record Command(Guid LinkId, string ReviewedBy, string Reason) : ICommand;

    internal sealed class Handler(
        ILguCrosswalkReviewContext context,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command>
    {
        private static readonly Error LinkNotFound = new(
            ErrorType.NotFound,
            "lgu_link.not_found",
            "The proposed pairing was not found.");

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var link = await context.LguCodeLinks
                .FirstOrDefaultAsync(candidate => candidate.Id == request.LinkId, cancellationToken);

            if (link is null)
            {
                return Result.Failure(LinkNotFound);
            }

            var rejected = link.Reject(request.ReviewedBy, request.Reason, timeProvider.GetUtcNow());

            if (rejected.IsFailure)
            {
                return rejected;
            }

            await context.SaveChangesAsync(cancellationToken);

            ReviewLog.Rejected(logger, link.HistoricalPsgcCode, request.ReviewedBy, request.Reason);

            return Result.Success();
        }
    }
}

internal static partial class ReviewLog
{
    [LoggerMessage(
        EventId = 7120,
        Level = LogLevel.Information,
        Message = "Pairing to {HistoricalCode} confirmed on {Evidence} by {ReviewedBy}")]
    public static partial void Confirmed(
        ILogger logger,
        string historicalCode,
        string evidence,
        string reviewedBy);

    [LoggerMessage(
        EventId = 7121,
        Level = LogLevel.Information,
        Message = "Pairing to {HistoricalCode} rejected by {ReviewedBy}: {Reason}")]
    public static partial void Rejected(
        ILogger logger,
        string historicalCode,
        string reviewedBy,
        string reason);

    [LoggerMessage(
        EventId = 7122,
        Level = LogLevel.Information,
        Message = "Evidence class {Evidence} confirmed as a named batch by {ReviewedBy} against edition "
            + "'{EditionLabel}': {Confirmed} confirmed, {Refused} refused by the domain and left proposed")]
    public static partial void ClassConfirmed(
        ILogger logger,
        string evidence,
        int confirmed,
        int refused,
        string reviewedBy,
        string editionLabel);

    [LoggerMessage(
        EventId = 7123,
        Level = LogLevel.Information,
        Message = "Exception accepted by {AcceptedBy} for {Subject}: {Reason}")]
    public static partial void ExceptionAccepted(
        ILogger logger,
        string acceptedBy,
        string subject,
        string reason);
}
