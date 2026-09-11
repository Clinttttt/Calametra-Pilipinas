using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Replaces <c>event_track_points</c> with <c>cyclone_track_points</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This migration drops and recreates, and that is deliberate.</b> The previous rename
    /// — <c>event_observations</c> to <c>earthquake_observations</c> — had to be hand-written
    /// as <c>ALTER TABLE ... RENAME TO</c> because EF's scaffolded drop-and-create would have
    /// destroyed 27,241 rows. Here the scaffolded form is correct, for two reasons that were
    /// checked rather than assumed:
    /// </para>
    /// <para>
    /// First, the table is empty. <c>SELECT count(*) FROM event_track_points</c> returned 0
    /// before this was generated; no cyclone data has been ingested yet.
    /// </para>
    /// <para>
    /// Second, this is not only a rename. The schema changes shape: sustained wind gains an
    /// averaging-period column so a speed can never be stored without the interval it was
    /// averaged over, latitude and longitude are materialised alongside the geography column
    /// for the same reason as on the earthquake table, and a unique index now enforces one
    /// fix per agency per event per timestamp. A rename could not have produced any of that.
    /// </para>
    /// <para>
    /// If cyclone data is ever ingested before this migration is applied in a given
    /// environment, this becomes destructive. Check the row count first.
    /// </para>
    /// </remarks>
    public partial class AddCycloneTrackPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_track_points");

            migrationBuilder.CreateTable(
                name: "cyclone_track_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hazard_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    position = table.Column<Point>(type: "geography (Point, 4326)", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    wind_speed_knots = table.Column<double>(type: "double precision", nullable: true),
                    wind_averaging_period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    minimum_pressure_millibars = table.Column<int>(type: "integer", nullable: true),
                    classification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    distance_to_land_km = table.Column<double>(type: "double precision", nullable: true),
                    is_landfall = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cyclone_track_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_cyclone_track_points_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cyclone_track_points_hazard_events_hazard_event_id",
                        column: x => x.hazard_event_id,
                        principalTable: "hazard_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cyclone_track_points_data_source_id",
                table: "cyclone_track_points",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_cyclone_track_points_hazard_event_id_captured_at",
                table: "cyclone_track_points",
                columns: new[] { "hazard_event_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cyclone_track_points_hazard_event_id_data_source_id_capture",
                table: "cyclone_track_points",
                columns: new[] { "hazard_event_id", "data_source_id", "captured_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cyclone_track_points_position",
                table: "cyclone_track_points",
                column: "position")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_cyclone_track_points_wind_averaging_period_wind_speed_knots",
                table: "cyclone_track_points",
                columns: new[] { "wind_averaging_period", "wind_speed_knots" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cyclone_track_points");

            migrationBuilder.CreateTable(
                name: "event_track_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    classification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    distance_to_land_km = table.Column<double>(type: "double precision", nullable: true),
                    hazard_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_landfall = table.Column<bool>(type: "boolean", nullable: false),
                    max_sustained_wind_knots = table.Column<int>(type: "integer", nullable: true),
                    minimum_pressure_millibars = table.Column<int>(type: "integer", nullable: true),
                    position = table.Column<Point>(type: "geography (Point, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_track_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_track_points_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_track_points_hazard_events_hazard_event_id",
                        column: x => x.hazard_event_id,
                        principalTable: "hazard_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_track_points_data_source_id",
                table: "event_track_points",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_track_points_hazard_event_id_captured_at",
                table: "event_track_points",
                columns: new[] { "hazard_event_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_track_points_position",
                table: "event_track_points",
                column: "position")
                .Annotation("Npgsql:IndexMethod", "gist");
        }
    }
}
