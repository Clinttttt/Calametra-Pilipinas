namespace Calametra.Infrastructure.Sources.Mgb;

/// <summary>
/// Identity and endpoints of the DOST-MGB susceptibility services.
/// </summary>
/// <remarks>
/// <para>
/// Every value here was probed on 2026-09-11 rather than taken from documentation. The host was
/// the hard part: <c>gis.mgb.gov.ph</c> and <c>gisserver.mgb.gov.ph</c> do not resolve,
/// <c>gdis.mgb.gov.ph</c> presents a certificate that does not validate, and
/// <c>mgb.gov.ph</c> answers 403 to a programmatic request. The live ArcGIS server is
/// <c>controlmap.mgb.gov.ph</c>, found through the ArcGIS Online search API.
/// </para>
/// <para>
/// <b>Both services are proxied and neither is stored.</b> MGB publishes no licence: the
/// services carry an empty <c>copyrightText</c> and the layers are not ArcGIS Online items, so
/// there is no terms statement to read. Reachability is not permission, so the data source stays
/// non-redistributable and the domain refuses to store a feature from it.
/// </para>
/// </remarks>
public sealed class MgbOptions
{
    public const string SectionName = "Sources:Mgb";

    /// <summary>Slug of the <c>DataSource</c> row for the rain-induced landslide susceptibility map.</summary>
    public const string RainInducedLandslideSlug = "mgb-rain-induced-landslide-susceptibility";

    /// <summary>Slug of the <c>DataSource</c> row for the flood susceptibility map.</summary>
    public const string FloodSlug = "mgb-flood-susceptibility";

    /// <summary>
    /// Base path of the OGC services, which is where the WMS extension is exposed.
    /// </summary>
    /// <remarks>
    /// Note <c>/arcgis/services/</c> rather than <c>/arcgis/rest/services/</c>. The REST path
    /// serves the service metadata and the <c>identify</c> operation; the OGC path serves WMS.
    /// Both are needed, and confusing them yields a 404 rather than an informative error.
    /// </remarks>
    public string ServicesRoot { get; set; } =
        "https://controlmap.mgb.gov.ph/arcgis/services/GeospatialDataInventory_Public";

    /// <summary>Base path of the REST services, used for the <c>identify</c> operation.</summary>
    public string RestServicesRoot { get; set; } =
        "https://controlmap.mgb.gov.ph/arcgis/rest/services/GeospatialDataInventory_Public";

    /// <summary>Service name of the rain-induced landslide susceptibility map.</summary>
    public string RainInducedLandslideService { get; set; } =
        "GDI_Detailed_Rain_induced_Landslide_Susceptibility_Public";

    /// <summary>Service name of the flood susceptibility map.</summary>
    public string FloodService { get; set; } = "GDI_Detailed_Flood_Susceptibility_Public";
}
