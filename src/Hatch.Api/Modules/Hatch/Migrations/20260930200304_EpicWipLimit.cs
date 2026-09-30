using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class EpicWipLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WipLimit",
                schema: "hatch",
                table: "Issues",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WipLimit",
                schema: "hatch",
                table: "Issues");
        }
    }
}
