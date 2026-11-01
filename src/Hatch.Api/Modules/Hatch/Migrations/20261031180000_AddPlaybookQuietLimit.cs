using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A row's optional quiet limit, in seconds - see
    /// <see cref="EfHatchPlaybook.QuietLimitSeconds"/>. Nullable and defaultless:
    /// every existing row reads blank until a person sets one.
    /// </summary>
    public partial class AddPlaybookQuietLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuietLimitSeconds",
                schema: "hatch",
                table: "Playbooks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuietLimitSeconds",
                schema: "hatch",
                table: "Playbooks");
        }
    }
}
