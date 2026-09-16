using System.Globalization;

using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Administrative;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.CodAb;

/// <summary>
/// The COD-AB archive as this platform's canonical on-land geometry source.
/// </summary>
internal sealed class CodAbBoundarySource(
    CodAbBoundaryReader reader,
    IOptions<CodAbOptions> options,
    TimeProvider timeProvider) : ICanonicalBoundarySource
{
    private readonly CodAbOptions options = options.Value;

    public string SourceSlug => CodAbOptions.Slug;

    public Task<CanonicalBoundarySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var path = options.ArchiveFile;

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Sources:CodAb:ArchiveFile is not set, so there is no boundary archive to read.");
        }

        var trimmed = path.Trim();
        var result = reader.ReadGeometry(trimmed, cancellationToken);

        var features = new List<CanonicalBoundaryFeature>(result.Features.Count);
        var rejected = new List<string>(result.Rejected);

        foreach (var feature in result.Features)
        {
            var canonical = CodAbCatalogueSource.ToCanonicalCode(feature.Pcode);

            if (canonical is null)
            {
                // A pcode this platform cannot normalise is reported rather than guessed at. The alternative
                // would be attaching an outline on the strength of a code nobody could explain.
                rejected.Add(
                    $"'{feature.Name}': pcode '{feature.Pcode}' is not the expected PH-plus-seven-digit form");

                continue;
            }

            features.Add(new CanonicalBoundaryFeature
            {
                CanonicalCode = canonical,
                PublishedCode = feature.Pcode,
                Name = feature.Name,
                Geometry = feature.Geometry,
                AreaSquareKm = feature.AreaSquareKm,
                WasRepaired = feature.WasRepaired,
                RepairNote = feature.WasRepaired
                    ? "invalid outline repaired by zero-width buffer"
                    : null,
            });
        }

        return Task.FromResult(new CanonicalBoundarySnapshot
        {
            Features = features,
            Rejected = rejected,
            ExtractedAt = timeProvider.GetUtcNow(),

            // Declared by the operator, never inferred: a shapefile archive looks the same whoever built it.
            Provenance = options.IsOchaPublication
                ? BoundaryProvenance.OchaCodAb
                : BoundaryProvenance.LocalFile,
            Label = string.IsNullOrWhiteSpace(options.Label)
                ? $"COD-AB archive {result.OriginalFileName}"
                : options.Label.Trim(),
            AccessRoute = trimmed,
            OriginalFileName = result.OriginalFileName,
            FileSha256 = result.FileSha256,
            FileSizeBytes = result.FileSizeBytes,
            Vintage = DateOnly.TryParse(options.Vintage, CultureInfo.InvariantCulture, out var vintage)
                ? vintage
                : null,
            AcquisitionNote = options.AcquisitionNote,
        });
    }
}
