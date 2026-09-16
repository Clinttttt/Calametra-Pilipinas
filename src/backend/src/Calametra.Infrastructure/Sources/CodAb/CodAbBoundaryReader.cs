using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.IO.Streams;

namespace Calametra.Infrastructure.Sources.CodAb;

/// <summary>
/// Reads the OCHA COD-AB Philippine administrative boundary set from its published archive.
/// </summary>
/// <remarks>
/// <para>
/// <b>Land outlines, and that is the point.</b> This set answers which municipality a point on land belongs
/// to. Measured 2026-09-16 it sums to 293,507 km² across 1,642 units, against roughly 300,000 km² of
/// Philippine land — whereas the OSM boundary relations sum to 260,529 km² from only 550 units because they
/// extend to municipal waters. The two are different quantities and ADR-005 D2a forbids mixing them.
/// </para>
/// <para>
/// <b>Identity comes from <c>adm3_pcode</c>.</b> The set carries the PSGC, so no boundary here is matched
/// by name — which matters because 1,647 units in the comparable geoBoundaries republication share only
/// 1,424 distinct names. geoBoundaries was evaluated and rejected for exactly this reason: it strips the
/// PCODEs its own upstream publishes, leaving nothing but names to join on.
/// </para>
/// <para>
/// <b>The catalogue is readable without the geometry.</b> The attribute table lives in the shapefile's
/// <c>.dbf</c>, a few hundred kilobytes beside hundreds of megabytes of coordinates, so the edition
/// correspondence can be proposed and reviewed before a single polygon is imported. That ordering is
/// deliberate: identity is settled first, as it has been at every step of ADR-005.
/// </para>
/// </remarks>
internal sealed class CodAbBoundaryReader(ILogger<CodAbBoundaryReader> logger)
{
    /// <summary>WGS 84. The archive's own projection file is empty; the coordinates are lon/lat degrees.</summary>
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    /// <summary>The ADM3 layer: cities and municipalities.</summary>
    private const string Adm3Layer = "phl_admin3";

    /// <summary>
    /// Reads the unit catalogue — codes, names and stated areas — without touching geometry.
    /// </summary>
    public CatalogueResult ReadCatalogue(string archivePath, CancellationToken cancellationToken)
    {
        var (hash, info) = Fingerprint(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);

        var entry = FindLayerEntry(archive, ".dbf");

        using var dbf = ToSeekableStream(entry, cancellationToken);

        var units = ReadDbf(dbf, cancellationToken);

        CodAbLog.CatalogueRead(logger, info.Name, hash, units.Count);

        return new CatalogueResult(units, info.Name, hash, info.Length);
    }

