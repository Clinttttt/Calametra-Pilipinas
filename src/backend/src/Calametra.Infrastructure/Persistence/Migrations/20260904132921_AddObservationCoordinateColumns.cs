using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddObservationCoordinateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "event_observations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "event_observations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // Populate the new columns from the existing geography values, rather
            // than leaving already-ingested rows at the 0,0 default — which would
            // silently place every historical event in the Gulf of Guinea.
            //
            // The ::geometry cast is required because ST_X and ST_Y are defined for
            // geometry only, which is the entire reason these columns exist.
            migrationBuilder.Sql("""
                UPDATE event_observations
                SET latitude  = ST_Y(epicenter::geometry),
                    longitude = ST_X(epicenter::geometry);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "latitude",
                table: "event_observations");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "event_observations");
        }
    }
}
