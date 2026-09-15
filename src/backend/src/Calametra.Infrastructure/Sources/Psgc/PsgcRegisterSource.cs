using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Administrative;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.Psgc;

/// <summary>
/// Reads the PSGC register, preferring the PSA's own API and falling back to a mirror.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fallback records itself.</b> When the PSA route does not answer, the snapshot is returned with
/// <see cref="RegisterProvenance.Mirror"/> and a note stating what was attempted and how it failed. The
/// domain then refuses to let any crosswalk row cite that edition, so the platform can load a mirror,
/// match against it and report on it without ever certifying a pairing from it. That is ADR-004's pattern
/// applied to a register instead of to fault geometry: use what is available, register what it is, and
/// never let the substitute be mistaken for the authority.
/// </para>
/// <para>
/// <b>Both editions of the code come from the register itself.</b> Every tier the mirror serves carries
/// <c>code</c> (nine digits) beside <c>psgc10DigitCode</c> (ten), which is what makes
/// <c>RegisterMatch</c> evidence possible at all — the alternative would be re-slicing digits for all
/// 1,634 units and hoping the capital behaved like the provinces.
/// </para>
/// <para>
/// Parents are resolved from nine-digit to canonical ten-digit inside this adapter, because the register
/// publishes hierarchy in the old edition's codes while the identity is the new edition's. Resolving it
/// here keeps that reconciliation in one place instead of leaking a mixed-edition key into the domain.
/// </para>
/// </remarks>
internal sealed class PsgcRegisterSource(
    HttpClient httpClient,
    IOptions<PsgcRegisterOptions> options,
    TimeProvider timeProvider,
    ILogger<PsgcRegisterSource> logger) : IPsgcRegisterSource
{
    private readonly PsgcRegisterOptions options = options.Value;

    public string SourceSlug => PsgcRegisterOptions.Slug;

    public async Task<RegisterSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.LocalFileDirectory))
        {
            return await ReadLocalAsync(options.LocalFileDirectory, cancellationToken);
        }

        var psaFailure = await ProbePsaAsync(cancellationToken);

        if (psaFailure is null)
        {
            // Reached only if the PSA route starts answering. Left as an explicit branch rather than
            // removed, so adopting it is a configuration change rather than a rewrite.
            return await ReadFromApiAsync(
                options.PsaApiBaseUrl,
                RegisterProvenance.PsaDirect,
                $"Read directly from the PSA classification API at {options.PsaApiBaseUrl}.",
                cancellationToken);
        }

        RegisterLog.PsaUnavailable(logger, options.PsaApiBaseUrl, psaFailure);

        return await ReadFromApiAsync(
            options.MirrorBaseUrl,
            RegisterProvenance.Mirror,
            $"PSA route {options.PsaApiBaseUrl} was attempted first and did not serve the register: "
            + $"{psaFailure}. Loaded instead from the mirror at {options.MirrorBaseUrl}. This edition "
            + "cannot certify a crosswalk — see ADR-005 gate 1.",
            cancellationToken);
    }

    /// <summary>
    /// Attempts the PSA route, returning null on success or a description of the failure.
    /// </summary>
    /// <remarks>
    /// A probe rather than an exception-driven fallback, so the reason lands in the edition's notes and in
    /// the readiness report instead of only in a log line an operator has to go looking for.
    /// </remarks>
    private async Task<string?> ProbePsaAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                new Uri($"{options.PsaApiBaseUrl.TrimEnd('/')}/regions", UriKind.Absolute),
                cancellationToken);

            return response.IsSuccessStatusCode
                ? null
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException exception)
        {
            return exception.Message;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "the request timed out";
        }
    }

    private async Task<RegisterSnapshot> ReadFromApiAsync(
        string baseUrl,
        RegisterProvenance provenance,
        string notes,
        CancellationToken cancellationToken)
    {
        var trimmed = baseUrl.TrimEnd('/');

        var (regions, regionsModified) = await GetAsync($"{trimmed}/regions.json", cancellationToken);
        var (provinces, _) = await GetAsync($"{trimmed}/provinces.json", cancellationToken);
        var (units, unitsModified) = await GetAsync($"{trimmed}/cities-municipalities.json", cancellationToken);

        var snapshotUnits = Assemble(regions, provinces, units);

        // The newest stamp any tier reported. A mirror serving a 2022 file will say so here, which is the
        // signal that established the mirror is a pre-Negros-Island-Region snapshot.
        var lastModified = new[] { regionsModified, unitsModified }
            .Where(stamp => stamp is not null)
            .Select(stamp => stamp!.Value)
            .DefaultIfEmpty()
            .Max();

        return new RegisterSnapshot
        {
            Label = BuildLabel(provenance, lastModified == default ? null : lastModified),
            Provenance = provenance,
            AccessRoute = trimmed,
            UpstreamLastModified = lastModified == default ? null : lastModified,
            Notes = notes,
            Units = snapshotUnits,
        };
    }

    private async Task<RegisterSnapshot> ReadLocalAsync(string directory, CancellationToken cancellationToken)
    {
        var regions = await ReadFileAsync(Path.Combine(directory, "regions.json"), cancellationToken);
        var provinces = await ReadFileAsync(Path.Combine(directory, "provinces.json"), cancellationToken);
        var units = await ReadFileAsync(
            Path.Combine(directory, "cities-municipalities.json"),
            cancellationToken);

        var provenance = options.LocalFileIsPsaPublication
            ? RegisterProvenance.PsaDirect
            : RegisterProvenance.LocalFile;

        return new RegisterSnapshot
        {
            Label = options.LocalFileLabel ?? $"PSGC register from {directory}",
            Provenance = provenance,
            AccessRoute = directory,
            UpstreamLastModified = File.GetLastWriteTimeUtc(Path.Combine(directory, "regions.json")),
            Notes = options.LocalFileIsPsaPublication
                ? "Loaded from a local file the operator declares to be a PSA publication."
                : "Loaded from a local file whose provenance the operator did not declare as PSA. It "
                    + "cannot certify a crosswalk.",
            Units = Assemble(regions, provinces, units),
        };
    }

    /// <summary>
    /// Turns three tier files into one flat unit list with canonical parents resolved.
    /// </summary>
    /// <remarks>
    /// The register publishes hierarchy in nine-digit codes while identity is the ten-digit code, so a
    /// lookup is built across every tier first and parents are translated through it. A parent that
    /// cannot be resolved is left null rather than guessed: an unresolvable parent is a fact about the
    /// register, and <c>Lgu</c> holds the parent as a code precisely so it can survive one.
    /// </remarks>
    private static List<RegisterUnit> Assemble(
        IReadOnlyList<PsgcApiEntry> regions,
        IReadOnlyList<PsgcApiEntry> provinces,
        IReadOnlyList<PsgcApiEntry> citiesAndMunicipalities)
    {
        var canonicalByHistorical = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in regions.Concat(provinces).Concat(citiesAndMunicipalities))
        {
            if (!string.IsNullOrWhiteSpace(entry.Code) && !string.IsNullOrWhiteSpace(entry.Psgc10DigitCode))
            {
                canonicalByHistorical[entry.Code] = entry.Psgc10DigitCode;
            }
        }

        string? Canonical(string? historical) =>
            historical is not null && canonicalByHistorical.TryGetValue(historical, out var code)
                ? code
                : null;

        var assembled = new List<RegisterUnit>(regions.Count + provinces.Count + citiesAndMunicipalities.Count);

        foreach (var entry in regions)
        {
            assembled.Add(Build(entry, LguLevel.Region, parentCanonicalCode: null));
        }

        foreach (var entry in provinces)
        {
            assembled.Add(Build(entry, LguLevel.Province, Canonical(entry.RegionCode)));
        }

        foreach (var entry in citiesAndMunicipalities)
        {
            // The register's own classification, read rather than inferred from the name. The directory's
            // city/municipality split is inferred from the name and is wrong for about ten cities, which
            // is exactly the disagreement a reviewer needs to see rather than have smoothed over.
            var level = entry.IsCity ? LguLevel.City : LguLevel.Municipality;

            assembled.Add(Build(entry, level, Canonical(entry.ProvinceCode) ?? Canonical(entry.RegionCode)));
        }

        return assembled;
    }

    private static RegisterUnit Build(PsgcApiEntry entry, LguLevel level, string? parentCanonicalCode) =>
        new()
        {
            CanonicalCode = entry.Psgc10DigitCode ?? string.Empty,
            Name = entry.Name ?? string.Empty,
            Level = level,
            ParentCanonicalCode = parentCanonicalCode,
            StatedHistoricalCode = string.IsNullOrWhiteSpace(entry.Code) ? null : entry.Code,
        };

    private async Task<(IReadOnlyList<PsgcApiEntry> Entries, DateTimeOffset? LastModified)> GetAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(url, UriKind.Absolute), cancellationToken);

        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<PsgcApiEntry>>(cancellationToken)
            ?? [];

        return (entries, response.Content.Headers.LastModified);
    }

    private static async Task<IReadOnlyList<PsgcApiEntry>> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        return await System.Text.Json.JsonSerializer.DeserializeAsync<List<PsgcApiEntry>>(
            stream,
            cancellationToken: cancellationToken) ?? [];
    }

    /// <summary>
    /// Labels the edition by what can actually be established about it.
    /// </summary>
    /// <remarks>
    /// The upstream stamp is in the label rather than only in a column, because the label is what an
    /// operator reads in the readiness report — and "snapshot 2022-08-27" is the fact that tells them
    /// immediately why gate 1 is unmet.
    /// </remarks>
    private string BuildLabel(RegisterProvenance provenance, DateTimeOffset? lastModified)
    {
        var stamp = lastModified?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return provenance == RegisterProvenance.PsaDirect
            ? $"PSGC register, PSA classification API, read {stamp}"
            : $"PSGC register, third-party mirror, upstream snapshot {stamp}";
    }

    /// <summary>One entry as the PSGC JSON serves it, across all three tiers.</summary>
    private sealed record PsgcApiEntry
    {
        /// <summary>The nine-digit code. The register's own historical pairing.</summary>
        [JsonPropertyName("code")]
        [JsonConverter(typeof(InapplicableAsNullConverter))]
        public string? Code { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("psgc10DigitCode")]
        [JsonConverter(typeof(InapplicableAsNullConverter))]
        public string? Psgc10DigitCode { get; init; }

        [JsonPropertyName("regionCode")]
        [JsonConverter(typeof(InapplicableAsNullConverter))]
        public string? RegionCode { get; init; }

        [JsonPropertyName("provinceCode")]
        [JsonConverter(typeof(InapplicableAsNullConverter))]
        public string? ProvinceCode { get; init; }

        [JsonPropertyName("isCity")]
        public bool IsCity { get; init; }
    }

    /// <summary>
    /// Reads a code field that the register serves as <c>false</c> when it does not apply.
    /// </summary>
    /// <remarks>
    /// Not a defensive flourish — the register genuinely does this. Metro Manila's cities belong to no
    /// province, and rather than omitting <c>provinceCode</c> or sending null, the JSON sends the boolean
    /// <c>false</c>; entry 1,156 of the cities-and-municipalities document is where it first bites. A
    /// plain <c>string?</c> throws on it and takes the whole import down, so the inapplicable case is
    /// mapped to null, which is what it means.
    /// </remarks>
    private sealed class InapplicableAsNullConverter : System.Text.Json.Serialization.JsonConverter<string?>
    {
        public override string? Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                System.Text.Json.JsonTokenType.String => reader.GetString(),
                System.Text.Json.JsonTokenType.True or System.Text.Json.JsonTokenType.False => null,
                System.Text.Json.JsonTokenType.Null => null,
                _ => null,
            };

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            string? value,
            System.Text.Json.JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }
    }
}

internal static partial class RegisterLog
{
    [LoggerMessage(
        EventId = 7130,
        Level = LogLevel.Warning,
        Message = "The PSA register route {PsaUrl} did not serve the register ({Failure}). Falling back "
            + "to the mirror; the resulting edition cannot certify a crosswalk.")]
    public static partial void PsaUnavailable(ILogger logger, string psaUrl, string failure);
}
