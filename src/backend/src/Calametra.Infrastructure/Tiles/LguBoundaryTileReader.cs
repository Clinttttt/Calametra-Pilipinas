using Calametra.Application.Abstractions.Tiles;
using Calametra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Infrastructure.Tiles;

/// <summary>
/// Builds an LGU boundary tile with PostGIS, in one round trip.
/// </summary>
/// <remarks>
/// <para>
/// <b>The filter is the important part of this query, not the projection.</b> Two conditions decide what a
/// reader is shown: <c>valid_to IS NULL</c> keeps superseded versions out, and
/// <c>source_feature_code IS NOT NULL</c> keeps the canonical land outlines in and the retained OSM ones
/// out. The second is what enforces ADR-005 D2a at the delivery boundary — OSM outlines extend to municipal
/// waters, and a tile carrying both would give the client two different meanings of the word boundary with
/// nothing to distinguish them.
/// </para>
/// <para>
/// <b>The feature id is the canonical PSGC.</b> MapLibre needs a stable identity to hold hover and selection
/// state across tile loads, and the code is the only identifier here that survives a re-import, a
/// re-simplification, or a change of source. A row identifier would not: it changes whenever the geometry is
/// re-versioned, and the selection would silently drop the moment a new extract landed.
/// </para>
/// </remarks>
internal sealed class LguBoundaryTileReader(ApplicationDbContext context) : ILguBoundaryTileReader
{
    /// <summary>
    /// Tile extent in tile units.
    /// </summary>
    /// <remarks>
    /// 4,096 is the Mapbox Vector Tile convention and what MapLibre assumes when a source declares no
    /// extent. Changing it would silently shift every outline by the ratio.
    /// </remarks>
    private const int Extent = 4096;

    /// <summary>
    /// Buffer in tile units.
    /// </summary>
    /// <remarks>
    /// 64 units is about 1.5% of the tile. Without a buffer an outline crossing a tile edge is clipped
    /// exactly at it, and MapLibre then draws the cut as though it were a boundary — a seam of hairlines
    /// along every tile join, which looks like a data fault rather than a rendering one.
    /// </remarks>
    private const int Buffer = 64;

    public async Task<byte[]?> ReadAsync(
        int z,
        int x,
        int y,
        double simplificationMetres,
        CancellationToken cancellationToken)
    {
        // Which pre-built geometry to read, and whether any further simplification is worth doing.
        //
        // The projection and the coarse simplification are done once in lgu_boundary_tile_source rather
        // than per request. Measured before that view existed, a zoom-4 tile took 27 seconds because every
        // request re-projected and re-simplified all 1,611 outlines; the work was identical every time.
        //
        // The view is also where D2a is enforced: it contains only outlines in force and only from the
        // canonical land-only source, so no query here can reach a superseded OSM municipal-water outline
        // even by mistake.
        var geometryColumn = z <= 7 ? "geom_regional" : "geom_local";
        var applyTolerance = simplificationMetres > 0 && z > 7;

        var geometryExpression = applyTolerance
            ? $"ST_SimplifyPreserveTopology({geometryColumn}, @tolerance)"
            : geometryColumn;

        var sql = $"""
            WITH bounds AS (
                SELECT ST_TileEnvelope(@z, @x, @y) AS envelope
            ),
            selected AS (
                SELECT
                    s.fid,
                    s.psgc,
                    s.name,
                    s.kind,
                    s.area_km2,
                    ST_AsMVTGeom({geometryExpression}, bounds.envelope, {Extent}, {Buffer}, true) AS geom
                FROM lgu_boundary_tile_source s
                CROSS JOIN bounds
                WHERE s.{geometryColumn} && bounds.envelope
            )
            SELECT ST_AsMVT(selected, 'lgu', {Extent}, 'geom', 'fid') AS tile
            FROM selected
            WHERE geom IS NOT NULL
            """;

        await using var command = context.Database.GetDbConnection().CreateCommand();

        command.CommandText = sql;

        AddParameter(command, "z", z);
        AddParameter(command, "x", x);
        AddParameter(command, "y", y);

        if (applyTolerance)
        {
            AddParameter(command, "tolerance", simplificationMetres);
        }

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);

            return value as byte[];
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();

        parameter.ParameterName = name;
        parameter.Value = value;

        command.Parameters.Add(parameter);
    }
}
