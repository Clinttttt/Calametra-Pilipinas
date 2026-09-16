using Calametra.Application.Abstractions.Sources;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.CodAb;

/// <summary>
/// Options for the OCHA COD-AB Philippine administrative boundary set.
/// </summary>
/// <remarks>
/// A file rather than an endpoint. HDX publishes the set as a single archive, and the immutable-file pattern
/// this project already uses for the PSA workbook and the OSM extract gives one digest, one vintage and one
/// licence for the whole read — rather than provenance assembled from however many requests happened to
/// succeed.
/// </remarks>
public sealed class CodAbOptions
{
    public const string SectionName = "Sources:CodAb";

    /// <summary>Slug of the corresponding <c>DataSource</c> row.</summary>
    public const string Slug = "ocha-cod-ab-phl";

    /// <summary>Path to the published <c>phl_admin_boundaries.shp.zip</c>.</summary>
    public string? ArchiveFile { get; set; }

    /// <summary>What a person would cite, e.g. "OCHA COD-AB Philippines, 2026-05-28".</summary>
    public string? Label { get; set; }

    /// <summary>
    /// The date the publisher states the set represents.
    /// </summary>
    /// <remarks>
    /// Configured rather than parsed from the file, for the same reason the OSM extract's is: a filename is a
    /// convention and a vintage is a claim.
    /// </remarks>
    public string? Vintage { get; set; }

    /// <summary>Where the operator says they got it, in their own words.</summary>
    public string? AcquisitionNote { get; set; }

    /// <summary>
    /// The operator's declaration that this archive is the OCHA COD-AB publication.
    /// </summary>
    /// <remarks>
    /// Nothing infers it. A shapefile archive looks the same whoever built it, and provenance that can be
    /// guessed is provenance nobody can rely on.
    /// </remarks>
    public bool IsOchaPublication { get; set; }
}

/// <summary>
/// Reads the COD-AB unit list, normalising its PCODEs to canonical ten-digit PSGC codes.
/// </summary>/// <remarks>
/// <para>
/// COD-AB writes the code as <c>PH</c> followed by seven digits — <c>PH0102801</c> for Adams — which is the
/// ten-digit canonical code with its trailing barangay triplet dropped. The normalisation restores it:
/// <c>PH0102801</c> becomes <c>0102801000</c>.
/// </para>
/// <para>
/// That is a <b>format</b> normalisation of one code, not an identity claim. Whether the code means the same
/// unit as the current register's is a separate question, answered by direct match where the editions agree
/// and by reviewed correspondence where they do not.
/// </para>
/// </remarks>
internal sealed class CodAbCatalogueSource(
    CodAbBoundaryReader reader,
    IOptions<CodAbOptions> options) : IBoundaryCatalogueSource
{
    private readonly CodAbOptions options = options.Value;

    public Task<IReadOnlyList<BoundaryCatalogueUnit>> ReadUnitsAsync(
        CancellationToken cancellationToken)
    {
        var path = options.ArchiveFile;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Sources:CodAb:ArchiveFile is not set, so there is no boundary catalogue to read.");
        }

        var catalogue = reader.ReadCatalogue(path.Trim(), cancellationToken);

        var units = new List<BoundaryCatalogueUnit>(catalogue.Units.Count);

        foreach (var unit in catalogue.Units)
        {
            var canonical = ToCanonicalCode(unit.Pcode);

            if (canonical is not null)
            {
                units.Add(new BoundaryCatalogueUnit(canonical, unit.Name, unit.StatedAreaSquareKm));
            }
        }

        return Task.FromResult<IReadOnlyList<BoundaryCatalogueUnit>>(units);
    }

    /// <summary>
    /// <c>PH0102801</c> to <c>0102801000</c>. Returns null for anything that is not that shape.
    /// </summary>
    internal static string? ToCanonicalCode(string? pcode)
    {
        if (string.IsNullOrWhiteSpace(pcode))
        {
            return null;
        }

        var trimmed = pcode.Trim();

        if (!trimmed.StartsWith("PH", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = trimmed[2..];

        return digits.Length == 7 && digits.All(char.IsAsciiDigit)
            ? digits + "000"
            : null;
    }
}
