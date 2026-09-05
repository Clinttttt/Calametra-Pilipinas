using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Sources;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Ingestion;

/// <summary>
/// Pulls a window of earthquakes from an upstream catalogue and reconciles them into
/// the local archive.
/// </summary>
/// <remarks>
/// Idempotent by design: a record already present is revised in place rather than
/// inserted again, keyed on (source, the source's own identifier). This matters
/// because agencies routinely refine origin, depth and magnitude for days after an
/// event, so re-reading a window is normal operation rather than an error.
/// <para>
/// Lives in the Application layer, not in the worker host, so the same behaviour is
/// reachable from a scheduled job, an administrative endpoint, or a test without
/// being reimplemented.
/// </para>
/// </remarks>
public static class IngestEarthquakeCatalog
{
    public sealed record Command : ICommand<IngestionSummary>
    {
        public required DateTimeOffset From { get; init; }

        public required DateTimeOffset To { get; init; }

        /// <summary>Magnitude floor. Omitted requests everything the source holds.</summary>
        public double? MinMagnitude { get; init; }
    }

    /// <summary>What an ingestion run did, for logging and for the sync indicator.</summary>
    public sealed record IngestionSummary(
        string SourceSlug,
        int FetchedCount,
        int CreatedCount,
        int RevisedCount,
        int SkippedCount);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.To).GreaterThan(command => command.From);

            // A single request covering decades would time out upstream and return a
            // payload too large to process. Backfill is done in windows by the caller.
            RuleFor(command => command)
                .Must(command => command.To - command.From <= TimeSpan.FromDays(370))
                .WithMessage("Ingest at most one year per run; backfill in successive windows.");
        }
    }

    internal sealed class Handler(
        IApplicationDbContext context,
        IEarthquakeCatalogSource catalogSource,
        TimeProvider timeProvider,
        ILogger<Handler> logger)
        : ICommandHandler<Command, IngestionSummary>
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

            var source = await context.DataSources
                .FirstOrDefaultAsync(candidate => candidate.Slug == catalogSource.SourceSlug, cancellationToken);

            if (source is null)
            {
                return Result<IngestionSummary>.Failure(SourceNotRegistered);
            }

            var fetch = await catalogSource.FetchAsync(
                new EarthquakeCatalogQuery
                {
                    From = request.From,
                    To = request.To,
                    MinLatitude = PhilippineStudyArea.MinLatitude,
                    MaxLatitude = PhilippineStudyArea.MaxLatitude,
                    MinLongitude = PhilippineStudyArea.MinLongitude,
                    MaxLongitude = PhilippineStudyArea.MaxLongitude,
                    MinMagnitude = request.MinMagnitude,
                },
                cancellationToken);

            if (fetch.IsFailure)
            {
                return Result<IngestionSummary>.Failure(fetch.Error!);
            }

            var fetched = fetch.Value;
            var externalIds = fetched.Select(item => item.ExternalId).ToHashSet(StringComparer.Ordinal);

            // One round trip to find everything already known, rather than a query per record.
            var existing = await context.EventObservations
                .Where(observation => observation.DataSourceId == source.Id
                    && externalIds.Contains(observation.ExternalEventId))
                .ToDictionaryAsync(
                    observation => observation.ExternalEventId,
                    cancellationToken);

            var created = 0;
            var revised = 0;
            var skipped = 0;

            foreach (var incoming in fetched)
            {
                var epicenter = Wgs84.Point(incoming.Latitude, incoming.Longitude);

                if (existing.TryGetValue(incoming.ExternalId, out var observation))
                {
                    observation.Revise(
                        incoming.OccurredAt,
                        epicenter,
                        incoming.Depth,
                        incoming.Magnitude,
                        now);

                    revised++;

                    continue;
                }

                var creation = HazardEvent.Create(
                    HazardEventType.Earthquake,
                    incoming.OccurredAt,
                    epicenter,
                    now);

                if (creation.IsFailure)
                {
                    skipped++;

                    continue;
                }

                var hazardEvent = creation.Value;

                var added = hazardEvent.AddObservation(
                    source.Id,
                    incoming.ExternalId,
                    incoming.OccurredAt,
                    epicenter,
                    incoming.Depth,
                    incoming.Magnitude,
                    now,
                    incoming.SourceUrl);

                if (added.IsFailure)
                {
                    skipped++;

                    continue;
                }

                context.HazardEvents.Add(hazardEvent);
                created++;
            }

            source.RecordRetrieval(now);

            await context.SaveChangesAsync(cancellationToken);

            var summary = new IngestionSummary(
                catalogSource.SourceSlug,
                fetched.Count,
                created,
                revised,
                skipped);

            IngestionLog.Completed(logger, summary.SourceSlug, created, revised, skipped);

            return Result<IngestionSummary>.Success(summary);
        }
    }
}

internal static partial class IngestionLog
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Ingested from {SourceSlug}: {CreatedCount} created, {RevisedCount} revised, {SkippedCount} skipped")]
    public static partial void Completed(
        ILogger logger,
        string sourceSlug,
        int createdCount,
        int revisedCount,
        int skippedCount);
}
