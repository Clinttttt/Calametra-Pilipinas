using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// Imports the canonical on-land administrative geometry, attaching it by reviewed identity only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two routes to a unit, both by code.</b> Either the active register uses the same ten-digit code the
/// boundary set does, or a <em>confirmed</em> edition correspondence says what that code now means. There is
/// no third route: a feature whose code satisfies neither is reported and not stored, because 1,647 units in
/// the comparable republication of this data share only 1,424 distinct names and matching on those would be
/// wrong in at least 223 places.
/// </para>
/// <para>
/// <b>Land outlines only, per ADR-005 D2a.</b> These polygons stop at the coast. They are stored in the same
/// versioned table as the earlier OSM outlines and they supersede them, which is exactly why they may not sit
/// alongside them in force: OSM boundaries extend to municipal waters and mixing the two would make one
/// column mean two things. Superseding rather than deleting keeps the earlier figures explainable.
/// </para>
/// </remarks>
public static class ImportCodAbBoundaries
{
    public sealed record Command : ICommand<CodAbImportSummary>;

    /// <param name="MatchedOnCorrespondence">
    /// Units reached through a confirmed edition correspondence rather than a direct code match. These are
    /// the recodings — the Negros Island Region, Sulu's transfer, the highly urbanised cities.
    /// </param>
    /// <param name="UnmatchedFeatures">
    /// Features whose code neither the active register nor a confirmed correspondence claims. Reported, never
    /// name-matched.
    /// </param>
    public sealed record CodAbImportSummary(
        string SourceSlug,
        string ExtractLabel,
        string? OriginalFileName,
        string? FileSha256,
        DateOnly? Vintage,
        int FeaturesRead,
        int MatchedOnCanonicalCode,
        int MatchedOnCorrespondence,
        IReadOnlyList<string> CodeNameConflicts,
        int Stored,
        int Unchanged,
        int Superseded,
        int Repaired,
        IReadOnlyList<string> UnmatchedFeatures,
        IReadOnlyList<string> AmbiguousUnits,
        IReadOnlyList<string> RejectedByReader,
        IReadOnlyList<string> RejectedByDomain);

    internal sealed class Handler(
        ILguCrosswalkReviewContext review,
        IApplicationDbContext analytics,
        ICanonicalBoundarySource source,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, CodAbImportSummary>
    {
        private static readonly Error SourceNotRegistered = new(
            ErrorType.Validation,
            "cod_ab.source_not_registered",
            "The COD-AB DataSource is not registered. ADR-005 gate condition 4 requires the geometry source "
            + "to be licence-checked, dated and registered before any geometry is stored.");

        private static readonly Error NoCurrentEdition = new(
            ErrorType.Validation,
            "cod_ab.no_current_edition",
            "No current PSGC register edition is held, so there are no canonical units to attach geometry to.");

        public async Task<Result<CodAbImportSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var dataSource = await analytics.DataSources
                .FirstOrDefaultAsync(candidate => candidate.Slug == source.SourceSlug, cancellationToken);

            if (dataSource is null)
            {
                return Result<CodAbImportSummary>.Failure(SourceNotRegistered);
            }

            var edition = await review.PsgcRegisterEditions
                .Where(candidate => candidate.SupersededAt == null)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (edition is null)
            {
                return Result<CodAbImportSummary>.Failure(NoCurrentEdition);
            }

            var units = await review.Lgus
                .Where(lgu => lgu.RegisterEditionId == edition.Id
                    && (lgu.Level == LguLevel.City || lgu.Level == LguLevel.Municipality))
                .Select(lgu => new { lgu.Id, lgu.CanonicalPsgcCode, lgu.Name })
                .ToListAsync(cancellationToken);

            var byCanonicalCode = units.ToDictionary(unit => unit.CanonicalPsgcCode, StringComparer.Ordinal);
            var unitById = units.ToDictionary(unit => unit.Id);

            // Confirmed correspondences only. A proposal deciding what a legacy code means would let
            // unreviewed identity place a polygon, which is the substitution ADR-005 D4 forbids.
            var correspondences = await review.LguEditionCorrespondences
                .Where(item => item.Status == LguLinkStatus.Confirmed)
                .Select(item => new { item.LegacyCanonicalPsgcCode, item.CurrentLguId })
                .ToListAsync(cancellationToken);

            var byLegacyCode = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var item in correspondences)
            {
                if (unitById.ContainsKey(item.CurrentLguId))
                {
                    byLegacyCode.TryAdd(item.LegacyCanonicalPsgcCode, item.CurrentLguId);
                }
            }

            var snapshot = await source.ReadAsync(cancellationToken);

            var extract = await ReconcileExtractAsync(snapshot, dataSource.Id, now, cancellationToken);

            var claims = new Dictionary<Guid, List<CanonicalBoundaryFeature>>();
            var unmatched = new List<string>();
            var conflicts = new List<string>();
            var onCanonical = 0;
            var onCorrespondence = 0;

            foreach (var feature in snapshot.Features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Guid? lguId = null;
                var route = string.Empty;

                if (byCanonicalCode.TryGetValue(feature.CanonicalCode, out var direct))
                {
                    // A matching code is not sufficient on its own, and Maguindanao is why.
                    //
                    // COD-AB and PSA 2Q 2026 both number that province's municipalities in the 1908 block
                    // and they do not agree on which number is which municipality. Sixteen outlines attached
                    // silently to the wrong unit on an exact code match — the register's Matanog received
                    // COD-AB's Datu Odin Sinsuat, its South Upi received Pagagawan — and the error only
                    // surfaced because town centres then fell up to 63 km outside their own municipality.
                    //
                    // So the publisher's own name has to agree as well. A code is an assertion about
                    // identity and here two publishers make incompatible ones; the name is the independent
                    // check that catches it. Where they disagree nothing is stored and the conflict is
                    // reported, because the alternative is geometry that is confidently wrong.
                    if (NamesMatch(feature.Name, direct.Name))
                    {
                        lguId = direct.Id;
                        onCanonical++;
                        route = "canonical code";
                    }
                    else
                    {
                        conflicts.Add(
                            $"{feature.CanonicalCode}: the register calls this unit '{direct.Name}' and the "
                            + $"source calls it '{feature.Name}'. Same code, different municipality — "
                            + "nothing stored.");

                        continue;
                    }
                }
                else if (byLegacyCode.TryGetValue(feature.CanonicalCode, out var viaCorrespondence))
                {
                    lguId = viaCorrespondence;
                    onCorrespondence++;
                    route = "confirmed correspondence";
                }

                if (lguId is null)
                {
                    unmatched.Add(
                        $"{feature.CanonicalCode} '{feature.Name}': no current unit and no confirmed "
                        + "edition correspondence");

                    continue;
                }

                if (!claims.TryGetValue(lguId.Value, out var list))
                {
                    list = [];
                    claims[lguId.Value] = list;
                }

                list.Add(feature);
            }

            var inForce = await review.LguBoundaries
                .Where(boundary => boundary.ValidTo == null)
                .ToListAsync(cancellationToken);

            var inForceByLgu = new Dictionary<Guid, LguBoundary>();

            foreach (var boundary in inForce)
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
                    var codes = string.Join(", ", features.ConvertAll(item => item.CanonicalCode));
                    ambiguous.Add($"{unit.CanonicalPsgcCode} {unit.Name}: claimed by {codes}; nothing stored");

                    continue;
                }

