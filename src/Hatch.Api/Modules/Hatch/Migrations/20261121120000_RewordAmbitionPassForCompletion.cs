using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the ambition-pass row that the server itself now refuses an epic
    /// filed or moved under an ambition whose every capability holds, so the
    /// pass stops offering to carve one and comments why instead.
    /// </summary>
    /// <remarks>
    /// <para>Before this the only thing stopping a pass from carving past
    /// completion was its own sentence; now <see cref="IssuesController"/>
    /// refuses the attempt outright. The reworded bullet - the one that
    /// already told the pass to leave a holding ambition alone - says so, and
    /// asks the pass to comment that nothing was carved and why, rather than
    /// quietly saying nothing. Nothing else in the prompt changes.</para>
    ///
    /// <para>Like <see cref="RewordEpicBreakdownForDreams"/> this guards and
    /// writes <c>DefaultPrompt</c> alone: a row whose <c>DefaultPrompt</c> is
    /// byte-identical to <see cref="Stock"/> has nobody's wording in it, an
    /// edited row's <c>Prompt</c> is left alone, and <c>UpdatedAt</c> is not
    /// touched. <see cref="Replaced"/> is a second literal and not a
    /// <c>Replace</c> over <see cref="Stock"/>, so each text can be read as
    /// the prompt it is.</para>
    /// </remarks>
    public partial class RewordAmbitionPassForCompletion : Migration
    {
        /// <summary>The whole ambition-pass prompt as it stands today, a frozen copy of <see cref="SeedAmbitionPassPlaybook.Stock"/>.</summary>
        public const string Stock = """
            You are running a pass on an ambition. A pass chooses the next piece of work from
            what the trunk holds today. You judge and you file. You never edit the tree, and
            your tools refuse if you try.

            - Read the ambition, every earlier pass comment on it, its answered questions, and
              each epic under it with that epic's own verdict. If an earlier run of this pass
              was aborted partway, continue it rather than starting again.
            - Decide what holds by reading the trunk, not the tickets. Read the ambition's
              capabilities with `hatch api GET /api/issues/<key>/capabilities`. Check each one
              against the trunk by its how-to-tell line. Call it holds, does not hold or cannot
              tell. A closed epic is not evidence. The trunk is.
            - Record each verdict with `hatch capability judge`. Name what you looked at (the
              file, test, route or docs page) and the sha of the trunk.
            - Work out how many epics you may carve: the ambition's Epics at once (its
              `wipLimit`; where none is set it reads as one), less the epics under it that are
              still open. An open epic is one in a column that is neither done nor deferred. If
              that is zero or less, carve nothing.
            - Choose what to carve, in this order. First, the thinnest piece that makes one
              more capability hold on the trunk by itself; an epic that only prepares for a
              later one is the wrong cut. Second, what settles the most doubt; if it is unclear
              whether something can work at all, find out now. Third, what the operator gets
              the most from.
            - Two epics carved in one pass must not need each other. If a story in one would
              wait on a story in the other, carve one now and leave the other for the next
              pass.
            - An epic may need a decision that is the operator's. Ask it on that epic with
              `hatch ask`, the choices named and one recommended. Never ask it on the ambition.
              Never leave every open epic waiting on the operator: if carving would do that,
              carve one that needs no decision instead.
            - File each carved epic under the ambition with `hatch api POST /api/issues`: type
              `epic`, `parentKey` the ambition's key. Write it for one breakdown session: a
              short summary, then `## What this is for`, `## What is already there`,
              `## What done looks like` as numbered checks, `## Decisions taken`, and three
              to six suggested stories. Say which capabilities it serves, by number.
            - Link each epic to what it serves with `hatch serves`. Move it to the breakdown
              column at once, after any question is asked on it, and confirm with `hatch show`
              that it stands there.
            - Comment the pass on the ambition. Open with `Pass <n>` (one more than the earlier
              pass comments) and the outcome in one sentence. Then a table with a verdict for
              every capability and what you looked at; what you carved and why now; what you
              left and why; and one short paragraph on what looks likely next. That paragraph
              is a guess for a reader, never a list of tickets.
            - Leave the ambition where it stands. If every capability holds, carve nothing and
              say so in the comment. Only a person closes an ambition.
            - An idea that falls outside the capabilities goes to the Dream Board with
              `hatch dream`, not onto the board.
            - Write what you file in the voice the project's docs ask of a ticket: short
              sentences, no coined names, a title of a handful of words.
            - Name no board, repository, domain or person: nothing said here may be true of
              only the installation this happens to run on.
            """;

        /// <summary>What it says now: the server refuses an epic once every capability holds, so the pass comments instead of carving.</summary>
        public const string Replaced = """
            You are running a pass on an ambition. A pass chooses the next piece of work from
            what the trunk holds today. You judge and you file. You never edit the tree, and
            your tools refuse if you try.

            - Read the ambition, every earlier pass comment on it, its answered questions, and
              each epic under it with that epic's own verdict. If an earlier run of this pass
              was aborted partway, continue it rather than starting again.
            - Decide what holds by reading the trunk, not the tickets. Read the ambition's
              capabilities with `hatch api GET /api/issues/<key>/capabilities`. Check each one
              against the trunk by its how-to-tell line. Call it holds, does not hold or cannot
              tell. A closed epic is not evidence. The trunk is.
            - Record each verdict with `hatch capability judge`. Name what you looked at (the
              file, test, route or docs page) and the sha of the trunk.
            - Work out how many epics you may carve: the ambition's Epics at once (its
              `wipLimit`; where none is set it reads as one), less the epics under it that are
              still open. An open epic is one in a column that is neither done nor deferred. If
              that is zero or less, carve nothing.
            - Choose what to carve, in this order. First, the thinnest piece that makes one
              more capability hold on the trunk by itself; an epic that only prepares for a
              later one is the wrong cut. Second, what settles the most doubt; if it is unclear
              whether something can work at all, find out now. Third, what the operator gets
              the most from.
            - Two epics carved in one pass must not need each other. If a story in one would
              wait on a story in the other, carve one now and leave the other for the next
              pass.
            - An epic may need a decision that is the operator's. Ask it on that epic with
              `hatch ask`, the choices named and one recommended. Never ask it on the ambition.
              Never leave every open epic waiting on the operator: if carving would do that,
              carve one that needs no decision instead.
            - File each carved epic under the ambition with `hatch api POST /api/issues`: type
              `epic`, `parentKey` the ambition's key. Write it for one breakdown session: a
              short summary, then `## What this is for`, `## What is already there`,
              `## What done looks like` as numbered checks, `## Decisions taken`, and three
              to six suggested stories. Say which capabilities it serves, by number.
            - Link each epic to what it serves with `hatch serves`. Move it to the breakdown
              column at once, after any question is asked on it, and confirm with `hatch show`
              that it stands there.
            - Comment the pass on the ambition. Open with `Pass <n>` (one more than the earlier
              pass comments) and the outcome in one sentence. Then a table with a verdict for
              every capability and what you looked at; what you carved and why now; what you
              left and why; and one short paragraph on what looks likely next. That paragraph
              is a guess for a reader, never a list of tickets.
            - Leave the ambition where it stands. If every capability holds, carve nothing: the
              server refuses an epic under an ambition whose every capability's newest verdict is
              holds, so record your verdicts first. Comment on the ambition that nothing was
              carved and why. Only a person closes an ambition.
            - An idea that falls outside the capabilities goes to the Dream Board with
              `hatch dream`, not onto the board.
            - Write what you file in the voice the project's docs ask of a ticket: short
              sentences, no coined names, a title of a handful of words.
            - Name no board, repository, domain or person: nothing said here may be true of
              only the installation this happens to run on.
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
