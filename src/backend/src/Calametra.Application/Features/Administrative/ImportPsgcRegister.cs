using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Loads an edition of the PSGC register as canonical local government units.
/// </summary>
/// <remarks>
/// <para>
/// <b>This creates identities, not pairings.</b> The register says what units exist and what the PSA
/// calls them; whether a unit corresponds to a row in this platform's gazetteer is a separate question
/// answered by a reviewed crosswalk. Keeping the two apart is what stops a register import from silently
/// establishing 1,600 unreviewed relationships.
/// </para>
/// <para>
/// <b>Provenance travels with the edition and is never upgraded.</b> A mirror is stored as a mirror. It
/// can be matched against and reported on, and <c>LguCodeLink.Confirm</c> will refuse to cite it — which
/// is ADR-005's first gate condition enforced in code rather than in a checklist.
/// </para>
/// <para>
/// Idempotent: re-reading the same publication reconciles the edition row and each unit rather than
/// accumulating duplicates, following the same rule as the cyclone and place imports.
/// </para>
/// </remarks>
public static class ImportPsgcRegister
{
    public sealed record Command : ICommand<RegisterImportSummary>;

    /// <param name="EditionLabel">The edition as loaded, so the operator can see what they got.</param>
    /// <param name="Provenance">
    /// <c>PsaDirect</c>, <c>Mirror</c> or <c>LocalFile</c>. Only the first can certify a crosswalk.
    /// </param>
    /// <param name="UnitsCreated">Units new to this platform.</param>
    /// <param name="UnitsReconciled">Units already held, re-read from this edition.</param>
    /// <param name="UnitsRejected">Rows the register offered that failed the domain's own rules.</param>
    /// <param name="RegisterStatedPairings">
    /// Units for which the register itself published both editions of the code. This is the population
    /// that can eventually be confirmed on <c>RegisterMatch</c> — the strongest evidence available.
    /// </param>
    /// <param name="EditionsSuperseded">Earlier editions retired by this import.</param>
    /// <param name="ProposalsSuperseded">
    /// Unreviewed proposals retired with them. Confirmed and rejected rows are untouched: a review is a
    /// person's decision against a stated publication and an import does not retract it.
    /// </param>
    public sealed record RegisterImportSummary(
        string EditionLabel,
        string Provenance,
        bool IsCitableAsAuthority,
        int RegionCount,
        int ProvinceCount,
        int CityCount,
        int MunicipalityCount,
        int UnitsCreated,
        int UnitsReconciled,
        int UnitsRejected,
        int RegisterStatedPairings,
        DateTimeOffset? UpstreamLastModified,
        DateOnly? PublicationDate,
        string? OriginalFileName,
        string? FileSha256,
        int EditionsSuperseded,
        int ProposalsSuperseded);

