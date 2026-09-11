using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Materialises each place's coordinate as two columns beside its geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same change <c>AddObservationCoordinateColumns</c> made for readings, and for the same
    /// two reasons. PostGIS defines <c>ST_Y</c> and <c>ST_X</c> on <c>geometry</c> only, so a query
    /// projecting <c>Centroid.Y</c> against this <c>geography</c> column compiles and then fails at
    /// the database. And naming an epicentre after the nearest of 1,647 municipalities means
    /// reading the whole gazetteer per request, which as geometry measured at roughly 55 ms of
    /// point parsing.
    /// </para>
    /// <para>
    /// <b>The scaffolded version defaulted both columns to 0.0.</b> Applied as generated, every one
    /// of the 1,750 stored places would have been moved to 0°N 0°E — in the Gulf of Guinea — and
    /// the place explorer and the epicentre naming would then have been answering from those
    /// coordinates without any error being raised. The values are instead derived from the geometry
    /// already stored, which is the only source that can be right.
    /// </para>
    /// <para>
    /// Added nullable, backfilled, then constrained. The intermediate state exists for the length
    /// of one transaction and is what allows the column to end up <c>NOT NULL</c> with no default
    /// left behind: a lingering <c>DEFAULT 0</c> would let a later insert that bypassed the domain
    /// store a place off the coast of Africa silently.
    /// </para>
    /// </remarks>
    public partial class AddPlaceCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE places
                    ADD COLUMN latitude double precision,
                    ADD COLUMN longitude double precision;

                UPDATE places
                SET latitude = ST_Y(centroid::geometry),
                    longitude = ST_X(centroid::geometry);

                ALTER TABLE places
                    ALTER COLUMN latitude SET NOT NULL,
                    ALTER COLUMN longitude SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "latitude",
                table: "places");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "places");
        }
    }
}
