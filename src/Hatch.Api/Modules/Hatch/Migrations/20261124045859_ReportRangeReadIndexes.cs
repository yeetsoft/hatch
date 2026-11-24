using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class ReportRangeReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_EndedAt",
                schema: "hatch",
                table: "WorkLogEntries",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_FailedSessions",
                schema: "hatch",
                table: "WorkLogEntries",
                column: "CreatedAt",
                filter: "\"IsError\"");

            migrationBuilder.CreateIndex(
                name: "IX_IssueEvents_IssueId_Kind_At",
                schema: "hatch",
                table: "IssueEvents",
                columns: new[] { "IssueId", "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueEvents_Kind_At",
                schema: "hatch",
                table: "IssueEvents",
                columns: new[] { "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Answers",
                schema: "hatch",
                table: "Comments",
                column: "CreatedAt",
                filter: "\"AnswersId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Questions",
                schema: "hatch",
                table: "Comments",
                column: "CreatedAt",
                filter: "\"Kind\" = 'question'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogEntries_EndedAt",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_WorkLogEntries_FailedSessions",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_IssueEvents_IssueId_Kind_At",
                schema: "hatch",
                table: "IssueEvents");

            migrationBuilder.DropIndex(
                name: "IX_IssueEvents_Kind_At",
                schema: "hatch",
                table: "IssueEvents");

            migrationBuilder.DropIndex(
                name: "IX_Comments_Answers",
                schema: "hatch",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_Questions",
                schema: "hatch",
                table: "Comments");
        }
    }
}
