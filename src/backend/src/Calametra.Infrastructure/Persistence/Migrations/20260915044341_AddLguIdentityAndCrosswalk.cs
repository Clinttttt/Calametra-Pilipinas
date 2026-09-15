using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLguIdentityAndCrosswalk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lgu_code_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lgu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    historical_psgc_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    evidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    proposal_basis = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    names_agree = table.Column<bool>(type: "boolean", nullable: false),
                    proposed_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    confirmed_against_edition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_code_links", x => x.id);
                    table.CheckConstraint("ck_lgu_code_links_confirmed_is_complete", "status <> 'Confirmed' OR (\n    evidence <> 'Unknown'\n    AND reviewed_by IS NOT NULL\n    AND reviewed_at IS NOT NULL\n    AND confirmed_against_edition_id IS NOT NULL\n    AND (evidence <> 'ManualReview' OR reason IS NOT NULL)\n    AND (evidence <> 'DigitReslice' OR names_agree)\n)");
                    table.CheckConstraint("ck_lgu_code_links_historical_code_is_nine_digits", "char_length(historical_psgc_code) = 9 AND historical_psgc_code ~ '^[0-9]+$'");
                    table.CheckConstraint("ck_lgu_code_links_rejected_has_reason", "status <> 'Rejected' OR (reviewed_by IS NOT NULL AND reason IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "lgus",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_psgc_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    register_edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_canonical_psgc_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    register_stated_historical_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgus", x => x.id);
                    table.CheckConstraint("ck_lgus_canonical_code_is_ten_digits", "char_length(canonical_psgc_code) = 10 AND canonical_psgc_code ~ '^[0-9]+$'");
                });

            migrationBuilder.CreateTable(
                name: "psgc_register_editions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provenance = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    access_route = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    upstream_last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    region_count = table.Column<int>(type: "integer", nullable: false),
                    province_count = table.Column<int>(type: "integer", nullable: false),
                    city_count = table.Column<int>(type: "integer", nullable: false),
                    municipality_count = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_psgc_register_editions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lgu_code_links_historical_psgc_code",
                table: "lgu_code_links",
                column: "historical_psgc_code");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_code_links_lgu_id_historical_psgc_code",
                table: "lgu_code_links",
                columns: new[] { "lgu_id", "historical_psgc_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lgu_code_links_status",
                table: "lgu_code_links",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_code_links_confirmed_per_lgu",
                table: "lgu_code_links",
                column: "lgu_id",
                unique: true,
                filter: "status = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_code_links_confirmed_per_place",
                table: "lgu_code_links",
                column: "place_id",
                unique: true,
                filter: "status = 'Confirmed' AND place_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lgus_canonical_psgc_code",
                table: "lgus",
                column: "canonical_psgc_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lgus_parent_canonical_psgc_code",
                table: "lgus",
                column: "parent_canonical_psgc_code");

            migrationBuilder.CreateIndex(
                name: "ix_psgc_register_editions_label_access_route",
                table: "psgc_register_editions",
                columns: new[] { "label", "access_route" },
                unique: true);

            // ── THE READ BOUNDARY ────────────────────────────────────────────────────────
            //
            // ADR-005 D4 requires that no figure rendered to a reader can be derived from an
            // unreviewed proposal. A CHECK constraint cannot express that — it constrains the row it
            // sits on, it cannot forbid a SELECT — so the rule is enforced where reads happen. This
            // view is the first of three layers: the analytics context maps `ConfirmedLguLink` to it
            // and does not expose the base table at all, database permissions tighten the base table
            // where the deployment has separate roles, and the boundary tests assert that a proposal
            // is invisible through every public read path.
            //
            // The unit's code, name and level are carried on the view so a caller needs no second
            // join, which also removes the temptation to reach for the base table for convenience.
            migrationBuilder.Sql(
                """
                CREATE VIEW lgu_code_links_confirmed AS
                SELECT
                    link.id,
                    link.lgu_id,
                    lgu.canonical_psgc_code,
                    lgu.name        AS lgu_name,
                    lgu.level       AS lgu_level,
                    link.historical_psgc_code,
                    link.place_id,
                    link.evidence,
                    link.reviewed_by,
                    link.reviewed_at,
                    link.reason,
                    link.confirmed_against_edition_id
                FROM lgu_code_links AS link
                JOIN lgus AS lgu ON lgu.id = link.lgu_id
                WHERE link.status = 'Confirmed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped before the tables it reads, or the drop fails on the dependency.
            migrationBuilder.Sql("DROP VIEW IF EXISTS lgu_code_links_confirmed;");

            migrationBuilder.DropTable(
                name: "lgu_code_links");

            migrationBuilder.DropTable(
                name: "lgus");

            migrationBuilder.DropTable(
                name: "psgc_register_editions");
        }
    }
}
