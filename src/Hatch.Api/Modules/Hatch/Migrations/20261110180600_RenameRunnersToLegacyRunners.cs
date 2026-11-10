using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RenameRunnersToLegacyRunners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Runners",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.RenameTable(
                name: "Runners",
                schema: "hatch",
                newName: "LegacyRunners",
                newSchema: "hatch");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AdoptedAt",
                schema: "hatch",
                table: "LegacyRunners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdoptedInto",
                schema: "hatch",
                table: "LegacyRunners",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LegacyRunners",
                schema: "hatch",
                table: "LegacyRunners",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LegacyRunners",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "AdoptedAt",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "AdoptedInto",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.RenameTable(
                name: "LegacyRunners",
                schema: "hatch",
                newName: "Runners",
                newSchema: "hatch");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Runners",
                schema: "hatch",
                table: "Runners",
                column: "Name");
        }
    }
}