    /// <summary>
    /// Reads the ADM3 geometry, keyed by the PSGC code each feature carries.
    /// </summary>
    public GeometryResult ReadGeometry(string archivePath, CancellationToken cancellationToken)
    {
        var (hash, info) = Fingerprint(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);

        // The reader needs .shp and .dbf as seekable streams and reads them in step, so both are buffered.
        // A shapefile is not a stream format: the index is at the end and records are addressed by offset.
        using var shp = ToSeekableStream(FindLayerEntry(archive, ".shp"), cancellationToken);
        using var dbf = ToSeekableStream(FindLayerEntry(archive, ".dbf"), cancellationToken);

        var features = new List<CodAbFeature>();
        var rejected = new List<string>();
        var repaired = 0;

        using var reader = new ShapefileDataReader(
            new ShapefileStreamProviderRegistry(
                new ByteStreamProvider(StreamTypes.Shape, shp.ToArray()),
                new ByteStreamProvider(StreamTypes.Data, dbf.ToArray()),
                true,
                true),
            Factory);

        var pcodeIndex = -1;
        var nameIndex = -1;

        // ShapefileDataReader exposes the geometry at ordinal 0 and the .dbf fields after it, while
        // DbaseHeader.Fields is indexed from zero over the fields alone. Off by one here reads the geometry
        // as though it were the code column, which is exactly what it did until this was corrected.
        for (var field = 0; field < reader.DbaseHeader.NumFields; field++)
        {
            var name = reader.DbaseHeader.Fields[field].Name;

            if (string.Equals(name, "adm3_pcode", StringComparison.OrdinalIgnoreCase))
            {
                pcodeIndex = field + 1;
            }
            else if (string.Equals(name, "adm3_name", StringComparison.OrdinalIgnoreCase))
            {
                nameIndex = field + 1;
            }
        }

        if (pcodeIndex < 0)
        {
            throw new InvalidOperationException(
                "The ADM3 layer carries no adm3_pcode column. Identity cannot be established from this "
                + "archive, and ADR-005 forbids falling back to names.");
        }

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pcode = reader.GetValue(pcodeIndex)?.ToString()?.Trim();
            var name = nameIndex >= 0 ? reader.GetValue(nameIndex)?.ToString()?.Trim() : null;

            if (string.IsNullOrEmpty(pcode))
            {
                rejected.Add($"'{name}': no adm3_pcode");

                continue;
            }

            var geometry = Normalise(reader.Geometry);

            if (geometry is null)
            {
                rejected.Add($"{pcode} '{name}': geometry is not polygonal");

                continue;
            }

            var wasRepaired = false;

            if (!geometry.IsValid)
            {
                var fixedUp = Repair(geometry);

                if (fixedUp is null)
                {
                    rejected.Add($"{pcode} '{name}': invalid geometry could not be repaired");

                    continue;
                }

                geometry = fixedUp;
                wasRepaired = true;
                repaired++;
            }

            features.Add(new CodAbFeature(
                pcode,
                name,
                geometry,
                GeodeticArea.SquareKilometres(geometry),
                wasRepaired));
        }

        CodAbLog.GeometryRead(logger, features.Count, repaired, rejected.Count);

