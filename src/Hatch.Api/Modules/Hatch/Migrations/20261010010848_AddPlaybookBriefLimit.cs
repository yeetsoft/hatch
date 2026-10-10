using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A row's optional brief limit, in characters - see
    /// <see cref="EfHatchPlaybook.BriefLimit"/>. Nullable and defaultless: every
    /// existing row reads blank until a person sets one.
    /// </summary>
    public partial class AddPlaybookBriefLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BriefLimit",
                schema: "hatch",
                table: "Playbooks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BriefLimit",
                schema: "hatch",
                table: "Playbooks");
        }
    }
}
