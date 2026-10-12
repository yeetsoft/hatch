using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class PlaybookProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types_Shape",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                schema: "hatch",
                table: "Playbooks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types_Shape_ProjectId",
                schema: "hatch",
                table: "Playbooks",
                columns: new[] { "FromStatusId", "ToStatusId", "Types", "Shape", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Playbooks_ProjectId",
                schema: "hatch",
                table: "Playbooks",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Playbooks_Projects_ProjectId",
                schema: "hatch",
                table: "Playbooks",
                column: "ProjectId",
                principalSchema: "hatch",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Playbooks_Projects_ProjectId",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.DropIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types_Shape_ProjectId",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.DropIndex(
                name: "IX_Playbooks_ProjectId",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.CreateIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types_Shape",
                schema: "hatch",
                table: "Playbooks",
                columns: new[] { "FromStatusId", "ToStatusId", "Types", "Shape" },
                unique: true);
        }
    }
}
