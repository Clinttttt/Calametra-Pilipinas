using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Calametra.Application.Abstractions.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure.Sources.OpenStreetMap;

/// <summary>
/// Reads mapped Philippine town centres from OpenStreetMap through the Overpass API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why place nodes rather than administrative boundaries.</b> Boundary relations would give a
/// polygon and therefore a real centroid, and a boundary is the thing this platform actually lacks.
/// They were tried first and rejected on coverage: probed on 2026-09-12, a bounding box over
/// Surigao del Sur returned three administrative relations for a province of nineteen
/// municipalities, and Philippine boundaries use <c>admin_level=4</c> for provinces and <c>6</c> for
/// municipalities rather than the 6/8 convention assumed elsewhere. The same box returned seven
/// <c>place</c> nodes, one per municipality including Carmen. Nationally the node query returns
/// 1,695 — 155 cities and 1,540 towns — against 1,647 cities and municipalities in this archive.
/// </para>
/// <para>
/// A place node is also the more honest answer to the question being asked. It marks the
/// <i>poblacion</i>, the built-up centre where the municipal hall stands, which is what a reader
/// means by "how far was this earthquake from Carmen". The centroid of a municipality that wraps a
/// mountain range is a point nobody lives at.
/// </para>
/// <para>
/// <b>The response is not trusted to be well-formed.</b> Overpass returns <c>lat</c>/<c>lon</c> on a
/// node only for <c>out body</c>, not <c>out tags</c> — asked the wrong way it answers 200 with
/// every coordinate absent, which is how a silently empty import happens. Nodes missing a name or a
/// coordinate are dropped and counted rather than defaulted.
/// </para>
/// <para>
/// <b>No resilience handler, deliberately.</b> The same reasoning as the GEM and IBTrACS adapters:
/// the standard handler's per-attempt timeout would abort a legitimately slow national query, and
/// retrying a heavy Overpass request against a shared public instance is exactly what its usage
/// policy asks callers not to do.
/// </para>
/// </remarks>
internal sealed class OverpassSettlementSource(
    HttpClient httpClient,
    IOptions<OpenStreetMapOptions> options,
    ILogger<OverpassSettlementSource> logger) : ISettlementCoordinateSource
{
    /// <summary>
    /// The query, as sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>place=municipality</c> is included although the Philippine data uses <c>town</c> and
    /// <c>city</c>: it costs nothing and the tag is used in some regions for exactly this level.
    /// Villages and hamlets are excluded — those are barangays, which this directory does not hold,
    /// and including them would put thousands of candidate names in front of the matcher.
    /// </para>
    /// <para>
    /// The bounding box is the archipelago plus a margin, rather than an <c>area</c> lookup on the
    /// country relation. An area query has to resolve and buffer the national boundary first, which
    /// on a shared instance is the slower and more failure-prone half of the request.
    /// </para>
    /// </remarks>
    private const string OverpassQuery =
        "[out:json][timeout:300];"
        + "node[\"place\"~\"^(city|town|municipality)$\"](4.0,116.0,21.5,127.2);"
        + "out body;";

    public string SourceSlug => OpenStreetMapOptions.Slug;

    public double MatchRadiusKm => options.Value.MatchRadiusKm;

    public async Task<IReadOnlyList<MappedSettlement>> FetchAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", OverpassQuery)]);

        using var response = await httpClient.PostAsync((Uri?)null, content, cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken);

        var elements = payload?.Elements ?? [];
        var settlements = new List<MappedSettlement>(elements.Count);
        var incomplete = 0;

        foreach (var element in elements)
        {
            var name = element.Tags?.Name;

            if (string.IsNullOrWhiteSpace(name) || element.Latitude is null || element.Longitude is null)
            {
                incomplete++;
                continue;
            }

            settlements.Add(new MappedSettlement
            {
                Key = element.Id.ToString(CultureInfo.InvariantCulture),
                Name = name.Trim(),
                IsCity = string.Equals(element.Tags?.Place, "city", StringComparison.OrdinalIgnoreCase),
                Latitude = element.Latitude.Value,
                Longitude = element.Longitude.Value,
            });
        }

        OverpassLog.Fetched(logger, settlements.Count, incomplete);

        return settlements;
    }

    private sealed record OverpassResponse
    {
        [JsonPropertyName("elements")]
        public IReadOnlyList<OverpassElement> Elements { get; init; } = [];
    }

    private sealed record OverpassElement
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("lat")]
        public double? Latitude { get; init; }

        [JsonPropertyName("lon")]
        public double? Longitude { get; init; }

        [JsonPropertyName("tags")]
        public OverpassTags? Tags { get; init; }
    }

    private sealed record OverpassTags
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("place")]
        public string? Place { get; init; }
    }
}

internal static partial class OverpassLog
{
    [LoggerMessage(
        EventId = 6300,
        Level = LogLevel.Information,
        Message = "Read {SettlementCount} mapped town centres from OpenStreetMap ({IncompleteCount} skipped as incomplete)")]
    public static partial void Fetched(ILogger logger, int settlementCount, int incompleteCount);
}
