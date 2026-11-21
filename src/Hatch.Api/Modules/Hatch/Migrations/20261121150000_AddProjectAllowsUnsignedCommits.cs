using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAllowsUnsignedCommits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsUnsignedCommits",
                schema: "hatch",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AllowsUnsignedCommitsAt",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowsUnsignedCommitsBy",
                schema: "hatch",
                table: "Projects",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowsUnsignedCommits",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AllowsUnsignedCommitsAt",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AllowsUnsignedCommitsBy",
                schema: "hatch",
                table: "Projects");
        }
    }
}
