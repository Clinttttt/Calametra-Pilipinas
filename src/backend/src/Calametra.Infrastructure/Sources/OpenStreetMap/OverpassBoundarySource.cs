using System.Globalization;
using System.Text.Json;

using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Administrative;
using Calametra.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace Calametra.Infrastructure.Sources.OpenStreetMap;

/// <summary>
/// Reads administrative boundary polygons from the Overpass API, one chunk at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chunked because a national boundary query does not complete.</b> Measured against the public
/// instance on 2026-09-15: a tag-only query for one province's worth of level-6 relations returns in
/// under twenty seconds, while the same query asking for geometry over a wide area drops the connection.
/// The alternative to chunking is one request that times out and yields nothing at all, so the register's
/// own provinces drive the fetch and each chunk that fails is named in the result rather than reducing
/// coverage silently.
/// </para>
/// <para>
/// <b>Geometry is assembled here, not by the caller.</b> Overpass returns relation members as loose ways;
/// <see cref="OsmRingAssembler"/> stitches them. Repair is attempted once, with the result recorded, so a
/// self-intersecting outline becomes either a flagged polygon or a reported failure but never a silently
/// altered shape.
/// </para>
/// <para>
/// Requests are serialised with a pause between them. The public Overpass instance publishes a two-slot
/// limit and this platform is a guest on volunteer infrastructure: parallelising would be both rude and
/// counterproductive, since exceeding the limit returns errors rather than data.
/// </para>
/// </remarks>
internal sealed class OverpassBoundarySource(
    HttpClient httpClient,
    IOptions<OpenStreetMapOptions> options,
    TimeProvider timeProvider,
    ILogger<OverpassBoundarySource> logger) : ILguBoundarySource
{    private readonly OpenStreetMapOptions options = options.Value;

    /// <summary>
    /// WGS 84. The geometry is stored as geography, so no projection happens anywhere in this adapter.
    /// </summary>
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    /// <summary>Courtesy pause between chunks, on volunteer infrastructure with a published slot limit.</summary>
    private static readonly TimeSpan[] LoadRetryPauses =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(90),
    ];

    /// <summary>
    /// How many times a failing cell may be quartered.
    /// </summary>
    /// <remarks>
    /// Three, taking a two-degree cell down to a quarter degree — about 28 km, smaller than most Philippine
    /// municipalities. Bounded because subdivision is a response to volume, and a cell that still fails at
    /// that size is failing for some other reason; splitting further would turn one upstream problem into
    /// sixty-four requests and report the gap later rather than sooner.
    /// </remarks>
    private const int MaximumSubdivisionDepth = 3;

    public async Task<BoundarySnapshot> ReadAsync(
        int adminLevel,
        IReadOnlyList<BoundaryChunk> chunks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var features = new Dictionary<long, BoundaryFeature>();
        var unassembled = new List<UnassembledRelation>();
        var failedChunks = new List<string>();
        string? extractVersion = null;

        // A queue rather than a loop, because a cell that fails is replaced by its four quarters and those
        // have to be processed too.
        //
        // This is the correction to the first national run: 15 of 54 fixed cells failed with a gateway
        // timeout, and they were precisely the dense ones — Metro Manila, Central Luzon, Cebu, Leyte,
        // Bicol. A gap correlated with density is the worst shape a gap can have, because the map then
        // goes quiet exactly where most of the country lives. Subdividing puts small cells where the data
        // is thick and leaves ocean cells cheap, without anyone having to know in advance where the
        // density is.
        var pending = new Queue<PendingChunk>();

        foreach (var chunk in chunks)
        {
            pending.Enqueue(new PendingChunk(chunk, 0));
        }

        var completed = 0;

        while (pending.Count > 0)
        {
            var next = pending.Dequeue();
            var chunk = next.Chunk;

            completed++;

            BoundaryLog.ChunkStarting(logger, chunk.Label, completed, completed + pending.Count);

            var outcome = await TryFetchAsync(adminLevel, chunk, cancellationToken);

            if (outcome.Document is not null)
            {
                using (outcome.Document)
                {
                    extractVersion ??= ReadExtractVersion(outcome.Document);

                    var before = features.Count;

                    ReadRelations(outcome.Document, features, unassembled);

                    BoundaryLog.ChunkCompleted(logger, chunk.Label, features.Count - before);
                }
            }
            else if (outcome.WasTooLarge && next.Depth < MaximumSubdivisionDepth)
            {
                foreach (var quarter in Subdivide(chunk))
                {
                    pending.Enqueue(new PendingChunk(quarter, next.Depth + 1));
                }

                BoundaryLog.ChunkSubdivided(logger, chunk.Label, next.Depth + 1);
            }
            else
            {
                // Named rather than thrown. One unreachable cell must not discard the fifty that worked,
                // and a coverage report that cannot say which areas were never fetched is worse than one
                // that reports a gap.
                failedChunks.Add(chunk.Label);
                BoundaryLog.ChunkFailed(logger, chunk.Label);
            }

            if (pending.Count > 0)
            {
                await Task.Delay(options.BoundaryPause, timeProvider, cancellationToken);
            }
        }

        return new BoundarySnapshot
        {
            Features = [.. features.Values],
            ExtractedAt = timeProvider.GetUtcNow(),
            ExtractVersion = extractVersion,
            Unassembled = unassembled,
            FailedChunks = failedChunks,
            Provenance = BoundaryProvenance.OverpassApi,
            Label = $"Overpass read, admin_level {adminLevel}, {extractVersion ?? "vintage not stated"}",
            AccessRoute = options.BoundaryEndpoints[0],
        };
    }

    /// <summary>
    /// Fetches one cell. Returns the outcome, which distinguishes "too big" from "too busy".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two failures look identical and need opposite responses.</b> An oversized cell must be
    /// quartered; a loaded server must be waited for. Subdividing a cell that failed because the instance
    /// was busy makes it worse — it turns one request into four against the thing that is already
    /// struggling, which is how a national read becomes an outage.
    /// </para>
    /// <para>
    /// They are told apart by how long the failure took. A gateway refusal arrives in seconds, well inside
    /// the timeout; a cell genuinely too large to assemble consumes the whole budget. Measured on the
    /// public instance, load failures returned 504 in about thirty seconds against a three-minute timeout,
    /// including for cells over open sea that can contain almost nothing.
    /// </para>
    /// <para>
    /// Load failures are retried with escalating pauses and rotated across the configured mirrors. Only a
    /// failure that used its full budget subdivides.
    /// </para>
    /// </remarks>
    private async Task<FetchOutcome> TryFetchAsync(
        int adminLevel,
        BoundaryChunk chunk,
        CancellationToken cancellationToken)
    {
        var budget = options.BoundaryTimeout;
        var endpoints = options.BoundaryEndpoints;

        for (var attempt = 1; attempt <= LoadRetryPauses.Length + 1; attempt++)
        {
            var endpoint = endpoints[(attempt - 1) % endpoints.Length];
            var started = timeProvider.GetTimestamp();

            try
            {
                var document = await FetchChunkAsync(adminLevel, chunk, endpoint, cancellationToken);

                return FetchOutcome.Served(document);
            }
            catch (Exception exception) when (exception is HttpRequestException
                or HttpIOException
                or TaskCanceledException
                or JsonException)
            {
                var elapsed = timeProvider.GetElapsedTime(started);

                // Used most of its budget: the cell itself is the problem, and no amount of waiting or
                // mirror-hopping will make it smaller.
                if (elapsed >= budget * 0.8)
                {
                    BoundaryLog.ChunkTooLarge(logger, chunk.Label, (int)elapsed.TotalSeconds);

                    return FetchOutcome.TooLarge();
                }

                BoundaryLog.ChunkRefused(
                    logger,
                    chunk.Label,
                    attempt,
                    (int)elapsed.TotalSeconds,
                    exception.GetType().Name);

                if (attempt <= LoadRetryPauses.Length)
                {
                    await Task.Delay(LoadRetryPauses[attempt - 1], timeProvider, cancellationToken);
                }
            }
        }

        return FetchOutcome.Unavailable();
    }

    /// <summary>Splits a cell into quarters.</summary>
    private static IEnumerable<BoundaryChunk> Subdivide(BoundaryChunk chunk)
    {
        var midLatitude = (chunk.South + chunk.North) / 2d;
        var midLongitude = (chunk.West + chunk.East) / 2d;

        yield return Quarter(chunk.South, chunk.West, midLatitude, midLongitude);
        yield return Quarter(chunk.South, midLongitude, midLatitude, chunk.East);
        yield return Quarter(midLatitude, chunk.West, chunk.North, midLongitude);
        yield return Quarter(midLatitude, midLongitude, chunk.North, chunk.East);

        static BoundaryChunk Quarter(double south, double west, double north, double east) =>
            new(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"lat {south:0.###}-{north:0.###} lon {west:0.###}-{east:0.###}"),
                south,
                west,
                north,
                east);
    }

    private readonly record struct PendingChunk(BoundaryChunk Chunk, int Depth);

    private async Task<JsonDocument> FetchChunkAsync(
        int adminLevel,
        BoundaryChunk chunk,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"[out:json][timeout:{(int)options.BoundaryTimeout.TotalSeconds}];rel[\"boundary\"=\"administrative\"][\"admin_level\"=\"{adminLevel}\"]({chunk.South:F5},{chunk.West:F5},{chunk.North:F5},{chunk.East:F5});out geom;");

        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
        using var response = await httpClient.PostAsync(new Uri(endpoint), content, cancellationToken);

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await using (stream.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    /// <param name="WasTooLarge">
    /// True only when the request consumed its whole budget, which is the signature of a cell that needs
    /// splitting rather than a server that needs waiting for.
    /// </param>
    private readonly record struct FetchOutcome(JsonDocument? Document, bool WasTooLarge)
    {
        public static FetchOutcome Served(JsonDocument document) => new(document, false);

        public static FetchOutcome TooLarge() => new(null, true);

        public static FetchOutcome Unavailable() => new(null, false);
    }

    /// <summary>
    /// The vintage of the data Overpass served, which is not the same as when it was asked.
    /// </summary>
    private static string? ReadExtractVersion(JsonDocument document) =>
        document.RootElement.TryGetProperty("osm3s", out var osm3s)
            && osm3s.TryGetProperty("timestamp_osm_base", out var stamp)
                ? stamp.GetString()
                : null;

    private static void ReadRelations(
        JsonDocument document,
        Dictionary<long, BoundaryFeature> features,
        List<UnassembledRelation> unassembled)
    {
        if (!document.RootElement.TryGetProperty("elements", out var elements))
        {
            return;
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("type", out var type)
                || type.GetString() != "relation"
                || !element.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            var relationId = idElement.GetInt64();

            // Chunks overlap at their edges, so the same relation arrives more than once. First wins:
            // a boundary is not more true for having been returned twice.
            if (features.ContainsKey(relationId) || unassembled.Exists(item => item.OsmRelationId == relationId))
            {
                continue;
            }

            var (refTag, name, adminLevel) = ReadTags(element);
            var members = ReadMembers(element);

            if (members.Count == 0)
            {
                unassembled.Add(new UnassembledRelation(
                    relationId,
                    refTag,
                    name,
                    "the relation was returned without way geometry"));

                continue;
            }

            var assembled = OsmRingAssembler.Assemble(members, Factory);

            if (!assembled.IsSuccess)
            {
                unassembled.Add(new UnassembledRelation(relationId, refTag, name, assembled.Failure!));

                continue;
            }

            var geometry = assembled.Geometry!;
            var repairNote = assembled.Note;
            var wasRepaired = false;

            if (!geometry.IsValid)
            {
                var repaired = Repair(geometry);

                if (repaired is null)
                {
                    unassembled.Add(new UnassembledRelation(
                        relationId,
                        refTag,
                        name,
                        "the assembled outline is not valid and could not be repaired"));

                    continue;
                }

                geometry = repaired;
                wasRepaired = true;
                repairNote = repairNote is null
                    ? "self-intersecting outline repaired by zero-width buffer"
                    : $"{repairNote}; self-intersecting outline repaired by zero-width buffer";
            }

            features[relationId] = new BoundaryFeature
            {
                OsmRelationId = relationId,
                RefTag = refTag,
                Name = name,
                AdminLevel = adminLevel,
                Geometry = geometry,
                AreaSquareKm = GeodeticArea.SquareKilometres(geometry),
                WasRepaired = wasRepaired,
                RepairNote = repairNote,
            };
        }
    }

    /// <summary>
    /// Repairs a self-intersecting outline with a zero-width buffer, or gives up.
    /// </summary>
    /// <remarks>
    /// The standard trick, and it is a guess dressed as an operation: the buffer re-nodes the geometry and
    /// keeps the interior, which is almost always what the mapper meant. It is applied because the
    /// alternative is discarding a real municipality over a few metres of crossed line, and the fact that
    /// it happened is recorded on the row so the guess is visible rather than absorbed.
    /// </remarks>
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

    private static (string? RefTag, string? Name, int AdminLevel) ReadTags(JsonElement element)
    {
        if (!element.TryGetProperty("tags", out var tags))
        {
            return (null, null, 0);
        }

        var refTag = tags.TryGetProperty("ref", out var refElement) ? refElement.GetString() : null;
        var name = tags.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var level = 0;

        if (tags.TryGetProperty("admin_level", out var levelElement)
            && int.TryParse(levelElement.GetString(), CultureInfo.InvariantCulture, out var parsed))
        {
            level = parsed;
        }

        return (refTag, name, level);
    }

    private static List<(string Role, IReadOnlyList<Coordinate> Coordinates)> ReadMembers(
        JsonElement element)
    {
        var members = new List<(string, IReadOnlyList<Coordinate>)>();

        if (!element.TryGetProperty("members", out var memberArray))
        {
            return members;
        }

        foreach (var member in memberArray.EnumerateArray())
        {
            if (!member.TryGetProperty("type", out var type) || type.GetString() != "way")
            {
                continue;
            }

            if (!member.TryGetProperty("geometry", out var geometry))
            {
                continue;
            }

            var role = member.TryGetProperty("role", out var roleElement)
                ? roleElement.GetString() ?? string.Empty
                : string.Empty;

            var coordinates = new List<Coordinate>();

            foreach (var point in geometry.EnumerateArray())
            {
                if (point.TryGetProperty("lat", out var lat) && point.TryGetProperty("lon", out var lon))
                {
                    coordinates.Add(new Coordinate(lon.GetDouble(), lat.GetDouble()));
                }
            }

            if (coordinates.Count >= 2)
            {
                members.Add((role, coordinates));
            }
        }

        return members;
    }
}

