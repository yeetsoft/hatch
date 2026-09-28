using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The two flags the night train reads: <c>Express</c> on an issue and
    /// <c>ExpressSkips</c> on a column - see <see cref="EfHatchIssue.Express"/>
    /// and <see cref="EfHatchStatus.ExpressSkips"/>.
    ///
    /// <c>ExpressSkips</c> is seeded true on <c>Backlog</c>, and only on a
    /// board that is still exactly the stock seven columns <c>TheFlow</c>
    /// shipped: the guard is the same shape as that migration's, checked
    /// against its <c>Down</c>'s array rather than its <c>Up</c>'s, because
    /// that is the one that names the seven-column board this migration needs
    /// to check against. An install whose board has been arranged by hand has
    /// already answered the question a guessed tick would be asking, and their
    /// answer stands.
    /// </summary>
    /// <inheritdoc />
    public partial class Express : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExpressSkips",
                schema: "hatch",
                table: "Statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Express",
                schema: "hatch",
                table: "Issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
            DO $express$
            BEGIN
                IF (SELECT array_agg("Name" || CASE WHEN "IsTerminal" THEN ' (terminal)' ELSE '' END
                                     ORDER BY "SortOrder", "Id")
                    FROM hatch."Statuses")
                   IS DISTINCT FROM
                   ARRAY['Draft', 'Breakdown', 'Backlog', 'To Do', 'In Progress', 'In Review', 'Done (terminal)']
                THEN
                    RETURN;
                END IF;

                UPDATE hatch."Statuses" SET "ExpressSkips" = true WHERE "Name" = 'Backlog';
            END
            $express$;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpressSkips",
                schema: "hatch",
                table: "Statuses");

            migrationBuilder.DropColumn(
                name: "Express",
                schema: "hatch",
                table: "Issues");
        }
    }
}
