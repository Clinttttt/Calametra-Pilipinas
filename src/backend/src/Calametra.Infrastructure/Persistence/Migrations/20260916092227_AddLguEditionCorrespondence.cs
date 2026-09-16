using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLguEditionCorrespondence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lgu_edition_correspondences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legacy_canonical_psgc_code = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    legacy_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    current_lgu_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_canonical_psgc_code = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    proposal_basis = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    names_agree = table.Column<bool>(type: "boolean", nullable: false),
                    has_register_bridge = table.Column<bool>(type: "boolean", nullable: false),
                    proposed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proposed_against_edition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    confirmed_against_edition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_edition_correspondences", x => x.id);
                    table.CheckConstraint("ck_lgu_edition_correspondences_codes_are_ten_digits", "legacy_canonical_psgc_code ~ '^[0-9]{10}$' AND current_canonical_psgc_code ~ '^[0-9]{10}$'");
                    table.CheckConstraint("ck_lgu_edition_correspondences_codes_differ", "legacy_canonical_psgc_code <> current_canonical_psgc_code");
                    table.CheckConstraint("ck_lgu_edition_correspondences_confirmed_is_complete", "status <> 'Confirmed' OR (\n    reviewed_by IS NOT NULL\n    AND reviewed_at IS NOT NULL\n    AND confirmed_against_edition_id IS NOT NULL\n    AND evidence IN ('EditionCorrespondence', 'ManualReview')\n    AND (evidence <> 'ManualReview' OR char_length(btrim(reason)) >= 20)\n    AND (evidence <> 'EditionCorrespondence' OR names_agree)\n)");
                    table.CheckConstraint("ck_lgu_edition_correspondences_rejected_has_reason", "status <> 'Rejected' OR (reviewed_by IS NOT NULL AND reason IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lgu_edition_correspondences_proposed_against_edition_id",
                table: "lgu_edition_correspondences",
                column: "proposed_against_edition_id");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_edition_correspondences_status",
                table: "lgu_edition_correspondences",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_edition_correspondences_confirmed_per_unit",
                table: "lgu_edition_correspondences",
                column: "current_lgu_id",
                unique: true,
                filter: "status = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_edition_correspondences_live_per_legacy_code",
                table: "lgu_edition_correspondences",
                column: "legacy_canonical_psgc_code",
                unique: true,
                filter: "status <> 'Superseded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lgu_edition_correspondences");
        }
    }
}
