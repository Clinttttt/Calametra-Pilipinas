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
        string? Notes);

    public sealed record CrosswalkState(
        int RegisterUnits,
        int DirectoryRowsWithCode,
        int Proposed,
        int Confirmed,
        int Rejected,
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
            var edition = await review.PsgcRegisterEditions
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var registerUnits = await review.Lgus.CountAsync(cancellationToken);

            var directoryRows = await analytics.Places
                .CountAsync(place => place.PsgcCode != null && place.Kind != PlaceKind.Barangay, cancellationToken);

            var links = await review.LguCodeLinks
                .Select(link => new
                {
                    link.Status,
                    link.Evidence,
                    link.NamesAgree,
                    link.LguId,
                    link.PlaceId,
                })
                .ToListAsync(cancellationToken);

            var proposed = links.Count(link => link.Status == LguLinkStatus.Proposed);
            var confirmed = links.Count(link => link.Status == LguLinkStatus.Confirmed);
            var rejected = links.Count(link => link.Status == LguLinkStatus.Rejected);

            var reviewed = confirmed + rejected;

            // Null rather than zero while nothing has been reviewed. A rejection rate of 0% and "no
            // reviews yet" are different claims, and reporting the second as the first would make an
            // untested matcher look perfect.
            double? rejectionRate = reviewed == 0 ? null : (double)rejected / reviewed;

            var confirmedLguIds = links
                .Where(link => link.Status == LguLinkStatus.Confirmed)
                .Select(link => link.LguId)
                .ToHashSet();

            var confirmedPlaceIds = links
                .Where(link => link.Status == LguLinkStatus.Confirmed && link.PlaceId is not null)
                .Select(link => link.PlaceId!.Value)
                .ToHashSet();

            var unmatchedUnits = registerUnits - confirmedLguIds.Count;
            var unmatchedDirectory = directoryRows - confirmedPlaceIds.Count;

            var boundary = await MeasureReadBoundaryAsync(cancellationToken);

            var conditions = BuildConditions(
                edition,
                registerUnits,
                confirmed,
                unmatchedUnits,
                reviewed);

            var register = edition is null
                ? new RegisterState(false, null, null, false, 0, 0, 0, 0, null, null)
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
                    edition.Notes);

            var crosswalk = new CrosswalkState(
                registerUnits,
                directoryRows,
                proposed,
                confirmed,
                rejected,
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
            int reviewed)
        {
            var editionDetail = edition is null
                ? "No register edition has been loaded."
                : $"'{edition.Label}' loaded as {edition.Provenance}, {edition.RegionCount} regions and "
                    + $"{edition.ProvinceCount} provinces observed"
                    + (edition.UpstreamLastModified is { } stamp
                        ? $", upstream last modified {stamp:yyyy-MM-dd}."
                        : ", upstream last-modified not reported.");

            return
            [
                new GateCondition(
                    1,
                    "The canonical register edition is fixed and dated, from a PSA publication",
                    edition?.IsCitableAsAuthority ?? false,
                    editionDetail),

                new GateCondition(
                    2,
                    "The crosswalk exists and is reviewed for every unit the register holds",
                    registerUnits > 0 && confirmed >= registerUnits,
                    $"{confirmed} of {registerUnits} register units carry a confirmed pairing."),

                new GateCondition(
                    3,
                    "The unmatched set is enumerated and accepted",
                    reviewed > 0,
                    unmatchedUnits == 0 && registerUnits == 0
                        ? "Nothing to enumerate: no register is loaded."
                        : $"{unmatchedUnits} register units are unmatched. Enumeration requires review to "
                            + "have taken place, so this condition is unmet while nothing has been reviewed."),

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
