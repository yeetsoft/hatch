using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ForPersonId",
                schema: "hatch",
                table: "Runners",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Usage",
                schema: "hatch",
                table: "Runners",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UsageReadAt",
                schema: "hatch",
                table: "Runners",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForPersonId",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Usage",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "UsageReadAt",
                schema: "hatch",
                table: "Runners");
        }
    }
}
