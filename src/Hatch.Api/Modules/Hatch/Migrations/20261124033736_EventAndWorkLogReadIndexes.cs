using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class EventAndWorkLogReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_SettledPasses",
                schema: "hatch",
                table: "WorkLogEntries",
                column: "IssueId",
                filter: "\"IssueType\" = 'ambition' AND \"Outcome\" = 'worked' AND \"QuestionId\" IS NULL AND \"ChangeEventId\" IS NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "EndedAt", "PlaybookFromStatusId", "PlaybookToStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueEvents_Payload",
                schema: "hatch",
                table: "IssueEvents",
                column: "Payload",
                filter: "\"Kind\" = 'claim_released'")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogEntries_SettledPasses",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_IssueEvents_Payload",
                schema: "hatch",
                table: "IssueEvents");
        }
    }
}
