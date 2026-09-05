using System.Text.Json;
using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Hazards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Features.Ingestion;

/// <summary>
/// Imports mapped active fault geometry into PostGIS from an openly licensed
/// catalogue.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes fault geometry queryable rather than merely viewable. A
/// proxied raster layer can be looked at; stored traces can answer "which fault
/// is nearest this epicentre" and "which faults lie within 25 km of this town",
/// which are the questions the platform exists to answer.
/// </para>
/// <para>
/// Guarded by the source's redistribution flag at two levels: the layer cannot be
/// created as local-vector, and each feature refuses construction, unless the
/// owning source permits redistribution. Running this against a source awaiting
/// permission fails rather than quietly storing data it should not.
/// </para>
/// <para>
/// Idempotent, reconciling on the publisher's own identifier, so re-running after
/// the upstream catalogue is revised updates traces in place.
/// </para>
/// </remarks>
public static class ImportActiveFaults
{
    public sealed record Command : ICommand<FaultImportSummary>;

    public sealed record FaultImportSummary(
        string SourceSlug,
        int FetchedCount,
        int CreatedCount,
        int RevisedCount,
        int RejectedCount);

    internal sealed class Handler(
        IApplicationDbContext context,
        IActiveFaultSource faultSource,
        TimeProvider timeProvider,
        ILogger<Handler> logger)
        : ICommandHandler<Command, FaultImportSummary>
    {
        private static readonly Error SourceNotRegistered = new(
            ErrorType.NotFound,
            "fault_import.source_not_registered",
            "The fault data source is not registered. Reference data must be seeded first.");

        public async Task<Result<FaultImportSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var source = await context.DataSources
                .FirstOrDefaultAsync(candidate => candidate.Slug == faultSource.SourceSlug, cancellationToken);

            if (source is null)
            {
                return Result<FaultImportSummary>.Failure(SourceNotRegistered);
            }

            if (!source.IsRedistributable)
            {
                // Refusing here rather than at the first feature keeps the failure
                // legible: the problem is the source's terms, not any one record.
                return Result<FaultImportSummary>.Failure(HazardFeatureErrors.SourceNotRedistributable);
            }

            var layerResult = await EnsureLayerAsync(source.Id, now, cancellationToken);

            if (layerResult.IsFailure)
            {
                return Result<FaultImportSummary>.Failure(layerResult.Error!);
            }

            var layer = layerResult.Value;

            var fetch = await faultSource.FetchAsync(
                new ActiveFaultQuery
                {
                    MinLatitude = PhilippineStudyArea.MinLatitude,
                    MaxLatitude = PhilippineStudyArea.MaxLatitude,
                    MinLongitude = PhilippineStudyArea.MinLongitude,
                    MaxLongitude = PhilippineStudyArea.MaxLongitude,
                },
                cancellationToken);

            if (fetch.IsFailure)
            {
                return Result<FaultImportSummary>.Failure(fetch.Error!);
            }

            var fetched = fetch.Value;
            var externalIds = fetched.Select(fault => fault.ExternalId).ToHashSet(StringComparer.Ordinal);

            var existing = await context.HazardFeatures
                .Where(feature => feature.DataSourceId == source.Id
                    && externalIds.Contains(feature.ExternalId))
                .ToDictionaryAsync(feature => feature.ExternalId, cancellationToken);

            var created = 0;
            var revised = 0;
            var rejected = 0;

            foreach (var fault in fetched)
            {
                var attributesJson = SerialiseAttributes(fault);

                if (existing.TryGetValue(fault.ExternalId, out var feature))
                {
                    feature.Revise(fault.Trace, fault.Name, fault.SlipType, attributesJson, now);
                    revised++;

                    continue;
                }

                var creation = HazardFeature.Create(
                    layer.Id,
                    source.Id,
                    fault.ExternalId,
                    fault.Trace,
                    source.IsRedistributable,
                    now);

                if (creation.IsFailure)
                {
                    rejected++;

                    continue;
                }

                context.HazardFeatures.Add(
                    creation.Value.WithDescription(fault.Name, fault.SlipType, attributesJson));

                created++;
            }

            source.RecordRetrieval(now);

            await context.SaveChangesAsync(cancellationToken);

            var summary = new FaultImportSummary(
                faultSource.SourceSlug,
                fetched.Count,
                created,
                revised,
                rejected);

            FaultImportLog.Completed(logger, summary.SourceSlug, created, revised, rejected);

            return Result<FaultImportSummary>.Success(summary);
        }

        /// <summary>Finds or creates the catalogue entry these features belong to.</summary>
        private async Task<Result<HazardLayerDefinition>> EnsureLayerAsync(
            Guid dataSourceId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var existing = await context.HazardLayers
                .FirstOrDefaultAsync(
                    layer => layer.DataSourceId == dataSourceId && layer.HazardType == HazardType.ActiveFault,
                    cancellationToken);

            if (existing is not null)
            {
                return Result<HazardLayerDefinition>.Success(existing);
            }

            var creation = HazardLayerDefinition.CreateLocal(
                dataSourceId,
                HazardType.ActiveFault,
                HazardLens.Seismic,
                displayName: "Active Faults (GEM)",
                sourceIsRedistributable: true,
                now);

            if (creation.IsFailure)
            {
                return creation;
            }

            var layer = creation.Value
                .WithExplainer(
                    explainer:
                        "An active fault is a fracture in the Earth's crust that has moved in the recent "
                        + "geological past and is considered capable of moving again.",
                    interpretationNote:
                        "These traces come from a global research compilation and show principal named "
                        + "faults at regional scale. They are not a substitute for detailed national "
                        + "hazard mapping, and an earthquake near a mapped fault does not establish that "
                        + "this fault produced it.")
                .WithPresentation(isEnabledByDefault: true, sortOrder: 15, supportsFeatureInfo: true);

            context.HazardLayers.Add(layer);

            return Result<HazardLayerDefinition>.Success(layer);
        }

        /// <summary>
        /// Serialises the publisher's remaining attributes. Fields already promoted
        /// to columns are dropped so the JSON does not duplicate them.
        /// </summary>
        private static string? SerialiseAttributes(CatalogFault fault)
        {
            var remaining = fault.Attributes
                .Where(pair => pair.Key is not ("catalog_id" or "name" or "slip_type"))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            return remaining.Count == 0 ? null : JsonSerializer.Serialize(remaining);
        }
    }
}

internal static partial class FaultImportLog
{
    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Information,
        Message = "Fault import from {SourceSlug}: {CreatedCount} created, {RevisedCount} revised, {RejectedCount} rejected")]
    public static partial void Completed(
        ILogger logger,
        string sourceSlug,
        int createdCount,
        int revisedCount,
        int rejectedCount);
}
