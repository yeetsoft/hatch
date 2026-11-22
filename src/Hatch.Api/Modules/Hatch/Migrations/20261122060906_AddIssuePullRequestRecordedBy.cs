using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuePullRequestRecordedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PullRequestRecordedBy",
                schema: "hatch",
                table: "Issues",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PullRequestRecordedByKind",
                schema: "hatch",
                table: "Issues",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PullRequestRecordedBy",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "PullRequestRecordedByKind",
                schema: "hatch",
                table: "Issues");
        }
    }
}
