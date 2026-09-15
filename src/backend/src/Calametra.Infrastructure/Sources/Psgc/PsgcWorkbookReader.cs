using System.Globalization;
using System.Security.Cryptography;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Administrative;
using ClosedXML.Excel;

namespace Calametra.Infrastructure.Sources.Psgc;

/// <summary>
/// Reads the PSA's PSGC publication workbook from disk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a file and not a fetch.</b> Measured 2026-09-15: <c>psa.gov.ph</c> returned HTTP 403 to two
/// different clients, the classification API's advertised endpoints returned 400, and the legacy
/// <c>nap.psa.gov.ph</c> host no longer resolves. A browser session reaches the publication; a script
/// does not. ADR-005 gate 1 therefore completes through an operator download, which is also why
/// provenance is a declaration: a file on disk carries none of its own.
/// </para>
/// <para>
/// <b>Header-driven, not position-driven.</b> The PSA has changed column order and worksheet names
/// between publications, so every column is located by matching its header text. When a required column
/// cannot be found the reader fails with the headers it actually saw, which is a fixable error message
/// rather than a silent misparse — the failure mode of a position-driven reader is a register full of
/// plausible nonsense.
/// </para>
/// <para>
/// <b>The hash is over the original bytes.</b> Taken from the file as downloaded, before any parsing, so
/// it describes what the PSA served rather than what this platform made of it.
/// </para>
/// </remarks>
internal static class PsgcWorkbookReader
{
    /// <summary>Header variants seen across PSA publications for the ten-digit code.</summary>
    private static readonly string[] CanonicalCodeHeaders =
        ["10-digit psgc", "psgc code", "10 digit psgc", "psgc 10-digit code", "10-digit code"];

    /// <summary>The nine-digit code. The PSA calls it the correspondence code.</summary>
    private static readonly string[] HistoricalCodeHeaders =
        ["correspondence code", "9-digit psgc", "old psgc", "9 digit code", "correspondence"];

    private static readonly string[] NameHeaders =
        ["name", "geographic name", "psgc name", "location name"];

    /// <summary>Region, Province, City, Mun — the PSA's own level marker.</summary>
    private static readonly string[] LevelHeaders =
        ["geographic level", "level", "geolevel", "geographic  level"];

    public static RegisterSnapshot Read(
        string path,
        string? worksheetName,
        string? label,
        DateOnly? publicationDate,
        string? acquisitionNote,
        bool isPsaPublication)
    {
        var fileName = Path.GetFileName(path);
        var hash = Sha256Of(path);

        using var workbook = new XLWorkbook(path);

        var sheet = worksheetName is null
            ? workbook.Worksheets.First()
            : workbook.Worksheet(worksheetName);

        var headerRow = FindHeaderRow(sheet, out var columns);

        var units = new List<RegisterUnit>();

        foreach (var row in sheet.RowsUsed().Where(row => row.RowNumber() > headerRow))
        {
            var canonical = Cell(row, columns.CanonicalCode);

            if (canonical.Length == 0)
            {
                continue;
            }

            var level = ParseLevel(Cell(row, columns.Level));

            // Barangays are excluded by ADR-005 and make up the overwhelming majority of the masterlist's
            // 42,000-odd rows, so skipping them here is also what keeps the import proportionate.
            if (level == LguLevel.Unknown)
            {
                continue;
            }

            var historical = columns.HistoricalCode is { } historicalColumn
                ? Cell(row, historicalColumn)
                : string.Empty;

            units.Add(new RegisterUnit
            {
                // The PSA writes codes as text but Excel readers frequently surface them as numbers,
                // which drops the leading zero every Luzon code has. Padding restores it rather than
                // letting 102801000 masquerade as a valid ten-digit code.
                CanonicalCode = canonical.Length == 9 ? canonical.PadLeft(10, '0') : canonical,
                Name = Cell(row, columns.Name),
                Level = level,
                ParentCanonicalCode = null,
                StatedHistoricalCode = historical.Length == 0
                    ? null
                    : historical.Length == 8 ? historical.PadLeft(9, '0') : historical,
            });
        }

        return new RegisterSnapshot
        {
            Label = label ?? $"PSGC publication {fileName}",
            // Declared by the operator. The reader will not promote a file to PSA-direct on its own.
            Provenance = isPsaPublication ? RegisterProvenance.PsaDirect : RegisterProvenance.LocalFile,
            AccessRoute = path,
            UpstreamLastModified = File.GetLastWriteTimeUtc(path),
            PublicationDate = publicationDate,
            OriginalFileName = fileName,
            FileSha256 = hash,
            AcquisitionNote = acquisitionNote,
            Notes = isPsaPublication
                ? $"Read from the PSA publication '{fileName}' (SHA-256 {hash}), worksheet "
                    + $"'{sheet.Name}'. Declared by the operator as a PSA publication."
                : $"Read from '{fileName}' (SHA-256 {hash}). The operator did not declare this a PSA "
                    + "publication, so it cannot certify a crosswalk.",
            Units = units,
        };
    }

