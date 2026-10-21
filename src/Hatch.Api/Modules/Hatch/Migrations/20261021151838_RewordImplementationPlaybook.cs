using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the implementation row to render a changed screen in the headless
    /// driver instead of leaving all browser verification to the operator.
    /// </summary>
    /// <remarks>
    /// <para>The row's green-before-pushed bullet said the operator does all
    /// browser and UI verification. With the headless driver landed and
    /// documented (<c>docs/browser-testing.md</c>), that line is wrong: a
    /// session that changes a screen can render it itself and say what it saw,
    /// rather than leaving the operator to find out whether the screen renders
    /// at all.</para>
    ///
    /// <para>This is the first reword since the prescribed-text split
    /// (<see cref="SeedPlaybookDefaultPrompt"/>), so it guards and writes
    /// <c>DefaultPrompt</c> alone - unlike <see cref="ShrinkStockPlaybooks"/>,
    /// which rewrote <c>Prompt</c> before the split moved stock wording there.
    /// A row whose <c>DefaultPrompt</c> is byte-identical to the stock text has
    /// nobody's wording in it; an edited row's <c>Prompt</c> is left alone, and
    /// a row that has handed its wording back learns the new default the next
    /// time the Playbooks page reads it, the way <see cref="SeedPlaybookDefaultPrompt"/>'s
    /// own remarks describe. <c>UpdatedAt</c> is not touched: learning a default
    /// is not an edit, the same precedent.</para>
    /// </remarks>
    public partial class RewordImplementationPlaybook : Migration
    {
        /// <summary>The green-before-pushed bullet, as the live default text says it.</summary>
        public const string OldGreenBullet = """
            - Green before pushed: lint, build, tests. The operator does all browser and UI
              verification, so never claim a screen works - only that it builds. If
              something is red and you cannot fix it, stop, comment what you found, and do
              not push.
            """;

        /// <summary>What it says now.</summary>
        public const string NewGreenBullet = """
            - Green before pushed: lint, build, tests. A change to a screen is rendered in
              the prescribed headless driver (docs/browser-testing.md) and what was seen
              goes on the ticket; never claim a screen works from the build alone. If
              something is red and you cannot fix it, stop, comment what you found, and do
              not push.
            """;

        /// <summary>The whole implementation prompt as it stands today, a frozen copy of <see cref="SeedPlaybookDefaultPrompt.Implementation"/>.</summary>
        public const string Stock = """
            You are implementing a ticket that has already been analysed. The description is
            the brief, its acceptance criteria are the definition of done, and its child
            tasks are the plan.

            - Read what has already happened to it - its comments and its events - so that
              you continue the work rather than start it again. A previous run may have been
              aborted partway; if it ran to completion and only the status was left behind,
              say so and move it on.
            - If every child task already stands in a terminal or a deferred column, this
              ticket's work is already done, task by task: read each task's own comments
              and pull request, check this ticket's acceptance criteria one at a time
              against the trunk at the sha its tasks left it on, naming the task and the
              commit or pull request that met each. Comment the verdict, open the pull
              request if none of the tasks did or record the one that stands with
              `hatch pr`, and move this ticket on. A gap becomes a new task filed under
              this ticket, with parentKey, not a fix made here. Do not implement it
              again.
            - Start on the branch the prompt's "The branch" section names, and cut none of
              your own unless it tells you to: the runner has already put the tree on the
              issue's branch with the trunk merged in, or on the trunk with a name to cut,
              and that section overrides anything said here about where to branch from.
            - Work the child tasks in order, testing and committing along the way as it
              makes sense to. Read the pattern file the ticket names before writing the
              thing it patterns.
            - Build with make (make build, make test-api, make test-web), never a bare
              dotnet - the npm step needs the shell profile. No hardcoded domains,
              addresses, hostnames or people anywhere, including in comments.
            - Green before pushed: lint, build, tests. The operator does all browser and UI
              verification, so never claim a screen works - only that it builds. If
              something is red and you cannot fix it, stop, comment what you found, and do
              not push.
            - Check the acceptance criteria one at a time and say which are met. A criterion
              you cannot verify is one to say out loud you cannot verify, not one to quietly
              count.
            - Commit and push, the subject in house style: an area, then what changed, as a
              sentence. If there is no code change, make no commit - say what you found
              instead.
            - Comment the summary a reviewer needs: the branch, the sha, what changed, what
              to look at first, what did not land, and anything you are unsure about. If you
              opened a pull request, record it on the issue with hatch pr.
            - Move the child tasks you worked along with the issue. A task left standing in
              the column it started in reads as work nobody did.
            """;

        /// <summary>The same prompt with the one bullet replaced.</summary>
        public static string Replaced =>
            Stock.Replace(OldGreenBullet, NewGreenBullet, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DefaultPrompt alone, not Prompt: the first reword since the
            // prescribed-text split, so there is no row's own wording to
            // disturb and no UpdatedAt to bump - learning a default is not an
            // edit.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{Replaced}}$new$
                 WHERE "DefaultPrompt" = $old${{Stock}}$old$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{Stock}}$old$
                 WHERE "DefaultPrompt" = $new${{Replaced}}$new$;
                """);
        }
    }
}
