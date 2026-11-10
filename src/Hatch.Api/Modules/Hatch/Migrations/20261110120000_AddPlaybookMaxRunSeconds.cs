using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A row's optional run limit, in seconds - see
    /// <see cref="EfHatchPlaybook.MaxRunSeconds"/>. Nullable and defaultless:
    /// every existing row reads blank until a person sets one.
    /// </summary>
    public partial class AddPlaybookMaxRunSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxRunSeconds",
                schema: "hatch",
                table: "Playbooks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRunSeconds",
                schema: "hatch",
                table: "Playbooks");
        }
    }
}