    internal sealed class Handler(
        ILguCrosswalkReviewContext context,
        IApplicationDbContext analytics,
        IPsgcRegisterSource registerSource,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, RegisterImportSummary>
    {
        private static readonly Error SourceNotRegistered = new(
            ErrorType.NotFound,
            "psgc_import.source_not_registered",
            "The PSGC register is not registered as a data source. Reference data must be seeded "
            + "before the register is imported.");

        private static readonly Error RegisterEmpty = new(
            ErrorType.Validation,
            "psgc_import.register_empty",
            "The register returned no units. An empty register is a failed read, not an empty country.");

        public async Task<Result<RegisterImportSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            // Read through the analytics surface because DataSource is reference data every surface may
            // see. The crosswalk itself is written through the review context.
            var source = await analytics.DataSources
                .FirstOrDefaultAsync(candidate => candidate.Slug == registerSource.SourceSlug, cancellationToken);

            if (source is null)
            {
                return Result<RegisterImportSummary>.Failure(SourceNotRegistered);
            }

            var snapshot = await registerSource.ReadAsync(cancellationToken);

            if (snapshot.Units.Count == 0)
            {
                return Result<RegisterImportSummary>.Failure(RegisterEmpty);
            }

            var edition = await ReconcileEditionAsync(snapshot, source.Id, now, cancellationToken);

            // A new edition retires the previous one and every unreviewed proposal made against it. Done
            // before the units are read so the run cannot half-happen: if the import fails later, nothing
            // has been marked current that is not.
            var superseded = await SupersedePreviousAsync(edition, now, cancellationToken);

            var existing = await context.Lgus
                .ToDictionaryAsync(lgu => lgu.CanonicalPsgcCode, cancellationToken);

            var created = 0;
            var reconciled = 0;
            var rejected = 0;

            foreach (var unit in snapshot.Units)
            {
                if (existing.TryGetValue(unit.CanonicalCode, out var held))
                {
                    held.Reconcile(
                        unit.Name,
                        unit.Level,
                        unit.ParentCanonicalCode,
                        unit.StatedHistoricalCode,
                        edition.Id,
                        now);

                    reconciled++;

                    continue;
                }

                var candidate = Lgu.Create(unit.CanonicalCode, unit.Name, unit.Level, edition.Id, now);

                if (candidate.IsFailure)
                {
                    // Counted rather than thrown. A register row this platform's rules refuse is a fact
                    // about the register, and the readiness report is where it belongs — an import that
                    // died on the first bad row would tell an operator nothing about the other 1,633.
                    rejected++;
                    ImportLog.UnitRejected(logger, unit.CanonicalCode, unit.Name, candidate.Error!.Code);

                    continue;
                }

                var lgu = candidate.Value
                    .WithHierarchy(unit.ParentCanonicalCode)
                    .WithRegisterStatedHistoricalCode(unit.StatedHistoricalCode);

                context.Lgus.Add(lgu);
                existing[lgu.CanonicalPsgcCode] = lgu;
                created++;
            }

            var regions = snapshot.Units.Count(unit => unit.Level == LguLevel.Region);
            var provinces = snapshot.Units.Count(unit => unit.Level == LguLevel.Province);
            var cities = snapshot.Units.Count(unit => unit.Level == LguLevel.City);
            var municipalities = snapshot.Units.Count(unit => unit.Level == LguLevel.Municipality);

            edition.WithObservedComposition(
                regions,
                provinces,
                cities,
                municipalities,
                snapshot.UpstreamLastModified,
                snapshot.Notes);

            edition.WithAcquisition(
                snapshot.PublicationDate,
                snapshot.OriginalFileName,
                snapshot.FileSha256,
                snapshot.AcquisitionNote);

            await context.SaveChangesAsync(cancellationToken);

            var statedPairings = snapshot.Units.Count(unit => unit.StatedHistoricalCode is not null);
            var provenance = edition.Provenance.ToString();

            ImportLog.Completed(
                logger,
                edition.Label,
                provenance,
                created,
                reconciled,
                rejected,
                statedPairings);

            return Result<RegisterImportSummary>.Success(new RegisterImportSummary(
                edition.Label,
                provenance,
                edition.IsCitableAsAuthority,
                regions,
                provinces,
                cities,
                municipalities,
                created,
                reconciled,
                rejected,
                statedPairings,
                snapshot.UpstreamLastModified,
                snapshot.PublicationDate,
                snapshot.OriginalFileName,
                snapshot.FileSha256,
                superseded.Editions,
                superseded.Proposals));
        }

        /// <summary>
        /// Retires every earlier edition and the unreviewed proposals made against them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Confirmed and rejected rows are left alone. They record a person's decision against a stated
        /// publication, and a newer register arriving does not retract a review — it may make one worth
        /// revisiting, which is a judgement for a reviewer rather than for an import.
        /// </para>
        /// <para>
        /// Superseded rather than deleted, so the figures the earlier run produced stay explainable after
        /// the edition behind them is replaced. That is the same reason a rejected pairing is retained.
        /// </para>
        /// </remarks>
        private async Task<(int Editions, int Proposals)> SupersedePreviousAsync(
            PsgcRegisterEdition current,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var earlier = await context.PsgcRegisterEditions
                .Where(edition => edition.Id != current.Id && edition.SupersededAt == null)
                .ToListAsync(cancellationToken);

            if (earlier.Count == 0)
            {
                return (0, 0);
            }

            var earlierIds = earlier.ConvertAll(edition => edition.Id);

            foreach (var edition in earlier)
            {
                edition.Supersede(current.Id, now);
            }

            var stale = await context.LguCodeLinks
                .Where(link => link.Status == LguLinkStatus.Proposed
                    && earlierIds.Contains(link.ProposedAgainstEditionId))
                .ToListAsync(cancellationToken);

            foreach (var link in stale)
            {
                link.Supersede(now);
            }

            ImportLog.PreviousEditionsSuperseded(logger, earlier.Count, stale.Count);

            return (earlier.Count, stale.Count);
        }

        /// <summary>
        /// Finds or creates the edition row. Keyed on label and access route, so re-reading the same
        /// publication updates one row rather than adding another.
        /// </summary>
        private async Task<PsgcRegisterEdition> ReconcileEditionAsync(
            RegisterSnapshot snapshot,
            Guid dataSourceId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var existing = await context.PsgcRegisterEditions.FirstOrDefaultAsync(
                edition => edition.Label == snapshot.Label && edition.AccessRoute == snapshot.AccessRoute,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            // The domain validated the snapshot's own fields on construction; a failure here is a bug in
            // the adapter rather than a property of the register, so it surfaces as an exception instead
            // of being counted.
            var created = PsgcRegisterEdition.Create(
                snapshot.Label,
                snapshot.Provenance,
                snapshot.AccessRoute,
                dataSourceId,
                now,
                now).Value;

            context.PsgcRegisterEditions.Add(created);

            return created;
        }
    }
}

internal static partial class ImportLog
{
    [LoggerMessage(
        EventId = 7100,
        Level = LogLevel.Information,
        Message = "PSGC register '{EditionLabel}' ({Provenance}) imported: {Created} units created, "
            + "{Reconciled} reconciled, {Rejected} rejected, {StatedPairings} carrying the register's "
            + "own nine-digit pairing")]
    public static partial void Completed(
        ILogger logger,
        string editionLabel,
        string provenance,
        int created,
        int reconciled,
        int rejected,
        int statedPairings);

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "Register unit {CanonicalCode} '{Name}' rejected: {ErrorCode}")]
    public static partial void UnitRejected(
        ILogger logger,
        string canonicalCode,
        string name,
        string errorCode);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Information,
        Message = "{Editions} earlier register edition(s) superseded, retiring {Proposals} unreviewed "
            + "proposal(s). Confirmed and rejected pairings are untouched.")]
    public static partial void PreviousEditionsSuperseded(ILogger logger, int editions, int proposals);
}
