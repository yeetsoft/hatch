using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock implementation playbook - the one row whose <c>Prompt</c> is
    /// still byte-for-byte <see cref="ShrinkStockPlaybooks.ImplementationReplaced"/>
    /// - gets a starting brief limit of eight thousand characters. Every other
    /// row, breakdown, analysis, closeout and review alike, is left at the
    /// <c>null</c> a freshly-added nullable column already gives it; nobody has
    /// tuned a number for them yet.
    /// </summary>
    /// <remarks>
    /// Guarded on <c>Prompt</c> alone, the way <see cref="SeedImplementationPlaybookBudget"/>
    /// guards its own row: not on <c>FromStatusId</c>, <c>ToStatusId</c>, <c>Types</c>
    /// or <c>Shape</c>, because an operator may have already narrowed this row's
    /// <c>Types</c> by hand, the way the live board has - and that scoping is no
    /// reason to leave the row unlimited. An edited prompt is somebody's own
    /// wording and is left alone.
    ///
    /// The guard text is <see cref="ShrinkStockPlaybooks.ImplementationReplaced"/>,
    /// the prompt as it stands live today, not the original 2026-09-03 seed:
    /// that text has already been superseded once, so guarding on the stale
    /// original would seed nothing on every install. <see cref="Stock"/> is its
    /// own copy of that live text, the same way
    /// <see cref="SeedImplementationPlaybookBudget.Stock"/> is its own copy -
    /// not a reference, so this migration keeps reading the prompt it shipped
    /// against even if a later migration rewords it again.
    ///
    /// <see cref="Down"/> is the mirror of every other seed migration's
    /// reversible guard: it clears the brief limit only on a row that still reads
    /// both the stock <c>Prompt</c> and the exact <c>BriefLimit</c> this migration
    /// wrote, so a value a person has since changed is left as they set it.
    /// </remarks>
    public partial class SeedImplementationPlaybookBriefLimit : Migration
    {
        /// <summary>The brief limit this migration sets, in characters.</summary>
        public const int BriefLimit = 8000;

        /// <summary>The whole implementation prompt as it stands live today, and so as an unedited row still does.</summary>
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, so the prompt needs no character escaped and is
            // compared whole: "byte-identical" is the guard, and a LIKE on one
            // sentence would catch a prompt somebody had edited around it.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "BriefLimit" = {{BriefLimit}},
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $prompt${{Stock}}$prompt$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row that still carries both the
            // stock text and the exact value this migration wrote is cleared -
            // a brief limit a person has since changed is left as they set it.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "BriefLimit" = NULL,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $prompt${{Stock}}$prompt$
                   AND "BriefLimit" = {{BriefLimit}};
                """);
        }
    }
}
