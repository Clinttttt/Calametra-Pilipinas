using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycloneExternalStormId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_storm_id",
                table: "cyclone_track_points",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_cyclone_track_points_external_storm_id",
                table: "cyclone_track_points",
                column: "external_storm_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cyclone_track_points_external_storm_id",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "external_storm_id",
                table: "cyclone_track_points");
        }
    }
}
