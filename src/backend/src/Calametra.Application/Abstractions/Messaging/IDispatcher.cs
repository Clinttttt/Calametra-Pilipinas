using Calametra.Domain.Abstractions;

namespace Calametra.Application.Abstractions.Messaging;

/// <summary>A request that produces <typeparamref name="TResponse"/>.</summary>
/// <remarks>
/// Requests are transport-agnostic: no <c>HttpContext</c>, no <c>IFormFile</c>,
/// no <c>IResult</c>. That is enforced by the assembly boundary — this project
/// cannot reference ASP.NET Core.
/// </remarks>
public interface IRequest<out TResponse>;

/// <summary>A request that changes state and returns no value.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>A request that changes state and returns a value.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>A request that reads state.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Handles one request type. Implementations are <c>internal sealed</c>.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>The next stage of the pipeline.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting concern wrapped around every handler, applied in registration order.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// Sends a request to its handler through the configured pipeline.
/// </summary>
/// <remarks>
/// Calametra implements the mediator pattern directly rather than taking MediatR.
/// MediatR 13 and later are licensed under RPL-1.5 with a commercial tier, and the
/// reference architecture notes that nothing in the design depends on MediatR
/// specifically. See docs/adr/ADR-001-no-mediatr.md.
/// </remarks>
public interface IDispatcher
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
