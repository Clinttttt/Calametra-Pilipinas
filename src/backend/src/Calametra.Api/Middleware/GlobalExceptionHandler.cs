using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Calametra.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into Problem Details responses.
/// </summary>
/// <remarks>
/// Two paths, kept separate on purpose. Expected failures travel through
/// <c>Result</c> and are mapped by <c>ResultExtensions</c>. Unexpected exceptions
/// arrive here. The one deliberate crossover is
/// <see cref="ValidationException"/>: the validation pipeline throws it so that
/// field-level errors can be returned as a proper
/// <see cref="ValidationProblemDetails"/> dictionary, which a single-code
/// <c>Error</c> cannot express.
/// <para>
/// Nothing internal is disclosed for a genuine 500: no stack trace, no connection
/// string, no upstream payload. The trace identifier is returned instead so a report
/// can be correlated with the server log.
/// </para>
/// </remarks>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is ValidationException validationException)
        {
            return await WriteValidationProblemAsync(httpContext, validationException, cancellationToken);
        }

        UnhandledExceptionLog.Unhandled(logger, exception, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Title = "An unexpected error occurred",
                Detail = "The request could not be completed. Quote the trace identifier when reporting this.",
                Status = StatusCodes.Status500InternalServerError,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier },
            },
        });
    }

    private async ValueTask<bool> WriteValidationProblemAsync(
        HttpContext httpContext,
        ValidationException exception,
        CancellationToken cancellationToken)
    {
        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Title = "Validation failed",
            Detail = "One or more query parameters were rejected.",
            Status = StatusCodes.Status400BadRequest,
        };

        problemDetails.Extensions["code"] = "request.validation_failed";

        _ = cancellationToken;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });
    }
}

internal static partial class UnhandledExceptionLog
{
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled exception while processing {RequestPath}")]
    public static partial void Unhandled(ILogger logger, Exception exception, string requestPath);
}
