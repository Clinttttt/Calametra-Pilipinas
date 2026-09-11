using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycloneWindField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gale_radius_bearing_degrees",
                table: "cyclone_track_points",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gale_radius_long_axis_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gale_radius_north_east_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gale_radius_north_west_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gale_radius_short_axis_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gale_radius_south_east_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "gale_radius_south_west_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gale_threshold_knots",
                table: "cyclone_track_points",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "radius_of_maximum_wind_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "radius_outermost_isobar_nm",
                table: "cyclone_track_points",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wind_field_geometry",
                table: "cyclone_track_points",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gale_radius_bearing_degrees",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_radius_long_axis_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_radius_north_east_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_radius_north_west_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_radius_short_axis_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_radius_south_east_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_radius_south_west_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "gale_threshold_knots",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "radius_of_maximum_wind_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "radius_outermost_isobar_nm",
                table: "cyclone_track_points");

            migrationBuilder.DropColumn(
                name: "wind_field_geometry",
                table: "cyclone_track_points");
        }
    }
}
