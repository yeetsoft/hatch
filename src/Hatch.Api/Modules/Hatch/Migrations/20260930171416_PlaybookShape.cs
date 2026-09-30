using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class PlaybookShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.AddColumn<string>(
                name: "Shape",
                schema: "hatch",
                table: "Playbooks",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "any");

            migrationBuilder.CreateIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types_Shape",
                schema: "hatch",
                table: "Playbooks",
                columns: new[] { "FromStatusId", "ToStatusId", "Types", "Shape" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types_Shape",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.DropColumn(
                name: "Shape",
                schema: "hatch",
                table: "Playbooks");

            migrationBuilder.CreateIndex(
                name: "IX_Playbooks_FromStatusId_ToStatusId_Types",
                schema: "hatch",
                table: "Playbooks",
                columns: new[] { "FromStatusId", "ToStatusId", "Types" },
                unique: true);
        }
    }
}
