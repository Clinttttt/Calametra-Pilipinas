using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBoundaryExtractProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, then backfilled and tightened at the end of this migration. The generated
            // default was an empty GUID, which would have left 283 outlines pointing at an extract that does
            // not exist — the unauditable state this table was added to prevent.
            migrationBuilder.AddColumn<Guid>(
                name: "extract_id",
                table: "lgu_boundaries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lgu_boundary_extracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    provenance = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    access_route = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    file_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    vintage = table.Column<DateOnly>(type: "date", nullable: true),
                    acquisition_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    boundaries_read = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lgu_boundary_extracts", x => x.id);
                    table.CheckConstraint("ck_lgu_boundary_extracts_file_details_are_paired", "(original_file_name IS NULL AND file_sha256 IS NULL) OR (original_file_name IS NOT NULL AND file_sha256 IS NOT NULL)");
                    table.CheckConstraint("ck_lgu_boundary_extracts_hash_is_hex", "file_sha256 IS NULL OR file_sha256 ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_lgu_boundaries_extract_id",
                table: "lgu_boundaries",
                column: "extract_id");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_boundary_extracts_label_access_route",
                table: "lgu_boundary_extracts",
                columns: new[] { "label", "access_route" });

            migrationBuilder.CreateIndex(
                name: "ux_lgu_boundary_extracts_per_file",
                table: "lgu_boundary_extracts",
                column: "file_sha256",
                unique: true,
                filter: "file_sha256 IS NOT NULL");

            // Backfill the outlines already held.
            //
            // 283 city and municipality outlines were acquired from the Overpass API before this table
            // existed. They are real geometry from a real read and are retained rather than deleted, so they
            // need the provenance record they were stored without. It is written as what it was: an API read
            // with no file and therefore no digest, which is exactly why the file path was built afterwards.
            //
            // The id is fixed rather than generated so this migration is deterministic and can be reasoned
            // about from the SQL alone.
            migrationBuilder.Sql(
                """
                INSERT INTO lgu_boundary_extracts
                    (id, source_id, label, provenance, access_route, acquired_at, boundaries_read,
                     created_at, updated_at)
                SELECT
                    '01991a00-0000-7000-8000-000000000001'::uuid,
                    s.id,
                    'Overpass API read, admin_level 6, partial national acquisition',
                    'OverpassApi',
                    'https://overpass-api.de/api/interpreter',
                    COALESCE((SELECT min(extracted_at) FROM lgu_boundaries), now()),
                    (SELECT count(*) FROM lgu_boundaries),
                    now(),
                    now()
                FROM data_sources s
                WHERE s.slug = 'openstreetmap-ph-admin-boundaries'
                  AND EXISTS (SELECT 1 FROM lgu_boundaries)
                ON CONFLICT (id) DO NOTHING;

                UPDATE lgu_boundaries
                SET extract_id = '01991a00-0000-7000-8000-000000000001'::uuid
                WHERE extract_id IS NULL;
                """);

            // Any row left without an extract now would be geometry whose origin cannot be named, so the
            // column is tightened only after the backfill and the database enforces it from here on.
            migrationBuilder.Sql(
                "ALTER TABLE lgu_boundaries ALTER COLUMN extract_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lgu_boundary_extracts");

            migrationBuilder.DropIndex(
                name: "ix_lgu_boundaries_extract_id",
                table: "lgu_boundaries");

            migrationBuilder.DropColumn(
                name: "extract_id",
                table: "lgu_boundaries");
        }
    }
}
