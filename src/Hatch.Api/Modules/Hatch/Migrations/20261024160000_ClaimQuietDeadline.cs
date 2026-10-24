using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class ClaimQuietDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimQuietAt",
                schema: "hatch",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClaimLapseLimit",
                schema: "hatch",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StallGraceMaxMinutes",
                schema: "hatch",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StallLapseMinutes",
                schema: "hatch",
                table: "Projects",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ClaimQuietAt", schema: "hatch", table: "Issues");
            migrationBuilder.DropColumn(name: "ClaimLapseLimit", schema: "hatch", table: "Projects");
            migrationBuilder.DropColumn(name: "StallGraceMaxMinutes", schema: "hatch", table: "Projects");
            migrationBuilder.DropColumn(name: "StallLapseMinutes", schema: "hatch", table: "Projects");
        }
    }
}
