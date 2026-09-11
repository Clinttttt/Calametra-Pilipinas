namespace Calametra.Infrastructure.Sources.ArcGis;

/// <summary>
/// Settings for the hazard map proxy, shared by every agency whose imagery it serves.
/// </summary>
/// <remarks>
/// Separated from <c>PhivolcsOptions</c> when DOST-MGB became the second publisher behind the
/// same proxy. What remains agency-specific — the slugs and the services root used when seeding
/// the catalogue — stays with each agency's own options; the timeout, the cache duration and the
/// user agent are properties of how this platform behaves as a client, not of whose service it
/// is calling.
/// <para>
/// Individual layer endpoints live on the catalogue row rather than here, so adding a layer
/// remains a data change and not a configuration change.
/// </para>
/// </remarks>
public sealed class HazardProxyOptions
{
    public const string SectionName = "Sources:HazardProxy";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a proxied tile may be cached.
    /// </summary>
    /// <remarks>
    /// Hazard layers, fault traces and susceptibility maps all change on a timescale of years,
    /// so caching aggressively is both safe and the polite way to consume someone else's map
    /// service. This is the control that actually protects the upstream agency: a tile is
    /// fetched once and then served from memory to every later request, so the publisher sees
    /// one request per distinct tile per cache lifetime however many people are panning.
    /// </remarks>
    public TimeSpan TileCacheDuration { get; set; } = TimeSpan.FromDays(7);

    public string UserAgent { get; set; } = "Calametra-Pilipinas/0.1 (academic research platform)";
}
