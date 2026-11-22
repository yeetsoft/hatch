using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A playbook row can say it shapes an ambition: its session fills in a
    /// draft's title, description and capabilities, and cannot write code.
    /// The column is not null and false everywhere, so no existing row
    /// changes.
    /// </summary>
    public partial class AddPlaybookShapesAmbition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShapesAmbition",
                schema: "hatch",
                table: "Playbooks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShapesAmbition",
                schema: "hatch",
                table: "Playbooks");
        }
    }
}
