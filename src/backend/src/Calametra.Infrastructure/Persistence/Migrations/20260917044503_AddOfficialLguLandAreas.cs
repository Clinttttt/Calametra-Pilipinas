using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialLguLandAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lgu_official_land_area_editions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    register_edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    matrix_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    access_route = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    reference_year = table.Column<int>(type: "integer", nullable: false),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    metadata_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    payload_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    metadata_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    payload_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    metadata_snapshot = table.Column<string>(type: "text", nullable: false),
                    payload_snapshot = table.Column<string>(type: "text", nullable: false),
                    attribution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_row_count = table.Column<int>(type: "integer", nullable: false),
                    imported_lgu_count = table.Column<int>(type: "integer", nullable: false),
                    missing_active_lgu_count = table.Column<int>(type: "integer", nullable: false),
                    extra_source_row_count = table.Column<int>(type: "integer", nullable: false),
                    missing_canonical_psgc_codes = table.Column<string>(type: "text", nullable: true),
                    extra_source_codes = table.Column<string>(type: "text", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_edition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_official_land_area_editions", x => x.id);
                    table.CheckConstraint("ck_lgu_official_land_area_editions_activation_is_complete", "activated_at IS NULL OR (missing_active_lgu_count = 0 AND imported_lgu_count > 0)");
                    table.CheckConstraint("ck_lgu_official_land_area_editions_hashes_are_hex", "metadata_sha256 ~ '^[0-9a-f]{64}$' AND payload_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_lgu_official_land_area_editions_supersession_is_complete", "(superseded_at IS NULL AND superseded_by_edition_id IS NULL) OR (superseded_at IS NOT NULL AND superseded_by_edition_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_lgu_official_land_area_editions_data_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lgu_official_land_area_editions_psgc_register_editions_regi",
                        column: x => x.register_edition_id,
                        principalTable: "psgc_register_editions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lgu_official_land_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lgu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_psgc_code = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    area_square_km = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    basis = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_official_land_areas", x => x.id);
                    table.CheckConstraint("ck_lgu_official_land_areas_code_is_ten_digits", "char_length(canonical_psgc_code) = 10 AND canonical_psgc_code ~ '^[0-9]+$'");
                    table.CheckConstraint("ck_lgu_official_land_areas_value_is_positive", "area_square_km > 0");
                    table.ForeignKey(
                        name: "fk_lgu_official_land_areas_lgu_official_land_area_editions_edi",
                        column: x => x.edition_id,
                        principalTable: "lgu_official_land_area_editions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lgu_official_land_areas_lgus_lgu_id",
                        column: x => x.lgu_id,
                        principalTable: "lgus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lgu_official_land_area_editions_payload_sha256_register_edi",
                table: "lgu_official_land_area_editions",
                columns: new[] { "payload_sha256", "register_edition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lgu_official_land_area_editions_register_edition_id",
                table: "lgu_official_land_area_editions",
                column: "register_edition_id");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_official_land_area_editions_current_per_source",
                table: "lgu_official_land_area_editions",
                column: "source_id",
                unique: true,
                filter: "activated_at IS NOT NULL AND superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_official_land_areas_edition_id_canonical_psgc_code",
                table: "lgu_official_land_areas",
                columns: new[] { "edition_id", "canonical_psgc_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lgu_official_land_areas_edition_id_lgu_id",
                table: "lgu_official_land_areas",
                columns: new[] { "edition_id", "lgu_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lgu_official_land_areas_lgu_id",
                table: "lgu_official_land_areas",
                column: "lgu_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lgu_official_land_areas");

            migrationBuilder.DropTable(
                name: "lgu_official_land_area_editions");
        }
    }
}
