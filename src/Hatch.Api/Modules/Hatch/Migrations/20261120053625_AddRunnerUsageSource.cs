using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerUsageSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsageProbeOff",
                schema: "hatch",
                table: "RunnerSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageSource",
                schema: "hatch",
                table: "RunnerSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageProbeOff",
                schema: "hatch",
                table: "LegacyRunners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsageSource",
                schema: "hatch",
                table: "LegacyRunners",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsageProbeOff",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "UsageSource",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "UsageProbeOff",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "UsageSource",
                schema: "hatch",
                table: "LegacyRunners");
        }
    }
}
