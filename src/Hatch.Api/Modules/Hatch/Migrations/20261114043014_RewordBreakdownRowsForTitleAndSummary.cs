using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the three stock rows that turn an idea into an epic, a story or a
    /// task to ask for a title of a handful of words and a plain summary, the
    /// way <c>docs/issues.md</c>'s "How a ticket reads" now spells out.
    /// </summary>
    /// <remarks>
    /// <para>Three rows change, each independently guarded and independently
    /// reversible, the way <see cref="ShrinkStockPlaybooks"/> touches three of
    /// its own. Each keeps its existing bullet's subject and gains the
    /// title-and-summary rule in the same place. Like
    /// <see cref="RewordFinalAnalysisPlaybook"/> and
    /// <see cref="RewordEpicBreakdownForDreams"/> this guards and writes
    /// <c>DefaultPrompt</c> alone on all three rows: an operator's own
    /// <c>Prompt</c> is left untouched and <c>UpdatedAt</c> never moves -
    /// learning a default is not an edit.</para>
    ///
    /// <para>The epic row's stock text is not
    /// <see cref="SeedPlaybookDefaultPrompt.EpicBreakdown"/>: it has since
    /// learned to search the Dream Board
    /// (<see cref="RewordEpicBreakdownForDreams"/>) and had its
    /// <c>/api/hatch/</c> calls rewritten to <c>/api/</c>
    /// (<see cref="RewriteApiPrefixInPlaybooks"/>), so its guard here is a
    /// frozen copy of that live text. The story and task rows have never been
    /// reworded since they were seeded, so their guards are frozen copies of
    /// <see cref="SeedPlaybookDefaultPrompt.StoryBreakdown"/> and
    /// <see cref="SeedPlaybookDefaultPrompt.TaskBreakdown"/>.</para>
    /// </remarks>
    public partial class RewordBreakdownRowsForTitleAndSummary : Migration
    {
        /// <summary>The epic row's overview-and-architecture bullet, as the live default text says it.</summary>
        public const string OldEpicBullet = """
            - Rewrite the description as the epic's own overview and architecture document:
              what this is for, what done looks like, the decisions taken and the ones
              rejected with the reason, and the constraints that bound it.
            """;

        /// <summary>What it says now: a title-and-summary rule before the architecture document.</summary>
        public const string NewEpicBullet = """
            - Give it a title of a handful of words, the thing itself and not a coined name
              for it - replace one that still reads `Weave: ...`. Open the description with
              a summary, at most three short paragraphs, before any heading: what this is
              for and what done looks like. Put the architecture, the decisions taken and
              the ones rejected with the reason, and the constraints that bound it, after
              it. `src/Hatch.Web/apps/hatch/docs/issues.md`, under "How a ticket reads", has
              the rules in full.
            """;

        /// <summary>
        /// The whole epic-breakdown prompt as it stands today: a frozen copy of
        /// <see cref="RewordEpicBreakdownForDreams.Replaced"/> with every
        /// <c>/api/hatch/</c> rewritten to <c>/api/</c>
        /// (<see cref="RewriteApiPrefixInPlaybooks"/>) - not
        /// <see cref="SeedPlaybookDefaultPrompt.EpicBreakdown"/>, which is frozen at the
        /// pre-rewrite, pre-dream-board text.
        /// </summary>
        public const string EpicStock = """
            You are turning a captured idea into an epic somebody else could execute.

            What lands in the drafting column is intent, not a specification: a paragraph, a
            grievance, a link. Your increment is to make it real, and to stop there.

            - Read what has already happened to this issue - its comments and its events -
              before you start anything. A previous run may have been aborted partway, and
              continuing it beats writing over it. If it ran to completion and only the
              status was left behind, say so and move it on.
            - Search the Dream Board for dreams that bear on this epic -
              `hatch api GET /api/dreams?q=<words from the title and description>`
              lists them best first, any word matching. Read the ones that do bear on it
              and fold what they ask for into the brief, quoting them. Record which you
              drew on with `hatch api PUT /api/issues/<KEY>/dreams
              '{"dreamIds":[...]}'`. An empty answer costs one call; say
              *no dream bore on this* in the description and move on.
            - Ask the repository whatever the ticket does not answer. The code is the source
              of truth about what already exists; the ticket is only the source of truth
              about what is wanted.
            - Rewrite the description as the epic's own overview and architecture document:
              what this is for, what done looks like, the decisions taken and the ones
              rejected with the reason, and the constraints that bound it.
            - Break it into stories, each with parentKey set to this epic. A story is one
              landable outcome, not a phase of work. If you cannot say what a story delivers
              in one sentence, it is two stories. An epic with stories is not implemented
              by its own session - its stories are what get worked, one at a time, and the
              epic's own increment is the verification that they add up to it.
            - Sequence them. Anything that has to wait for a date or an event gets a
              readyAt; anything owed gets a dueAt. A note in a description saying "not until
              March" is a note nobody will see in March.

            Write no implementation code in this increment. Breaking work down and doing it
            are different jobs, and done in one session the plan comes out shaped like
            whatever you happened to build first.
            """;

        /// <summary>The same prompt with the overview-and-architecture bullet reworded.</summary>
        public static string EpicReplaced =>
            EpicStock.Replace(OldEpicBullet, NewEpicBullet, StringComparison.Ordinal);

        /// <summary>The story row's "who it is for" bullet, as the live default text says it.</summary>
        public const string OldStoryBullet = """
            - Say who it is for and what they get. The objective has to be concrete enough
              that somebody could disagree with it.
            """;

        /// <summary>What it says now: the sentence becomes the description's first line, and a title rule follows.</summary>
        public const string NewStoryBullet = """
            - Say who it is for and what they get, in one sentence, concrete enough that
              somebody could disagree with it - and make that sentence the first line of
              the description. Give it a title of a handful of words, the thing itself and
              not a coined name for it. `src/Hatch.Web/apps/hatch/docs/issues.md`, under
              "How a ticket reads", has the rules in full.
            """;

        /// <summary>
        /// The whole story-breakdown prompt as it stands today: a frozen copy of
        /// <see cref="SeedPlaybookDefaultPrompt.StoryBreakdown"/>, byte for byte, since that
        /// row has never been reworded or prefix-rewritten since it was seeded.
        /// </summary>
        public const string StoryStock = """
            You are turning a captured idea into a story that can be picked up cold.

            - Read the issue's comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Say who it is for and what they get. The objective has to be concrete enough
              that somebody could disagree with it.
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
            - Name the files and the pattern to copy, by path, where you found them. Half of
              what makes a story cheap is that the next session does not search twice.
            - Write the implementation plan onto the tasks, not into the description: cut
              it into tasks with parentKey set to this story, one task per self-contained
              change that could land on its own, more tasks rather than a longer story. A
              story small enough to be one session's work needs none.
            - A story with tasks is not implemented by its own session: the API carries
              its tasks across the board one at a time, and the story's own increment is
              the verification that they add up to it. Each task has to be complete on its
              own, and has to say what done looks like by itself.

            Research the repository as much as the criteria need to be accurate. Write no
            implementation code; that is a later increment's job.
            """;

        /// <summary>The same prompt with the "who it is for" bullet reworded.</summary>
        public static string StoryReplaced =>
            StoryStock.Replace(OldStoryBullet, NewStoryBullet, StringComparison.Ordinal);

        /// <summary>The task row's "rewrite the description" bullet, as the live default text says it.</summary>
        public const string OldTaskBullet = """
            - Rewrite the description as what is wrong or missing and what the finished
              state is. For a bug: what happens, what should happen, and how to reproduce it
              if you can work that out.
            """;

        /// <summary>What it says now: a title rule before the same rewrite-the-description bullet.</summary>
        public const string NewTaskBullet = """
            - Give it a title that names the change in one short line, and rewrite the
              description as what is wrong or missing and what the finished state is - the
              body may stay technical. For a bug: what happens, what should happen, and how
              to reproduce it if you can work that out.
              `src/Hatch.Web/apps/hatch/docs/issues.md`, under "How a ticket reads", has the
              rules in full.
            """;

        /// <summary>
        /// The whole task-breakdown prompt as it stands today: a frozen copy of
        /// <see cref="SeedPlaybookDefaultPrompt.TaskBreakdown"/>, byte for byte, since that
        /// row has never been reworded or prefix-rewritten since it was seeded.
        /// </summary>
        public const string TaskStock = """
            You are formalising a captured note into a task somebody can execute without
            having to ask a question first.

            - Read the issue's comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Rewrite the description as what is wrong or missing and what the finished
              state is. For a bug: what happens, what should happen, and how to reproduce it
              if you can work that out.
            - Find the code it concerns and name it by path and line.
            - Name the pattern file to copy if the house already does this thing somewhere.
              The house has one way of doing each thing; copying it beats inventing a
              second.
            - Give it a single unified technical acceptance criterion rather than a list of
              them. A task is one thing done; if it needs a list, it is more than one task.

            Write no implementation code in this increment.
            """;

        /// <summary>The same prompt with the "rewrite the description" bullet reworded.</summary>
        public static string TaskReplaced =>
            TaskStock.Replace(OldTaskBullet, NewTaskBullet, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Three independent guarded statements, one per row, DefaultPrompt alone
            // on each: no row's own Prompt to disturb and no UpdatedAt to bump -
            // learning a default is not an edit.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{EpicReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{EpicStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{StoryReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{StoryStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{TaskReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{TaskStock}}$old$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{EpicStock}}$old$
                 WHERE "DefaultPrompt" = $new${{EpicReplaced}}$new$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{StoryStock}}$old$
                 WHERE "DefaultPrompt" = $new${{StoryReplaced}}$new$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{TaskStock}}$old$
                 WHERE "DefaultPrompt" = $new${{TaskReplaced}}$new$;
                """);
        }
    }
}
