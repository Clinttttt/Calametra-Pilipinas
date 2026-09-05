using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Ingestion;

namespace Calametra.Ingestion;

/// <summary>
/// One-shot historical backfill, run instead of the rolling worker.
/// </summary>
/// <remarks>
/// <para>
/// The rolling worker deliberately reads only a short recent window, which is
/// right for keeping current but cannot load an archive. Backfill exists because
/// the platform's whole purpose is historical exploration, and an empty archive
/// makes the Time Machine, similarity search and the cross-section meaningless.
/// </para>
/// <para>
/// Walks the requested span in windows rather than issuing one enormous request.
/// The USGS service will refuse or time out on a decade-wide query, and the
/// ingestion command caps a single run at roughly a year for that reason. Windows
/// are processed oldest-first so a partial backfill still leaves a contiguous
/// archive rather than islands.
/// </para>
/// <para>
/// Usage:
/// <code>
/// dotnet run --project src/Calametra.Ingestion -- \
///   --Ingestion:Backfill:From=2015-01-01 --Ingestion:Backfill:To=2026-09-01
/// </code>
/// </para>
/// </remarks>
internal sealed class BackfillRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<BackfillRunner> logger)
{
    /// <summary>
    /// Window size. Just under the ingestion command's limit, leaving headroom so a
    /// leap year cannot push a window over the cap.
    /// </summary>
    private static readonly TimeSpan WindowSize = TimeSpan.FromDays(365);

    /// <summary>
    /// Pause between windows. Courtesy to a free public service: a backfill issues
    /// a dozen large queries back to back, and there is no reason to do that as
    /// fast as the network allows.
    /// </summary>
    private static readonly TimeSpan PauseBetweenWindows = TimeSpan.FromSeconds(2);

    public async Task<bool> RunAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        BackfillLog.Started(logger, from, to);

        var totalCreated = 0;
        var totalRevised = 0;
        var windowStart = from;
        var windowNumber = 0;
        var failed = false;

        while (windowStart < to && !cancellationToken.IsCancellationRequested)
        {
            var windowEnd = windowStart + WindowSize;

            if (windowEnd > to)
            {
                windowEnd = to;
            }

            windowNumber++;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

                var result = await dispatcher.Send(
                    new IngestEarthquakeCatalog.Command { From = windowStart, To = windowEnd },
                    cancellationToken);

                if (result.IsFailure)
                {
                    // Keep going. One bad window — a transient upstream failure — should
                    // not discard the windows that already succeeded.
                    BackfillLog.WindowFailed(
                        logger,
                        windowNumber,
                        windowStart,
                        windowEnd,
                        result.Error!.Code);

                    failed = true;
                }
                else
                {
                    var summary = result.Value;
                    totalCreated += summary.CreatedCount;
                    totalRevised += summary.RevisedCount;

                    BackfillLog.WindowCompleted(
                        logger,
                        windowNumber,
                        windowStart,
                        windowEnd,
                        summary.FetchedCount,
                        summary.CreatedCount);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A backfill spans decades of third-party data, and any one window can
                // contain something unanticipated. Losing 125 good windows because the
                // 126th threw is the wrong failure mode, so each window is isolated and
                // the run is reported as incomplete at the end.
                BackfillLog.WindowThrew(logger, exception, windowNumber, windowStart, windowEnd);

                failed = true;
            }

            windowStart = windowEnd;

            if (windowStart < to)
            {
                await Task.Delay(PauseBetweenWindows, cancellationToken);
            }
        }

        BackfillLog.Finished(logger, windowNumber, totalCreated, totalRevised);

        return !failed;
    }
}

internal static partial class BackfillLog
{
    [LoggerMessage(
        EventId = 7100,
        Level = LogLevel.Information,
        Message = "Backfill starting: {From:yyyy-MM-dd} to {To:yyyy-MM-dd}")]
    public static partial void Started(ILogger logger, DateTimeOffset from, DateTimeOffset to);

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Information,
        Message = "Window {WindowNumber} ({From:yyyy-MM-dd} to {To:yyyy-MM-dd}): fetched {FetchedCount}, created {CreatedCount}")]
    public static partial void WindowCompleted(
        ILogger logger,
        int windowNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        int fetchedCount,
        int createdCount);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Warning,
        Message = "Window {WindowNumber} ({From:yyyy-MM-dd} to {To:yyyy-MM-dd}) failed: {ErrorCode}. Continuing.")]
    public static partial void WindowFailed(
        ILogger logger,
        int windowNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        string errorCode);

    [LoggerMessage(
        EventId = 7104,
        Level = LogLevel.Error,
        Message = "Window {WindowNumber} ({From:yyyy-MM-dd} to {To:yyyy-MM-dd}) threw. Continuing with the next window.")]
    public static partial void WindowThrew(
        ILogger logger,
        Exception exception,
        int windowNumber,
        DateTimeOffset from,
        DateTimeOffset to);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Information,
        Message = "Backfill finished: {WindowCount} windows, {CreatedCount} events created, {RevisedCount} revised")]
    public static partial void Finished(ILogger logger, int windowCount, int createdCount, int revisedCount);
}
