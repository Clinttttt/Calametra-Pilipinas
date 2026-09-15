namespace Calametra.Infrastructure.Sources.OpenStreetMap;

/// <summary>
/// Configuration for reading Philippine town centres from OpenStreetMap.
/// </summary>
/// <remarks>
/// <para>
/// Read for one purpose: to place a city or municipality at its town centre rather than at the
/// gazetteer's administrative point. Measured across this archive, 1,090 of the 1,647 GeoNames
/// coordinates are rounded to the nearest arc-minute — a degrees-and-minutes table, which cannot be
/// better than about 900 m — five are rounded to a quarter of a degree, and the mean distance from
/// the GeoNames point to the mapped town centre is 5.2 km, reaching 31 km at worst. Every distance
/// this platform states for a place is measured from that point, so the error propagates into the
/// radius search, the place context and the naming of every epicentre.
/// </para>
/// <para>
/// <b>Queried through Overpass, and cached rather than polled.</b> Administrative geography changes
/// by legislation, so this is a one-shot import like the place directory itself, not a scheduled
/// worker. One request returns the whole country.
/// </para>
/// <para>
/// <b>ODbL 1.0, which is share-alike.</b> Attribution to OpenStreetMap contributors is mandatory and
/// a derived database inherits the licence — the same position as GEM's CC BY-SA fault traces, and
/// the reason this is registered as its own source rather than folded into the GeoNames row.
/// </para>
/// </remarks>
public sealed class OpenStreetMapOptions
{
    public const string SectionName = "Sources:OpenStreetMap";

    /// <summary>Slug of the corresponding <c>DataSource</c> row.</summary>
    public const string Slug = "openstreetmap-ph-places";

    /// <summary>
    /// Slug of the boundary polygons' own <c>DataSource</c> row.
    /// </summary>
    /// <remarks>
    /// A second row rather than a second use of the first, per ADR-005 D2: the polygons are read from
    /// different OSM objects at a different time, and the Sources page lists datasets rather than
    /// services. They also fail differently — a missing town centre leaves a place on its gazetteer
    /// point, while a missing boundary leaves a unit with no geometry at all.
    /// </remarks>
    public const string BoundarySlug = "openstreetmap-ph-admin-boundaries";

    /// <summary>
    /// The OSM <c>admin_level</c> Philippine cities and municipalities are mapped at.
    /// </summary>
    /// <remarks>
    /// <b>Six, not eight.</b> Verified against the OSM Philippines LGU mapping conventions: region 3,
    /// province 4, city and municipality 6, barangay 10. Level 8 in the Philippines is a city or
    /// municipal <em>administrative district</em> — Quezon City's Diliman and Cubao, Manila's fourteen
    /// districts. An import written against the general-purpose assumption that municipalities are level
    /// 8 would fetch that tier instead and look plausible while being the wrong unit everywhere.
    /// </remarks>
    public const int CityMunicipalityAdminLevel = 6;

    /// <summary>Overpass API endpoint.</summary>
    /// <remarks>
    /// The public instance. It rate-limits by IP and refuses requests without a user agent — a 406
    /// with an HTML body, which is not an obvious diagnosis — so <see cref="UserAgent"/> is not
    /// decoration.
    /// </remarks>
    public string OverpassEndpoint { get; set; } = "https://overpass-api.de/api/interpreter";

    /// <summary>
    /// Identifies this client to Overpass, per its usage policy.
    /// </summary>
    public string UserAgent { get; set; } = "Calametra/1.0 (Philippine hazard research platform)";

    /// <summary>
    /// How long to allow the national query.
    /// </summary>
    /// <remarks>
    /// Generous because the request is national and served from a shared public instance: measured
    /// 13 s for 1,695 nodes on 2026-09-12, but a busy instance queues.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Timeout for one chunk of boundary geometry, and the server-side timeout asked of Overpass.
    /// </summary>
    /// <remarks>
    /// Three minutes, and it is also the server-side timeout asked of Overpass. Chosen from measurement
    /// rather than caution: a cell small enough to be served answers well inside it, and a cell that is
    /// too large is better discovered quickly and quartered than waited out. A generous timeout here does
    /// not buy the data — it only delays the split that actually gets it.
    /// </remarks>
    public TimeSpan BoundaryTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Endpoints to try for boundary geometry, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// More than one because the public instance publishes a two-slot limit and answers 504 within seconds
    /// when it is busy — measured repeatedly on 2026-09-15, including for cells over open sea that can
    /// contain almost nothing. That failure is load, not payload, and rotating to a mirror is both the
    /// effective response and the polite one: it spreads a national read across volunteer infrastructure
    /// rather than queueing against one host.
    /// </para>
    /// <para>
    /// Kumi Systems runs a well-known mirror with more capacity than the main instance. Both serve the same
    /// OSM data under ODbL, so which one answered changes nothing about provenance beyond the extract
    /// timestamp, which is recorded per import.
    /// </para>
    /// </remarks>
    public string[] BoundaryEndpoints { get; set; } =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
    ];

    /// <summary>
    /// Pause between boundary requests.
    /// </summary>
    /// <remarks>
    /// Five seconds rather than one. This platform is a guest on volunteer infrastructure for a read it
    /// performs once and then holds for months, so the polite pace is also the one that finishes: hammering
    /// a loaded instance returns errors rather than data.
    /// </remarks>
    public TimeSpan BoundaryPause { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far a candidate town centre may be from the gazetteer's point and still be accepted as
    /// the same place.
    /// </summary>
    /// <remarks>
    /// 40 km, and it is a compromise rather than a preference. The gazetteer's own error reaches
    /// 31 km, so a tight radius would reject exactly the places most in need of correction; a wider
    /// one starts accepting a same-named town in the next province, of which the Philippines has
    /// many — 1,459 distinct names across 1,695 nodes. Ambiguity is resolved by refusing rather
    /// than guessing: see <c>RefinePlaceCoordinates</c>.
    /// </remarks>
    public double MatchRadiusKm { get; set; } = 40d;
}
