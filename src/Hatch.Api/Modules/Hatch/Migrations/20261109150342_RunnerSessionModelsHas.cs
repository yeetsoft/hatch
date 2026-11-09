using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerSessionModelsHas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Has",
                schema: "hatch",
                table: "RunnerSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Models",
                schema: "hatch",
                table: "RunnerSessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Has",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "Models",
                schema: "hatch",
                table: "RunnerSessions");
        }
    }
}
