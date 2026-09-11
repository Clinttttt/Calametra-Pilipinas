namespace Calametra.Infrastructure.Sources.Gem;

/// <summary>Configuration for the GEM Global Active Faults adapter.</summary>
public sealed class GemOptions
{
    public const string SectionName = "Sources:Gem";

    /// <summary>Slug of the corresponding <c>DataSource</c> row.</summary>
    public const string Slug = "gem-global-active-faults";

    /// <summary>
    /// The harmonised GeoJSON build. Chosen over <c>gem_active_faults.geojson</c>
    /// because attribute names are normalised across the constituent national
    /// catalogues, so one parser handles every region.
    /// </summary>
    public Uri DatasetUrl { get; set; } = new(
        "https://raw.githubusercontent.com/GEMScienceTools/gem-global-active-faults/master/geojson/gem_active_faults_harmonized.geojson");

    /// <summary>
    /// Generous: the dataset is a single ~10 MB document covering the whole world.
    /// There is no bounding-box query parameter, so the entire file is fetched and
    /// filtered locally — which is also why import is a deliberate, occasional
    /// operation rather than a scheduled poll.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public string UserAgent { get; set; } = "Calametra-Pilipinas/0.1 (academic research platform)";
}
