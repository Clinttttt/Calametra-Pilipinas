using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycloneInnerWindBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "hurricane_radius_north_east_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "hurricane_radius_north_west_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "hurricane_radius_south_east_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "hurricane_radius_south_west_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "storm_radius_north_east_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "storm_radius_north_west_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "storm_radius_south_east_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "storm_radius_south_west_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hurricane_radius_north_east_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "hurricane_radius_north_west_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "hurricane_radius_south_east_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "hurricane_radius_south_west_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "storm_radius_north_east_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "storm_radius_north_west_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "storm_radius_south_east_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "storm_radius_south_west_nm",
                table: "cyclone_track_points");
        }
    }
}
