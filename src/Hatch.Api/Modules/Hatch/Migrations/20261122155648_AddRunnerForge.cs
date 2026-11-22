using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerForge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Forge",
                schema: "hatch",
                table: "RunnerSessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForgeWhy",
                schema: "hatch",
                table: "RunnerSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Forge",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "ForgeWhy",
                schema: "hatch",
                table: "RunnerSessions");
        }
    }
}
