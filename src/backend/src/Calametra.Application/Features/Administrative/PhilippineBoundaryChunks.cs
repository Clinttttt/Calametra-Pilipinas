using Calametra.Application.Abstractions.Sources;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// The bounding boxes a national boundary fetch is split into.
/// </summary>
/// <remarks>
/// <para>
/// <b>A grid rather than one national query, because the national query does not return.</b> Measured
/// against the public Overpass instance on 2026-09-15: a tag-only query over a province-sized box answers
/// in under twenty seconds, while asking for geometry over a wide area drops the connection. Roughly
/// sixteen hundred municipal outlines is on the order of a hundred megabytes of coordinates, and no single
/// request on volunteer infrastructure should be expected to carry it.
/// </para>
/// <para>
/// <b>A grid rather than one box per province,</b> although the register does hold all 82 provinces. A
/// province box would need province geometry, which is what this import is trying to acquire — and it
/// would make the fetch depend on the correctness of the thing being fetched. Latitude and longitude are
/// known independently of any boundary being right.
/// </para>
/// <para>
/// Cells overlap by design at their shared edges, since Overpass returns any relation with a member in the
/// box: a municipality straddling a cell edge is returned by both. The adapter deduplicates by relation
/// id, so overlap costs a little traffic and buys the guarantee that nothing falls between two cells.
/// </para>
/// <para>
/// Ocean cells are queried rather than excluded. Skipping them would need a landmask, and a wrong landmask
/// would silently drop islands — Batanes in the far north and Tawi-Tawi in the far south are exactly the
/// places a carelessly drawn extent would lose. An empty cell answers in about a second.
/// </para>
/// </remarks>
public static class PhilippineBoundaryChunks
{
    /// <summary>
    /// The archipelago's extent, generously drawn.
    /// </summary>
    /// <remarks>
    /// Y'Ami in the Batanes group is the northernmost point at about 21.1°N and the Turtle Islands in
    /// Tawi-Tawi the southernmost at about 4.6°N; the box is widened past both so a boundary that extends
    /// into municipal waters is not clipped at the frame.
    /// </remarks>
    private const double South = 4.0;
    private const double North = 21.5;
    private const double West = 116.0;
    private const double East = 127.0;

    /// <summary>
    /// Cell size in degrees.
    /// </summary>
    /// <remarks>
    /// Two degrees is about 220 km. Chosen from the measurement above: small enough that a dense cell such
    /// as Cebu or Metro Manila stays inside one response, large enough that the whole country is 54 cells
    /// rather than several hundred requests against a two-slot rate limit.
    /// </remarks>
    private const double CellDegrees = 2.0;

    public static IReadOnlyList<BoundaryChunk> All { get; } = Build();

    private static List<BoundaryChunk> Build()
    {
        var chunks = new List<BoundaryChunk>();

        for (var south = South; south < North; south += CellDegrees)
        {
            for (var west = West; west < East; west += CellDegrees)
            {
                var north = Math.Min(south + CellDegrees, North);
                var east = Math.Min(west + CellDegrees, East);

                chunks.Add(new BoundaryChunk(
                    $"lat {south:0.#}-{north:0.#} lon {west:0.#}-{east:0.#}",
                    south,
                    west,
                    north,
                    east));
            }
        }

        return chunks;
    }
}
