using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The issue whose session filed this one, as a nullable self-reference
    /// that a deleted source clears - see
    /// <see cref="Hatch.Api.Modules.Hatch.EfHatchIssue.FiledFromId"/>.
    /// Every existing issue reads blank until something sets one.
    /// </summary>
    public partial class AddIssueFiledFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FiledFromId",
                schema: "hatch",
                table: "Issues",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_FiledFromId",
                schema: "hatch",
                table: "Issues",
                column: "FiledFromId");

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_Issues_FiledFromId",
                schema: "hatch",
                table: "Issues",
                column: "FiledFromId",
                principalSchema: "hatch",
                principalTable: "Issues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Issues_Issues_FiledFromId",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_FiledFromId",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "FiledFromId",
                schema: "hatch",
                table: "Issues");
        }
    }
}