internal static partial class BoundaryLog
{
    [LoggerMessage(
        EventId = 7300,
        Level = LogLevel.Information,
        Message = "Boundary chunk {Label} ({Index} of {Total}) starting")]
    public static partial void ChunkStarting(ILogger logger, string label, int index, int total);

    [LoggerMessage(
        EventId = 7301,
        Level = LogLevel.Information,
        Message = "Boundary chunk {Label} yielded {NewRelations} new relation(s)")]
    public static partial void ChunkCompleted(ILogger logger, string label, int newRelations);

    [LoggerMessage(
        EventId = 7302,
        Level = LogLevel.Warning,
        Message = "Boundary chunk {Label} refused after {Seconds}s on attempt {Attempt} ({ExceptionType}). "
            + "Too quick to be a size problem, so treated as load: waiting and trying another mirror.")]
    public static partial void ChunkRefused(
        ILogger logger,
        string label,
        int attempt,
        int seconds,
        string exceptionType);

    [LoggerMessage(
        EventId = 7305,
        Level = LogLevel.Information,
        Message = "Boundary chunk {Label} used its whole {Seconds}s budget, so the cell is too large "
            + "rather than the server too busy. Splitting it.")]
    public static partial void ChunkTooLarge(ILogger logger, string label, int seconds);

    [LoggerMessage(
        EventId = 7304,
        Level = LogLevel.Information,
        Message = "Boundary chunk {Label} was too large to fetch; split into quarters at depth {Depth}")]
    public static partial void ChunkSubdivided(ILogger logger, string label, int depth);

    [LoggerMessage(
        EventId = 7303,
        Level = LogLevel.Error,
        Message = "Boundary chunk {Label} could not be fetched. Reported as a coverage gap rather than "
            + "reducing coverage silently; re-run to fill it.")]
    public static partial void ChunkFailed(ILogger logger, string label);
}
