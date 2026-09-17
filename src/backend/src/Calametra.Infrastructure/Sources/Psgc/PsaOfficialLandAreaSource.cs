using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calametra.Application.Abstractions.Sources;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.Psgc;

/// <summary>Reads the PSA 2024 POPCEN land-area matrix without deriving identity from names.</summary>
internal sealed class PsaOfficialLandAreaSource(
    HttpClient httpClient,
    IOptions<PsaOfficialLandAreaOptions> options,
    TimeProvider timeProvider) : IOfficialLandAreaSource
{
    private readonly PsaOfficialLandAreaOptions options = options.Value;

    public string SourceSlug => PsaOfficialLandAreaOptions.Slug;

    public async Task<OfficialLandAreaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(options.ApiUrl, UriKind.Absolute);

        using var metadataResponse = await httpClient.GetAsync(uri, cancellationToken);
        metadataResponse.EnsureSuccessStatusCode();
        var metadataBytes = await metadataResponse.Content.ReadAsByteArrayAsync(cancellationToken);

        var query = new
        {
            query = new object[]
            {
                new
                {
                    code = "Geographic Location",
                    selection = new { filter = "all", values = new[] { "*" } },
                },
                new
                {
                    code = "Parameter",
                    selection = new { filter = "item", values = new[] { "3" } },
                },
            },
            response = new { format = "json-stat" },
        };

        using var payloadResponse = await httpClient.PostAsJsonAsync(uri, query, cancellationToken);
        payloadResponse.EnsureSuccessStatusCode();
        var payloadBytes = await payloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);

        var metadataJson = Encoding.UTF8.GetString(metadataBytes);
        var payloadJson = Encoding.UTF8.GetString(payloadBytes);

        using var document = JsonDocument.Parse(payloadBytes);
        var dataset = document.RootElement.GetProperty("dataset");
        var geographic = dataset
            .GetProperty("dimension")
            .GetProperty("Geographic Location")
            .GetProperty("category");
        var indexes = geographic.GetProperty("index");
        var labels = geographic.GetProperty("label");
        var values = dataset.GetProperty("value");

        var rows = new List<OfficialLandAreaSourceRow>(indexes.GetPropertyCount());

        foreach (var code in indexes.EnumerateObject())
        {
            var index = code.Value.GetInt32();

            if (index < 0 || index >= values.GetArrayLength())
            {
                throw new InvalidOperationException(
                    $"PSA matrix {options.MatrixId} mapped {code.Name} to missing value index {index}.");
            }

            var value = values[index];

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var area))
            {
                throw new InvalidOperationException(
                    $"PSA matrix {options.MatrixId} supplied no decimal land area for {code.Name}.");
            }

            rows.Add(new OfficialLandAreaSourceRow(
                code.Name,
                labels.GetProperty(code.Name).GetString()?.TrimStart('.') ?? code.Name,
                area));
        }

        var updated = dataset.TryGetProperty("updated", out var updatedElement)
            && DateTimeOffset.TryParse(
                updatedElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedUpdated)
                ? parsedUpdated
                : (DateTimeOffset?)null;

        var source = dataset.GetProperty("source").GetString()?.Replace('#', ';')
            ?? "Philippine Statistics Authority 2024 Census of Population; "
                + "DENR-LMB 2019 Masterlist of Land Areas; SGA land area from MENRE-BARMM";

        return new OfficialLandAreaSnapshot
        {
            Label = $"PSA 2024 POPCEN official land area / {options.ReferenceYear} masterlist",
            MatrixId = options.MatrixId,
            AccessRoute = options.ApiUrl,
            ReferenceYear = options.ReferenceYear,
            RetrievedAt = timeProvider.GetUtcNow(),
            SourceUpdatedAt = updated,
            Attribution = source,
            MetadataJson = metadataJson,
            PayloadJson = payloadJson,
            MetadataSha256 = Hash(metadataBytes),
            PayloadSha256 = Hash(payloadBytes),
            Rows = rows,
        };
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
