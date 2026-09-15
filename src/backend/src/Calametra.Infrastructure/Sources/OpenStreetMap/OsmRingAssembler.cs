using NetTopologySuite.Geometries;

namespace Calametra.Infrastructure.Sources.OpenStreetMap;

/// <summary>
/// Builds polygons from the loose ways an OSM relation is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>An OSM boundary relation is not a polygon.</b> It is an unordered bag of way members, each a
/// fragment of the outline, in arbitrary direction, frequently shared with the neighbouring unit and
/// often split into dozens of pieces by unrelated edits. Nothing guarantees the fragments arrive in
/// order, wind consistently, or even that a ring closes. Turning that into geometry a containment test
/// can be trusted against is the actual work of a boundary import, and it is where such imports usually
/// go quietly wrong.
/// </para>
/// <para>
/// The approach is the standard one and is deliberately explicit about failure: stitch fragments end to
/// end into closed rings, reversing a fragment when its endpoint rather than its start point matches;
/// then use the relation's own <c>outer</c> and <c>inner</c> roles to decide which rings are shells and
/// which are holes, rather than inferring it from winding order — OSM winding is not reliable, whereas
/// the roles are what mappers actually maintain. Holes are assigned to the shell that contains them,
/// because a relation with two islands and a hole in one of them cannot be resolved any other way.
/// </para>
/// <para>
/// A ring that will not close is reported rather than closed by force. Joining the loose ends would
/// invent a boundary segment nobody mapped, and it would look exactly like a real one.
/// </para>
/// </remarks>
internal static class OsmRingAssembler
{
    /// <summary>
    /// Coordinates within this distance are treated as the same point.
    /// </summary>
    /// <remarks>
    /// Roughly a centimetre at the equator. OSM node coordinates are stored to seven decimal places, so
    /// shared endpoints are usually bit-identical; the tolerance exists for the fragments that have been
    /// re-projected or rounded by an editor at some point in their history.
    /// </remarks>
    private const double SnapTolerance = 1e-7;

    public static AssemblyResult Assemble(
        IReadOnlyList<(string Role, IReadOnlyList<Coordinate> Coordinates)> members,
        GeometryFactory factory)
    {
        var outerFragments = new List<List<Coordinate>>();
        var innerFragments = new List<List<Coordinate>>();

        foreach (var (role, coordinates) in members)
        {
            if (coordinates.Count < 2)
            {
                continue;
            }

            // Anything that is not explicitly inner is treated as outer. Empty roles are common on
            // older relations and mean the outline itself; subarea and label members carry roles this
            // never sees because the caller filters to ways.
            var target = string.Equals(role, "inner", StringComparison.Ordinal)
                ? innerFragments
                : outerFragments;

            target.Add([.. coordinates]);
        }

        var outerRings = StitchRings(outerFragments, out var openOuter);
        var innerRings = StitchRings(innerFragments, out _);

        if (outerRings.Count == 0)
        {
            return AssemblyResult.Failed(
                openOuter > 0
                    ? $"no outer ring closed ({openOuter} open chain(s) after stitching)"
                    : "the relation carried no usable outer way members");
        }

        var shells = new List<LinearRing>();

        foreach (var ring in outerRings)
        {
            var shell = ToRing(ring, factory);

            if (shell is not null)
            {
                shells.Add(shell);
            }
        }

        if (shells.Count == 0)
        {
            return AssemblyResult.Failed("every outer ring had fewer than four distinct points");
        }

        var holes = new List<LinearRing>();

        foreach (var ring in innerRings)
        {
            var hole = ToRing(ring, factory);

            if (hole is not null)
            {
                holes.Add(hole);
            }
        }

        var polygons = BuildPolygons(shells, holes, factory);

        if (polygons.Count == 0)
        {
            return AssemblyResult.Failed("no polygon could be built from the assembled rings");
        }

        var assembled = factory.CreateMultiPolygon([.. polygons]);

        return AssemblyResult.Succeeded(
            assembled,
            openOuter > 0
                ? $"{openOuter} outer chain(s) did not close and were discarded"
                : null);
    }

