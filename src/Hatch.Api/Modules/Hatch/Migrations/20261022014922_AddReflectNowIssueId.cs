using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddReflectNowIssueId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReflectNowIssueId",
                schema: "hatch",
                table: "Projects",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ReflectNowIssueId",
                schema: "hatch",
                table: "Projects",
                column: "ReflectNowIssueId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Issues_ReflectNowIssueId",
                schema: "hatch",
                table: "Projects",
                column: "ReflectNowIssueId",
                principalSchema: "hatch",
                principalTable: "Issues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Issues_ReflectNowIssueId",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_ReflectNowIssueId",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ReflectNowIssueId",
                schema: "hatch",
                table: "Projects");
        }
    }
}