                var feature = features[0];

                inForceByLgu.TryGetValue(lguId, out var current);

                if (current is not null
                    && string.Equals(current.SourceFeatureCode, feature.CanonicalCode, StringComparison.Ordinal)
                    && current.ExtractId == extract.Id)
                {
                    unchanged++;

                    continue;
                }

                var recorded = LguBoundary.Record(
                    lguId,
                    unit.CanonicalPsgcCode,
                    dataSource.Id,
                    extract.Id,
                    osmRelationId: null,
                    feature.CanonicalCode,
                    feature.PublishedCode,
                    feature.Name,
                    osmAdminLevel: 0,
                    feature.Geometry,
                    feature.AreaSquareKm,
                    snapshot.ExtractedAt,
                    snapshot.Vintage?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    feature.WasRepaired,
                    feature.RepairNote,
                    now);

                if (recorded.IsFailure)
                {
                    rejected.Add($"{unit.CanonicalPsgcCode} {unit.Name}: {recorded.Error!.Code}");

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

            CodAbImportLog.Completed(logger, snapshot.Features.Count, stored, superseded, unmatched.Count);

            return Result<CodAbImportSummary>.Success(new CodAbImportSummary(
                source.SourceSlug,
                extract.Label,
                extract.OriginalFileName,
                extract.FileSha256,
                extract.Vintage,
                snapshot.Features.Count,
                onCanonical,
                onCorrespondence,
                conflicts,
                stored,
                unchanged,
                superseded,
                repaired,
                unmatched,
                ambiguous,
                snapshot.Rejected,
                rejected));
        }

        /// <summary>
        /// Whether the publisher and the register call a unit the same thing.
        /// </summary>
        /// <remarks>
        /// Folds case, diacritics and punctuation, drops the "City of" / "City" forms the two use
        /// interchangeably, and ignores parenthesised former names — COD-AB writes "Kabuntalan (Tumbao)" and
        /// "Shariff Aguak (Maganoy) (Capital)" where the register writes the current name alone.
        /// </remarks>
        private static bool NamesMatch(string? left, string? right) =>
            !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(Fold(left), Fold(right), StringComparison.Ordinal);

        private static string Fold(string value)
        {
            var withoutParentheticals = System.Text.RegularExpressions.Regex.Replace(value, @"\([^)]*\)", " ");

            var withoutTitles = withoutParentheticals
                .Replace("City of", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("Municipality of", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("City", " ", StringComparison.OrdinalIgnoreCase);

            var decomposed = withoutTitles.Normalize(System.Text.NormalizationForm.FormD);
            var builder = new System.Text.StringBuilder(decomposed.Length);

            foreach (var character in decomposed)
            {
                if (char.IsAsciiLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        private async Task<LguBoundaryExtract> ReconcileExtractAsync(
            CanonicalBoundarySnapshot snapshot,
            Guid sourceId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var existing = await review.LguBoundaryExtracts
                .FirstOrDefaultAsync(
                    candidate => candidate.FileSha256 == snapshot.FileSha256,
                    cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            var created = LguBoundaryExtract.Create(
                sourceId,
                snapshot.Label,
                snapshot.Provenance,
                snapshot.AccessRoute,
                now).Value;

            var recorded = created.WithFile(
                snapshot.OriginalFileName,
                snapshot.FileSha256,
                snapshot.FileSizeBytes,
                snapshot.Vintage,
                snapshot.AcquisitionNote);

            if (recorded.IsFailure)
            {
                throw new InvalidOperationException(
                    $"The extract's acquisition chain was rejected: {recorded.Error!.Code} — "
                    + recorded.Error.Description);
            }

            review.LguBoundaryExtracts.Add(created);

            return created;
        }
    }
}

internal static partial class CodAbImportLog
{
    [LoggerMessage(
        EventId = 7360,
        Level = LogLevel.Information,
        Message = "COD-AB import complete: {Features} feature(s) read, {Stored} stored, {Superseded} "
            + "superseded, {Unmatched} unmatched")]
    public static partial void Completed(
        ILogger logger,
        int features,
        int stored,
        int superseded,
        int unmatched);
}
