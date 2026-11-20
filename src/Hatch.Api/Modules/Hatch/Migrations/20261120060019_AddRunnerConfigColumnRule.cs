using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerConfigColumnRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatusIds",
                schema: "hatch",
                table: "RunnerConfigs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "StatusMode",
                schema: "hatch",
                table: "RunnerConfigs",
                type: "text",
                nullable: false,
                defaultValue: "all");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusIds",
                schema: "hatch",
                table: "RunnerConfigs");

            migrationBuilder.DropColumn(
                name: "StatusMode",
                schema: "hatch",
                table: "RunnerConfigs");
        }
    }
}
