using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerLauncherVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LauncherVersion",
                schema: "hatch",
                table: "RunnerSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LauncherVersion",
                schema: "hatch",
                table: "LegacyRunners",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LauncherVersion",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "LauncherVersion",
                schema: "hatch",
                table: "LegacyRunners");
        }
    }
}
