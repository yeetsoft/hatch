using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddReflectionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReflectionCadenceDays",
                schema: "hatch",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReflectionLastPeriodStart",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReflectionCadenceDays",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ReflectionLastPeriodStart",
                schema: "hatch",
                table: "Projects");
        }
    }
}
