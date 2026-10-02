using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Makes room for <c>Low</c> between <c>Normal</c> and <c>Economy</c> - see
    /// <see cref="Hatch.Contracts.PriorityLevels"/> - by shifting everything
    /// already below normal down one: economy from -1 to -2, paused from -2 to
    /// -3. The column itself does not change; normal stays 0.
    ///
    /// <c>Down</c> cannot tell a row that was already economy from one that was
    /// moved down to make room for low, so it puts every row at -1 back to
    /// economy rather than restoring a now-nonexistent low. That is the only
    /// lossy part of the rollback, and it is one-directional: a low issue
    /// landing at economy can only make the dispatcher spend less on it, not
    /// more.
    /// </summary>
    /// <inheritdoc />
    public partial class Low : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One set-based statement, so -1 -> -2 and -2 -> -3 cannot collide.
            migrationBuilder.Sql("""UPDATE hatch."Issues" SET "Priority" = "Priority" - 1 WHERE "Priority" < 0;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE hatch."Issues" SET "Priority" = "Priority" + 1 WHERE "Priority" < -1;""");
        }
    }
}
