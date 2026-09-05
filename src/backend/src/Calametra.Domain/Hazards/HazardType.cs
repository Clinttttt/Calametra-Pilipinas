namespace Calametra.Domain.Hazards;

/// <summary>
/// Hazard layer types available from official sources.
/// </summary>
/// <remarks>
/// Enumerated from the twelve services published on the DOST-PHIVOLCS public
/// ArcGIS endpoint, verified reachable on 2026-09-04. Phase 1 uses
/// <see cref="ActiveFault"/> and <see cref="Trench"/>; the remainder are Phase 2
/// and later, listed now so the catalogue and the lens grouping do not need
/// reshaping when they are switched on.
/// </remarks>
public enum HazardType
{
    Unknown = 0,

    // Phase 1 — seismic context
    ActiveFault = 1,
    Trench = 2,

    // Phase 2 — earthquake hazard context
    GroundShaking = 10,
    Liquefaction = 11,
    EarthquakeInducedLandslide = 12,
    Tsunami = 13,
    Seiche = 14,

    // Later phases — volcanic
    VolcanoLocation = 20,
    PyroclasticFlow = 21,
    Lava = 22,
    VolcanicLahar = 23,
    BaseSurge = 24,
}

/// <summary>
/// Thematic groupings that replace a long list of unrelated checkboxes.
/// </summary>
/// <remarks>
/// Implements the "lens" interaction from the project concept: selecting a lens
/// emphasises related layers and de-emphasises everything else, so the map stays
/// legible as the number of datasets grows.
/// </remarks>
public enum HazardLens
{
    Seismic = 0,
    Coastal = 1,
    Terrain = 2,
    Cyclone = 3,
    Exposure = 4,
    History = 5,
}

/// <summary>How a hazard layer's geometry reaches the browser.</summary>
public enum LayerDeliveryMode
{
    /// <summary>
    /// Imagery is proxied from the publisher's map service at request time and
    /// geometry is never stored. The only mode permitted for sources whose
    /// <c>IsRedistributable</c> flag is false.
    /// </summary>
    RemoteWms = 0,

    /// <summary>
    /// Geometry is stored in PostGIS and served as vector tiles or GeoJSON.
    /// Requires a source marked redistributable.
    /// </summary>
    LocalVector = 1,
}
