using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Application.Features.Cyclones;
using Calametra.Application.Features.Earthquakes;
using Calametra.Application.Features.HazardLayers;
using Calametra.Application.Features.Ingestion;
using Calametra.Application.Features.Places;
using Calametra.Application.Features.Sources;
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

        // The crosswalk review surface is faked separately from the analytics surface, which is the
        // point of ADR-005 D4: they are two interfaces precisely so that a handler must ask for the one
        // on which an unreviewed proposal exists. A single fake for both would erase the distinction
        // this test is meant to keep honest.
        services.AddScoped(_ => Substitute.For<ILguCrosswalkReviewContext>());
        services.AddScoped(_ => Substitute.For<IPsgcRegisterSource>());
        services.AddScoped(_ => Substitute.For<IOfficialLandAreaSource>());
        services.AddScoped(_ => Substitute.For<ILguBoundarySource>());
        services.AddScoped(_ => Substitute.For<IBoundaryCatalogueSource>());
        services.AddScoped(_ => Substitute.For<ICanonicalBoundarySource>());
        services.AddScoped(_ => Substitute.For<Abstractions.Tiles.ILguBoundaryTileReader>());
        services.AddScoped(_ => Substitute.For<IEarthquakeCatalogSource>());
        services.AddScoped(_ => Substitute.For<IActiveFaultSource>());
        services.AddScoped(_ => Substitute.For<IHazardMapService>());
        services.AddScoped(_ => Substitute.For<ICycloneTrackSource>());
        services.AddScoped(_ => Substitute.For<IPlaceDirectorySource>());
        services.AddScoped(_ => Substitute.For<ISettlementCoordinateSource>());

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
            typeof(SearchCyclones.Query),
            typeof(Domain.Abstractions.Result<IReadOnlyList<SearchCyclones.CycloneSummaryResponse>>)
        },
        {
            typeof(GetCycloneDecades.Query),
            typeof(Domain.Abstractions.Result<GetCycloneDecades.CycloneDecadesResponse>)
        },
        {
            typeof(GetCycloneTracksNearby.Query),
            typeof(Domain.Abstractions.Result<GetCycloneTracksNearby.NearbyTracksResponse>)
        },
        {
            typeof(Features.Administrative.ImportPsgcRegister.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ImportPsgcRegister.RegisterImportSummary>)
        },
        {
            typeof(Features.Administrative.ProposeLguCodeLinks.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ProposeLguCodeLinks.ProposalSummary>)
        },
        {
            typeof(Features.Administrative.GetLguCrosswalkReadiness.Query),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.GetLguCrosswalkReadiness.ReadinessReport>)
        },

        // The two single-row review commands return a bare Result: confirming or rejecting a pairing
        // produces a decision, not a figure.
        {
            typeof(Features.Administrative.ConfirmLguCodeLink.Command),
            typeof(Domain.Abstractions.Result)
        },
        {
            typeof(Features.Administrative.RejectLguCodeLink.Command),
            typeof(Domain.Abstractions.Result)
        },

        // The review queue decides nothing; it presents the evidence so a person can.
        {
            typeof(Features.Administrative.GetLguReviewQueue.Query),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.GetLguReviewQueue.ReviewQueue>)
        },

        // A class confirmation returns a summary because a named, dated batch has to report what it
        // settled and what the domain refused. ADR-005 D4 permits the batch and forbids the threshold.
        {
            typeof(Features.Administrative.ConfirmLguCodeLinkClass.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ConfirmLguCodeLinkClass.ClassConfirmationSummary>)
        },

        // Accepting an exception is a finding, not a measurement.
        {
            typeof(Features.Administrative.AcceptLguCrosswalkException.Command),
            typeof(Domain.Abstractions.Result)
        },

        // Edition correspondence: reviewed identity between two editions of the ten-digit register.
        {
            typeof(Features.Administrative.ProposeLguEditionCorrespondences.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ProposeLguEditionCorrespondences.CorrespondenceProposalSummary>)
        },
        {
            typeof(Features.Administrative.ConfirmLguEditionCorrespondenceClass.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ConfirmLguEditionCorrespondenceClass.CorrespondenceConfirmationSummary>)
        },
        {
            typeof(Features.Administrative.ConfirmLguEditionCorrespondence.Command),
            typeof(Domain.Abstractions.Result)
        },

        {
            typeof(Features.Administrative.GetLguBoundaryTile.Query),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.GetLguBoundaryTile.LguBoundaryTile>)
        },
        {
            typeof(Features.Administrative.ConfirmLguSourceNameOverride.Command),
            typeof(Domain.Abstractions.Result)
        },
        {
            typeof(Features.Administrative.ImportCodAbBoundaries.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ImportCodAbBoundaries.CodAbImportSummary>)
        },

        // Geometry. ADR-005 D2 and D7: acquiring outlines and judging whether there are enough of them are
        // separate requests, so the coverage verdict is never a side effect of a successful import.
        {
            typeof(Features.Administrative.ImportLguBoundaries.Command),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.ImportLguBoundaries.BoundaryImportSummary>)
        },
        {
            typeof(Features.Administrative.GetLguBoundaryCoverage.Query),
            typeof(Domain.Abstractions.Result<
                Features.Administrative.GetLguBoundaryCoverage.BoundaryCoverageReport>)
        },
        {
            typeof(GetCycloneTrack.Query),
            typeof(Domain.Abstractions.Result<GetCycloneTrack.CycloneTrackResponse>)
        },
        {
            typeof(IngestCycloneTracks.Command),
            typeof(Domain.Abstractions.Result<IngestCycloneTracks.IngestionSummary>)
        },
        {
            typeof(CompareEarthquakes.Query),
            typeof(Domain.Abstractions.Result<CompareEarthquakes.ComparisonResponse>)
        },
        {
            typeof(GetSimilarEarthquakes.Query),
            typeof(Domain.Abstractions.Result<GetSimilarEarthquakes.SimilarEarthquakesResponse>)
        },
        {
            typeof(GetCrossSection.Query),
            typeof(Domain.Abstractions.Result<GetCrossSection.CrossSectionResponse>)
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
            typeof(GetEarthquakeContainment.Query),
            typeof(Domain.Abstractions.Result<GetEarthquakeContainment.Response>)
        },
        {
            typeof(GetContainedEarthquakeMapData.Query),
            typeof(Domain.Abstractions.Result<GetContainedEarthquakeMapData.Response>)
        },
        {
            typeof(Features.Administrative.GetAdministrativeUnit.Query),
            typeof(Domain.Abstractions.Result<Features.Administrative.GetAdministrativeUnit.Response>)
        },
        {
            typeof(GetCatalogueCompleteness.Query),
            typeof(Domain.Abstractions.Result<GetCatalogueCompleteness.CompletenessResponse>)
        },
        {
            typeof(GetEarthquakeDetail.Query),
            typeof(Domain.Abstractions.Result<Features.Earthquakes.Shared.EarthquakeDetailResponse>)
        },
        {
            typeof(GetEarthquakeByExternalId.Query),
            typeof(Domain.Abstractions.Result<Features.Earthquakes.Shared.EarthquakeDetailResponse>)
        },
        {
            typeof(GetCycloneByExternalId.Query),
            typeof(Domain.Abstractions.Result<GetCycloneTrack.CycloneTrackResponse>)
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
        {
            typeof(ImportOfficialLandAreas.Command),
            typeof(Domain.Abstractions.Result<ImportOfficialLandAreas.Summary>)
        },
        {
            typeof(ImportPlaces.Command),
            typeof(Domain.Abstractions.Result<ImportPlaces.PlaceImportSummary>)
        },
        {
            typeof(RefinePlaceCoordinates.Command),
            typeof(Domain.Abstractions.Result<RefinePlaceCoordinates.CoordinateRefinementSummary>)
        },
        {
            typeof(SearchPlaces.Query),
            typeof(Domain.Abstractions.Result<IReadOnlyList<SearchPlaces.PlaceMatch>>)
        },
        {
            typeof(GetPlaceContext.Query),
            typeof(Domain.Abstractions.Result<GetPlaceContext.PlaceContextResponse>)
        },
        {
            typeof(ListDataSources.Query),
            typeof(Domain.Abstractions.Result<IReadOnlyList<ListDataSources.DataSourceResponse>>)
        },
    };

    /// <summary>
    /// Asserts the list above is complete.
    /// </summary>
    /// <remarks>
    /// The list is deliberately hand-written, so that reading it tells you what the application can
    /// dispatch. But "adding a slice and forgetting this list is a visible omission" was not true
    /// until this test existed: an unlisted request was simply never exercised, and the suite stayed
    /// green. Two requests had in fact gone unlisted — both external-identifier lookups — which is
    /// what prompted this.
    /// <para>
    /// So reflection is used to police the list rather than to replace it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRequestList_ShouldCoverEveryRequestInTheApplication()
    {
        var listed = EveryRequestType()
            .Select(row => (Type)row[0]!)
            .ToHashSet();

        var declared = typeof(SearchEarthquakes.Query).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => Array.Exists(
                type.GetInterfaces(),
                contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToArray();

        var missing = Array.FindAll(declared, type => !listed.Contains(type))
            .Select(type => type.FullName!)
            .ToArray();

        missing.ShouldBeEmpty(
            "these requests are absent from EveryRequestType(), so nothing verifies they can be "
            + "dispatched: " + string.Join(", ", missing));
    }
}
