using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Ingestion;

/// <summary>
/// Imports the administrative place directory — regions, provinces, cities and municipalities.
/// </summary>
/// <remarks>
/// <para>
/// This is what lets the platform answer "what has happened here?" for a place a reader can
/// name. Until it runs, the archive can only be interrogated by coordinate, and no stored
/// earthquake carries a descriptive location: <c>HazardEvent</c> holds none, and the sub-areas
/// on <c>PhilippineStudyArea</c> are viewport hints that overlap, so an event at 9°N 125°E falls
/// in Caraga, Visayas and Mindanao at once and cannot be labelled from them.
/// </para>
/// <para>
/// <b>Create-only, and idempotent.</b> An existing place is left untouched rather than
/// reconciled. That is the opposite of the fault import, which revises in place, and the reason
/// is that a place row is a join target: population figures, and later boundaries, hang off it,
/// and a re-import that replaced rows would orphan them. A renamed or newly created
/// municipality is a deliberate operation, not something a refresh should infer.
/// </para>
/// <para>
/// <b>Parents are resolved within the run.</b> The source yields coarsest level first, so a
/// province's region is already stored by the time the province is created. Nothing is inferred
/// from names.
/// </para>
/// </remarks>
public static class ImportPlaces
{
    public sealed record Command : ICommand<PlaceImportSummary>;

    /// <param name="FetchedCount">Places offered by the source.</param>
    /// <param name="CreatedCount">Places newly stored.</param>
    /// <param name="SkippedCount">Places already present, left untouched.</param>
    /// <param name="RejectedCount">
    /// Places the domain refused, which should be zero. A non-zero value means an invariant was
    /// violated and is worth investigating rather than tolerating.
    /// </param>
    /// <param name="WithoutPsgcCodeCount">
    /// Places stored with no Philippine Standard Geographic Code. Reported rather than hidden:
    /// these are the places that cannot be referenced durably in a shared link, so the figure is
    /// the size of a known limitation.
    /// </param>
    /// <param name="UnresolvedParentCount">
    /// Places whose stated parent could not be found. Expected to be zero; a non-zero value means
    /// the hierarchy is broken and search results will be missing their containing province.
    /// </param>
    public sealed record PlaceImportSummary(
        string SourceSlug,
        int FetchedCount,
        int CreatedCount,
        int SkippedCount,
        int RejectedCount,
        int WithoutPsgcCodeCount,
        int UnresolvedParentCount);

