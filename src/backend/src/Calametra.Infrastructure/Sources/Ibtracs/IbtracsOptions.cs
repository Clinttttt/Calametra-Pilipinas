namespace Calametra.Infrastructure.Sources.Ibtracs;

/// <summary>
/// Configuration for the IBTrACS best-track archive.
/// </summary>
/// <remarks>
/// The URL is configurable because IBTrACS is versioned in its path — <c>v04r01</c> today,
/// <c>v04r00</c> before it. A version bump is a config change, not a deployment.
/// </remarks>
public sealed class IbtracsOptions
{
    public const string SectionName = "Ibtracs";

    /// <summary>Slug for the archive itself, distinct from the agencies whose fixes it carries.</summary>
    public const string ArchiveSlug = "noaa-ibtracs";

    /// <summary>
    /// The western North Pacific basin file.
    /// </summary>
    /// <remarks>
    /// The basin containing the Philippines. Roughly 114 MB and streamed rather than
    /// downloaded, so its size affects ingestion duration but not memory.
    /// </remarks>
    public string WesternPacificCsvUrl { get; set; } =
        "https://www.ncei.noaa.gov/data/international-best-track-archive-for-climate-stewardship-ibtracs/"
        + "v04r01/access/csv/ibtracs.WP.list.v04r01.csv";

    /// <summary>
    /// Earliest season to ingest.
    /// </summary>
    /// <remarks>
    /// Defaults to 1945. IBTrACS reaches back to 1884, but pre-satellite tracks in this basin
    /// are sparse and positionally uncertain, and only one agency reports them — so they carry
    /// none of the multi-agency comparison this platform is built around. A lower value is
    /// accepted; the completeness caveat is the caller's to communicate.
    /// </remarks>
    public int FromSeason { get; set; } = 1945;

    /// <summary>How long to allow for streaming the whole basin file.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
