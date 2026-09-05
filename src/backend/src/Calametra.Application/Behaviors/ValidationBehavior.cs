using Calametra.Application.Abstractions.Messaging;
using FluentValidation;

namespace Calametra.Application.Behaviors;

/// <summary>
/// Runs every registered validator for a request before its handler executes.
/// </summary>
/// <remarks>
/// Throws <see cref="ValidationException"/> rather than returning a failed
/// <c>Result</c>. That is deliberate and is the one place Calametra uses an
/// exception for an expected condition: field-level validation needs to reach the
/// client as a <c>ValidationProblemDetails</c> with a per-field error dictionary,
/// and <c>Error</c> carries a single code and message by design. The API's
/// <c>GlobalExceptionHandler</c> performs that translation in exactly one place.
/// <para>
/// Everything else — not found, conflict, forbidden, domain rule violations —
/// travels through <c>Result</c> as normal.
/// </para>
/// </remarks>
internal sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var applicable = validators as IValidator<TRequest>[] ?? validators.ToArray();

        if (applicable.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                applicable.Select(validator => validator.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToArray();

        return failures.Length > 0
            ? throw new ValidationException(failures)
            : await next();
    }
}
