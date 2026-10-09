using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Shrinks the story-breakdown and analysis rows further, and gives the
    /// implementation row its first reword since it was seeded.
    /// </summary>
    /// <remarks>
    /// <para>Three rows change, each independently guarded and independently
    /// reversible, the way <see cref="RewordDiscreteTaskPlaybooks"/> touches three
    /// of its own: only a row whose <c>Prompt</c> is still byte-identical to its
    /// current guard text is rewritten. <c>Prompt</c> alone, and deliberately not
    /// <c>FromStatusId</c>, <c>ToStatusId</c>, <c>Types</c> or <c>Shape</c> - a row
    /// whose <c>Types</c> the operator has since narrowed is still reworded. An
    /// edited prompt is somebody's own wording and is left alone.</para>
    ///
    /// <para>Two of the three rows were already reworded once, by
    /// <see cref="RewordDiscreteTaskPlaybooks"/>, so the guard here is that row's
    /// live <c>Replaced</c> text, not the original 2026-09-03 seed - rewording the
    /// original a second time would miss every install, because the first
    /// migration ships before this one can. The third row, the implementation
    /// prompt, has never been reworded before, so its guard is the original seed
    /// at <c>20260903204217_Playbooks.cs</c>.</para>
    ///
    /// <para><b>Story breakdown</b> (<c>inbox</c>&#8594;<c>todo</c>, <c>story</c>):
    /// the acceptance-criteria bullet is capped at five, with a new bullet saying
    /// a story that needs more is an epic, retyped and broken into stories; the
    /// plan bullet moves the plan onto the tasks it is cut into, never into the
    /// story's own description. <b>Analysis</b> (<c>todo</c>&#8594;<c>in
    /// progress</c>, every type): the one task-cutting bullet splits into two -
    /// the files-by-path/pattern/tests detail, cut into tasks filed into the
    /// column the ticket is leaving; and a new bullet keeping the ticket's own
    /// description a brief, not the plan. <b>Implementation</b> (<c>in
    /// progress</c>&#8594;<c>review</c>, <c>story,task,bug</c>): a new bullet,
    /// second, says that a ticket whose children are all terminal or deferred is
    /// verified against the trunk criterion by criterion rather than implemented
    /// again.</para>
    /// </remarks>
    public partial class ShrinkStockPlaybooks : Migration
    {
        /// <summary>The story breakdown's acceptance-criteria bullet, as the live seeded text says it.</summary>
        public const string OldStoryCriteriaBullet = """
            - Write acceptance criteria as user-observable phenomena: an ordinal outline,
              one behaviour to a line, each an observable fact about the finished system
              rather than a task you intend to perform. "The board folds issues whose ready
              date has not arrived" is a criterion; "add folding logic" is not. Technical
              detail is not an acceptance criterion - it is a child task.
            """;

        /// <summary>What it says now - two bullets, not one.</summary>
        public const string NewStoryCriteriaBullets = """
            - Write at most five acceptance criteria, as user-observable phenomena: an
              ordinal outline, one behaviour to a line, each an observable fact about the
              finished system rather than a task you intend to perform. "The board folds
              issues whose ready date has not arrived" is a criterion; "add folding logic"
              is not. Technical detail is not an acceptance criterion - it is a task.
            - A story that still needs more than five is an epic: retype it (`PATCH` the
              issue's `type` to `epic`) and break it into stories under it instead, each
              with its own handful. Leave the files-and-pattern and the plan-onto-tasks
              bullets below for each of those stories to follow on its own turn through
              this same row - they do not apply to the epic itself.
            """;

        /// <summary>The story breakdown's plan bullet, as the live seeded text says it.</summary>
        public const string OldStoryPlanBullet = """
            - Write the implementation plan into the description, and cut it into tasks
              with parentKey set to this story: one task per self-contained change that
              could land on its own, more tasks rather than a longer story. A story small
              enough to be one session's work needs none.
            """;

        /// <summary>What it says now.</summary>
        public const string NewStoryPlanBullet = """
            - Write the implementation plan onto the tasks, not into the description: cut
              it into tasks with parentKey set to this story, one task per self-contained
              change that could land on its own, more tasks rather than a longer story. A
              story small enough to be one session's work needs none.
            """;

        /// <summary>The whole story breakdown prompt as it stands today, after <see cref="RewordDiscreteTaskPlaybooks"/>, and so as an unedited row still does.</summary>
        public const string StoryStock = """
            You are turning a captured idea into a story that can be picked up cold.

            - Read the issue's comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Say who it is for and what they get. The objective has to be concrete enough
              that somebody could disagree with it.
            - Write acceptance criteria as user-observable phenomena: an ordinal outline,
              one behaviour to a line, each an observable fact about the finished system
              rather than a task you intend to perform. "The board folds issues whose ready
              date has not arrived" is a criterion; "add folding logic" is not. Technical
              detail is not an acceptance criterion - it is a child task.
            - Name the files and the pattern to copy, by path, where you found them. Half of
              what makes a story cheap is that the next session does not search twice.
            - Write the implementation plan into the description, and cut it into tasks
              with parentKey set to this story: one task per self-contained change that
              could land on its own, more tasks rather than a longer story. A story small
              enough to be one session's work needs none.
            - A story with tasks is not implemented by its own session: the API carries
              its tasks across the board one at a time, and the story's own increment is
              the verification that they add up to it. Each task has to be complete on its
              own, and has to say what done looks like by itself.

            Research the repository as much as the criteria need to be accurate. Write no
            implementation code; that is a later increment's job.
            """;

        /// <summary>The same prompt with both bullets replaced.</summary>
        public static string StoryReplaced =>
            StoryStock
                .Replace(OldStoryCriteriaBullet, NewStoryCriteriaBullets, StringComparison.Ordinal)
                .Replace(OldStoryPlanBullet, NewStoryPlanBullet, StringComparison.Ordinal);

        /// <summary>The analysis prompt's one task-cutting bullet, as the live seeded text says it.</summary>
        public const string OldAnalysisPlanBullet = """
            - Write the implementation detail onto the ticket: the files that change, by
              path; the pattern file to copy; the shape of the tests; and the order the work
              goes in. Where the plan has real seams, cut it into child tasks carrying that
              detail, so that each is one thing done.
            """;

        /// <summary>What it says now - two bullets, not one.</summary>
        public const string NewAnalysisPlanBullets = """
            - Cut the plan into tasks, one seam to a task and one acceptance criterion to
              a task: the files it touches, by path; the pattern file to copy; the shape
              of its tests - each task complete on its own, a single session's work.
              Create each with parentKey set to this ticket, then move it into the column
              this ticket is leaving, not the one it is moving into, so the next pass
              finds it already specified. A ticket with no real seam needs none.
            - Keep this ticket's own description a brief: what done means and why, not
              the plan. The plan lives on the tasks it was cut into, or, where it needed
              none, is what is already written above.
            """;

        /// <summary>The whole analysis prompt as it stands today, after <see cref="RewordDiscreteTaskPlaybooks"/>, and so as an unedited row still does.</summary>
        public const string AnalysisStock = """
            You are doing the final analysis of a ticket that has already been specified,
            and leaving it in a state where implementing it is mechanical.

            The next increment writes the code. Everything it would otherwise have to decide
            - what done means, which files change, which pattern to copy, what the tests are
            - is yours to settle now and to write onto the ticket, because a decision taken
            with the code half-written is taken under pressure to make the code already
            written correct.

            If this issue has children, the next thing that happens is not code - its
            first child is pulled on its own. This increment's job is then to be sure the
            children are the whole plan and are in the right order, filing or reordering
            them now if they are not.

            - Read the issue's comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Research the repository until the plan is accurate rather than plausible. What
              the repository can answer, answer by reading the repository.
            - Sharpen the acceptance criteria until each is clear, concise and specific. On
              a story they are user-observable phenomena, an ordinal outline with one
              behaviour to a line; on a task or a bug they are one unified technical
              criterion. Technical detail is not an acceptance criterion.
            - Write the implementation detail onto the ticket: the files that change, by
              path; the pattern file to copy; the shape of the tests; and the order the work
              goes in. Where the plan has real seams, cut it into child tasks carrying that
              detail, so that each is one thing done.
            - Say what you checked and what you could not check. The reasoning belongs on
              the ticket, not in a session log nobody can read afterwards.

            Write no implementation code and make no commit in this increment. Nothing here
            changes the tree.
            """;

        /// <summary>The same prompt with the one bullet replaced by two.</summary>
        public static string AnalysisReplaced =>
            AnalysisStock.Replace(OldAnalysisPlanBullet, NewAnalysisPlanBullets, StringComparison.Ordinal);

        /// <summary>The implementation prompt's first bullet, the anchor the new one follows, as the seed used to say it.</summary>
        public const string OldImplementationAnchor = """
            aborted partway; if it ran to completion and only the status was left behind,
              say so and move it on.
            - Start on the branch the prompt's "The branch" section names, and cut none of
            """;

        /// <summary>What it says now - a new bullet in between.</summary>
        public const string NewImplementationAnchor = """
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
            """;

        /// <summary>The whole implementation prompt as it was seeded, and so as an unedited row still is - untouched since 2026-09-03.</summary>
        public const string ImplementationStock = """
            You are implementing a ticket that has already been analysed. The description is
            the brief, its acceptance criteria are the definition of done, and its child
            tasks are the plan.

            - Read what has already happened to it - its comments and its events - so that
              you continue the work rather than start it again. A previous run may have been
              aborted partway; if it ran to completion and only the status was left behind,
              say so and move it on.
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

        /// <summary>The same prompt with the new bullet inserted second.</summary>
        public static string ImplementationReplaced =>
            ImplementationStock.Replace(OldImplementationAnchor, NewImplementationAnchor, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Three independent guarded statements, one per row, each dollar-quoted
            // so neither prompt needs a character escaped and compared whole:
            // "byte-identical" is the guard, and a LIKE on the one sentence would
            // rewrite a prompt somebody had edited around it. No
            // FromStatusId/ToStatusId/Types/Shape condition on any of the three -
            // Prompt alone, so a row whose Types the operator has since narrowed is
            // still reworded.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{StoryReplaced}}$new$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $old${{StoryStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{AnalysisReplaced}}$new$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $old${{AnalysisStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{ImplementationReplaced}}$new$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $old${{ImplementationStock}}$old$;
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
                   SET "Prompt" = $old${{StoryStock}}$old$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $new${{StoryReplaced}}$new$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $old${{AnalysisStock}}$old$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $new${{AnalysisReplaced}}$new$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $old${{ImplementationStock}}$old$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $new${{ImplementationReplaced}}$new$;
                """);
        }
    }
}
