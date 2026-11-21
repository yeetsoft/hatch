using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkLogQuestionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "QuestionId",
                schema: "hatch",
                table: "WorkLogEntries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_QuestionId",
                schema: "hatch",
                table: "WorkLogEntries",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkLogEntries_QuestionId",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                schema: "hatch",
                table: "WorkLogEntries");
        }
    }
}
