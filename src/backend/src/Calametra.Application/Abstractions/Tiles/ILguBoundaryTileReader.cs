namespace Calametra.Application.Abstractions.Tiles;

/// <summary>
/// Builds one Mapbox Vector Tile of the current city and municipality outlines.
/// </summary>
/// <remarks>
/// <para>
/// A port because the work is a single PostGIS aggregate — <c>ST_AsMVT</c> over a projected, clipped,
/// simplified geometry — and the point of doing it in the database is that no outline is ever materialised
/// into managed memory. Expressing that through the analytics context would mean exposing raw SQL on an
/// interface whose whole purpose is to constrain what the application can reach.
/// </para>
/// <para>
/// The implementation is responsible for one guarantee this platform cares about more than performance:
/// only geometry <b>in force</b> and only from the <b>canonical land-only source</b>. ADR-005 D2a forbids
/// serving COD-AB land outlines and superseded OSM municipal-water outlines as one concept.
/// </para>
/// </remarks>
public interface ILguBoundaryTileReader
{
    /// <param name="simplificationMetres">
    /// Tolerance in web-mercator metres, or zero for full resolution. Chosen by the caller per zoom, because
    /// which detail is worth sending is a delivery decision rather than a storage one.
    /// </param>
    /// <returns>Protobuf bytes, or null when the tile covers no outline.</returns>
    Task<byte[]?> ReadAsync(
        int z,
        int x,
        int y,
        double simplificationMetres,
        CancellationToken cancellationToken);
}
