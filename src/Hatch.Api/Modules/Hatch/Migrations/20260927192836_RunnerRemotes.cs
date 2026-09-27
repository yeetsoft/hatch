using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerRemotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Clones",
                schema: "hatch",
                table: "Runners",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remotes",
                schema: "hatch",
                table: "Runners",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Clones",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Remotes",
                schema: "hatch",
                table: "Runners");
        }
    }
}
