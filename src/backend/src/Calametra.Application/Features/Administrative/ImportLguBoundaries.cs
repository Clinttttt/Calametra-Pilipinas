using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Attaches OSM boundary polygons to canonical PSGC units, by code and never by name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity is the whole difficulty, and the crosswalk is what solves it.</b> 111 city and
/// municipality names in this country are not unique, so a name match would be wrong in at least that
/// many places before anyone counts diacritics and "City of" prefixes. The Philippine OSM community
/// records the PSGC in the relation's <c>ref</c> tag, so that tag is the only thing consulted:
/// </para>
/// <list type="bullet">
/// <item>
/// A ten-digit <c>ref</c> is the current PSA code and matches the canonical unit directly. This is what
/// the mapped data mostly carries.
/// </item>
/// <item>
/// A nine-digit <c>ref</c> is the pre-2019 edition, and resolves through the <b>confirmed</b> crosswalk —
/// the reviewed pairings, not the proposals. This is the case the crosswalk was built for, and it is why
/// gate 2 had to close before any polygon could be stored.
/// </item>
/// <item>
/// Anything else is reported for a person to look at. Nothing is guessed from the name, the area or the
/// centroid.
/// </item>
/// </list>
/// <para>
/// <b>Two relations claiming one unit is a conflict, not a tie to break.</b> It usually means OSM holds a
/// duplicate or a stale relation, and choosing the larger or the newer would silently pick one rendering
/// of a boundary over another. Both are reported and neither is stored.
/// </para>
/// <para>
/// <b>Nothing is overwritten.</b> ADR-005 D7: a new version supersedes the previous one, which is retained
/// with the period it was in force. An identical geometry is not re-recorded at all, so re-running the
/// import does not manufacture versions.
/// </para>
/// </remarks>
public static class ImportLguBoundaries
{
    /// <param name="AdminLevel">6 for cities and municipalities in the Philippines.</param>
    public sealed record Command(int AdminLevel = 6) : ICommand<BoundaryImportSummary>;

    /// <param name="RelationsFetched">Relations the upstream served and this platform could assemble.</param>
    /// <param name="BoundariesStored">New versions written.</param>
    /// <param name="BoundariesUnchanged">
    /// Units whose stored outline already matched what the upstream served. Not re-recorded: a version per
    /// import run would make the history a log of when the importer ran rather than of when boundaries
    /// changed.
    /// </param>
    /// <param name="BoundariesSuperseded">Previous versions retired, retained with their validity period.</param>
    /// <param name="Repaired">Outlines stored after a recorded repair.</param>
    /// <param name="UnassembledRelations">Relations whose rings would not close.</param>
    /// <param name="UnmatchedRelations">Relations whose ref matched no canonical unit.</param>
    /// <param name="AmbiguousUnits">Units claimed by more than one relation. Nothing stored for these.</param>
    /// <param name="RejectedByDomain">Assembled outlines the domain refused, with the reason.</param>
    /// <param name="FailedChunks">Areas never fetched, so partial coverage is never silent.</param>
    public sealed record BoundaryImportSummary(
        string SourceSlug,
        DateTimeOffset ExtractedAt,
        string? ExtractVersion,
        int RelationsFetched,
        int BoundariesStored,
        int BoundariesUnchanged,
        int BoundariesSuperseded,
        int Repaired,
        int MatchedOnCanonicalCode,
        int MatchedOnConfirmedCrosswalk,
        IReadOnlyList<string> UnassembledRelations,
        IReadOnlyList<string> UnmatchedRelations,
        IReadOnlyList<string> AmbiguousUnits,
        IReadOnlyList<string> RejectedByDomain,
        IReadOnlyList<string> FailedChunks);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IApplicationDbContext analytics,
        ILguBoundarySource source,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, BoundaryImportSummary>
    {
        /// <summary>
        /// Finds or creates the row for this acquisition.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Keyed on the digest for a file, so re-reading the same extract reuses its row rather than adding
        /// a second provenance record for one file. That is what makes re-running the import safe: the
        /// partial Overpass acquisition and this extract are different rows, and the outlines from each stay
        /// attributable to what they actually came from.
        /// </para>
        /// <para>
        /// Keyed on label and route for an API read, which has no digest to key on — there is no single set
        /// of bytes when the answer arrives in fifty-four pieces from whichever mirror was free.
        /// </para>
        /// </remarks>
        private async Task<LguBoundaryExtract> ReconcileExtractAsync(
            BoundarySnapshot snapshot,
            Guid sourceId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var existing = snapshot.FileSha256 is { } digest
                ? await review.LguBoundaryExtracts
                    .FirstOrDefaultAsync(
                        candidate => candidate.FileSha256 == digest,
                        cancellationToken)
                : await review.LguBoundaryExtracts
                    .FirstOrDefaultAsync(
                        candidate => candidate.Label == snapshot.Label
                            && candidate.AccessRoute == snapshot.AccessRoute,
                        cancellationToken);

            if (existing is not null)
            {
                existing.WithUpstreamVintage(snapshot.Vintage);

                return existing;
            }

            var created = LguBoundaryExtract.Create(
                sourceId,
                snapshot.Label,
                snapshot.Provenance,
                snapshot.AccessRoute,
                now).Value;

            if (snapshot.OriginalFileName is not null && snapshot.FileSha256 is not null)
            {
                var recorded = created.WithFile(
                    snapshot.OriginalFileName,
                    snapshot.FileSha256,
                    snapshot.FileSizeBytes ?? 0L,
                    snapshot.Vintage,
                    snapshot.AcquisitionNote);

                if (recorded.IsFailure)
                {
                    // A malformed digest must not be recorded as provenance at all. Better to fail the
                    // import than to store geometry whose origin looks checkable and is not.
                    throw new InvalidOperationException(
                        $"The extract's acquisition chain was rejected: {recorded.Error!.Code} — "
                        + recorded.Error.Description);
                }
            }
            else
            {
                created.WithUpstreamVintage(snapshot.Vintage);
            }

            review.LguBoundaryExtracts.Add(created);

            return created;
        }

        /// <summary>The slug of the boundary <c>DataSource</c>, which must exist before geometry is stored.</summary>
        private const string BoundarySourceSlug = "openstreetmap-ph-admin-boundaries";

        private static readonly Error SourceNotRegistered = new(
            ErrorType.Validation,
            "lgu_boundary.source_not_registered",
            "The OpenStreetMap boundary DataSource is not registered. ADR-005 gate condition 4 requires "
            + "the extract to be licence-checked, dated and registered before any geometry is stored.");

        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "lgu_boundary.no_current_edition",
            "No current PSGC register edition is held, so there are no canonical units to attach geometry "
            + "to.");

