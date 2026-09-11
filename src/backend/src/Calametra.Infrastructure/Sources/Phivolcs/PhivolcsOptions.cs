namespace Calametra.Infrastructure.Sources.Phivolcs;

/// <summary>Configuration for the PHIVOLCS hazard map proxy.</summary>
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

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a proxied tile may be cached. Hazard layers and fault traces change
    /// on a timescale of years, so caching aggressively is both safe and the polite
    /// way to consume someone else's map service.
    /// </summary>
    public TimeSpan TileCacheDuration { get; set; } = TimeSpan.FromDays(7);

    public string UserAgent { get; set; } = "Calametra-Pilipinas/0.1 (academic research platform)";
}
