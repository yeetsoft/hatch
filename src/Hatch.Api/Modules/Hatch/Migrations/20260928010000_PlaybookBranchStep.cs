using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock implementation playbook stops telling a session to cut its own
    /// branch from <c>origin/main</c>.
    /// </summary>
    /// <remarks>
    /// <para>The runner now does the git side of an increment: it puts the tree on
    /// the issue's branch with the trunk merged in, or on the trunk with a name to
    /// cut, and says which in the prompt's <c>## The branch</c> section. A playbook
    /// that still says "fetch first, then cut a branch from <c>origin/main</c>"
    /// contradicts that on exactly the tickets it matters for - one that has a
    /// branch, which a session following the sentence would abandon.</para>
    ///
    /// <para>Only a row whose <c>Prompt</c> is still byte-identical to the seed is
    /// touched, the way <c>TheFlow</c> only touches a board that is still the one
    /// the earlier migrations shipped. <c>Prompt</c> alone, and deliberately not
    /// <c>Types</c>: an operator who narrowed which issue types a playbook covers
    /// has not touched the sentence being replaced, and that scoping is no reason
    /// to leave the old instruction in place. An edited prompt is somebody's
    /// wording, and this does not rewrite it - the operator is told on the ticket
    /// what to change.</para>
    ///
    /// <para>The seed in <c>Playbooks</c> carries the new bullet too, which is what a
    /// fresh install gets; <see cref="Stock"/> here is the text as it used to be, so
    /// it does not match there and does nothing.</para>
    /// </remarks>
    public partial class PlaybookBranchStep : Migration
    {
        /// <summary>The bullet as the seed used to say it.</summary>
        public const string OldBullet = """
            - Branch from the remote, not from local main: fetch first, then cut
              <key-lowercased>-<short-slug> from origin/main. Another session may share this
              tree, and a branch cut from a local main carries their unpushed commits into
              your push.
            """;

        /// <summary>What it says now.</summary>
        public const string NewBullet = """
            - Start on the branch the prompt's "The branch" section names, and cut none of
              your own unless it tells you to: the runner has already put the tree on the
              issue's branch with the trunk merged in, or on the trunk with a name to cut,
              and that section overrides anything said here about where to branch from.
            """;

        /// <summary>The whole implementation prompt as it was seeded, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are implementing a ticket that has already been analysed. The description is
            the brief, its acceptance criteria are the definition of done, and its child
            tasks are the plan.

            - Read what has already happened to it - its comments and its events - so that
              you continue the work rather than start it again. A previous run may have been
              aborted partway; if it ran to completion and only the status was left behind,
              say so and move it on.
            - Branch from the remote, not from local main: fetch first, then cut
              <key-lowercased>-<short-slug> from origin/main. Another session may share this
              tree, and a branch cut from a local main carries their unpushed commits into
              your push.
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
        public static string Replaced => Stock.Replace(OldBullet, NewBullet, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, so neither prompt needs a character escaped, and
            // compared whole: "byte-identical" is the guard, and a LIKE on the
            // one sentence would rewrite a prompt somebody had edited around it.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{Replaced}}$new$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $old${{Stock}}$old$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row that still says exactly what
            // this migration wrote is put back.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $old${{Stock}}$old$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $new${{Replaced}}$new$;
                """);
        }
    }
}
