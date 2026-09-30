using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Replaces the two-value <c>Expedited</c> flag with the three-value
    /// <c>Priority</c> - see <see cref="EfHatchIssue.Priority"/> and
    /// <see cref="Hatch.Contracts.PriorityLevels"/>.
    ///
    /// <c>Down</c> collapses expedited and emergency back into one flag and
    /// the distinction between them is lost - that is expected of a rollback
    /// of a migration that adds a level, not a bug in this one.
    /// </summary>
    /// <inheritdoc />
    public partial class Priority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "hatch",
                table: "Issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""UPDATE hatch."Issues" SET "Priority" = 1 WHERE "Expedited" = true;""");

            migrationBuilder.DropColumn(
                name: "Expedited",
                schema: "hatch",
                table: "Issues");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Expedited",
                schema: "hatch",
                table: "Issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE hatch."Issues" SET "Expedited" = ("Priority" >= 1);""");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "hatch",
                table: "Issues");
        }
    }
}
