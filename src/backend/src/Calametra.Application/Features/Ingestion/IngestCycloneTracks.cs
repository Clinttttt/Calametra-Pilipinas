using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Ingestion;

/// <summary>
/// Imports tropical cyclone tracks from a best-track archive.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="HazardEvent"/> per storm, and one <c>CycloneTrackPoint</c> per agency per
/// fix. A storm analysed by four agencies over eighty six-hourly fixes therefore produces up
/// to 320 track points, and that multiplicity is the payload rather than redundancy — it is
/// what lets the platform show four different intensities for one moment.
/// </para>
/// <para>
/// Idempotent by storm identifier. A storm already present is skipped rather than merged,
/// because best-track data is a post-season reanalysis: it is republished as a whole when it
/// changes, so a partial update would leave a track that is half one revision and half
/// another.
/// </para>
/// </remarks>
public static class IngestCycloneTracks
{
    public sealed record Command : ICommand<IngestionSummary>
    {
        /// <summary>Earliest season to import. Storms before it are skipped.</summary>
        public int FromSeason { get; init; } = 1945;
    }

    /// <param name="StormsCreated">Storms newly stored.</param>
    /// <param name="StormsSkipped">Storms already present, left untouched.</param>
    /// <param name="TrackPointsCreated">Agency fixes stored across all new storms.</param>
    /// <param name="StormsRejected">
    /// Storms the domain refused, which should be zero. A non-zero value means an invariant
    /// was violated and is worth investigating rather than tolerating.
    /// </param>
    public sealed record IngestionSummary(
        int StormsCreated,
        int StormsSkipped,
        int TrackPointsCreated,
        int StormsRejected);

    internal sealed class Handler(
        IApplicationDbContext context,
        ICycloneTrackSource source,
        TimeProvider timeProvider) : ICommandHandler<Command, IngestionSummary>
    {
        private static readonly Error SourceNotRegistered = new(
            ErrorType.NotFound,
            "ingestion.source_not_registered",
            "The data source is not registered. Reference data must be seeded before ingestion runs.");

        public async Task<Result<IngestionSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var sources = await context.DataSources.AsNoTracking()
                .Select(dataSource => new { dataSource.Id, dataSource.Slug })
                .ToDictionaryAsync(entry => entry.Slug, entry => entry.Id, cancellationToken);

            if (!sources.TryGetValue(source.SourceSlug, out var archiveSourceId))
            {
                return Result<IngestionSummary>.Failure(SourceNotRegistered);
            }

            // Existing storm identifiers fetched once, from the track points rather than the
            // events: HazardEvent deliberately holds no external identity. The alternative — a
            // query per storm — would be hundreds of round trips against a set small enough to
            // hold in memory.
            var existing = await context.CycloneTrackPoints.AsNoTracking()
                .Select(trackPoint => trackPoint.ExternalStormId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var known = new HashSet<string>(existing, StringComparer.Ordinal);

            var created = 0;
            var skipped = 0;
            var rejected = 0;
            var trackPoints = 0;

            await foreach (var cyclone in source.StreamAsync(request.FromSeason, cancellationToken))
            {
                if (!known.Add(cyclone.ExternalId))
                {
                    skipped++;
                    continue;
                }

                var stored = StoreCyclone(cyclone, archiveSourceId, sources, now);

                if (stored is null)
                {
                    rejected++;
                    continue;
                }

                created++;
                trackPoints += stored.Value;

                // Saved in batches so a long run does not accumulate an unbounded change
                // tracker, and so an interruption leaves completed storms persisted.
                if (created % 25 == 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            return Result<IngestionSummary>.Success(
                new IngestionSummary(created, skipped, trackPoints, rejected));
        }

        /// <summary>
        /// Creates the event and its fixes, returning the number of fixes stored.
        /// </summary>
        /// <remarks>
        /// Returns null when the domain refuses the event, which the caller counts rather than
        /// throwing on. A malformed storm in a 100 MB archive should not end the run.
        /// </remarks>
        private int? StoreCyclone(
            CatalogCyclone cyclone,
            Guid archiveSourceId,
            Dictionary<string, Guid> sources,
            DateTimeOffset now)
        {
            // The canonical position is the first fix. Unlike an earthquake, a cyclone has no
            // single location — the canonical value exists only so the map has something to
            // place and the timeline something to sort by, which is the same compromise
            // HazardEvent already documents for earthquakes.
            var first = cyclone.Fixes[0];

            var creation = HazardEvent.Create(
                HazardEventType.TropicalCyclone,
                cyclone.StartedAt,
                Wgs84.Point(first.Latitude, first.Longitude),
                now,
                // IBTrACS writes "UNNAMED" for storms that never earned a name. Stored as null
                // rather than that placeholder, so the interface can say "unnamed" in its own
                // words instead of shouting the archive's.
                cyclone.Name.Equals("UNNAMED", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : cyclone.Name);

            if (creation.IsFailure)
            {
                return null;
            }

            var hazardEvent = creation.Value;
            var stored = 0;

            foreach (var fix in cyclone.Fixes)
            {
                if (!sources.TryGetValue(fix.SourceSlug, out var fixSourceId))
                {
                    // An agency present in the archive but not registered as a source. Skipped
                    // rather than attributed to the archive, because an unattributed reading is
                    // not storable in this system.
                    continue;
                }

                var result = hazardEvent.AddTrackPoint(
                    fixSourceId,
                    cyclone.ExternalId,
                    fix.CapturedAt,
                    Wgs84.Point(fix.Latitude, fix.Longitude),
                    now,
                    fix.Wind,
                    fix.MinimumPressureMillibars,
                    fix.Classification,
                    fix.DistanceToLandKm,
                    fix.IsLandfall,
                    fix.RadiusOfMaximumWindNm,
                    fix.RadiusOutermostIsobarNm,
                    fix.GaleField,
                    fix.StormField,
                    fix.HurricaneField);

                if (result.IsSuccess)
                {
                    stored++;
                }
            }

            if (stored == 0)
            {
                return null;
            }

            context.HazardEvents.Add(hazardEvent);

            return stored;
        }
    }
}
