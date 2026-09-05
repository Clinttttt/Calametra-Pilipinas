using System.Diagnostics;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Calametra.Application.Behaviors;

/// <summary>
/// Records the outcome and duration of every request.
/// </summary>
/// <remarks>
/// Logs <c>Error.Code</c> on failure rather than the description, so log aggregation
/// groups by a stable key instead of prose that may be reworded.
/// <para>
/// Uses source-generated <see cref="LoggerMessageAttribute"/> methods rather than the
/// <c>ILogger.LogInformation</c> extensions. The extensions box every argument and
/// allocate a parameter array on each call, even when the level is disabled; the
/// generated methods do neither. Elapsed time is computed only after an
/// <c>IsEnabled</c> check for the same reason.
/// </para>
/// </remarks>
internal sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            var response = await next();

            if (response is Result { IsFailure: true } failure)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    var elapsedMilliseconds = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

                    RequestLog.Failed(logger, requestName, failure.Error!.Code, elapsedMilliseconds);
                }
            }
            else
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var elapsedMilliseconds = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

                    RequestLog.Completed(logger, requestName, elapsedMilliseconds);
                }
            }

            return response;
        }
        catch (Exception exception)
        {
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            RequestLog.Threw(logger, exception, requestName, elapsedMilliseconds);

            throw;
        }
    }
}

/// <summary>
/// Source-generated log methods. Non-generic and separate from the behaviour because
/// the LoggerMessage generator emits into a partial class, and keeping it out of the
/// generic type avoids one generated implementation per closed request type.
/// </summary>
internal static partial class RequestLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "{RequestName} completed in {ElapsedMilliseconds:0.##} ms")]
    public static partial void Completed(ILogger logger, string requestName, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "{RequestName} failed with {ErrorCode} in {ElapsedMilliseconds:0.##} ms")]
    public static partial void Failed(
        ILogger logger,
        string requestName,
        string errorCode,
        double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "{RequestName} threw after {ElapsedMilliseconds:0.##} ms")]
    public static partial void Threw(
        ILogger logger,
        Exception exception,
        string requestName,
        double elapsedMilliseconds);
}
