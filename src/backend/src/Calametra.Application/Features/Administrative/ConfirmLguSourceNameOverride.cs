using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Records that a publisher's name for one unit is a name the PSA has since replaced.
/// </summary>
/// <remarks>
/// One unit, one quoted name, one written reason. The import's requirement for an agreeing name stands; this
/// excuses a named instance of disagreement and nothing else.
/// </remarks>
public static class ConfirmLguSourceNameOverride
{
    public sealed record Command(
        string CanonicalPsgcCode,
        string SourceName,
        string ReviewedBy,
        string Reason) : ICommand;

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command>
    {
        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "source_name_override.no_current_edition",
            "No current PSGC register edition is held.");

        private static readonly Error UnitNotFound = new(
            ErrorType.NotFound,
            "source_name_override.unit_not_found",
            "No unit in the current register edition carries that canonical code.");

        private static readonly Error AlreadyHeld = new(
            ErrorType.Conflict,
            "source_name_override.already_held",
            "An override for that code and publisher name is already held.");

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result.Failure(NoCurrentEdition);
            }

            var code = request.CanonicalPsgcCode.Trim();
            var sourceName = request.SourceName.Trim();

            var unit = await review.Lgus.FirstOrDefaultAsync(
                candidate => candidate.CanonicalPsgcCode == code
                    && candidate.RegisterEditionId == edition.Id,
                cancellationToken);

            if (unit is null)
            {
                return Result.Failure(UnitNotFound);
            }

            var exists = await review.LguSourceNameOverrides.AnyAsync(
                candidate => candidate.CanonicalPsgcCode == code && candidate.SourceName == sourceName,
                cancellationToken);

            if (exists)
            {
                return Result.Failure(AlreadyHeld);
            }

            var confirmed = LguSourceNameOverride.Confirm(
                code,
                sourceName,
                unit.Name,
                request.ReviewedBy,
                request.Reason,
                edition,
                timeProvider.GetUtcNow());

            if (confirmed.IsFailure)
            {
                return Result.Failure(confirmed.Error!);
            }

            review.LguSourceNameOverrides.Add(confirmed.Value);

            await review.SaveChangesAsync(cancellationToken);

            OverrideLog.Confirmed(logger, code, sourceName, unit.Name, request.ReviewedBy);

            return Result.Success();
        }
    }
}

internal static partial class OverrideLog
{
    [LoggerMessage(
        EventId = 7370,
        Level = LogLevel.Information,
        Message = "Source name override confirmed for {Code}: publisher '{SourceName}' accepted as a "
            + "superseded name for register '{RegisterName}', by {ReviewedBy}")]
    public static partial void Confirmed(
        ILogger logger,
        string code,
        string sourceName,
        string registerName,
        string reviewedBy);
}
