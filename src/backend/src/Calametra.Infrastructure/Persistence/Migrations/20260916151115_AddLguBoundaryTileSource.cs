using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The tiling source: projected once, simplified per band, filtered to what may be served.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR-005 D6 serves geometry simplified per zoom band rather than full-resolution at every zoom.
    /// Measured before this existed, a zoom-4 tile took 27 seconds and 131 KB because every request
    /// re-projected all 1,611 land outlines from geography to web mercator and re-simplified them. The work
    /// was identical every time, so it is done once here; zoom 6 went from 10.6 seconds to 255 ms.
    /// </para>
    /// <para>
    /// A materialised view rather than columns on <c>lgu_boundaries</c>: that table is the versioned record
    /// of what was imported and should not carry a rendering concern, and a view can be refreshed after an
    /// import without touching stored geometry.
    /// </para>
    /// <para>
    /// It is also where ADR-005 D2a is enforced. The view holds only outlines in force and only those from
    /// the canonical land-only source, so no tile query can reach a superseded OpenStreetMap outline — which
    /// extends to municipal waters — even by mistake. The filter lives at the point the tiles are built
    /// rather than being repeated in every query that might forget it.
    /// </para>
    /// </remarks>
    public partial class AddLguBoundaryTileSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE MATERIALIZED VIEW lgu_boundary_tile_source AS
                SELECT
                    -- ST_AsMVT requires an integer feature id. The string form is carried too, because the
                    -- leading zero every Luzon code has is lost in the integer and the string is what any
                    -- link or figure is built from. The two are in bijection, so MapLibre's feature-state
                    -- stays keyed to the PSGC and survives tile loads and re-imports.
                    b.canonical_psgc_code::bigint                           AS fid,
                    b.canonical_psgc_code                                   AS psgc,
                    l.name                                                  AS name,
                    l.level                                                 AS kind,
                    round(b.area_square_km::numeric, 1)::double precision   AS area_km2,

                    -- Full resolution, projected once. Read from zoom 8 down, where the coast is the answer
                    -- rather than the frame.
                    ST_Transform(b.geometry::geometry, 3857)                AS geom_local,

                    -- The regional band. 400 m is about one screen pixel of ground at zoom 6; finer detail is
                    -- bytes the reader cannot see. PreserveTopology so a municipality does not lose a shared
                    -- edge and leave a sliver of nothing between itself and its neighbour.
                    ST_SimplifyPreserveTopology(
                        ST_Transform(b.geometry::geometry, 3857), 400)      AS geom_regional
                FROM lgu_boundaries b
                JOIN lgus l ON l.id = b.lgu_id
                WHERE b.valid_to IS NULL
                  AND b.source_feature_code IS NOT NULL;
                """);

            // Unique on the code so the view can be refreshed CONCURRENTLY. Without it a refresh takes an
            // exclusive lock and the map goes blank for as long as an import takes.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_lgu_boundary_tile_source_fid "
                + "ON lgu_boundary_tile_source (fid);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_lgu_boundary_tile_source_local_gist "
                + "ON lgu_boundary_tile_source USING gist (geom_local);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_lgu_boundary_tile_source_regional_gist "
                + "ON lgu_boundary_tile_source USING gist (geom_regional);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS lgu_boundary_tile_source;");
    }
}
