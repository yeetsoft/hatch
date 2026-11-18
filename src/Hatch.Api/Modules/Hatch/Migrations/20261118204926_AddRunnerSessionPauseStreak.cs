using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerSessionPauseStreak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PauseStreak",
                schema: "hatch",
                table: "RunnerSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PauseStreak",
                schema: "hatch",
                table: "RunnerSessions");
        }
    }
}
