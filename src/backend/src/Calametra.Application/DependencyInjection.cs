using System.Reflection;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Behaviors;
using Calametra.Application.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Calametra.Application;

/// <summary>Registers everything this assembly owns. The host composes; the layer registers itself.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = typeof(DependencyInjection).Assembly;

        // Scoped, not singleton. The dispatcher captures the IServiceProvider it is
        // constructed with, and handlers are scoped. A singleton dispatcher would
        // capture the root provider and then fail with "Cannot resolve scoped
        // service ... from root provider" the moment it tried to resolve a handler.
        //
        // Registering it scoped means it receives the scope's provider, so handlers,
        // the DbContext and everything else scoped resolve from the same scope as
        // the request or worker cycle that created it.
        //
        // Dispatch is still cheap: the closed-generic wrapper cache in Dispatcher is
        // static, so it is shared across every scope and the reflection cost is paid
        // once per request type for the lifetime of the process.
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Outermost first. Logging wraps validation so a rejected request is still timed.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddSingleton(TimeProvider.System);

        AddRequestHandlers(services, assembly);

        return services;
    }

    /// <summary>
    /// Registers every closed <c>IRequestHandler&lt;,&gt;</c> in the assembly.
    /// </summary>
    /// <remarks>
    /// Handlers are <c>internal sealed</c>, so this scan is the only thing that can
    /// see them — which is the point. Nothing outside the pipeline can reach a
    /// handler directly.
    /// </remarks>
    private static void AddRequestHandlers(IServiceCollection services, Assembly assembly)
    {
        var handlerInterface = typeof(IRequestHandler<,>);

        var registrations = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type
                .GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == handlerInterface)
                .Select(contract => (Contract: contract, Implementation: type)));

        foreach (var (contract, implementation) in registrations)
        {
            services.AddScoped(contract, implementation);
        }
    }
}