        return new GeometryResult(features, rejected, info.Name, hash, info.Length);
    }

    private static (string Hash, FileInfo Info) Fingerprint(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                $"The COD-AB archive was not found at '{archivePath}'. "
                + "Sources:CodAb:ArchiveFile must point at the .shp.zip as published on HDX.",
                archivePath);
        }

        var info = new FileInfo(archivePath);

        // Hashed before anything is parsed, so the digest describes the bytes HDX published rather than
        // whatever a conversion produced. The same rule the PSA workbook import follows.
        using var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16,
            FileOptions.SequentialScan);

        return (Convert.ToHexStringLower(SHA256.HashData(stream)), info);
    }

    private static ZipArchiveEntry FindLayerEntry(ZipArchive archive, string extension)
    {
        foreach (var entry in archive.Entries)
        {
            var name = Path.GetFileNameWithoutExtension(entry.FullName);

            if (name.Contains(Adm3Layer, StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        var available = string.Join(", ", archive.Entries.Select(entry => entry.FullName));

        throw new InvalidOperationException(
            $"No '{Adm3Layer}*{extension}' entry in the archive. It held: {available}");
    }

    private static MemoryStream ToSeekableStream(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));

        using var stream = entry.Open();

        stream.CopyTo(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        buffer.Position = 0;

        return buffer;
    }

    private static List<CodAbUnit> ReadDbf(MemoryStream dbf, CancellationToken cancellationToken)
    {
        var bytes = dbf.ToArray();
        var recordCount = BitConverter.ToInt32(bytes, 4);
        var headerLength = BitConverter.ToInt16(bytes, 8);
        var recordLength = BitConverter.ToInt16(bytes, 10);

        var fields = new List<(string Name, int Offset, int Length)>();
        var offset = 32;
        var cursor = 1;

        while (bytes[offset] != 0x0D)
        {
            var name = System.Text.Encoding.ASCII
                .GetString(bytes, offset, 11)
                .TrimEnd('\0')
                .Trim();

            var length = bytes[offset + 16];

            fields.Add((name, cursor, length));
            cursor += length;
            offset += 32;
        }

        var pcode = fields.FindIndex(field =>
            string.Equals(field.Name, "adm3_pcode", StringComparison.OrdinalIgnoreCase));
        var name_ = fields.FindIndex(field =>
            string.Equals(field.Name, "adm3_name", StringComparison.OrdinalIgnoreCase));
        var area = fields.FindIndex(field =>
            string.Equals(field.Name, "area_sqkm", StringComparison.OrdinalIgnoreCase));

        if (pcode < 0)
        {
            throw new InvalidOperationException(
                "The ADM3 attribute table carries no adm3_pcode column, so identity cannot be established "
                + $"by code. Columns present: {string.Join(", ", fields.Select(field => field.Name))}");
        }

        var units = new List<CodAbUnit>(recordCount);

        for (var record = 0; record < recordCount; record++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = headerLength + (record * recordLength);

            if (start + recordLength > bytes.Length)
            {
                break;
            }

            var code = Read(bytes, start, fields[pcode]);

            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            var statedArea = 0d;

            if (area >= 0
                && double.TryParse(
                    Read(bytes, start, fields[area]),
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                statedArea = parsed;
            }

            units.Add(new CodAbUnit(
                code,
                name_ >= 0 ? Read(bytes, start, fields[name_]) : null,
                statedArea));
        }

        return units;

        static string Read(byte[] bytes, int recordStart, (string Name, int Offset, int Length) field) =>
            System.Text.Encoding.UTF8
                .GetString(bytes, recordStart + field.Offset, field.Length)
                .Replace("\0", string.Empty, StringComparison.Ordinal)
                .Trim();
    }

    /// <summary>Brings every polygonal geometry to <see cref="MultiPolygon"/> so storage is one shape.</summary>
    private static MultiPolygon? Normalise(Geometry? geometry) =>
        geometry switch
        {
            MultiPolygon multi => multi,
            Polygon polygon => Factory.CreateMultiPolygon([polygon]),
            _ => null,
        };

    private static MultiPolygon? Repair(MultiPolygon geometry)
    {
        var buffered = geometry.Buffer(0);

        return buffered switch
        {
            MultiPolygon multi when multi.IsValid && !multi.IsEmpty => multi,
            Polygon polygon when polygon.IsValid && !polygon.IsEmpty =>
                Factory.CreateMultiPolygon([polygon]),
            _ => null,
        };
    }
}

/// <param name="StatedAreaSquareKm">The set's own area figure, kept to check this platform's computation against.</param>
internal sealed record CodAbUnit(string Pcode, string? Name, double StatedAreaSquareKm);

internal sealed record CatalogueResult(
    IReadOnlyList<CodAbUnit> Units,
    string OriginalFileName,
    string FileSha256,
    long FileSizeBytes);

internal sealed record CodAbFeature(
    string Pcode,
    string? Name,
    MultiPolygon Geometry,
    double AreaSquareKm,
    bool WasRepaired);

internal sealed record GeometryResult(
    IReadOnlyList<CodAbFeature> Features,
    IReadOnlyList<string> Rejected,
    string OriginalFileName,
    string FileSha256,
    long FileSizeBytes);

internal static partial class CodAbLog
{
    [LoggerMessage(
        EventId = 7340,
        Level = LogLevel.Information,
        Message = "COD-AB catalogue read from {FileName} (SHA-256 {Sha256}): {Units} ADM3 unit(s)")]
    public static partial void CatalogueRead(ILogger logger, string fileName, string sha256, int units);

    [LoggerMessage(
        EventId = 7341,
        Level = LogLevel.Information,
        Message = "COD-AB geometry read: {Features} outline(s), {Repaired} repaired, {Rejected} rejected")]
    public static partial void GeometryRead(ILogger logger, int features, int repaired, int rejected);
}
