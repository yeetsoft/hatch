using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                schema: "hatch",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriorityAt",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriorityBy",
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
                name: "Priority",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PriorityAt",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PriorityBy",
                schema: "hatch",
                table: "Projects");
        }
    }
}
