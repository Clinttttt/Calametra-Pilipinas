using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Application.Features.Earthquakes;
using Calametra.Application.Features.HazardLayers;
using Calametra.Application.Features.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Calametra.Application.UnitTests.Composition;

/// <summary>
/// Verifies that every request can actually be dispatched.
/// </summary>
/// <remarks>
/// These exist because of a real defect that reached a running system. The
/// dispatcher was originally registered as a singleton, so it captured the root
/// service provider; handlers are scoped, and resolving a scoped service from the
/// root provider throws. Nothing caught it — the solution built clean, all
/// architecture tests passed, and the failure only appeared when the worker
/// actually tried to dispatch a command:
///
///   "Cannot resolve scoped service 'IRequestHandler`2[ImportActiveFaults+Command, ...]'
///    from root provider."
///
/// A build cannot detect a lifetime mismatch and an architecture test cannot
/// either, because both are about types rather than about container wiring. Only
/// resolving through a real container from a real scope will do it.
/// </remarks>
public sealed class DependencyInjectionTests
{
    /// <summary>
    /// Builds the container the way a host does, with scope validation on so
    /// lifetime mistakes throw rather than working by luck.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddApplication();

        // Ports are faked: this asserts on wiring, not on behaviour. Infrastructure
        // supplies the real adapters and is verified by integration tests.
        services.AddScoped(_ => Substitute.For<IApplicationDbContext>());
        services.AddScoped(_ => Substitute.For<IEarthquakeCatalogSource>());
        services.AddScoped(_ => Substitute.For<IActiveFaultSource>());
        services.AddScoped(_ => Substitute.For<IHazardMapService>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public void TheContainer_ShouldBuildWithScopeValidation()
    {
        // ValidateOnBuild catches captive dependencies: a longer-lived service
        // holding a shorter-lived one.
        var build = () => BuildProvider();

        build.ShouldNotThrow();
    }

    [Fact]
    public void TheDispatcher_ShouldResolveFromAScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDispatcher>().ShouldNotBeNull();
    }

    [Fact]
    public void TheDispatcher_ShouldNotBeResolvableFromTheRootProvider()
    {
        using var provider = BuildProvider();

        // This is the assertion that encodes the original defect. The dispatcher is
        // scoped precisely so it receives a scoped provider and can reach scoped
        // handlers. If someone "simplifies" it back to a singleton to avoid creating
        // a scope, this test fails and explains why that is not a simplification.
        var resolveFromRoot = () => provider.GetRequiredService<IDispatcher>();

        resolveFromRoot.ShouldThrow<InvalidOperationException>();
    }

    [Theory]
    [MemberData(nameof(EveryRequestType))]
    public void EveryRequest_ShouldHaveAResolvableHandler(Type requestType, Type responseType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);

        var handler = scope.ServiceProvider.GetService(handlerType);

        handler.ShouldNotBeNull(
            $"'{requestType.Name}' has no registered handler, so dispatching it would throw at runtime.");
    }

    /// <summary>
    /// Every request in the application, paired with the response its handler
    /// returns. Listed explicitly rather than discovered by reflection so that
    /// adding a slice and forgetting this list is a visible omission.
    /// </summary>
    public static TheoryData<Type, Type> EveryRequestType() => new()
    {
        {
            typeof(SearchEarthquakes.Query),
            typeof(Domain.Abstractions.Result<Domain.Abstractions.PaginatedList<
                Features.Earthquakes.Shared.EarthquakeSummaryResponse>>)
        },
        {
            typeof(GetEarthquakeMapData.Query),
            typeof(Domain.Abstractions.Result<GetEarthquakeMapData.MapDataResponse>)
        },
        {
            typeof(GetEarthquakeActivity.Query),
            typeof(Domain.Abstractions.Result<GetEarthquakeActivity.ActivityResponse>)
        },
        {
            typeof(GetEarthquakeDetail.Query),
            typeof(Domain.Abstractions.Result<Features.Earthquakes.Shared.EarthquakeDetailResponse>)
        },
        {
            typeof(ListHazardLayers.Query),
            typeof(Domain.Abstractions.Result<IReadOnlyList<ListHazardLayers.HazardLayerResponse>>)
        },
        {
            typeof(GetHazardTile.Query),
            typeof(Domain.Abstractions.Result<HazardMapImage>)
        },
        {
            typeof(IdentifyHazardFeature.Query),
            typeof(Domain.Abstractions.Result<IReadOnlyList<HazardFeatureAttributes>>)
        },
        {
            typeof(GetHazardFeatures.Query),
            typeof(Domain.Abstractions.Result<GetHazardFeatures.HazardFeatureCollection>)
        },
        {
            typeof(IngestEarthquakeCatalog.Command),
            typeof(Domain.Abstractions.Result<IngestEarthquakeCatalog.IngestionSummary>)
        },
        {
            typeof(ImportActiveFaults.Command),
            typeof(Domain.Abstractions.Result<ImportActiveFaults.FaultImportSummary>)
        },
    };
}