        public async Task<Result<BoundaryImportSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var now = timeProvider.GetUtcNow();

            var source_ = await analytics.DataSources
                .FirstOrDefaultAsync(candidate => candidate.Slug == BoundarySourceSlug, cancellationToken);

            if (source_ is null)
            {
                return Result<BoundaryImportSummary>.Failure(SourceNotRegistered);
            }

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<BoundaryImportSummary>.Failure(NoCurrentEdition);
            }

            var units = await review.Lgus
                .Where(lgu => lgu.RegisterEditionId == edition.Id
                    && (lgu.Level == LguLevel.City || lgu.Level == LguLevel.Municipality))
                .Select(lgu => new { lgu.Id, lgu.CanonicalPsgcCode, lgu.Name })
                .ToListAsync(cancellationToken);

            var byCanonicalCode = units.ToDictionary(
                unit => unit.CanonicalPsgcCode,
                StringComparer.Ordinal);

            // The reviewed pairings only. Reading proposals here would let an unconfirmed guess decide
            // which unit a polygon belongs to, which is the exact substitution ADR-005 D4 forbids — and it
            // would do it in the geometry, where it is hardest to notice afterwards.
            var confirmed = await analytics.ConfirmedLguLinks
                .Select(link => new { link.LguId, link.HistoricalPsgcCode })
                .ToListAsync(cancellationToken);

            var unitIds = units.Select(unit => unit.Id).ToHashSet();

            var byHistoricalCode = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var link in confirmed)
            {
                if (unitIds.Contains(link.LguId))
                {
                    byHistoricalCode.TryAdd(link.HistoricalPsgcCode, link.LguId);
                }
            }

            var chunks = PhilippineBoundaryChunks.All;

            var snapshot = await source.ReadAsync(request.AdminLevel, chunks, cancellationToken);

            // The dated acquisition, reconciled rather than duplicated. Re-reading the same file — same
            // digest — reuses its row, so a re-run does not manufacture a second provenance record for one
            // extract.
            var extract = await ReconcileExtractAsync(snapshot, source_.Id, now, cancellationToken);

            var unitById = units.ToDictionary(unit => unit.Id);
            var claims = new Dictionary<Guid, List<BoundaryFeature>>();
            var unmatched = new List<string>();
            var matchedOnCanonical = 0;
            var matchedOnCrosswalk = 0;

            foreach (var feature in snapshot.Features)
            {
                var reference = feature.RefTag?.Trim();

                if (string.IsNullOrEmpty(reference))
                {
                    unmatched.Add($"relation {feature.OsmRelationId} '{feature.Name}': no ref tag");

                    continue;
                }

                Guid? lguId = null;

                if (reference.Length == 10 && byCanonicalCode.TryGetValue(reference, out var direct))
                {
                    lguId = direct.Id;
                    matchedOnCanonical++;
                }
                else if (reference.Length == 9 && byHistoricalCode.TryGetValue(reference, out var viaLink))
                {
                    lguId = viaLink;
                    matchedOnCrosswalk++;
                }

                if (lguId is null)
                {
                    unmatched.Add(
                        $"relation {feature.OsmRelationId} '{feature.Name}': ref '{reference}' matches no "
                        + "canonical unit or confirmed pairing");

                    continue;
                }

                if (!claims.TryGetValue(lguId.Value, out var list))
                {
                    list = [];
                    claims[lguId.Value] = list;
                }

                list.Add(feature);
            }

            var existing = await review.LguBoundaries
                .Where(boundary => boundary.ValidTo == null)
                .ToListAsync(cancellationToken);

            var inForceByLgu = new Dictionary<Guid, LguBoundary>();

            foreach (var boundary in existing)
            {
                inForceByLgu.TryAdd(boundary.LguId, boundary);
            }

            var stored = 0;
            var unchanged = 0;
            var superseded = 0;
            var repaired = 0;
            var ambiguous = new List<string>();
            var rejected = new List<string>();
            foreach (var (lguId, features) in claims)
            {
                var unit = unitById[lguId];

                if (features.Count > 1)
                {
                    var relations = string.Join(", ", features.ConvertAll(item => item.OsmRelationId));
                    ambiguous.Add(
                        $"{unit.CanonicalPsgcCode} {unit.Name}: claimed by relations {relations}; nothing "
                        + "stored");

                    BoundaryImportLog.AmbiguousUnit(logger, unit.CanonicalPsgcCode, unit.Name, relations);

                    continue;
                }

                var feature = features[0];

                inForceByLgu.TryGetValue(lguId, out var current);

                // Re-recording an identical outline would turn the version history into a log of importer
                // runs. D7 versions boundaries because they change by legislation, not by re-reading.
                if (current is not null
                    && current.OsmRelationId == feature.OsmRelationId
                    && current.Geometry.EqualsTopologically(feature.Geometry))
                {
                    unchanged++;

                    continue;
                }

                var recorded = LguBoundary.Record(
                    lguId,
                    unit.CanonicalPsgcCode,
                    source_.Id,
                    extract.Id,
                    feature.OsmRelationId,
                    sourceFeatureCode: null,
                    feature.RefTag,
                    feature.Name,
                    feature.AdminLevel,
                    feature.Geometry,
                    feature.AreaSquareKm,
                    snapshot.ExtractedAt,
                    snapshot.ExtractVersion,
                    feature.WasRepaired,
                    feature.RepairNote,
                    now);

                if (recorded.IsFailure)
                {
                    rejected.Add(
                        $"{unit.CanonicalPsgcCode} {unit.Name}: {recorded.Error!.Code}");

                    continue;
                }

                review.LguBoundaries.Add(recorded.Value);
                stored++;

                if (feature.WasRepaired)
                {
                    repaired++;
                }

                if (current is not null)
                {
                    current.Supersede(recorded.Value.Id, now);
                    superseded++;
                }
            }

            await review.SaveChangesAsync(cancellationToken);

            extract.RecordBoundariesRead(stored, now);

            await review.SaveChangesAsync(cancellationToken);

            var unassembled = snapshot.Unassembled
                .Select(item => $"relation {item.OsmRelationId} '{item.Name}' (ref {item.RefTag}): {item.Reason}")
                .ToList();

            BoundaryImportLog.Completed(
                logger,
                snapshot.Features.Count,
                stored,
                unchanged,
                superseded,
                unmatched.Count,
                ambiguous.Count);

            return Result<BoundaryImportSummary>.Success(new BoundaryImportSummary(
                BoundarySourceSlug,
                snapshot.ExtractedAt,
                snapshot.ExtractVersion,
                snapshot.Features.Count,
                stored,
                unchanged,
                superseded,
                repaired,
                matchedOnCanonical,
                matchedOnCrosswalk,
                unassembled,
                unmatched,
                ambiguous,
                rejected,
                snapshot.FailedChunks));
        }
    }
}

internal static partial class BoundaryImportLog
{
    [LoggerMessage(
        EventId = 7310,
        Level = LogLevel.Warning,
        Message = "Unit {CanonicalCode} '{Name}' is claimed by more than one OSM relation ({Relations}). "
            + "Nothing stored: choosing between them would pick one rendering of a boundary over another.")]
    public static partial void AmbiguousUnit(
        ILogger logger,
        string canonicalCode,
        string name,
        string relations);

    [LoggerMessage(
        EventId = 7311,
        Level = LogLevel.Information,
        Message = "Boundary import complete: {Fetched} relation(s) assembled, {Stored} stored, "
            + "{Unchanged} unchanged, {Superseded} superseded, {Unmatched} unmatched, {Ambiguous} ambiguous")]
    public static partial void Completed(
        ILogger logger,
        int fetched,
        int stored,
        int unchanged,
        int superseded,
        int unmatched,
        int ambiguous);
}
