using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Records that a unit or a directory row has no counterpart, with the reason, per ADR-005 D5.
/// </summary>
/// <remarks>
/// <para>
/// <b>The alternative to this command is a fabricated pairing.</b> D5's population is not noise to be
/// cleaned up: Cotabato City is in BARMM and geographically inside SOCCSKSARGEN, the two Maguindanaos
/// descend from a province whose nine-digit code was never split, and Manila's fourteen districts are
/// sub-city units the register classifies below the city. Forcing any of these to pair would put an
/// invented fact about Philippine administrative geography into a column every join reads, and it would be
/// indistinguishable from a real one afterwards.
/// </para>
/// <para>
/// One subject at a time, each with its own reason. There is no "accept the remainder" here, because the
/// reasons differ per case and a shared reason would be a summary rather than an explanation — and the
/// reason is the thing D5 requires to be rendered to the reader.
/// </para>
/// </remarks>
public static class AcceptLguCrosswalkException
{
    /// <param name="CanonicalPsgcCode">
    /// The ten-digit code of a register unit with no directory row. Exactly one of this and
    /// <paramref name="DirectoryPsgcCode"/> must be given.
    /// </param>
    /// <param name="DirectoryPsgcCode">The nine-digit code of a directory row with no register unit.</param>
    /// <param name="Reason">Why the pairing cannot be made. Published to readers.</param>
    /// <param name="AcceptedBy">The person accountable.</param>
    public sealed record Command(
        string? CanonicalPsgcCode,
        string? DirectoryPsgcCode,
        string Reason,
        string AcceptedBy) : ICommand;

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IApplicationDbContext analytics,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command>
    {
        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "crosswalk_exception.no_current_edition",
            "No current register edition is held, so there is nothing to accept an exception against.");

        private static readonly Error SubjectAmbiguous = new(
            ErrorType.Validation,
            "crosswalk_exception.subject_ambiguous",
            "Give exactly one subject: a canonical code for a register unit with no directory row, or a "
            + "directory code for a gazetteer row with no register unit. The two are different claims.");

        private static readonly Error UnitNotFound = new(
            ErrorType.NotFound,
            "crosswalk_exception.unit_not_found",
            "No unit in the current register edition carries that canonical code.");

        private static readonly Error DirectoryRowNotFound = new(
            ErrorType.NotFound,
            "crosswalk_exception.directory_row_not_found",
            "No directory row carries that nine-digit code.");

        private static readonly Error AlreadyAccepted = new(
            ErrorType.Conflict,
            "crosswalk_exception.already_accepted",
            "An exception for that subject has already been accepted against the current edition.");

        private static readonly Error UnitIsPaired = new(
            ErrorType.Conflict,
            "crosswalk_exception.unit_is_paired",
            "That unit has a live pairing. An exception says no counterpart exists, so the proposal must be "
            + "rejected first if that is the reviewer's finding.");

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var hasUnit = !string.IsNullOrWhiteSpace(request.CanonicalPsgcCode);
            var hasRow = !string.IsNullOrWhiteSpace(request.DirectoryPsgcCode);

            if (hasUnit == hasRow)
            {
                return Result.Failure(SubjectAmbiguous);
            }

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result.Failure(NoCurrentEdition);
            }

            var now = timeProvider.GetUtcNow();

            return hasUnit
                ? await AcceptForUnitAsync(request, edition, now, cancellationToken)
                : await AcceptForDirectoryRowAsync(request, edition, now, cancellationToken);
        }

        private async Task<Result> AcceptForUnitAsync(
            Command request,
            PsgcRegisterEdition edition,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var code = request.CanonicalPsgcCode!.Trim();

            var unit = await review.Lgus.FirstOrDefaultAsync(
                candidate => candidate.CanonicalPsgcCode == code
                    && candidate.RegisterEditionId == edition.Id,
                cancellationToken);

            if (unit is null)
            {
                return Result.Failure(UnitNotFound);
            }

            var alreadyAccepted = await review.LguCrosswalkExceptions.AnyAsync(
                candidate => candidate.LguId == unit.Id && candidate.RegisterEditionId == edition.Id,
                cancellationToken);

            if (alreadyAccepted)
            {
                return Result.Failure(AlreadyAccepted);
            }

            // An exception and a live pairing are contradictory claims about the same unit. Refused rather
            // than silently preferred, so a reviewer cannot excuse a unit they have also paired.
            var isPaired = await review.LguCodeLinks.AnyAsync(
                candidate => candidate.LguId == unit.Id
                    && candidate.Status != LguLinkStatus.Superseded
                    && candidate.Status != LguLinkStatus.Rejected,
                cancellationToken);

            if (isPaired)
            {
                return Result.Failure(UnitIsPaired);
            }

            var accepted = LguCrosswalkException.ForRegisterUnit(
                unit,
                request.Reason,
                request.AcceptedBy,
                edition,
                now);

            if (accepted.IsFailure)
            {
                return Result.Failure(accepted.Error!);
            }

            review.LguCrosswalkExceptions.Add(accepted.Value);

            await review.SaveChangesAsync(cancellationToken);

            var subject = $"{unit.CanonicalPsgcCode} {unit.Name}";

            ReviewLog.ExceptionAccepted(
                logger,
                request.AcceptedBy,
                subject,
                request.Reason);

            return Result.Success();
        }

        private async Task<Result> AcceptForDirectoryRowAsync(
            Command request,
            PsgcRegisterEdition edition,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var code = request.DirectoryPsgcCode!.Trim();

            var row = await analytics.Places
                .Where(place => place.PsgcCode == code && place.Kind != PlaceKind.Barangay)
                .Select(place => new { place.Id, place.Name })
                .FirstOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return Result.Failure(DirectoryRowNotFound);
            }

            var alreadyAccepted = await review.LguCrosswalkExceptions.AnyAsync(
                candidate => candidate.PlaceId == row.Id && candidate.RegisterEditionId == edition.Id,
                cancellationToken);

            if (alreadyAccepted)
            {
                return Result.Failure(AlreadyAccepted);
            }

            var accepted = LguCrosswalkException.ForDirectoryRow(
                row.Id,
                code,
                row.Name,
                request.Reason,
                request.AcceptedBy,
                edition,
                now);

            if (accepted.IsFailure)
            {
                return Result.Failure(accepted.Error!);
            }

            review.LguCrosswalkExceptions.Add(accepted.Value);

            await review.SaveChangesAsync(cancellationToken);

            var subject = $"{code} {row.Name}";

            ReviewLog.ExceptionAccepted(
                logger,
                request.AcceptedBy,
                subject,
                request.Reason);

            return Result.Success();
        }
    }
}
