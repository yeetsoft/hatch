using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Freezes the bugs that were already split into tasks, now that a bug can
    /// carry the one-pull-request setting a story does.
    /// </summary>
    /// <remarks>
    /// <para>Until now only a story could, so a bug's tasks were always planned and
    /// begun on branches of their own. A bug reads its project's default the way
    /// a story with no override does, so a project whose default is on would
    /// move that in-flight work onto the bug's branch the moment this shipped.
    /// A bug with children is given an explicit <c>false</c> instead, which is
    /// what it has been doing. A bug with no children, and every story, stays
    /// null: a bug filed from here on follows the project's default as a story
    /// does.</para>
    ///
    /// <para>No events are written. No migration here writes them, and the
    /// setting did not change in any way the trail has ever recorded - these bugs
    /// were never able to share. Down puts them back to null; an override
    /// refused on a bug until now, so none can be <c>false</c> by any other
    /// route than this one or a person's later choice, which Down also returns
    /// to "inherit".</para>
    /// </remarks>
    public partial class FreezeBugOnePr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE hatch."Issues"
                   SET "OnePullRequest" = false
                 WHERE "Type" = 'bug'
                   AND "OnePullRequest" IS NULL
                   AND EXISTS (SELECT 1 FROM hatch."Issues" c WHERE c."ParentId" = "Issues"."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE hatch."Issues"
                   SET "OnePullRequest" = NULL
                 WHERE "Type" = 'bug'
                   AND "OnePullRequest" = false;
                """);
        }
    }
}
