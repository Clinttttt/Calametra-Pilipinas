namespace Calametra.Infrastructure.Sources.GeoNames;

/// <summary>Configuration for the GeoNames administrative place directory.</summary>
public sealed class GeoNamesOptions
{
    public const string SectionName = "Sources:GeoNames";

    /// <summary>Slug of the corresponding <c>DataSource</c> row.</summary>
    public const string Slug = "geonames-ph";

    /// <summary>
    /// The Philippine country dump.
    /// </summary>
    /// <remarks>
    /// The per-country file rather than <c>cities15000.zip</c> or <c>allCountries.zip</c>. The
    /// cities file holds only settlements above fifteen thousand people and carries no
    /// administrative hierarchy, so it could not answer "which province is this in"; the global
    /// file is 400 MB to obtain the same 1,750 rows. Measured 2,553,638 bytes on 2026-09-11.
    /// </remarks>
    public Uri DatasetUrl { get; set; } = new("https://download.geonames.org/export/dump/PH.zip");

    /// <summary>Name of the entry inside the archive. GeoNames names it after the country code.</summary>
    public string ArchiveEntryName { get; set; } = "PH.txt";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);

    public string UserAgent { get; set; } = "Calametra-Pilipinas/0.1 (academic research platform)";
}
