using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLguCrosswalkExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lgu_crosswalk_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lgu_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_psgc_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    place_id = table.Column<Guid>(type: "uuid", nullable: true),
                    directory_psgc_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    subject_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    accepted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    register_edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_crosswalk_exceptions", x => x.id);
                    table.CheckConstraint("ck_lgu_crosswalk_exceptions_has_one_subject", "(kind = 'RegisterUnitHasNoHistoricalCode'\n    AND lgu_id IS NOT NULL AND canonical_psgc_code IS NOT NULL AND place_id IS NULL)\nOR (kind = 'DirectoryRowHasNoRegisterUnit'\n    AND place_id IS NOT NULL AND directory_psgc_code IS NOT NULL AND lgu_id IS NULL)");
                    table.CheckConstraint("ck_lgu_crosswalk_exceptions_reason_is_written", "char_length(btrim(reason)) >= 20");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lgu_crosswalk_exceptions_register_edition_id",
                table: "lgu_crosswalk_exceptions",
                column: "register_edition_id");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_crosswalk_exceptions_per_place_and_edition",
                table: "lgu_crosswalk_exceptions",
                columns: new[] { "place_id", "register_edition_id" },
                unique: true,
                filter: "place_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_crosswalk_exceptions_per_unit_and_edition",
                table: "lgu_crosswalk_exceptions",
                columns: new[] { "lgu_id", "register_edition_id" },
                unique: true,
                filter: "lgu_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lgu_crosswalk_exceptions");
        }
    }
}
