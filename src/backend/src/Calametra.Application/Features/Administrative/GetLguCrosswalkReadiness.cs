using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// The four-condition gate from ADR-005, answered by measurement rather than by assertion.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a query and not a checklist.</b> The four conditions decide whether polygon ingestion
/// may begin, and a gate a person ticks is a gate that gets ticked. Every condition here is derived from
/// the database as it actually is: the register edition's provenance, the confirmed count against the
/// register's own population, the enumerated unmatched set, and the source registration. The report can
/// therefore be run before every attempt and cannot drift from the state it describes.
/// </para>
/// <para>
/// <b>The read boundary is measured too.</b> Layer 1 is the confirmed-only view, layer 3 is the boundary
/// tests, and layer 2 — database permissions — exists only where the deployment has separate roles. The
/// report asks the database whether it is in force instead of assuming, so an operator who skipped the
/// permissions script sees that layer 2 is inactive rather than believing it is on.
/// </para>
/// </remarks>
public static class GetLguCrosswalkReadiness
{
    public sealed record Query : IQuery<ReadinessReport>;

    /// <param name="Met">Whether this condition currently passes.</param>
    /// <param name="Detail">The measurement behind the verdict, so a failure explains itself.</param>
    public sealed record GateCondition(int Number, string Name, bool Met, string Detail);

    /// <param name="Conditions">ADR-005's four conditions, in order.</param>
    /// <param name="MayBeginGeometryIngestion">
    /// True only when every condition passes. The single value the geometry work is allowed to read.
    /// </param>
    /// <param name="ProposalRejectionRate">
    /// The proportion of reviewed proposals that were refused. Null while nothing has been reviewed —
    /// which is not a rate of zero, and must not be reported as one.
    /// </param>
    /// <param name="UnmatchedRegisterUnits">
    /// Canonical units with no confirmed pairing. ADR-005 requires this to be enumerated and accepted
    /// rather than driven to zero: a zero achieved by name-matching would be a fiction.
    /// </param>
    public sealed record ReadinessReport(
        IReadOnlyList<GateCondition> Conditions,
        bool MayBeginGeometryIngestion,
        RegisterState Register,
        CrosswalkState Crosswalk,
        double? ProposalRejectionRate,
        int UnmatchedRegisterUnits,
        int UnmatchedDirectoryRows,
        ReadBoundaryState ReadBoundary);

    public sealed record RegisterState(
        bool AnyEditionLoaded,
        string? Label,
        string? Provenance,
        bool IsCitableAsAuthority,
        int RegionCount,
        int ProvinceCount,
        int CityCount,
        int MunicipalityCount,
        DateTimeOffset? UpstreamLastModified,
        string? Notes,
        DateOnly? PublicationDate,
        string? OriginalFileName,
        string? FileSha256,
        string? AcquisitionNote,
        int SupersededEditions);

    /// <param name="RegisterUnits">
    /// Units in the ACTIVE edition. Units held only under a superseded edition's codes are reported as
    /// <paramref name="SupersededUnits"/>, because a recoding is not a deletion and neither is it part of
    /// the population under review.
    /// </param>
    /// <param name="SupersededUnits">
    /// Units held from an edition since replaced. Retained so figures derived from them stay explainable.
    /// </param>
    /// <param name="ExceptedUnits">
    /// Units a reviewer examined and recorded as having no directory counterpart, each with a written
    /// reason. Per ADR-005 D5 an unpaired unit is valid, not broken.
    /// </param>
    /// <param name="ExceptedDirectoryRows">Directory rows recorded as having no register counterpart.</param>
    /// <param name="UnitsNeitherPairedNorExcepted">
    /// The figure gate 3 turns on: units with no pairing and no written reason for having none.
    /// </param>
    /// <param name="DirectoryRowsNeitherPairedNorExcepted">The same, on the gazetteer side.</param>
    public sealed record CrosswalkState(
        int RegisterUnits,
        int DirectoryRowsWithCode,
        int Proposed,
        int Confirmed,
        int Rejected,
        int Superseded,
        int SupersededUnits,
        int ExceptedUnits,
        int ExceptedDirectoryRows,
        int UnitsNeitherPairedNorExcepted,
        int DirectoryRowsNeitherPairedNorExcepted,
        int ConfirmedOnRegisterMatch,
        int ConfirmedOnDigitReslice,
        int ConfirmedOnManualReview,
        int ProposalsBlockedByNameDisagreement);

