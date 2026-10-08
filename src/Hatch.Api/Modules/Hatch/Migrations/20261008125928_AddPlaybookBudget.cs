using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A row's optional thinking budget, in millions of tokens - see
    /// <see cref="EfHatchPlaybook.Budget"/>. Nullable and defaultless: every
    /// existing row reads blank until a person sets one.
    /// </summary>
    public partial class AddPlaybookBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Budget",
                schema: "hatch",
                table: "Playbooks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Budget",
                schema: "hatch",
                table: "Playbooks");
        }
    }
}
