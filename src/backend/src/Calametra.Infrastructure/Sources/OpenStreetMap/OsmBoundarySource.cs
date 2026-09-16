using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Administrative;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.OpenStreetMap;

/// <summary>
/// Chooses how boundary geometry is acquired: a dated local extract if one is configured, else Overpass.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>PsgcRegisterSource</c>, and for the same reason. A configured file is checked
/// <b>first</b>: an operator who has gone to the trouble of downloading a dated extract means to use it, and
/// silently preferring a live API would make the run depend on which upstream happened to answer.
/// </para>
/// <para>
/// <b>Provenance is the operator's declaration, not this adapter's inference.</b> A <c>.osm.pbf</c> on disk
/// looks the same whether it came from Geofabrik or was assembled by hand, so the reader never promotes a
/// file to a named upstream extract on its own — it records what it was told and hashes what it read.
/// </para>
/// </remarks>
internal sealed class OsmBoundarySource(
    OsmPbfBoundaryReader extractReader,
    OverpassBoundarySource overpass,
    IOptions<OpenStreetMapOptions> options,
    TimeProvider timeProvider,
    ILogger<OsmBoundarySource> logger) : ILguBoundarySource
{
    private readonly OpenStreetMapOptions options = options.Value;

    public async Task<BoundarySnapshot> ReadAsync(
        int adminLevel,
        IReadOnlyList<BoundaryChunk> chunks,
        CancellationToken cancellationToken)
    {
        var path = options.BoundaryExtractFile;

        if (string.IsNullOrWhiteSpace(path))
        {
            return await overpass.ReadAsync(adminLevel, chunks, cancellationToken);
        }

        return ReadExtract(path.Trim(), adminLevel, cancellationToken);
    }

    private BoundarySnapshot ReadExtract(string path, int adminLevel, CancellationToken cancellationToken)
    {
        var result = extractReader.Read(path, adminLevel, cancellationToken);

        var provenance = options.BoundaryExtractIsGeofabrik
            ? BoundaryProvenance.GeofabrikExtract
            : BoundaryProvenance.LocalFile;

        var vintage = ParseVintage(options.BoundaryExtractVintage)
            ?? DateOnly.FromDateTime(
                (result.NewestElementTimestamp ?? timeProvider.GetUtcNow()).UtcDateTime);

        var label = string.IsNullOrWhiteSpace(options.BoundaryExtractLabel)
            ? $"OSM extract {result.OriginalFileName}"
            : options.BoundaryExtractLabel.Trim();

        var vintageText = vintage.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        BoundaryExtractLog.ExtractRead(
            logger,
            result.OriginalFileName,
            result.FileSha256,
            result.Features.Count,
            vintageText);

        return new BoundarySnapshot
        {
            Features = result.Features,
            Unassembled = result.Unassembled,

            // No chunks, and therefore no chunk can be missing. That is the substantive advantage of the
            // file path over the API one: coverage is limited by what the extract contains rather than by
            // which requests a loaded server happened to refuse.
            FailedChunks = [],
            ExtractedAt = timeProvider.GetUtcNow(),
            ExtractVersion = result.NewestElementTimestamp?.ToString("O"),
            Provenance = provenance,
            Label = label,
            AccessRoute = path,
            OriginalFileName = result.OriginalFileName,
            FileSha256 = result.FileSha256,
            FileSizeBytes = result.FileSizeBytes,
            Vintage = vintage,
            AcquisitionNote = options.BoundaryExtractAcquisitionNote,
        };
    }

    private static DateOnly? ParseVintage(string? value) =>
        DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

internal static partial class BoundaryExtractLog
{
    [LoggerMessage(
        EventId = 7325,
        Level = LogLevel.Information,
        Message = "Read OSM extract {FileName} (SHA-256 {Sha256}), vintage {Vintage}: {Outlines} outline(s)")]
    public static partial void ExtractRead(
        ILogger logger,
        string fileName,
        string sha256,
        int outlines,
        string vintage);
}
