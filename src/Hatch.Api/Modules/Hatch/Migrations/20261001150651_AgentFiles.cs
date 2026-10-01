using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <remarks>
    /// Joins the seed on name, the way <c>ParentPulls</c> does: <c>Backlog</c>
    /// is the stock board's column a program's issues are born in before
    /// anything reads this flag, and a board that has renamed it is one whose
    /// operator gets to decide for themselves rather than one this migration
    /// guesses at. Guarded the same way - <c>NOT "IsDeferred" AND NOT
    /// "IsTerminal"</c> - so it never writes a flag
    /// <see cref="StatusesController.PutAgentFiles"/> would itself refuse to a
    /// column that no longer counts as one.
    /// </remarks>
    public partial class AgentFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AgentFiles",
                schema: "hatch",
                table: "Statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE hatch."Statuses" SET "AgentFiles" = true
                 WHERE "Name" = 'Backlog' AND NOT "IsDeferred" AND NOT "IsTerminal";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentFiles",
                schema: "hatch",
                table: "Statuses");
        }
    }
}
