using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegisterAcquisitionAndSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acquisition_note",
                table: "psgc_register_editions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_sha256",
                table: "psgc_register_editions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_file_name",
                table: "psgc_register_editions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "publication_date",
                table: "psgc_register_editions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "superseded_at",
                table: "psgc_register_editions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_edition_id",
                table: "psgc_register_editions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "proposed_against_edition_id",
                table: "lgu_code_links",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_psgc_register_editions_file_sha256",
                table: "psgc_register_editions",
                column: "file_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_psgc_register_editions_superseded_at",
                table: "psgc_register_editions",
                column: "superseded_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_psgc_register_editions_hash_is_hex",
                table: "psgc_register_editions",
                sql: "file_sha256 IS NULL OR file_sha256 ~ '^[0-9a-f]{64}$'");

            migrationBuilder.CreateIndex(
                name: "ix_lgu_code_links_proposed_against_edition_id",
                table: "lgu_code_links",
                column: "proposed_against_edition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_psgc_register_editions_file_sha256",
                table: "psgc_register_editions");

            migrationBuilder.DropIndex(
                name: "ix_psgc_register_editions_superseded_at",
                table: "psgc_register_editions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_psgc_register_editions_hash_is_hex",
                table: "psgc_register_editions");

            migrationBuilder.DropIndex(
                name: "ix_lgu_code_links_proposed_against_edition_id",
                table: "lgu_code_links");

            migrationBuilder.DropColumn(
                name: "acquisition_note",
                table: "psgc_register_editions");

            migrationBuilder.DropColumn(
                name: "file_sha256",
                table: "psgc_register_editions");

            migrationBuilder.DropColumn(
                name: "original_file_name",
                table: "psgc_register_editions");

            migrationBuilder.DropColumn(
                name: "publication_date",
                table: "psgc_register_editions");

            migrationBuilder.DropColumn(
                name: "superseded_at",
                table: "psgc_register_editions");

            migrationBuilder.DropColumn(
                name: "superseded_by_edition_id",
                table: "psgc_register_editions");

            migrationBuilder.DropColumn(
                name: "proposed_against_edition_id",
                table: "lgu_code_links");
        }
    }
}
