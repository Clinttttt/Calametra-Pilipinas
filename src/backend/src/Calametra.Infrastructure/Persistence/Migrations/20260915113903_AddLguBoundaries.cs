using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLguBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lgu_boundaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lgu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_psgc_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    osm_relation_id = table.Column<long>(type: "bigint", nullable: false),
                    osm_ref_tag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    osm_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    osm_admin_level = table.Column<int>(type: "integer", nullable: false),
                    geometry = table.Column<MultiPolygon>(type: "geography (MultiPolygon, 4326)", nullable: false),
                    area_square_km = table.Column<double>(type: "double precision", nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    extract_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    was_repaired = table.Column<bool>(type: "boolean", nullable: false),
                    repair_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_boundary_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_boundaries", x => x.id);
                    table.CheckConstraint("ck_lgu_boundaries_area_is_positive", "area_square_km > 0");
                    table.CheckConstraint("ck_lgu_boundaries_repair_is_explained", "NOT was_repaired OR repair_note IS NOT NULL");
                    table.CheckConstraint("ck_lgu_boundaries_supersession_is_complete", "(valid_to IS NULL AND superseded_by_boundary_id IS NULL) OR (valid_to IS NOT NULL AND superseded_by_boundary_id IS NOT NULL)");
                    table.CheckConstraint("ck_lgu_boundaries_valid_period_is_ordered", "valid_to IS NULL OR valid_to >= valid_from");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lgu_boundaries_geometry",
                table: "lgu_boundaries",
                column: "geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_boundaries_osm_relation_id",
                table: "lgu_boundaries",
                column: "osm_relation_id");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_boundaries_in_force_per_lgu",
                table: "lgu_boundaries",
                column: "lgu_id",
                unique: true,
                filter: "valid_to IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lgu_boundaries");
        }
    }
}
