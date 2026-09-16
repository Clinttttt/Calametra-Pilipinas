using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLguSourceNameOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lgu_source_name_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_psgc_code = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    source_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    register_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reviewed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_against_edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_source_name_overrides", x => x.id);
                    table.CheckConstraint("ck_lgu_source_name_overrides_code_is_ten_digits", "canonical_psgc_code ~ '^[0-9]{10}$'");
                    table.CheckConstraint("ck_lgu_source_name_overrides_reason_is_evidenced", "char_length(btrim(reason)) >= 40");
                });

            migrationBuilder.CreateIndex(
                name: "ux_lgu_source_name_overrides_per_code_and_name",
                table: "lgu_source_name_overrides",
                columns: new[] { "canonical_psgc_code", "source_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lgu_source_name_overrides");
        }
    }
}