    /// <summary>
    /// Assigns each hole to the smallest shell that contains it, then builds the polygons.
    /// </summary>
    /// <remarks>
    /// Smallest containing shell rather than first: a relation can hold an island inside a lagoon inside
    /// a larger landmass, and attaching the hole to the outermost shell would punch a hole through the
    /// wrong piece of the municipality.
    /// </remarks>
    private static List<Polygon> BuildPolygons(
        List<LinearRing> shells,
        List<LinearRing> holes,
        GeometryFactory factory)
    {
        var shellPolygons = shells.ConvertAll(shell => factory.CreatePolygon(shell));
        var assigned = new List<List<LinearRing>>(shells.Count);

        for (var index = 0; index < shells.Count; index++)
        {
            assigned.Add([]);
        }

        foreach (var hole in holes)
        {
            var holePolygon = factory.CreatePolygon(hole);
            var bestIndex = -1;
            var bestArea = double.MaxValue;

            for (var index = 0; index < shellPolygons.Count; index++)
            {
                var candidate = shellPolygons[index];

                if (!candidate.Covers(holePolygon))
                {
                    continue;
                }

                if (candidate.Area < bestArea)
                {
                    bestArea = candidate.Area;
                    bestIndex = index;
                }
            }

            // A hole contained by no shell is discarded rather than promoted to a shell. It is usually a
            // mis-roled member, and turning it into land would add territory the relation does not claim.
            if (bestIndex >= 0)
            {
                assigned[bestIndex].Add(hole);
            }
        }

        var polygons = new List<Polygon>(shells.Count);

        for (var index = 0; index < shells.Count; index++)
        {
            polygons.Add(assigned[index].Count == 0
                ? factory.CreatePolygon(shells[index])
                : factory.CreatePolygon(shells[index], [.. assigned[index]]));
        }

        return polygons;
    }

    /// <summary>
    /// Stitches fragments end to end into closed rings.
    /// </summary>
    /// <param name="openChains">Chains that ran out of connecting fragments without closing.</param>
    private static List<List<Coordinate>> StitchRings(
        List<List<Coordinate>> fragments,
        out int openChains)
    {
        var rings = new List<List<Coordinate>>();
        var remaining = new LinkedList<List<Coordinate>>(fragments);

        openChains = 0;

        while (remaining.First is not null)
        {
            var current = remaining.First.Value;
            remaining.RemoveFirst();

            var chain = new List<Coordinate>(current);

            while (!IsClosed(chain))
            {
                var node = remaining.First;
                var joined = false;

                while (node is not null)
                {
                    var candidate = node.Value;
                    var next = node.Next;
                    var tail = chain[^1];

                    if (Same(tail, candidate[0]))
                    {
                        AppendSkippingFirst(chain, candidate);
                        remaining.Remove(node);
                        joined = true;
                    }
                    else if (Same(tail, candidate[^1]))
                    {
                        // The fragment is mapped in the opposite direction. Reversing is not a repair:
                        // OSM way direction carries no meaning for a boundary member.
                        var reversed = new List<Coordinate>(candidate);
                        reversed.Reverse();
                        AppendSkippingFirst(chain, reversed);
                        remaining.Remove(node);
                        joined = true;
                    }

                    if (joined)
                    {
                        break;
                    }

                    node = next;
                }

                if (!joined)
                {
                    break;
                }
            }

            if (IsClosed(chain) && chain.Count >= 4)
            {
                rings.Add(chain);
            }
            else
            {
                openChains++;
            }
        }

        return rings;
    }

    private static LinearRing? ToRing(List<Coordinate> ring, GeometryFactory factory)
    {
        var deduplicated = new List<Coordinate>(ring.Count) { ring[0] };

        for (var index = 1; index < ring.Count; index++)
        {
            if (!Same(ring[index], deduplicated[^1]))
            {
                deduplicated.Add(ring[index]);
            }
        }

        if (!Same(deduplicated[0], deduplicated[^1]))
        {
            deduplicated.Add(deduplicated[0].Copy());
        }

        return deduplicated.Count < 4 ? null : factory.CreateLinearRing([.. deduplicated]);
    }

    private static void AppendSkippingFirst(List<Coordinate> chain, List<Coordinate> fragment)
    {
        for (var index = 1; index < fragment.Count; index++)
        {
            chain.Add(fragment[index]);
        }
    }

    private static bool IsClosed(List<Coordinate> chain) =>
        chain.Count >= 4 && Same(chain[0], chain[^1]);

    private static bool Same(Coordinate left, Coordinate right) =>
        Math.Abs(left.X - right.X) <= SnapTolerance && Math.Abs(left.Y - right.Y) <= SnapTolerance;
}

/// <param name="Note">Set when the geometry was built but something was discarded doing it.</param>
internal sealed record AssemblyResult(MultiPolygon? Geometry, string? Note, string? Failure)
{
    public bool IsSuccess => Geometry is not null;

    public static AssemblyResult Succeeded(MultiPolygon geometry, string? note) =>
        new(geometry, note, null);

    public static AssemblyResult Failed(string failure) => new(null, null, failure);
}
