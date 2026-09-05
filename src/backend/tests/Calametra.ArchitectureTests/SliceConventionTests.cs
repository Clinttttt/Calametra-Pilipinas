using Calametra.Application.Abstractions.Messaging;

namespace Calametra.ArchitectureTests;

/// <summary>
/// Enforces the conventions that keep a slice split across two assemblies navigable.
/// </summary>
/// <remarks>
/// The pairing test is the one that earns its keep. Splitting a use case across
/// Application and Api makes it possible to add a handler nobody can call, or to
/// leave an endpoint pointing at a deleted use case. Nothing else catches either.
/// </remarks>
public sealed class SliceConventionTests
{
    [Fact]
    public void EveryHttpUseCase_ShouldHaveExactlyOneEndpoint()
    {
        var useCases = HttpUseCaseNames();
        var endpoints = EndpointNames();

        var missingEndpoints = useCases.Except(endpoints, StringComparer.Ordinal).ToArray();
        var orphanedEndpoints = endpoints.Except(useCases, StringComparer.Ordinal).ToArray();

        missingEndpoints.ShouldBeEmpty(
            "these use cases have no endpoint, so nothing can reach them: "
            + string.Join(", ", missingEndpoints));

        orphanedEndpoints.ShouldBeEmpty(
            "these endpoints have no matching use case: " + string.Join(", ", orphanedEndpoints));
    }

    [Fact]
    public void Handlers_ShouldBeInternalAndSealed()
    {
        var offenders = Assemblies.Application
            .GetTypes()
            // Concrete handlers only. The ICommandHandler / IQueryHandler interfaces
            // themselves derive from IRequestHandler and are public by design.
            .Where(type => type is { IsInterface: false, IsAbstract: false })
            .Where(type => Array.Exists(
                type.GetInterfaces(),
                contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .Where(type => type.IsPublic || !type.IsSealed)
            .Select(type => type.FullName!)
            .ToArray();

        // Handlers are reached only through the dispatcher. A public handler invites a
        // caller to bypass validation and logging entirely.
        offenders.ShouldBeEmpty(
            "handlers must be internal and sealed: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Validators_ShouldLiveBesideTheirRequest()
    {
        var offenders = Assemblies.Application
            .GetTypes()
            .Where(type => type.Name == "Validator" && type.IsNested)
            .Where(type => type.DeclaringType is null
                || !HasRequestType(type.DeclaringType))
            .Select(type => type.FullName!)
            .ToArray();

        offenders.ShouldBeEmpty(
            "a Validator must be nested inside the static use-case class that declares its request: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Requests_ShouldBeSealed()
    {
        var offenders = Assemblies.Application
            .GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && Array.Exists(type.GetInterfaces(), IsRequestInterface))
            .Where(type => !type.IsSealed)
            .Select(type => type.FullName!)
            .ToArray();

        offenders.ShouldBeEmpty("requests must be sealed records: " + string.Join(", ", offenders));
    }

    private static bool IsRequestInterface(Type contract) =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRequest<>);

    private static bool HasRequestType(Type useCaseType) =>
        useCaseType.GetNestedType("Command") is not null
        || useCaseType.GetNestedType("Query") is not null;

    /// <summary>
    /// Use cases reachable over HTTP.
    /// </summary>
    /// <remarks>
    /// Deviation from the reference architecture's pairing rule, and a necessary one.
    /// The template assumes a single host, so every use case must have an endpoint.
    /// Calametra has two hosts: Calametra.Api serves HTTP and Calametra.Ingestion runs
    /// scheduled work. An ingestion command is invoked by the worker and deliberately
    /// has no endpoint — exposing "go and re-read the USGS catalogue" as an anonymous
    /// public GET would hand any caller a way to generate outbound traffic to a
    /// third-party agency.
    /// <para>
    /// So the rule becomes: every use case outside <c>Features.Ingestion</c> needs
    /// exactly one endpoint. If ingestion ever needs manual triggering, it gets an
    /// authenticated administrative endpoint and moves out of the exemption.
    /// </para>
    /// </remarks>
    private const string WorkerInvokedNamespace = "Calametra.Application.Features.Ingestion";

    private static HashSet<string> HttpUseCaseNames() =>
        Assemblies.Application
            .GetTypes()
            .Where(HasRequestType)
            .Where(type => type.Namespace is null
                || !type.Namespace.StartsWith(WorkerInvokedNamespace, StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// An endpoint is a type named <c>{UseCase}Endpoint</c>. Route-group modules end
    /// in <c>Module</c> and are excluded.
    /// </summary>
    private static HashSet<string> EndpointNames() =>
        Assemblies.Api
            .GetTypes()
            .Where(type => type.Name.EndsWith("Endpoint", StringComparison.Ordinal))
            .Select(type => type.Name[..^"Endpoint".Length])
            .ToHashSet(StringComparer.Ordinal);
}
