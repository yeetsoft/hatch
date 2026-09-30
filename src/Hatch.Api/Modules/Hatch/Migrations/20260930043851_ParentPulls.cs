using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <remarks>
    /// Joins the seed on name, the way <c>StatusWip</c> does: <c>Backlog</c> is
    /// the stock board's column a parent's children stand in before a session
    /// picks one up, and a board that has renamed it is one whose operator gets
    /// to decide for themselves rather than one this migration guesses at.
    /// Guarded the same way - <c>NOT "IsDeferred" AND NOT "IsTerminal"</c> - so
    /// it never writes a flag <see cref="StatusesController.PutParentPulls"/>
    /// would itself refuse to a column that no longer counts as one.
    /// </remarks>
    public partial class ParentPulls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ParentPulls",
                schema: "hatch",
                table: "Statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE hatch."Statuses" SET "ParentPulls" = true
                 WHERE "Name" = 'Backlog' AND NOT "IsDeferred" AND NOT "IsTerminal";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentPulls",
                schema: "hatch",
                table: "Statuses");
        }
    }
}
