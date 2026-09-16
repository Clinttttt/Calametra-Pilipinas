using System.Security.Cryptography;

using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Geospatial;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;

namespace Calametra.Infrastructure.Sources.OpenStreetMap;

/// <summary>
/// Reads administrative boundaries from a dated <c>.osm.pbf</c> regional extract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a file at all.</b> The Overpass route is closed for this dataset: measured on 2026-09-15 the
/// public gateway returned 504 at about 31 seconds for every formulation tried — <c>out geom</c>,
/// <c>out geom qt</c>, and the recurse-and-skeleton form — and Metro Manila alone exceeds that budget. A
/// national read cannot fit inside a ceiling it hits on one metropolitan area. A dated extract is also
/// better provenance: one file, one digest, one vintage, rather than fifty-four requests answered by
/// whichever mirror was free.
/// </para>
/// <para>
/// <b>Three passes, not one.</b> The obvious approach is to ask the library for complete relations with
/// members resolved, which requires holding every node in the file — hundreds of millions of coordinates
/// for a country extract, and gigabytes of memory for a job that needs a few thousand outlines. Instead the
/// file is read three times, each pass narrowing what the next has to keep:
/// </para>
/// <list type="number">
/// <item>relations: keep the administrative ones at the wanted level, and note which ways they need;</item>
/// <item>ways: keep only those ways, and note which nodes they need;</item>
/// <item>nodes: keep only those coordinates.</item>
/// </list>
/// <para>
/// Reading 578 MB three times costs a few minutes of sequential I/O and bounds memory to the boundary
/// network alone. The alternative trades a resource the machine has for one it may not.
/// </para>
/// <para>
/// <b>The digest is taken first, over the original bytes.</b> Hashing after parsing would describe
/// something the upstream never published — the same trap the PSGC import avoided by reading the PSA
/// workbook rather than a CSV exported from it.
/// </para>
/// </remarks>
public sealed class OsmPbfBoundaryReader(ILogger<OsmPbfBoundaryReader> logger)
{
    /// <summary>WGS 84. Geometry is stored as geography, so nothing is projected here.</summary>
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    /// <summary>
    /// Counts administrative relations by level, and how many carry a usable code.
    /// </summary>
    /// <remarks>
    /// A diagnostic, added because the alternative was asserting something significant without evidence. The
    /// extract yielded 891 relations at level 6 against 1,642 cities and municipalities in the register, and
    /// "OSM has not mapped the rest" and "this reader is looking at the wrong tier" are very different
    /// conclusions from the same number. One pass settles which.
    /// </remarks>
    public static IReadOnlyList<LevelSurvey> Survey(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The OSM extract was not found at '{path}'.", path);
        }

        var counts = new Dictionary<string, (int Total, int WithRef, int WithTenDigitRef)>(
            StringComparer.Ordinal);

        using var stream = OpenRead(path);

