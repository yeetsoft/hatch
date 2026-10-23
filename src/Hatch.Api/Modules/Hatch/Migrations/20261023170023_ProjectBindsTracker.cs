using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class ProjectBindsTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BindsTracker",
                schema: "hatch",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BindsTrackerAt",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BindsTrackerBy",
                schema: "hatch",
                table: "Projects",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_BindsTracker",
                schema: "hatch",
                table: "Projects",
                column: "BindsTracker",
                unique: true,
                filter: "\"BindsTracker\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_BindsTracker",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BindsTracker",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BindsTrackerAt",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BindsTrackerBy",
                schema: "hatch",
                table: "Projects");
        }
    }
}
