using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeLiveLinkUniquenessToActiveClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lgu_code_links_lgu_id_historical_psgc_code",
                table: "lgu_code_links");

            migrationBuilder.CreateIndex(
                name: "ux_lgu_code_links_live_per_unit_and_code",
                table: "lgu_code_links",
                columns: new[] { "lgu_id", "historical_psgc_code" },
                unique: true,
                filter: "status <> 'Superseded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_lgu_code_links_live_per_unit_and_code",
                table: "lgu_code_links");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_code_links_lgu_id_historical_psgc_code",
                table: "lgu_code_links",
                columns: new[] { "lgu_id", "historical_psgc_code" },
                unique: true);
        }
    }
}
