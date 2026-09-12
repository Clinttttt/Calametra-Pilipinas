using System.Net.Http.Json;
using System.Text.Json;
using Calametra.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// The seeded hazard layer catalogue and the provenance behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here calls an agency's service.</b> These assertions are about what the platform
/// says of itself — which layers exist, who publishes them, whether their data may be stored, and
/// how their tiles are addressed. Reaching out to PHIVOLCS or MGB would make the suite fail
/// whenever a government server was slow, which trains people to ignore a red build.
/// </para>
/// <para>
/// The catalogue is seeded rather than fixtured, so this also exercises
/// <see cref="ReferenceDataSeeder"/> — the code that decides what the platform claims about its
/// own sources.
/// </para>
/// </remarks>
[Collection(PostgisCollection.Name)]
public sealed class HazardCatalogueTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TheCatalogue_ShouldStateWhoPublishesEachLayerAndWhetherItMayBeStored()
    {
        await SeedReferenceDataAsync();

        var layers = await GetLayersAsync();

        layers.ShouldNotBeEmpty();

        // Every layer names an agency. Guardrail 3: no value without its source, applied to the
        // catalogue itself rather than only to readings.
        layers.ShouldAllBe(layer => !string.IsNullOrWhiteSpace(layer.SourceAgency));
        layers.ShouldAllBe(layer => !string.IsNullOrWhiteSpace(layer.Attribution));
    }

    [Fact]
    public async Task TheProxiedLayers_ShouldNotBeDeliveredAsStoredVector()
    {
        await SeedReferenceDataAsync();

        var layers = await GetLayersAsync();

        // PHIVOLCS awaits a signed agreement and MGB publishes no licence at all, so both are
        // display-only. A proxied layer reporting LocalVector would mean the platform had stored
        // geometry it has no permission to hold.
        foreach (var layer in layers.Where(layer => layer.SourceAgency is "DOST-PHIVOLCS" or "DOST-MGB"))
        {
            layer.DeliveryMode.ShouldBe("RemoteWms");
        }

        // And nothing the seeder creates is stored geometry. The one locally stored layer in a
        // running system — GEM's fault traces, which are CC BY-SA 4.0 — is created by
        // ImportActiveFaults when it runs, not here, so it is deliberately absent from a database
        // where only reference data has been seeded.
        layers.ShouldAllBe(layer => layer.DeliveryMode == "RemoteWms");
    }

    [Fact]
    public async Task TheMgbSusceptibilityLayers_ShouldBeCataloguedUnderTheStormViewWithACachedTilePath()
    {
        await SeedReferenceDataAsync();

        var layers = await GetLayersAsync();

        var mgb = layers.Where(layer => layer.SourceAgency == "DOST-MGB").ToList();

        mgb.Count.ShouldBe(2);
        mgb.ShouldContain(layer => layer.HazardType == "RainInducedLandslide");
        mgb.ShouldContain(layer => layer.HazardType == "Flood");

        // Filed under the storm view because a tropical cyclone is the dominant rainfall trigger
        // in this country. Terrain and Coastal are reachable by no hazard a reader can select, so
        // a layer filed there would be catalogued and invisible.
        mgb.ShouldAllBe(layer => layer.Lens == "Cyclone");

        // MGB publishes a 24-level cache and PHIVOLCS publishes none, which is why this is a
        // per-layer capability rather than a platform setting. Without the cached path the same
        // tile takes about 19 seconds to render instead of about a tenth of a second.
        mgb.ShouldAllBe(layer => layer.SupportsCachedTiles);
        layers.Where(layer => layer.SourceAgency == "DOST-PHIVOLCS")
            .ShouldAllBe(layer => !layer.SupportsCachedTiles);

        // Off by default: they are dense national polygon fills, and switching them on
        // automatically would bury the storm track a reader opened the view to see.
        mgb.ShouldAllBe(layer => !layer.IsEnabledByDefault);

        // Susceptibility is the platform's most easily misread layer, so the note is required
        // rather than optional.
        mgb.ShouldAllBe(layer => layer.InterpretationNote != null
            && layer.InterpretationNote.Contains("not a forecast"));
    }

    [Fact]
    public async Task ThePhivolcsEarthquakeHazardLayers_ShouldBeCataloguedUnderTheEarthquakeViewWithoutTheTwoThatCannotBeRendered()
    {
        await SeedReferenceDataAsync();

        var layers = await GetLayersAsync();

        var seismic = layers.Where(layer => layer.SourceAgency == "DOST-PHIVOLCS").ToList();

        // Ground shaking and liquefaction render a 400 km extent in 2.9 s and 1.6 s, so they can be
        // proxied per tile.
        seismic.ShouldContain(layer => layer.HazardType == "GroundShaking");
        seismic.ShouldContain(layer => layer.HazardType == "Liquefaction");
        seismic.Where(layer => layer.HazardType is "GroundShaking" or "Liquefaction")
            .ShouldAllBe(layer => layer.Lens == "Seismic");

        // The other two earthquake-hazard services PHIVOLCS publishes are deliberately absent, on
        // measurement: the same extent takes 81 s for earthquake-induced landslide and 140 s for
        // tsunami inundation, and tsunami failed with HTTP 500 through this platform's own proxy
        // after 91 s on the Luzon coast. Asserted rather than only commented, so re-adding either
        // is a deliberate act — it needs a path that avoids a national-extent render, either a
        // minimum zoom per layer or stored geometry once the Data User Agreement is granted.
        layers.ShouldNotContain(layer => layer.HazardType == "Tsunami");
        layers.ShouldNotContain(layer => layer.HazardType == "EarthquakeInducedLandslide");

        // Off by default. The fault traces are the layer a reader opens the earthquake view for;
        // these are dense polygon fills that would cover them.
        seismic.Where(layer => layer.HazardType is "GroundShaking" or "Liquefaction")
            .ShouldAllBe(layer => !layer.IsEnabledByDefault);

        // Both are the platform's most easily misread kind of layer — a modelled scenario and a
        // susceptibility class — so the note is required rather than optional.
        seismic.Where(layer => layer.HazardType is "GroundShaking" or "Liquefaction")
            .ShouldAllBe(layer => layer.InterpretationNote != null
                && layer.InterpretationNote.Contains("not a forecast"));

        // Feature inspection is by ArcGIS REST identify, as for the fault traces: WMS
        // GetFeatureInfo on these services advertises GeoJSON and returns an empty collection.
        seismic.Where(layer => layer.HazardType is "GroundShaking" or "Liquefaction")
            .ShouldAllBe(layer => layer.SupportsFeatureInfo);
    }

    [Fact]
    public async Task TheCreditsEndpoint_ShouldSeparateStoredSourcesFromDisplayedOnes()
    {
        await SeedReferenceDataAsync();

        using var client = fixture.CreateClient();

        var sources = await client.GetFromJsonAsync<List<SourceDto>>(
            new Uri("/api/data-sources", UriKind.Relative), Json);

        sources.ShouldNotBeNull();

        // The split the Sources page renders. A proxied source contributes to no figure on this
        // platform, which is the fact a flat credits list hides.
        sources.ShouldContain(source => source.Slug == "usgs-comcat" && source.IsRedistributable);
        sources.ShouldContain(source => source.Slug == "phivolcs-active-fault" && !source.IsRedistributable);
        sources.ShouldContain(source =>
            source.Slug == "mgb-rain-induced-landslide-susceptibility" && !source.IsRedistributable);

        // Registered at all, which it was not until the credits page was generated from this
        // table: 39 PAGASA names were being shown with nothing recording whose they are.
        sources.ShouldContain(source => source.Slug == "pagasa-cyclone-names");

        // Every source states its limits. These notes are what the interface quotes when it warns
        // a reader, so an empty one is a source presented as unqualified.
        //
        // Scoped to the slugs the platform registers rather than asserted over the whole table:
        // the suite shares one database and other classes arrange fixture sources of their own.
        // An earlier version of this assertion covered every row and so passed or failed depending
        // on which class ran first — exactly the trap documented on the collection fixture.
        var registered = sources
            .Where(source => !source.Slug.StartsWith("test-", StringComparison.Ordinal))
            .ToList();

        registered.Count.ShouldBeGreaterThanOrEqualTo(13);
        registered.ShouldAllBe(source => !string.IsNullOrWhiteSpace(source.CoverageNotes));
    }

    /// <summary>
    /// Runs the reference data seeder, which is idempotent and keyed on slug.
    /// </summary>
    /// <remarks>
    /// Called per test rather than once for the class: xUnit gives no ordering guarantee, so a
    /// test that assumed another had seeded first would pass or fail by luck.
    /// </remarks>
    private async Task SeedReferenceDataAsync()
    {
        await using var scope = fixture.Api.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>().SeedAsync();
    }

    private async Task<List<LayerDto>> GetLayersAsync()
    {
        using var client = fixture.CreateClient();

        var layers = await client.GetFromJsonAsync<List<LayerDto>>(
            new Uri("/api/hazard-layers", UriKind.Relative), Json);

        return layers ?? [];
    }

    // Declared locally rather than reusing the Application's response records: a test that
    // deserialises into the type the server serialises from cannot detect a renamed field.
    private sealed record LayerDto(
        Guid Id,
        string HazardType,
        string Lens,
        string DisplayName,
        string DeliveryMode,
        bool SupportsFeatureInfo,
        bool SupportsCachedTiles,
        bool IsEnabledByDefault,
        string? InterpretationNote,
        string SourceAgency,
        string Attribution);

    private sealed record SourceDto(
        string Slug,
        string Agency,
        bool IsRedistributable,
        string? CoverageNotes);
}
