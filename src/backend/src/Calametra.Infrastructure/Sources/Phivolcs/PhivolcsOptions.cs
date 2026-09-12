namespace Calametra.Infrastructure.Sources.Phivolcs;

/// <summary>Identity of the DOST-PHIVOLCS datasets in the catalogue.</summary>
/// <remarks>
/// The timeout, cache duration and user agent that used to live here moved to
/// <c>HazardProxyOptions</c> when DOST-MGB became the second publisher behind the same proxy:
/// those are properties of how this platform behaves as a client rather than of whose service is
/// being called. What is left is PHIVOLCS's own.
/// </remarks>
public sealed class PhivolcsOptions
{
    public const string SectionName = "Sources:Phivolcs";

    /// <summary>Slug of the <c>DataSource</c> row for the active fault dataset.</summary>
    public const string ActiveFaultSlug = "phivolcs-active-fault";

    /// <summary>Slug of the <c>DataSource</c> row for the trenches dataset.</summary>
    public const string TrenchesSlug = "phivolcs-trenches";

    /// <summary>Slug of the <c>DataSource</c> row for the deterministic ground shaking maps.</summary>
    public const string GroundShakingSlug = "phivolcs-ground-shaking";

    /// <summary>Slug of the <c>DataSource</c> row for the liquefaction susceptibility maps.</summary>
    public const string LiquefactionSlug = "phivolcs-liquefaction";

    /// <summary>
    /// Slug of the <c>DataSource</c> row for the earthquake-induced landslide maps.
    /// </summary>
    /// <remarks>
    /// Distinct from DOST-MGB's rain-induced landslide susceptibility, and deliberately so: the
    /// trigger is ground motion rather than rainfall, the mapping is a different agency's, and the two
    /// disagree about which slopes matter because the mechanisms differ.
    /// </remarks>
    public const string EarthquakeInducedLandslideSlug = "phivolcs-earthquake-induced-landslide";

    /// <summary>
    /// Base path of the public ArcGIS OGC services. Individual layer endpoints are
    /// stored per layer in the hazard catalogue rather than here, so adding a layer
    /// is a data change and not a configuration change.
    /// </summary>
    public string ServicesRoot { get; set; } =
        "https://gisweb.phivolcs.dost.gov.ph/arcgis/services/PHIVOLCSPublic";

    /// <summary>Base path of the REST services, used for the <c>identify</c> operation.</summary>
    /// <remarks>
    /// Note <c>/arcgis/rest/services/</c> against <c>/arcgis/services/</c> above. The REST path
    /// serves the service metadata and <c>identify</c>; the OGC path serves WMS. Confusing them
    /// yields a 404 rather than an informative error — the same trap <see cref="Mgb.MgbOptions"/>
    /// records for the other publisher behind this proxy.
    /// </remarks>
    public string RestServicesRoot { get; set; } =
        "https://gisweb.phivolcs.dost.gov.ph/arcgis/rest/services/PHIVOLCSPublic";
}
