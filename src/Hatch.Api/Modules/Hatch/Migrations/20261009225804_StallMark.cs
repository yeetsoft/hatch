using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <remarks>
    /// Adds the three columns and gates nothing - no seed, the same shape as
    /// <c>20261009213335_StatusIsImplementation.cs</c>. A field nothing sets
    /// and nothing reads changes no board until HA-351 and HA-352 land.
    /// </remarks>
    public partial class StallMark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StalledAt",
                schema: "hatch",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StalledWhy",
                schema: "hatch",
                table: "Issues",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Held",
                schema: "hatch",
                table: "Issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StalledAt",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "StalledWhy",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Held",
                schema: "hatch",
                table: "Issues");
        }
    }
}
