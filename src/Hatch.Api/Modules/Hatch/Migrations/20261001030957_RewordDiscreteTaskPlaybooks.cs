using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The breakdown and analysis playbooks push discrete tasks rather than a
    /// bloated story, and say what a child does to the session that owns it.
    /// </summary>
    /// <remarks>
    /// <para>Three rows change, each independently guarded and independently
    /// reversible, the way <see cref="PlaybookBranchStep"/> touches one: only a
    /// row whose <c>Prompt</c> is still byte-identical to what it was seeded with
    /// is rewritten. <c>Prompt</c> alone, and deliberately not <c>FromStatusId</c>,
    /// <c>ToStatusId</c> or <c>Types</c> - the live board has narrowed
    /// <c>Types</c> on two of these rows without touching their wording, and that
    /// scoping is no reason to leave the old instruction in place. An edited
    /// prompt is somebody's own wording and is left alone; the operator is told
    /// on the ticket what to change.</para>
    ///
    /// <para>The seed in <c>Playbooks</c> carries the new text too, which is what
    /// a fresh install gets; the <c>Stock</c> constants here are the text as it
    /// used to be, so they do not match there and this migration does nothing on
    /// a fresh install.</para>
    ///
    /// <para><b>Epic breakdown</b> (<c>inbox</c>&#8594;<c>todo</c>, <c>epic</c>):
    /// the stories bullet gains the consequence of a story being the unit that
    /// gets worked - an epic with stories is not implemented by its own session.
    /// <b>Story breakdown</b> (<c>inbox</c>&#8594;<c>todo</c>, <c>story</c>): the
    /// one task-cutting bullet, which said to cut tasks only where the story was
    /// nontrivially complex, becomes two - cut one task per self-contained
    /// change, and a story with tasks is not implemented by its own session
    /// either; the API carries its tasks across the board one at a time, and the
    /// story's own increment verifies they add up to it. <b>Analysis</b>
    /// (<c>todo</c>&#8594;<c>in progress</c>, every type): a new paragraph says
    /// that for an issue with children, the next increment pulls the first
    /// child rather than writing code, so this increment's job is to be sure the
    /// children are the whole plan and are in the right order.</para>
    /// </remarks>
    public partial class RewordDiscreteTaskPlaybooks : Migration
    {
        /// <summary>The epic breakdown's stories bullet, as the seed used to say it.</summary>
        public const string OldEpicBullet = """
            - Break it into stories, each with parentKey set to this epic. A story is one
              landable outcome, not a phase of work. If you cannot say what a story delivers
              in one sentence, it is two stories.
            """;

        /// <summary>What it says now.</summary>
        public const string NewEpicBullet = """
            - Break it into stories, each with parentKey set to this epic. A story is one
              landable outcome, not a phase of work. If you cannot say what a story delivers
              in one sentence, it is two stories. An epic with stories is not implemented
              by its own session - its stories are what get worked, one at a time, and the
              epic's own increment is the verification that they add up to it.
            """;

        /// <summary>The whole epic breakdown prompt as it was seeded, and so as an unedited row still is.</summary>
        public const string EpicStock = """
            You are turning a captured idea into an epic somebody else could execute.

            What lands in the drafting column is intent, not a specification: a paragraph, a
            grievance, a link. Your increment is to make it real, and to stop there.

            - Read what has already happened to this issue - its comments and its events -
              before you start anything. A previous run may have been aborted partway, and
              continuing it beats writing over it. If it ran to completion and only the
              status was left behind, say so and move it on.
            - Ask the repository whatever the ticket does not answer. The code is the source
              of truth about what already exists; the ticket is only the source of truth
              about what is wanted.
            - Rewrite the description as the epic's own overview and architecture document:
              what this is for, what done looks like, the decisions taken and the ones
              rejected with the reason, and the constraints that bound it.
            - Break it into stories, each with parentKey set to this epic. A story is one
              landable outcome, not a phase of work. If you cannot say what a story delivers
              in one sentence, it is two stories.
            - Sequence them. Anything that has to wait for a date or an event gets a
              readyAt; anything owed gets a dueAt. A note in a description saying "not until
              March" is a note nobody will see in March.

            Write no implementation code in this increment. Breaking work down and doing it
            are different jobs, and done in one session the plan comes out shaped like
            whatever you happened to build first.
            """;

        /// <summary>The same prompt with the one bullet replaced.</summary>
        public static string EpicReplaced => EpicStock.Replace(OldEpicBullet, NewEpicBullet, StringComparison.Ordinal);

        /// <summary>The story breakdown's one task-cutting bullet, as the seed used to say it.</summary>
        public const string OldStoryBullet = """
            - Write the implementation plan into the description, and where it is
              nontrivially complex, cut it into tasks with parentKey set to this story. A
              story that is one afternoon does not need three tasks.
            """;

        /// <summary>What it says now - two bullets, not one.</summary>
        public const string NewStoryBullets = """
            - Write the implementation plan into the description, and cut it into tasks
              with parentKey set to this story: one task per self-contained change that
              could land on its own, more tasks rather than a longer story. A story small
              enough to be one session's work needs none.
            - A story with tasks is not implemented by its own session: the API carries
              its tasks across the board one at a time, and the story's own increment is
              the verification that they add up to it. Each task has to be complete on its
              own, and has to say what done looks like by itself.
            """;

        /// <summary>The whole story breakdown prompt as it was seeded, and so as an unedited row still is.</summary>
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
            - Write the implementation plan into the description, and where it is
              nontrivially complex, cut it into tasks with parentKey set to this story. A
              story that is one afternoon does not need three tasks.

            Research the repository as much as the criteria need to be accurate. Write no
            implementation code; that is a later increment's job.
            """;

        /// <summary>The same prompt with the one bullet replaced by two.</summary>
        public static string StoryReplaced => StoryStock.Replace(OldStoryBullet, NewStoryBullets, StringComparison.Ordinal);

        /// <summary>The analysis prompt's anchor - the paragraph the new one follows - as the seed used to say it.</summary>
        public const string OldAnalysisAnchor = """
            written correct.

            - Read the issue's comments and events first.
            """;

        /// <summary>What it says now - a new paragraph in between.</summary>
        public const string NewAnalysisAnchor = """
            written correct.

            If this issue has children, the next thing that happens is not code - its
            first child is pulled on its own. This increment's job is then to be sure the
            children are the whole plan and are in the right order, filing or reordering
            them now if they are not.

            - Read the issue's comments and events first.
            """;

        /// <summary>The whole analysis prompt as it was seeded, and so as an unedited row still is.</summary>
        public const string AnalysisStock = """
            You are doing the final analysis of a ticket that has already been specified,
            and leaving it in a state where implementing it is mechanical.

            The next increment writes the code. Everything it would otherwise have to decide
            - what done means, which files change, which pattern to copy, what the tests are
            - is yours to settle now and to write onto the ticket, because a decision taken
            with the code half-written is taken under pressure to make the code already
            written correct.

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

        /// <summary>The same prompt with the new paragraph inserted.</summary>
        public static string AnalysisReplaced => AnalysisStock.Replace(OldAnalysisAnchor, NewAnalysisAnchor, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Three independent guarded statements, one per row, each dollar-quoted
            // so neither prompt needs a character escaped and compared whole:
            // "byte-identical" is the guard, and a LIKE on the one sentence would
            // rewrite a prompt somebody had edited around it. No
            // FromStatusId/ToStatusId/Types condition on any of the three - Prompt
            // alone, so a row whose Types the operator has since narrowed is still
            // reworded.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $new${{EpicReplaced}}$new$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $old${{EpicStock}}$old$;
                """);
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row that still says exactly what
            // this migration wrote is put back.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "Prompt" = $old${{EpicStock}}$old$,
                       "UpdatedAt" = now()
                 WHERE "Prompt" = $new${{EpicReplaced}}$new$;
                """);
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
        }
    }
}
