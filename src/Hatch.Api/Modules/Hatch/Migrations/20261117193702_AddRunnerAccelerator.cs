using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerAccelerator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceleratorBackend",
                schema: "hatch",
                table: "RunnerSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcceleratorDeviceCount",
                schema: "hatch",
                table: "RunnerSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AcceleratorMemoryBytes",
                schema: "hatch",
                table: "RunnerSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceleratorBackend",
                schema: "hatch",
                table: "LegacyRunners",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcceleratorDeviceCount",
                schema: "hatch",
                table: "LegacyRunners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AcceleratorMemoryBytes",
                schema: "hatch",
                table: "LegacyRunners",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceleratorBackend",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "AcceleratorDeviceCount",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "AcceleratorMemoryBytes",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "AcceleratorBackend",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "AcceleratorDeviceCount",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "AcceleratorMemoryBytes",
                schema: "hatch",
                table: "LegacyRunners");
        }
    }
}
