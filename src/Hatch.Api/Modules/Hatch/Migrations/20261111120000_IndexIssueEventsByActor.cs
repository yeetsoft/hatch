using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class IndexIssueEventsByActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IssueEvents_Actor_At",
                schema: "hatch",
                table: "IssueEvents",
                columns: new[] { "Actor", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssueEvents_Actor_At",
                schema: "hatch",
                table: "IssueEvents");
        }
    }
}
