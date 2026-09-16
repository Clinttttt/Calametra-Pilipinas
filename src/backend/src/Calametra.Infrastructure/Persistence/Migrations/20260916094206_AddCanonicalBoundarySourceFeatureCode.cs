using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalBoundarySourceFeatureCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "osm_relation_id",
                table: "lgu_boundaries",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "source_feature_code",
                table: "lgu_boundaries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lgu_boundaries_source_feature_code",
                table: "lgu_boundaries",
                column: "source_feature_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lgu_boundaries_source_feature_code",
                table: "lgu_boundaries");

            migrationBuilder.DropColumn(
                name: "source_feature_code",
                table: "lgu_boundaries");

            migrationBuilder.AlterColumn<long>(
                name: "osm_relation_id",
                table: "lgu_boundaries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
