using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFilesContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FilesContributions",
                schema: "hatch",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FilesContributionsAt",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilesContributionsBy",
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
                name: "FilesContributions",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FilesContributionsAt",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FilesContributionsBy",
                schema: "hatch",
                table: "Projects");
        }
    }
}
