using System.Collections.Concurrent;
using Calametra.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Calametra.Application.Messaging;

/// <summary>
/// Resolves the handler for a request and runs it through the registered pipeline
/// behaviours.
/// </summary>
/// <remarks>
/// The wrapper indirection exists because <see cref="IDispatcher.Send{TResponse}"/>
/// knows only <c>TResponse</c>, while resolving a handler needs the concrete request
/// type too. A closed generic wrapper is built once per request type and cached, so
/// reflection cost is paid on first use only and every later dispatch is a
/// dictionary lookup plus a virtual call.
/// </remarks>
internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> WrapperCache = new();

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = (RequestHandlerWrapper<TResponse>)WrapperCache.GetOrAdd(
            request.GetType(),
            static requestType =>
            {
                var wrapperType = typeof(RequestHandlerWrapperImplementation<,>)
                    .MakeGenericType(requestType, typeof(TResponse));

                return Activator.CreateInstance(wrapperType)
                    ?? throw new InvalidOperationException(
                        $"Could not construct a dispatch wrapper for '{requestType}'.");
            });

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    private abstract class RequestHandlerWrapper<TResponse>
    {
        public abstract Task<TResponse> Handle(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    private sealed class RequestHandlerWrapperImplementation<TRequest, TResponse>
        : RequestHandlerWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Handle(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException(
                    $"No handler is registered for '{typeof(TRequest)}'. Every request needs exactly one "
                    + "handler; check that the handler is public or internal and lives in Calametra.Application.");

            var typedRequest = (TRequest)request;

            RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle(typedRequest, cancellationToken);

            // Reversed so that the first-registered behaviour is the outermost.
            var behaviors = serviceProvider
                .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                .Reverse();

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                pipeline = () => behavior.Handle(typedRequest, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
