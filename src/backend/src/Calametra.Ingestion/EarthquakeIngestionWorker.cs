using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Ingestion;

namespace Calametra.Ingestion;

/// <summary>
/// Periodically pulls recent earthquakes from the upstream catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The overlap window is deliberate and is the point of the design. Each run re-reads
/// more history than strictly necessary because agencies revise solutions for days
/// after an event — origin time, depth and magnitude all get refined. Since ingestion
/// is idempotent and reconciles on the source's own identifier, re-reading is how
/// revisions are picked up rather than wasted work.
/// </para>
/// <para>
/// "Live" here means "as fresh as the upstream feed and our poll interval". This is
/// not real-time seismic monitoring and the interface must not imply that it is.
/// Official warnings come from PHIVOLCS, PAGASA and NDRRMC.
/// </para>
/// </remarks>
internal sealed class EarthquakeIngestionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<EarthquakeIngestionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue("Ingestion:Interval", TimeSpan.FromMinutes(10));
        var overlap = configuration.GetValue("Ingestion:OverlapWindow", TimeSpan.FromDays(7));

        WorkerLog.Started(logger, interval, overlap);

        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            try
            {
                await RunOnceAsync(overlap, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed cycle must never kill the worker: the next tick should try
                // again. Transient HTTP failures are already handled by the resilience
                // handler; this catches anything else.
                WorkerLog.CycleFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        WorkerLog.Stopped(logger);
    }

    private async Task RunOnceAsync(TimeSpan overlap, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var now = timeProvider.GetUtcNow();

        var command = new IngestEarthquakeCatalog.Command
        {
            From = now - overlap,
            // A small forward margin absorbs clock skew between us and the source.
            To = now.AddMinutes(5),
        };

        var result = await dispatcher.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            WorkerLog.IngestionFailed(logger, result.Error!.Code, result.Error.Description);

            return;
        }

        var summary = result.Value;

        WorkerLog.CycleCompleted(
            logger,
            summary.SourceSlug,
            summary.FetchedCount,
            summary.CreatedCount,
            summary.RevisedCount);
    }
}

internal static partial class WorkerLog
{
    [LoggerMessage(
        EventId = 7000,
        Level = LogLevel.Information,
        Message = "Ingestion worker started; interval {Interval}, overlap window {Overlap}")]
    public static partial void Started(ILogger logger, TimeSpan interval, TimeSpan overlap);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "Ingestion worker stopped")]
    public static partial void Stopped(ILogger logger);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Information,
        Message = "Cycle complete for {SourceSlug}: fetched {FetchedCount}, created {CreatedCount}, revised {RevisedCount}")]
    public static partial void CycleCompleted(
        ILogger logger,
        string sourceSlug,
        int fetchedCount,
        int createdCount,
        int revisedCount);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Error, Message = "Ingestion cycle threw")]
    public static partial void CycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 7004,
        Level = LogLevel.Warning,
        Message = "Ingestion returned a failure: {ErrorCode} — {ErrorDescription}")]
    public static partial void IngestionFailed(ILogger logger, string errorCode, string errorDescription);

    [LoggerMessage(
        EventId = 7005,
        Level = LogLevel.Warning,
        Message = "Active fault import did not complete: {ErrorCode} — {ErrorDescription}. "
            + "Earthquake ingestion continues; fault geometry will be retried on next start.")]
    public static partial void FaultImportFailed(ILogger logger, string errorCode, string errorDescription);

    [LoggerMessage(
        EventId = 7006,
        Level = LogLevel.Information,
        Message = "Cyclone import complete: {StormsCreated} storms created, {StormsSkipped} already "
            + "present, {TrackPointsCreated} agency fixes stored, {StormsRejected} rejected")]
    public static partial void CycloneImportCompleted(
        ILogger logger,
        int stormsCreated,
        int stormsSkipped,
        int trackPointsCreated,
        int stormsRejected);

    [LoggerMessage(
        EventId = 7007,
        Level = LogLevel.Error,
        Message = "Cyclone import failed: {ErrorCode} — {ErrorDescription}")]
    public static partial void CycloneImportFailed(ILogger logger, string errorCode, string errorDescription);

    [LoggerMessage(
        EventId = 7008,
        Level = LogLevel.Information,
        Message = "Place import complete: {Created} created, {Skipped} already present, {Rejected} "
            + "rejected, {WithoutCode} without a PSGC code, {UnresolvedParents} without a resolved parent")]
    public static partial void PlaceImportCompleted(
        ILogger logger,
        int created,
        int skipped,
        int rejected,
        int withoutCode,
        int unresolvedParents);

    [LoggerMessage(
        EventId = 7009,
        Level = LogLevel.Error,
        Message = "Place import failed: {ErrorCode} — {ErrorDescription}")]
    public static partial void PlaceImportFailed(ILogger logger, string errorCode, string errorDescription);
}
