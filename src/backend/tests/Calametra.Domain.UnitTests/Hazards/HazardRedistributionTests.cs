using Calametra.Domain.Geospatial;
using Calametra.Domain.Hazards;

namespace Calametra.Domain.UnitTests.Hazards;

/// <summary>
/// Guards the licensing position.
///
/// Two fault sources are in play and they differ in what may be done with them.
/// GEM Global Active Faults is CC BY-SA 4.0 and may be stored. The PHIVOLCS
/// service is publicly reachable but carries no redistribution grant, and is
/// pending a signed Data User Agreement.
///
/// The distinction is enforced by the model rather than by developer memory,
/// because "public endpoint" and "licensed to copy" are easy to conflate and the
/// consequence of conflating them is not a bug — it is using an agency's data
/// without permission.
/// </summary>
public sealed class HazardRedistributionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid LayerId = Guid.CreateVersion7();
    private static readonly Guid SourceId = Guid.CreateVersion7();

    private static NetTopologySuite.Geometries.LineString SurigaoFaultTrace() =>
        Wgs84.LineString([Wgs84.Point(9.8d, 125.4d), Wgs84.Point(9.95d, 125.5d)]);

    [Fact]
    public void StoringGeometry_ShouldBeRefused_WhenTheSourceDoesNotPermitRedistribution()
    {
        var result = HazardFeature.Create(
            LayerId,
            SourceId,
            externalId: "PHL_96",
            SurigaoFaultTrace(),
            sourceIsRedistributable: false,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(HazardFeatureErrors.SourceNotRedistributable);
    }

    [Fact]
    public void StoringGeometry_ShouldSucceed_WhenTheSourceIsOpenlyLicensed()
    {
        var result = HazardFeature.Create(
            LayerId,
            SourceId,
            externalId: "PHL_96",
            SurigaoFaultTrace(),
            sourceIsRedistributable: true,
            Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ExternalId.ShouldBe("PHL_96");
    }

    [Fact]
    public void StoringGeometry_ShouldRequireThePublishersIdentifier()
    {
        // Without it, a re-import cannot tell a revised trace from a new one and
        // the fault set silently duplicates.
        var result = HazardFeature.Create(
            LayerId,
            SourceId,
            externalId: "  ",
            SurigaoFaultTrace(),
            sourceIsRedistributable: true,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(HazardFeatureErrors.ExternalIdRequired);
    }

    [Fact]
    public void CreatingALocalVectorLayer_ShouldBeRefused_ForANonRedistributableSource()
    {
        var result = HazardLayerDefinition.CreateLocal(
            SourceId,
            HazardType.ActiveFault,
            HazardLens.Seismic,
            displayName: "Active Faults",
            sourceIsRedistributable: false,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(HazardLayerErrors.LocalStorageNotPermitted);
    }

    [Fact]
    public void CreatingALocalVectorLayer_ShouldSucceed_ForAnOpenlyLicensedSource()
    {
        var result = HazardLayerDefinition.CreateLocal(
            SourceId,
            HazardType.ActiveFault,
            HazardLens.Seismic,
            displayName: "Active Faults (GEM)",
            sourceIsRedistributable: true,
            Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DeliveryMode.ShouldBe(LayerDeliveryMode.LocalVector);
    }

    [Fact]
    public void APendingProxiedLayer_ShouldStayProxied_UntilPermissionIsRecorded()
    {
        // The PHIVOLCS path: the layer exists and renders through the proxy, and
        // attempting to promote it to local storage before approval fails.
        var layer = HazardLayerDefinition.CreateRemote(
            SourceId,
            HazardType.ActiveFault,
            HazardLens.Seismic,
            displayName: "Active Faults",
            wmsEndpoint: "https://example.invalid/WMSServer",
            wmsLayerName: "0",
            Now).Value;

        layer.DeliveryMode.ShouldBe(LayerDeliveryMode.RemoteWms);

        var refused = layer.PromoteToLocalVector(sourceIsRedistributable: false, Now);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.ShouldBe(HazardLayerErrors.LocalStorageNotPermitted);
        layer.DeliveryMode.ShouldBe(LayerDeliveryMode.RemoteWms);
    }

    [Fact]
    public void APendingProxiedLayer_ShouldBePromotable_OncePermissionIsRecorded()
    {
        var layer = HazardLayerDefinition.CreateRemote(
            SourceId,
            HazardType.ActiveFault,
            HazardLens.Seismic,
            displayName: "Active Faults",
            wmsEndpoint: "https://example.invalid/WMSServer",
            wmsLayerName: "0",
            Now).Value;

        // What happens the day the signed Data User Agreement is approved.
        var promoted = layer.PromoteToLocalVector(sourceIsRedistributable: true, Now);

        promoted.IsSuccess.ShouldBeTrue();
        layer.DeliveryMode.ShouldBe(LayerDeliveryMode.LocalVector);
    }
}
