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

    /// <summary>
    /// Base path of the public ArcGIS OGC services. Individual layer endpoints are
    /// stored per layer in the hazard catalogue rather than here, so adding a layer
    /// is a data change and not a configuration change.
    /// </summary>
    public string ServicesRoot { get; set; } =
        "https://gisweb.phivolcs.dost.gov.ph/arcgis/services/PHIVOLCSPublic";
}
