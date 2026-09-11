using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes a place's gazetteer a required part of the row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safe here, and checked rather than assumed: <c>SELECT count(*) FROM places</c> returned 0
    /// before this was applied. The table has existed since the initial schema and has never been
    /// populated — the place explorer is the feature that fills it.
    /// </para>
    /// <para>
    /// The scaffolded version added the column with a default of the all-zero GUID, which is
    /// exactly the wrong behaviour for this platform: an existing row would have been given a
    /// source identifier that points at nothing, and the guarantee the column exists to make
    /// would have been broken by the migration that introduced it. The default is removed and a
    /// guard added, so a database that does hold places stops with an explanation instead of
    /// silently acquiring unattributed rows.
    /// </para>
    /// </remarks>
    public partial class AddPlaceDataSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM places) THEN
                        RAISE EXCEPTION 'places already holds rows, and a place cannot be given a gazetteer retrospectively. Delete the rows, apply this migration, then re-run the ingestion host with --Ingestion:Places:Import=true.';
                    END IF;
                END $$;
                """);

            // No default value. On an empty table this is a catalogue change; on a populated one
            // PostgreSQL refuses it, which is the intended outcome.
            migrationBuilder.AddColumn<Guid>(
                name: "data_source_id",
                table: "places",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_places_data_source_id",
                table: "places",
                column: "data_source_id");

            migrationBuilder.AddForeignKey(
                name: "fk_places_data_sources_data_source_id",
                table: "places",
                column: "data_source_id",
                principalTable: "data_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_places_data_sources_data_source_id",
                table: "places");

            migrationBuilder.DropIndex(
                name: "ix_places_data_source_id",
                table: "places");

            migrationBuilder.DropColumn(
                name: "data_source_id",
                table: "places");
        }
    }
}