    /// <summary>Hashes the file before it is parsed, so the digest is of the bytes as delivered.</summary>
    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();

        return Convert.ToHexStringLower(sha.ComputeHash(stream));
    }

    /// <summary>
    /// Finds the header row by looking for the row that carries the code and name columns.
    /// </summary>
    /// <remarks>
    /// Scanned rather than assumed to be row 1: PSA workbooks carry a title block above the table, and
    /// its depth varies by publication. Only the first twenty rows are considered — beyond that the file
    /// is not the masterlist and saying so is more useful than reading further.
    /// </remarks>
    private static int FindHeaderRow(IXLWorksheet sheet, out ColumnMap columns)
    {
        var seen = new List<string>();

        foreach (var row in sheet.RowsUsed().Take(20))
        {
            var headers = row.CellsUsed()
                .ToDictionary(
                    cell => Normalise(cell.GetString()),
                    cell => cell.Address.ColumnNumber,
                    StringComparer.Ordinal);

            seen = [.. headers.Keys];

            var canonical = Match(headers, CanonicalCodeHeaders);
            var name = Match(headers, NameHeaders);
            var level = Match(headers, LevelHeaders);

            if (canonical is not null && name is not null && level is not null)
            {
                columns = new ColumnMap(
                    canonical.Value,
                    Match(headers, HistoricalCodeHeaders),
                    name.Value,
                    level.Value);

                return row.RowNumber();
            }
        }

        throw new InvalidOperationException(
            $"The worksheet '{sheet.Name}' does not carry the PSGC masterlist columns. Looked for a "
            + $"ten-digit code, a name and a geographic level in the first twenty rows. The last row "
            + $"examined held: {string.Join(", ", seen)}. Point "
            + $"Sources:PsgcRegister:LocalFileWorksheet at the masterlist sheet.");
    }

    private static int? Match(Dictionary<string, int> headers, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalised = Normalise(candidate);

            if (headers.TryGetValue(normalised, out var column))
            {
                return column;
            }
        }

        // Falls back to a prefix match, because publications append qualifiers such as
        // "Correspondence Code (2019)" that an exact list cannot anticipate.
        foreach (var (header, column) in headers)
        {
            if (candidates.Any(candidate =>
                header.StartsWith(Normalise(candidate), StringComparison.Ordinal)))
            {
                return column;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the PSA's geographic level marker.
    /// </summary>
    /// <remarks>
    /// The publication uses short markers — Reg, Prov, City, Mun — and adds others this platform does not
    /// hold: Bgy for barangays, SubMun for the districts of Manila, Dist for legislative districts. Those
    /// return <see cref="LguLevel.Unknown"/> and the row is skipped, which is the same boundary ADR-005
    /// draws in the enum itself.
    /// </remarks>
    private static LguLevel ParseLevel(string value)
    {
        var normalised = Normalise(value);

        return normalised switch
        {
            "reg" or "region" => LguLevel.Region,
            "prov" or "province" => LguLevel.Province,
            "city" => LguLevel.City,
            "mun" or "municipality" => LguLevel.Municipality,
            _ => LguLevel.Unknown,
        };
    }

    private static string Cell(IXLRow row, int column) =>
        row.Cell(column).GetString().Trim();

    private static string Normalise(string value) =>
        new(value
            .ToLower(CultureInfo.InvariantCulture)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .ToArray());

    private sealed record ColumnMap(int CanonicalCode, int? HistoricalCode, int Name, int Level);
}
