namespace Calametra.Domain.Sources;

/// <summary>
/// How Calametra obtains data from an upstream source. Determines what the
/// platform is permitted to store and serve, not merely how it fetches.
/// </summary>
public enum SourceAccessKind
{
    Unknown = 0,

    /// <summary>Documented HTTP API returning structured data. Example: USGS FDSN event service.</summary>
    RestApi = 1,

    /// <summary>Bulk file downloaded and parsed. Example: NOAA IBTrACS basin CSV.</summary>
    BulkFile = 2,

    /// <summary>
    /// Rendered map imagery proxied from the publisher at request time. Geometry is
    /// never stored locally. Example: PHIVOLCS ArcGIS WMS active-fault layer.
    /// </summary>
    WmsProxy = 3,

    /// <summary>
    /// Per-feature attribute lookup at user request. Example: ArcGIS
    /// <c>identify</c> / WMS <c>GetFeatureInfo</c> when a user clicks a fault.
    /// Suitable for interactive inspection, never for bulk extraction.
    /// </summary>
    FeatureInfoLookup = 4,

    /// <summary>Dataset supplied directly by the publisher, typically under a written request.</summary>
    GrantedDataset = 5,

    /// <summary>Loaded once by hand from a published file, with provenance recorded.</summary>
    ManualImport = 6,
}
