using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class WorkLogRequestsAndPeakContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PeakContextTokens",
                schema: "hatch",
                table: "WorkLogEntries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptChars",
                schema: "hatch",
                table: "WorkLogEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Requests",
                schema: "hatch",
                table: "WorkLogEntries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeakContextTokens",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "PromptChars",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "Requests",
                schema: "hatch",
                table: "WorkLogEntries");
        }
    }
}
