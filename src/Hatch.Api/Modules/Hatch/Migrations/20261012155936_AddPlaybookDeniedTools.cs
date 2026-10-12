using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A row's optional denied-tools list - see
    /// <see cref="EfHatchPlaybook.DeniedTools"/>. Nullable and defaultless: every
    /// existing row reads blank until a person sets one.
    /// </summary>
    public partial class AddPlaybookDeniedTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeniedTools",
                schema: "hatch",
                table: "Playbooks",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeniedTools",
                schema: "hatch",
                table: "Playbooks");
        }
    }
}
