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
