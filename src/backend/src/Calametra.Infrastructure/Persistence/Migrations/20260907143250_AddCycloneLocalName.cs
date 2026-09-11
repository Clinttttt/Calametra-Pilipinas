using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycloneLocalName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "local_name",
                table: "hazard_events",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_hazard_events_type_name",
                table: "hazard_events",
                columns: new[] { "type", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_hazard_events_type_name",
                table: "hazard_events");

            migrationBuilder.DropColumn(
                name: "local_name",
                table: "hazard_events");
        }
    }
}