        foreach (var element in new PBFOsmStreamSource(stream))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is not Relation relation
                || !string.Equals(Tag(relation, "boundary"), "administrative", StringComparison.Ordinal))
            {
                continue;
            }

            var level = Tag(relation, "admin_level") ?? "(none)";
            var reference = Tag(relation, "ref");

            counts.TryGetValue(level, out var current);

            counts[level] = (
                current.Total + 1,
                current.WithRef + (string.IsNullOrWhiteSpace(reference) ? 0 : 1),
                current.WithTenDigitRef + (reference?.Trim().Length == 10 ? 1 : 0));
        }

        return [.. counts
            .Select(pair => new LevelSurvey(pair.Key, pair.Value.Total, pair.Value.WithRef, pair.Value.WithTenDigitRef))
            .OrderBy(item => item.AdminLevel, StringComparer.Ordinal)];
    }

    public ExtractReadResult Read(string path, int adminLevel, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The OSM extract was not found at '{path}'. "
                + "Sources:OpenStreetMap:BoundaryExtractFile must point at the .osm.pbf as downloaded.",
                path);
        }

        var info = new FileInfo(path);

        PbfLog.Hashing(logger, info.Name, info.Length / (1024 * 1024));

        var hash = Sha256Of(path);

        // Pass 1: which relations matter, and which ways they are built from.
        var relations = new List<BoundaryRelation>();
        var wantedWays = new HashSet<long>();
        DateTimeOffset? newestTimestamp = null;

        using (var stream = OpenRead(path))
        {
            foreach (var element in new PBFOsmStreamSource(stream))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (element is not Relation relation || relation.Id is null)
                {
                    continue;
                }

                if (!IsWantedBoundary(relation, adminLevel))
                {
                    continue;
                }

                var members = new List<(long WayId, string Role)>();

                foreach (var member in relation.Members ?? [])
                {
                    if (member.Type != OsmGeoType.Way)
                    {
                        continue;
                    }

                    members.Add((member.Id, member.Role ?? string.Empty));
                    wantedWays.Add(member.Id);
                }

                if (relation.TimeStamp is { } stamp && (newestTimestamp is null || stamp > newestTimestamp))
                {
                    newestTimestamp = stamp;
                }

                relations.Add(new BoundaryRelation(
                    relation.Id.Value,
                    Tag(relation, "ref"),
                    Tag(relation, "name"),
                    adminLevel,
                    members));
            }
        }

        PbfLog.RelationsFound(logger, relations.Count, wantedWays.Count);

        // Pass 2: the node sequence of each needed way.
        var wayNodes = new Dictionary<long, long[]>(wantedWays.Count);
        var wantedNodes = new HashSet<long>();

        using (var stream = OpenRead(path))
        {
            foreach (var element in new PBFOsmStreamSource(stream))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (element is not Way way || way.Id is null || !wantedWays.Contains(way.Id.Value))
                {
                    continue;
                }

                var nodes = way.Nodes ?? [];
                wayNodes[way.Id.Value] = nodes;

                foreach (var node in nodes)
                {
                    wantedNodes.Add(node);
                }
            }
        }

        PbfLog.WaysResolved(logger, wayNodes.Count, wantedNodes.Count);

        // Pass 3: the coordinates.
        var coordinates = new Dictionary<long, Coordinate>(wantedNodes.Count);

        using (var stream = OpenRead(path))
        {
            foreach (var element in new PBFOsmStreamSource(stream))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (element is not Node node
                    || node.Id is null
                    || node.Latitude is null
                    || node.Longitude is null
                    || !wantedNodes.Contains(node.Id.Value))
                {
                    continue;
                }

                coordinates[node.Id.Value] = new Coordinate(node.Longitude.Value, node.Latitude.Value);
            }
        }

        PbfLog.NodesResolved(logger, coordinates.Count);

        var features = new List<BoundaryFeature>(relations.Count);
        var unassembled = new List<UnassembledRelation>();

        foreach (var relation in relations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var members = new List<(string Role, IReadOnlyList<Coordinate> Coordinates)>();
            var missingGeometry = false;

            foreach (var (wayId, role) in relation.Members)
            {
                if (!wayNodes.TryGetValue(wayId, out var nodes))
                {
                    // A member way outside the extract's cut line. Recorded rather than ignored: a boundary
                    // missing a fragment cannot close, and the reason is worth stating.
                    missingGeometry = true;

                    continue;
                }

                var line = new List<Coordinate>(nodes.Length);

                foreach (var nodeId in nodes)
                {
                    if (coordinates.TryGetValue(nodeId, out var coordinate))
                    {
                        line.Add(coordinate);
                    }
                    else
                    {
                        missingGeometry = true;
                    }
                }

                if (line.Count >= 2)
                {
                    members.Add((role, line));
                }
            }

            if (members.Count == 0)
            {
                unassembled.Add(new UnassembledRelation(
                    relation.OsmRelationId,
                    relation.RefTag,
                    relation.Name,
                    "no member way geometry was present in the extract"));

                continue;
            }

            var assembled = OsmRingAssembler.Assemble(members, Factory);

            if (!assembled.IsSuccess)
            {
                unassembled.Add(new UnassembledRelation(
                    relation.OsmRelationId,
                    relation.RefTag,
                    relation.Name,
                    missingGeometry
                        ? $"{assembled.Failure} (some member ways lie outside the extract)"
                        : assembled.Failure!));

                continue;
            }

            var geometry = assembled.Geometry!;
            var note = assembled.Note;
            var wasRepaired = false;

            if (!geometry.IsValid)
            {
                var repaired = Repair(geometry);

                if (repaired is null)
                {
                    unassembled.Add(new UnassembledRelation(
                        relation.OsmRelationId,
                        relation.RefTag,
                        relation.Name,
                        "the assembled outline is not valid and could not be repaired"));

                    continue;
                }

                geometry = repaired;
                wasRepaired = true;
                note = note is null
                    ? "self-intersecting outline repaired by zero-width buffer"
                    : $"{note}; self-intersecting outline repaired by zero-width buffer";
            }

            features.Add(new BoundaryFeature
            {
                OsmRelationId = relation.OsmRelationId,
                RefTag = relation.RefTag,
                Name = relation.Name,
                AdminLevel = relation.AdminLevel,
                Geometry = geometry,
                AreaSquareKm = GeodeticArea.SquareKilometres(geometry),
                WasRepaired = wasRepaired,
                RepairNote = note,
            });
        }

        PbfLog.Assembled(logger, features.Count, unassembled.Count);

        return new ExtractReadResult(
            features,
            unassembled,
            info.Name,
            hash,
            info.Length,
            newestTimestamp);
    }

    /// <summary>
    /// Opens the file tolerating another process holding it.
    /// </summary>
    /// <remarks>
    /// <see cref="FileShare.ReadWrite"/> because an operator may well still have the download open, and
    /// failing an import for that reason would be a lock the job does not need.
    /// </remarks>
    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);

    private static string Sha256Of(string path)
    {
        using var stream = OpenRead(path);

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Whether a relation is an administrative boundary at the wanted level.
    /// </summary>
    /// <remarks>
    /// <c>boundary=administrative</c> and the level, and nothing else. In particular not the name and not
    /// the presence of a <c>ref</c>: a Philippine municipality mapped without its PSGC code still belongs
    /// in the read, and reporting it as unmatched is more useful than never seeing it.
    /// </remarks>
    private static bool IsWantedBoundary(Relation relation, int adminLevel) =>
        string.Equals(Tag(relation, "boundary"), "administrative", StringComparison.Ordinal)
        && string.Equals(
            Tag(relation, "admin_level"),
            adminLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    private static string? Tag(OsmGeo element, string key) =>
        element.Tags is not null && element.Tags.TryGetValue(key, out var value) ? value : null;

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

    private sealed record BoundaryRelation(
        long OsmRelationId,
        string? RefTag,
        string? Name,
        int AdminLevel,
        List<(long WayId, string Role)> Members);
}

/// <param name="NewestElementTimestamp">
/// The newest edit timestamp seen on a boundary relation, used as a floor for the extract's vintage when the
/// operator does not state one.
/// </param>
public sealed record ExtractReadResult(
    IReadOnlyList<BoundaryFeature> Features,
    IReadOnlyList<UnassembledRelation> Unassembled,
    string OriginalFileName,
    string FileSha256,
    long FileSizeBytes,
    DateTimeOffset? NewestElementTimestamp);

/// <param name="WithTenDigitRef">
/// Refs of exactly ten characters, which is the current PSA code and the form this platform can attach
/// directly.
/// </param>
public sealed record LevelSurvey(string AdminLevel, int Total, int WithRef, int WithTenDigitRef);

internal static partial class PbfLog
{
    [LoggerMessage(
        EventId = 7320,
        Level = LogLevel.Information,
        Message = "Hashing OSM extract {FileName} ({Megabytes} MB) before parsing")]
    public static partial void Hashing(ILogger logger, string fileName, long megabytes);

    [LoggerMessage(
        EventId = 7321,
        Level = LogLevel.Information,
        Message = "Pass 1: {Relations} administrative relation(s) at the wanted level, needing {Ways} way(s)")]
    public static partial void RelationsFound(ILogger logger, int relations, int ways);

    [LoggerMessage(
        EventId = 7322,
        Level = LogLevel.Information,
        Message = "Pass 2: {Ways} way(s) resolved, needing {Nodes} node(s)")]
    public static partial void WaysResolved(ILogger logger, int ways, int nodes);

    [LoggerMessage(
        EventId = 7323,
        Level = LogLevel.Information,
        Message = "Pass 3: {Nodes} coordinate(s) resolved")]
    public static partial void NodesResolved(ILogger logger, int nodes);

    [LoggerMessage(
        EventId = 7324,
        Level = LogLevel.Information,
        Message = "Assembled {Assembled} outline(s); {Unassembled} relation(s) could not be closed")]
    public static partial void Assembled(ILogger logger, int assembled, int unassembled);
}