    internal sealed class Handler(
        IApplicationDbContext context,
        IPlaceDirectorySource placeSource,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, PlaceImportSummary>
    {
        private static readonly Error SourceNotRegistered = new(
            ErrorType.NotFound,
            "place_import.source_not_registered",
            "The gazetteer is not registered. Reference data must be seeded before ingestion runs.");

        public async Task<Result<PlaceImportSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var source = await context.DataSources
                .FirstOrDefaultAsync(candidate => candidate.Slug == placeSource.SourceSlug, cancellationToken);

            if (source is null)
            {
                return Result<PlaceImportSummary>.Failure(SourceNotRegistered);
            }

            if (!source.IsRedistributable)
            {
                // A gazetteer whose terms do not permit storage may not be copied into the
                // database, exactly as a hazard layer may not be. Refused at the source rather
                // than per row, so the failure names the real problem.
                return Result<PlaceImportSummary>.Failure(PlaceImportErrors.SourceNotRedistributable);
            }

            var fetched = await placeSource.FetchAsync(cancellationToken);

            // The whole directory is under two thousand rows, so existing places are read once
            // into memory. A query per candidate would be eighteen hundred round trips to
            // discover, on a second run, that nothing has changed.
            var existing = await context.Places.AsNoTracking()
                .Select(place => new
                {
                    place.Id,
                    place.PsgcCode,
                    place.Kind,
                    place.Name,
                    place.ParentPlaceId,
                })
                .ToListAsync(cancellationToken);

            var byCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var byIdentity = new Dictionary<string, Guid>(StringComparer.Ordinal);

            foreach (var place in existing)
            {
                if (place.PsgcCode is not null)
                {
                    byCode.TryAdd(place.PsgcCode, place.Id);
                }

                byIdentity.TryAdd(Identity(place.Kind, place.Name, place.ParentPlaceId), place.Id);
            }

            var idsByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);

            var created = 0;
            var skipped = 0;
            var rejected = 0;
            var withoutCode = 0;
            var unresolvedParents = 0;

            foreach (var candidate in fetched)
            {
                Guid? parentId = null;

                if (candidate.ParentKey is { } parentKey)
                {
                    if (idsByKey.TryGetValue(parentKey, out var resolved))
                    {
                        parentId = resolved;
                    }
                    else
                    {
                        // Stored without its parent rather than dropped: a municipality with an
                        // unresolved province is still a searchable place, and losing it would
                        // be the worse outcome. Counted so the gap is visible.
                        unresolvedParents++;
                    }
                }

                if (candidate.PsgcCode is not null && byCode.TryGetValue(candidate.PsgcCode, out var existingByCode))
                {
                    idsByKey[candidate.Key] = existingByCode;
                    skipped++;

                    continue;
                }

                var identity = Identity(candidate.Kind, candidate.Name, parentId);

                if (candidate.PsgcCode is null && byIdentity.TryGetValue(identity, out var existingByIdentity))
                {
                    // Only for code-less places. Where a code exists it is the identity, and
                    // matching on name as well would merge two distinct municipalities that
                    // happen to share one — 111 of the 1,646 names in this country are not
                    // unique.
                    idsByKey[candidate.Key] = existingByIdentity;
                    skipped++;

                    continue;
                }

                var creation = Place.Create(
                    candidate.Name,
                    candidate.Kind,
                    Wgs84.Point(candidate.Latitude, candidate.Longitude),
                    source.Id,
                    now);

                if (creation.IsFailure)
                {
                    rejected++;

                    continue;
                }

                var place = creation.Value.WithHierarchy(candidate.PsgcCode, parentId);

                context.Places.Add(place);

                idsByKey[candidate.Key] = place.Id;

                if (candidate.PsgcCode is null)
                {
                    withoutCode++;
                }
                else
                {
                    byCode[candidate.PsgcCode] = place.Id;
                }

                byIdentity.TryAdd(identity, place.Id);

                created++;

                // Saved level by level rather than at the end, because a child's foreign key
                // points at a parent created in this same run.
                if (created % 250 == 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            source.RecordRetrieval(now);

            await context.SaveChangesAsync(cancellationToken);

            var summary = new PlaceImportSummary(
                placeSource.SourceSlug,
                fetched.Count,
                created,
                skipped,
                rejected,
                withoutCode,
                unresolvedParents);

            PlaceImportLog.Completed(
                logger,
                summary.SourceSlug,
                created,
                skipped,
                rejected,
                withoutCode,
                unresolvedParents);

            return Result<PlaceImportSummary>.Success(summary);
        }

        /// <summary>
        /// Identity for a place with no code: its level, its name and its container.
        /// </summary>
        /// <remarks>
        /// The container is essential. Name and level alone are not unique in this country —
        /// there are several San Isidros at municipality level — so a key without the parent
        /// would collapse distinct places into one.
        /// </remarks>
        private static string Identity(PlaceKind kind, string name, Guid? parentPlaceId) =>
            $"{(int)kind}|{name.Trim().ToUpperInvariant()}|{parentPlaceId?.ToString() ?? "-"}";
    }
}

internal static class PlaceImportErrors
{
    public static readonly Error SourceNotRedistributable = new(
        ErrorType.Conflict,
        "place_import.source_not_redistributable",
        "This gazetteer's terms do not permit storing its contents, so places cannot be imported "
        + "from it.");
}

internal static partial class PlaceImportLog
{
    [LoggerMessage(
        EventId = 4200,
        Level = LogLevel.Information,
        Message = "Place import from {SourceSlug}: {CreatedCount} created, {SkippedCount} already present, "
            + "{RejectedCount} rejected, {WithoutPsgcCodeCount} without a PSGC code, "
            + "{UnresolvedParentCount} without a resolved parent")]
    public static partial void Completed(
        ILogger logger,
        string sourceSlug,
        int createdCount,
        int skippedCount,
        int rejectedCount,
        int withoutPsgcCodeCount,
        int unresolvedParentCount);
}
