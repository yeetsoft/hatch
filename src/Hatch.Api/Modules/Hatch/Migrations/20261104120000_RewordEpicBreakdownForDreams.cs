using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the epic-breakdown row to consult the Dream Board before it writes the
    /// brief, and to record which dreams it drew on.
    /// </summary>
    /// <remarks>
    /// <para>The board now keeps dreams and can say which of them an issue drew on,
    /// but nothing told the session that turns an idea into an epic to look. The new
    /// text adds one bullet, between reading what has already happened and asking the
    /// repository, and changes nothing else: the story and task breakdown rows are
    /// not reworded.</para>
    ///
    /// <para>Like <see cref="RewordReflectionPlaybook"/> this guards and writes
    /// <c>DefaultPrompt</c> alone: a row whose <c>DefaultPrompt</c> is
    /// byte-identical to <see cref="Stock"/> has nobody's wording in it, an
    /// edited row's <c>Prompt</c> is left alone, and <c>UpdatedAt</c> is not
    /// touched. <see cref="Replaced"/> is a second literal and not a
    /// <c>Replace</c> over <see cref="Stock"/>, so each text can be read as the
    /// prompt it is.</para>
    /// </remarks>
    public partial class RewordEpicBreakdownForDreams : Migration
    {
        /// <summary>The whole epic-breakdown prompt as it stands today, a frozen copy of <see cref="SeedPlaybookDefaultPrompt.EpicBreakdown"/>.</summary>
        public const string Stock = """
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

        /// <summary>What it says now: it searches the Dream Board and records the dreams it drew on.</summary>
        public const string Replaced = """
            You are turning a captured idea into an epic somebody else could execute.

            What lands in the drafting column is intent, not a specification: a paragraph, a
            grievance, a link. Your increment is to make it real, and to stop there.

            - Read what has already happened to this issue - its comments and its events -
              before you start anything. A previous run may have been aborted partway, and
              continuing it beats writing over it. If it ran to completion and only the
              status was left behind, say so and move it on.
            - Search the Dream Board for dreams that bear on this epic -
              `hatch api GET /api/hatch/dreams?q=<words from the title and description>`
              lists them best first, any word matching. Read the ones that do bear on it
              and fold what they ask for into the brief, quoting them. Record which you
              drew on with `hatch api PUT /api/hatch/issues/<KEY>/dreams
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DefaultPrompt alone, the way the reflection reword does it:
            // no row's own wording to disturb, no UpdatedAt to bump.
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
