using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageReceivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UsageReceivedAt",
                schema: "hatch",
                table: "RunnerSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UsageReceivedAt",
                schema: "hatch",
                table: "LegacyRunners",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsageReceivedAt",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "UsageReceivedAt",
                schema: "hatch",
                table: "LegacyRunners");
        }
    }
}