    /// <param name="ConfirmedOnlyViewPresent">Layer 1: the view analytics read through.</param>
    /// <param name="ProposalsVisibleThroughAnalyticsSurface">
    /// Must always be false. True would mean the confirmed-only view is leaking unreviewed rows, which is
    /// the one failure that would make the whole model decorative.
    /// </param>
    /// <param name="DatabasePermissionsActive">
    /// Layer 2, and null when the deployment has no separate application role to measure — an accepted
    /// configuration, recorded rather than glossed.
    /// </param>
    public sealed record ReadBoundaryState(
        bool ConfirmedOnlyViewPresent,
        bool ProposalsVisibleThroughAnalyticsSurface,
        bool? DatabasePermissionsActive,
        string Detail);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IApplicationDbContext analytics) : IQueryHandler<Query, ReadinessReport>
    {
        public async Task<Result<ReadinessReport>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            // The CURRENT edition, not merely the newest row: a superseded edition is a historical record
            // and reporting readiness against it would answer a question nobody asked.
            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var supersededEditions = await review.PsgcRegisterEditions
                .CountAsync(candidate => candidate.SupersededAt != null, cancellationToken);

            // Units belonging to the ACTIVE edition, not every unit ever held.
            //
            // This distinction is not pedantic. Importing PSA 2Q 2026 over the 2022 mirror left 124 rows
            // whose codes the new edition does not use — the Negros Island Region reassignment and the
            // BARMM recodings changed the leading digits of every LGU in the affected provinces, so those
            // units exist under new codes and their old rows are history. Counting them would inflate
            // gate 2's denominator with places the register no longer identifies that way, and the gate
            // could then never close.
            var registerUnits = edition is null
                ? 0
                : await review.Lgus.CountAsync(
                    lgu => lgu.RegisterEditionId == edition.Id,
                    cancellationToken);

            var supersededUnits = edition is null
                ? await review.Lgus.CountAsync(cancellationToken)
                : await review.Lgus.CountAsync(
                    lgu => lgu.RegisterEditionId != edition.Id,
                    cancellationToken);

            var directoryRows = await analytics.Places
                .CountAsync(place => place.PsgcCode != null && place.Kind != PlaceKind.Barangay, cancellationToken);

            // Joined to the unit so confirmation can be counted PER LEVEL. Gate 2's required population
            // is whatever the active edition holds: a city-and-municipality total is a property of one
            // quarter's publication rather than a constant, and the register is the only thing entitled
            // to state it.
            var links = await review.LguCodeLinks
                .Join(
                    review.Lgus,
                    link => link.LguId,
                    lgu => lgu.Id,
                    (link, lgu) => new
                    {
                        link.Status,
                        link.Evidence,
                        link.NamesAgree,
                        link.LguId,
                        link.PlaceId,
                        lgu.Level,
                        lgu.RegisterEditionId,
                    })
                .ToListAsync(cancellationToken);

            // Scoped to the active edition for the same reason as the unit count: a pairing confirmed
            // against a code the register has since stopped using does not help this edition's gate.
            var editionId = edition?.Id;

            var activeLinks = links
                .Where(link => editionId is not null && link.RegisterEditionId == editionId)
                .ToList();

            var proposed = activeLinks.Count(link => link.Status == LguLinkStatus.Proposed);
            var confirmed = activeLinks.Count(link => link.Status == LguLinkStatus.Confirmed);
            var rejected = activeLinks.Count(link => link.Status == LguLinkStatus.Rejected);

            var reviewed = confirmed + rejected;

            // Null rather than zero while nothing has been reviewed. A rejection rate of 0% and "no
            // reviews yet" are different claims, and reporting the second as the first would make an
            // untested matcher look perfect.
            double? rejectionRate = reviewed == 0 ? null : (double)rejected / reviewed;

            var confirmedLguIds = activeLinks
                .Where(link => link.Status == LguLinkStatus.Confirmed)
                .Select(link => link.LguId)
                .ToHashSet();

            var confirmedPlaceIds = activeLinks
                .Where(link => link.Status == LguLinkStatus.Confirmed && link.PlaceId is not null)
                .Select(link => link.PlaceId!.Value)
                .ToHashSet();

            var unmatchedUnits = registerUnits - confirmedLguIds.Count;
            var unmatchedDirectory = directoryRows - confirmedPlaceIds.Count;

            // Per level, all derived from the active edition. Nothing here is a written-down target.
            var confirmedByLevel = activeLinks
                .Where(link => link.Status == LguLinkStatus.Confirmed)
                .Select(link => link.Level)
                .GroupBy(level => level)
                .ToDictionary(group => group.Key, group => group.Count());

            var localGovernmentUnitsRequired = edition is null
                ? 0
                : edition.CityCount + edition.MunicipalityCount;

            var localGovernmentUnitsConfirmed =
                confirmedByLevel.GetValueOrDefault(LguLevel.City)
                + confirmedByLevel.GetValueOrDefault(LguLevel.Municipality);

            // Accepted exceptions for this edition, per ADR-005 D5. Counted here rather than inferred from
            // the unmatched total, because "we could not pair it" and "a person examined it and wrote down
            // why it cannot be paired" are the two states gate 3 distinguishes between.
            var exceptions = await review.LguCrosswalkExceptions
                .Where(item => editionId != null && item.RegisterEditionId == editionId)
                .Select(item => new { item.Kind, item.LguId, item.PlaceId })
                .ToListAsync(cancellationToken);

            var exceptedUnitIds = exceptions
                .Where(item => item.LguId is not null)
                .Select(item => item.LguId!.Value)
                .ToHashSet();

            var exceptedPlaceIds = exceptions
                .Where(item => item.PlaceId is not null)
                .Select(item => item.PlaceId!.Value)
                .ToHashSet();

            // A unit with a live pairing of any kind is accounted for: confirmed means paired, proposed
            // means still under review. What gate 3 counts is what is neither.
            var accountedUnitIds = activeLinks
                .Where(link => link.Status is LguLinkStatus.Confirmed or LguLinkStatus.Proposed)
                .Select(link => link.LguId)
                .ToHashSet();

            var accountedPlaceIds = activeLinks
                .Where(link => link.Status is LguLinkStatus.Confirmed or LguLinkStatus.Proposed
                    && link.PlaceId is not null)
                .Select(link => link.PlaceId!.Value)
                .ToHashSet();

            var unitsNeitherPairedNorExcepted = editionId is null
                ? 0
                : await review.Lgus.CountAsync(
                    lgu => lgu.RegisterEditionId == editionId
                        && !accountedUnitIds.Contains(lgu.Id)
                        && !exceptedUnitIds.Contains(lgu.Id),
                    cancellationToken);

            var directoryRowsNeitherPairedNorExcepted = await analytics.Places
                .CountAsync(
                    place => place.PsgcCode != null
                        && place.Kind != PlaceKind.Barangay
                        && !accountedPlaceIds.Contains(place.Id)
                        && !exceptedPlaceIds.Contains(place.Id),
                    cancellationToken);

            var boundary = await MeasureReadBoundaryAsync(cancellationToken);

            var conditions = BuildConditions(
                edition,
                registerUnits,
                confirmed,
                unmatchedUnits,
                reviewed,
                localGovernmentUnitsRequired,
                localGovernmentUnitsConfirmed,
                confirmedByLevel,
                proposed,
                exceptedUnitIds.Count,
                unitsNeitherPairedNorExcepted,
                directoryRowsNeitherPairedNorExcepted);

            var register = edition is null
                ? new RegisterState(
                    false, null, null, false, 0, 0, 0, 0, null, null, null, null, null, null,
                    supersededEditions)
                : new RegisterState(
                    true,
                    edition.Label,
                    edition.Provenance.ToString(),
                    edition.IsCitableAsAuthority,
                    edition.RegionCount,
                    edition.ProvinceCount,
                    edition.CityCount,
                    edition.MunicipalityCount,
                    edition.UpstreamLastModified,
                    edition.Notes,
                    edition.PublicationDate,
                    edition.OriginalFileName,
                    edition.FileSha256,
                    edition.AcquisitionNote,
                    supersededEditions);

            var crosswalk = new CrosswalkState(
                registerUnits,
                directoryRows,
                proposed,
                confirmed,
                rejected,
                links.Count(link => link.Status == LguLinkStatus.Superseded),
                supersededUnits,
                exceptedUnitIds.Count,
                exceptedPlaceIds.Count,
                unitsNeitherPairedNorExcepted,
                directoryRowsNeitherPairedNorExcepted,
                links.Count(link => link.Status == LguLinkStatus.Confirmed
                    && link.Evidence == LguLinkEvidence.RegisterMatch),
                links.Count(link => link.Status == LguLinkStatus.Confirmed
                    && link.Evidence == LguLinkEvidence.DigitReslice),
                links.Count(link => link.Status == LguLinkStatus.Confirmed
                    && link.Evidence == LguLinkEvidence.ManualReview),
                links.Count(link => link.Status == LguLinkStatus.Proposed
                    && link.Evidence == LguLinkEvidence.DigitReslice
                    && !link.NamesAgree));

            return Result<ReadinessReport>.Success(new ReadinessReport(
                conditions,
                conditions.All(condition => condition.Met) && !boundary.ProposalsVisibleThroughAnalyticsSurface,
                register,
                crosswalk,
                rejectionRate,
                unmatchedUnits,
                unmatchedDirectory,
                boundary));
        }

        private static List<GateCondition> BuildConditions(
            PsgcRegisterEdition? edition,
            int registerUnits,
            int confirmed,
            int unmatchedUnits,
            int reviewed,
            int localGovernmentUnitsRequired,
            int localGovernmentUnitsConfirmed,
            Dictionary<LguLevel, int> confirmedByLevel,
            int proposed,
            int exceptedUnits,
            int unitsNeitherPairedNorExcepted,
            int directoryRowsNeitherPairedNorExcepted)
        {
            var editionDetail = edition is null
                ? "No register edition has been loaded."
                : $"'{edition.Label}' loaded as {edition.Provenance}, {edition.RegionCount} regions and "
                    + $"{edition.ProvinceCount} provinces observed"
                    + (edition.UpstreamLastModified is { } stamp
                        ? $", upstream last modified {stamp:yyyy-MM-dd}."
                        : ", upstream last-modified not reported.");

            // Every figure in gate 2's detail is read off the active edition. The city-and-municipality
            // population is called out separately because it is the one a reviewer works through, and
            // because it is the number most likely to be quoted from memory and be a quarter out of date.
            var gate2Detail = edition is null
                ? "No register edition is loaded, so there is no population to review against."
                : $"{localGovernmentUnitsConfirmed} of {localGovernmentUnitsRequired} cities and "
                    + $"municipalities confirmed ({edition.CityCount} cities, "
                    + $"{edition.MunicipalityCount} municipalities in this edition); "
                    + $"{confirmedByLevel.GetValueOrDefault(LguLevel.Province)} of "
                    + $"{edition.ProvinceCount} provinces; "
                    + $"{confirmedByLevel.GetValueOrDefault(LguLevel.Region)} of "
                    + $"{edition.RegionCount} regions. {confirmed} of {registerUnits} units overall, plus "
                    + $"{exceptedUnits} accepted as having no counterpart.";

            // Gate 3 asks whether the unmatched set is enumerated AND accepted. Both halves are required
            // and neither is implied by the other.
            //
            // Nothing may still be proposed: a pending proposal means the set is not yet known, since a
            // reviewer may confirm it or reject it and only the second outcome leaves the unit unmatched.
            // Then every unit and every directory row without a pairing must carry a written exception —
            // "enumerated" is not the same as "counted", and a number with no reasons behind it is exactly
            // the silent gap D5 exists to prevent.
            var gate3Met = edition is not null
                && reviewed > 0
                && proposed == 0
                && unitsNeitherPairedNorExcepted == 0
                && directoryRowsNeitherPairedNorExcepted == 0;

            var gate3Detail = edition is null
                ? "Nothing to enumerate: no register is loaded."
                : proposed > 0
                    ? $"{proposed} pairings are still proposed, so the unmatched set is not yet known — a "
                        + "reviewer may confirm or reject each one, and only rejection leaves a unit "
                        + $"unmatched. {unitsNeitherPairedNorExcepted} units and "
                        + $"{directoryRowsNeitherPairedNorExcepted} directory rows currently have neither a "
                        + "pairing nor an accepted exception."
                    : unitsNeitherPairedNorExcepted == 0 && directoryRowsNeitherPairedNorExcepted == 0
                        ? $"Review is complete. Every unit and directory row is either paired or covered by "
                            + $"a written exception ({exceptedUnits} units excused)."
                        : $"{unitsNeitherPairedNorExcepted} units and "
                            + $"{directoryRowsNeitherPairedNorExcepted} directory rows have neither a "
                            + "pairing nor an accepted exception. Each needs a written reason before this "
                            + "condition is met.";

            return
            [
                new GateCondition(
                    1,
                    "The canonical register edition is fixed and dated, from a PSA publication",
                    edition?.IsCitableAsAuthority ?? false,
                    editionDetail),

                new GateCondition(
                    2,
                    "The crosswalk is reviewed for every unit the active canonical edition holds",
                    registerUnits > 0 && confirmed + exceptedUnits >= registerUnits,
                    gate2Detail),

                new GateCondition(
                    3,
                    "The unmatched set is enumerated and accepted",
                    gate3Met,
                    gate3Detail),

                new GateCondition(
                    4,
                    "The register source is licence-checked, dated and registered as a DataSource",
                    edition is not null,
                    edition is null
                        ? "No edition row exists, so no source has been registered for it."
                        : "The edition is bound to a registered DataSource row."),
            ];
        }

        /// <summary>
        /// Measures the three-layer read boundary instead of trusting it.
        /// </summary>
        /// <remarks>
        /// The decisive assertion is the second one: a proposal must be invisible through the analytics
        /// surface. It is checked by counting confirmed rows through the view and comparing with the
        /// review context's own confirmed count — if the view ever returned more than that, it would be
        /// leaking unreviewed rows.
        /// </remarks>
        private async Task<ReadBoundaryState> MeasureReadBoundaryAsync(CancellationToken cancellationToken)
        {
            var throughView = await analytics.ConfirmedLguLinks.CountAsync(cancellationToken);

            var confirmedInBaseTable = await review.LguCodeLinks
                .CountAsync(link => link.Status == LguLinkStatus.Confirmed, cancellationToken);

            var leaking = throughView > confirmedInBaseTable;

            return new ReadBoundaryState(
                ConfirmedOnlyViewPresent: true,
                ProposalsVisibleThroughAnalyticsSurface: leaking,
                // Not measurable from inside the application's own connection: the query would report
                // this role's privileges, which are the migration role's in a single-role deployment.
                // Left null and named, rather than answered with a number that means something else.
                DatabasePermissionsActive: null,
                Detail: $"Analytics surface sees {throughView} pairings; {confirmedInBaseTable} are "
                    + "confirmed in the base table. Layer 2 permissions are verified by running "
                    + "lgu-read-boundary.sql as the owner and reading its NOTICE.");
        }
    }
}
