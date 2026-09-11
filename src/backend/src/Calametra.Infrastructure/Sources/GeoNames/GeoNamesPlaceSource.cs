using System.Globalization;
using System.IO.Compression;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Places;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.GeoNames;

/// <summary>
/// Reads the Philippine administrative place directory from the GeoNames country dump.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why GeoNames and not the PSA.</b> The Philippine Statistics Authority is the authority for
/// the Philippine Standard Geographic Code, and it publishes the register as spreadsheets that
/// carry no coordinates. A place explorer needs a position to fly the camera to, so a coordinate
/// source is unavoidable. GeoNames publishes both — a coordinate and, for the city/municipality
/// level, the PSGC itself — under CC BY 4.0, which this platform may store.
/// </para>
/// <para>
/// <b>Columns are positional here, which is the opposite of the IBTrACS adapter's rule, because
/// this file has no header row at all.</b> The nineteen fields are fixed by the published export
/// specification in <c>readme.txt</c> alongside the dump. The indices below are named constants
/// and the field count is asserted per row, so a change in the export shape fails loudly instead
/// of silently reading a longitude as an elevation.
/// </para>
/// <para>
/// <b>Only the three administrative levels are read.</b> The dump holds 96,645 rows for the
/// Philippines, of which 42,232 are populated places and 19,168 are barangay-level units. Those
/// are settlements and sub-municipal divisions, not the searchable administrative units this
/// feature is about, and importing them would make the directory forty times larger for no
/// added answer. Kept: 17 regions (ADM1), 87 province-level units (ADM2), 1,646
/// cities and municipalities (ADM3). Measured 2026-09-11.
/// </para>
/// <para>
/// <b>Population is deliberately not read, although the dump carries it.</b> GeoNames aggregates
/// population figures from several sources and publishes no census year per row, and
/// <see cref="Place.SetPopulation"/> requires a vintage. The only date on the row is the
/// record's last-modified date, which is when someone edited the entry rather than when anyone
/// counted the people — storing that as the vintage would be inventing provenance. Exposure
/// figures wait on the WorldPop preprocessing the domain already documents.
/// </para>
/// <para>
/// <b>The archive is buffered, unlike the cyclone basin file.</b> 2.5 MB, and
/// <see cref="ZipArchive"/> in read mode needs a seekable stream. The contrast with IBTrACS is
/// deliberate: that file is 114 MB and must be streamed, this one is bounded and small enough
/// that buffering is the simpler correct choice.
/// </para>
/// </remarks>
internal sealed class GeoNamesPlaceSource(
    HttpClient httpClient,
    IOptions<GeoNamesOptions> options,
    ILogger<GeoNamesPlaceSource> logger) : IPlaceDirectorySource
{
    // Field positions from the GeoNames export specification. Zero-based.
    private const int NameColumn = 1;
    private const int LatitudeColumn = 4;
    private const int LongitudeColumn = 5;
    private const int FeatureCodeColumn = 7;
    private const int Admin1Column = 10;
    private const int Admin2Column = 11;
    private const int Admin3Column = 12;
    private const int ExpectedColumnCount = 19;

    /// <summary>Length of the code GeoNames publishes: the pre-2019 nine-digit PSGC.</summary>
    private const int PsgcCodeLength = 9;

    private readonly GeoNamesOptions _options = options.Value;

    public string SourceSlug => GeoNamesOptions.Slug;

    public async Task<IReadOnlyList<CatalogPlace>> FetchAsync(CancellationToken cancellationToken)
    {
        var rows = await ReadAdministrativeRowsAsync(cancellationToken);

        var regions = new List<GeoNamesRow>();
        var provinces = new List<GeoNamesRow>();
        var localGovernments = new List<GeoNamesRow>();

        foreach (var row in rows)
        {
            switch (row.FeatureCode)
            {
                case "ADM1":
                    regions.Add(row);
                    break;
                case "ADM2":
                    provinces.Add(row);
                    break;
                case "ADM3":
                    localGovernments.Add(row);
                    break;
                default:
                    break;
            }
        }

        GeoNamesLog.RowsRead(logger, rows.Count, regions.Count, provinces.Count, localGovernments.Count);

        // Derived codes are computed a whole level at a time, because a code that two units at the
        // same level would share identifies neither of them and has to be withheld from both.
        var regionCodes = UniqueDerivedCodes(
            regions,
            region => RegionKey(region.Admin1),
            region => localGovernments
                .Where(child => string.Equals(child.Admin1, region.Admin1, StringComparison.Ordinal))
                .Select(child => child.Admin3),
            significantDigits: 2);

        var provinceCodes = UniqueDerivedCodes(
            provinces,
            province => ProvinceKey(province.Admin1, province.Admin2),
            province => localGovernments
                .Where(child => string.Equals(child.Admin1, province.Admin1, StringComparison.Ordinal)
                    && string.Equals(child.Admin2, province.Admin2, StringComparison.Ordinal))
                .Select(child => child.Admin3),
            significantDigits: 4);

        var places = new List<CatalogPlace>(regions.Count + provinces.Count + localGovernments.Count);

        // Coarsest first, so a parent always precedes its children as the port promises.
        foreach (var region in regions)
        {
            places.Add(new CatalogPlace
            {
                Key = RegionKey(region.Admin1),
                ParentKey = null,
                Name = region.Name,
                Kind = PlaceKind.Region,
                Latitude = region.Latitude,
                Longitude = region.Longitude,
                PsgcCode = regionCodes[RegionKey(region.Admin1)],
            });
        }

        var regionKeys = regions
            .Select(region => region.Admin1)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var province in provinces)
        {
            places.Add(new CatalogPlace
            {
                Key = ProvinceKey(province.Admin1, province.Admin2),
                ParentKey = regionKeys.Contains(province.Admin1) ? RegionKey(province.Admin1) : null,
                Name = province.Name,
                // A city-marked name at this level is an independent city administered as a
                // province-equivalent — Cotabato City is the one in this dump, and it has no
                // child municipalities because it contains none.
                Kind = NameCarriesCityMarker(province.Name) ? PlaceKind.City : PlaceKind.Province,
                Latitude = province.Latitude,
                Longitude = province.Longitude,
                PsgcCode = provinceCodes[ProvinceKey(province.Admin1, province.Admin2)],
            });
        }

        var provinceKeys = provinces
            .Select(province => ProvinceKey(province.Admin1, province.Admin2))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var localGovernment in localGovernments)
        {
            var provinceKey = ProvinceKey(localGovernment.Admin1, localGovernment.Admin2);

            places.Add(new CatalogPlace
            {
                Key = LocalGovernmentKey(localGovernment.Admin3),
                ParentKey = provinceKeys.Contains(provinceKey)
                    ? provinceKey
                    : regionKeys.Contains(localGovernment.Admin1)
                        ? RegionKey(localGovernment.Admin1)
                        : null,
                Name = localGovernment.Name,
                Kind = NameCarriesCityMarker(localGovernment.Name)
                    ? PlaceKind.City
                    : PlaceKind.Municipality,
                Latitude = localGovernment.Latitude,
                Longitude = localGovernment.Longitude,
                PsgcCode = IsPsgcShaped(localGovernment.Admin3) ? localGovernment.Admin3 : null,
            });
        }

        return places;
    }

    /// <summary>
    /// Whether the official name marks this unit as a city.
    /// </summary>
    /// <remarks>
    /// GeoNames does not distinguish a city from a municipality: both are ADM3, which is the
    /// level the PSGC itself calls "city/municipality". The distinction is recovered from the
    /// official name, and the error that leaves was measured against the PSA register rather
    /// than assumed. The register holds 148 cities and 1,486 municipalities; every city in it
    /// carries a name marker and no municipality does, so the rule cannot promote a municipality.
    /// It can demote a city: GeoNames abbreviates a handful of names, and 138 of the 1,646 ADM3
    /// rows carry a marker against the register's 148. So roughly ten cities are recorded here as
    /// municipalities, and none of the 1,486 municipalities is recorded as a city — a
    /// one-directional error, which is the safer direction for a label the interface shows
    /// beside a name.
    /// </remarks>
    private static bool NameCarriesCityMarker(string name) =>
        name.StartsWith("City of ", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(" City", StringComparison.OrdinalIgnoreCase);

    private static bool IsPsgcShaped(string code) =>
        code.Length == PsgcCodeLength && code.All(char.IsAsciiDigit);

    /// <summary>
    /// Derives a code for every unit at one level, withholding any code two units would share.
    /// </summary>
    /// <remarks>
    /// Found by counting the result rather than by reading the code: the first run stored 1,749 of
    /// 1,750 places and reported one as already present. The missing entry was Maguindanao del
    /// Norte. Maguindanao was divided into del Norte and del Sur in 2022, and the nine-digit codes
    /// in this dump predate the division — every municipality in both halves still carries the
    /// undivided <c>1538</c>, so both provinces derived <c>153800000</c>. The second was then
    /// matched to the first as an existing place, which dropped one province and silently attached
    /// its 36 municipalities to the other.
    /// <para>
    /// A shared code identifies neither unit, so both are left without one. They remain
    /// distinguishable and correctly parented, because the hierarchy is built from the source's own
    /// keys — <c>MGN</c> and <c>MGS</c> here — and never from the derived code.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string?> UniqueDerivedCodes(
        IReadOnlyList<GeoNamesRow> units,
        Func<GeoNamesRow, string> keyOf,
        Func<GeoNamesRow, IEnumerable<string>> childCodesOf,
        int significantDigits)
    {
        var codes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var unit in units)
        {
            var code = DerivedCode(childCodesOf(unit), significantDigits);

            codes[keyOf(unit)] = code;

            if (code is not null)
            {
                occurrences[code] = occurrences.GetValueOrDefault(code) + 1;
            }
        }

        foreach (var key in codes.Keys.ToArray())
        {
            if (codes[key] is { } code && occurrences[code] > 1)
            {
                codes[key] = null;
            }
        }

        return codes;
    }

    /// <summary>
    /// Reconstructs a parent's PSGC code from the codes of its children, or returns null when
    /// they do not agree.
    /// </summary>
    /// <remarks>
    /// GeoNames publishes a PSGC only at the city/municipality level; its region and province
    /// codes are its own internal numbering and do not match the PSGC — Samar is province
    /// <c>55</c> to GeoNames and <c>60</c> in the PSGC. But the PSGC is hierarchical by
    /// construction, so a province code is the leading four digits shared by its municipalities
    /// and a region code the leading two.
    /// <para>
    /// Only when <em>every</em> child agrees. Two entries in this dump legitimately disagree,
    /// and both are real administrative history rather than a parse fault: Isabela City is part
    /// of Basilan but is administered under Region IX, so Basilan's municipalities split across
    /// two region prefixes and the Bangsamoro region does likewise; and Cotabato City has no
    /// child municipalities at all. Those get no code, which is honest — the alternative,
    /// taking the majority prefix, would mint a plausible identifier that PSA never issued.
    /// </para>
    /// </remarks>
    private static string? DerivedCode(IEnumerable<string> childCodes, int significantDigits)
    {
        string? prefix = null;
        var seen = false;

        foreach (var code in childCodes)
        {
            if (!IsPsgcShaped(code))
            {
                return null;
            }

            var candidate = code[..significantDigits];

            if (!seen)
            {
                prefix = candidate;
                seen = true;
                continue;
            }

            if (!string.Equals(prefix, candidate, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return seen
            ? prefix + new string('0', PsgcCodeLength - significantDigits)
            : null;
    }

    private static string RegionKey(string admin1) => "region:" + admin1;

    private static string ProvinceKey(string admin1, string admin2) =>
        "province:" + admin1 + "." + admin2;

    private static string LocalGovernmentKey(string admin3) => "lgu:" + admin3;

    private async Task<List<GeoNamesRow>> ReadAdministrativeRowsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            _options.DatasetUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var buffer = new MemoryStream();

        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await body.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;

        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        var entry = archive.GetEntry(_options.ArchiveEntryName)
            ?? throw new InvalidOperationException(
                $"The GeoNames archive does not contain '{_options.ArchiveEntryName}'. "
                + "Entries present: " + string.Join(", ", archive.Entries.Select(item => item.Name)));

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        var rows = new List<GeoNamesRow>();
        var malformed = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');

            if (fields.Length != ExpectedColumnCount)
            {
                malformed++;
                continue;
            }

            var featureCode = fields[FeatureCodeColumn];

            if (featureCode is not ("ADM1" or "ADM2" or "ADM3"))
            {
                continue;
            }

            var name = fields[NameColumn].Trim();

            if (name.Length == 0
                || !double.TryParse(
                    fields[LatitudeColumn],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var latitude)
                || !double.TryParse(
                    fields[LongitudeColumn],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var longitude))
            {
                malformed++;
                continue;
            }

            rows.Add(new GeoNamesRow(
                name,
                featureCode,
                latitude,
                longitude,
                fields[Admin1Column],
                fields[Admin2Column],
                fields[Admin3Column]));
        }

        if (malformed > 0)
        {
            // Counted and reported rather than thrown on. A row that does not parse is one
            // place missing from a directory of eighteen hundred; a shape change large enough
            // to matter shows up as a large count.
            GeoNamesLog.MalformedRows(logger, malformed);
        }

        return rows;
    }

    private sealed record GeoNamesRow(
        string Name,
        string FeatureCode,
        double Latitude,
        double Longitude,
        string Admin1,
        string Admin2,
        string Admin3);
}

internal static partial class GeoNamesLog
{
    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Information,
        Message = "GeoNames directory read: {AdministrativeRows} administrative rows "
            + "({Regions} regions, {Provinces} province-level, {LocalGovernments} city/municipality)")]
    public static partial void RowsRead(
        ILogger logger,
        int administrativeRows,
        int regions,
        int provinces,
        int localGovernments);

    [LoggerMessage(
        EventId = 6301,
        Level = LogLevel.Warning,
        Message = "GeoNames directory: {Count} rows did not match the published export shape and were skipped")]
    public static partial void MalformedRows(ILogger logger, int count);
}
