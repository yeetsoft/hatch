using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the final-analysis row to cut a ticket into tasks only along a
    /// real seam, instead of making fan-out the default.
    /// </summary>
    /// <remarks>
    /// <para>The row's "Cut the plan into tasks" bullet asked for one seam and
    /// one acceptance criterion to a task. Followed as written, a Medium story
    /// was cut into six chained tasks and an earlier one into five - each a
    /// full session with its own setup, several editing the same file, one a
    /// standalone closeout. An operator fixed their own install by editing the
    /// live row, but the stock text stayed the old one, so a fresh install, or
    /// a row that has handed its wording back to the default, got it again.
    /// The bullet is replaced by two: decide whether to cut at all (the
    /// default is not to), and when cutting, make as few tasks as the seams
    /// allow.</para>
    ///
    /// <para>This follows <see cref="RewordImplementationPlaybook"/>'s
    /// <c>DefaultPrompt</c>-only precedent: it guards and writes
    /// <c>DefaultPrompt</c> alone, so an edited row's <c>Prompt</c> is left
    /// alone and <c>UpdatedAt</c> is not touched - learning a default is not an
    /// edit.</para>
    /// </remarks>
    public partial class RewordFinalAnalysisPlaybook : Migration
    {
        /// <summary>The cut-the-plan bullet, as the live default text says it.</summary>
        public const string OldCutBullet = """
            - Cut the plan into tasks, one seam to a task and one acceptance criterion to
              a task: the files it touches, by path; the pattern file to copy; the shape
              of its tests - each task complete on its own, a single session's work.
              Create each with parentKey set to this ticket, then move it into the column
              this ticket is leaving, not the one it is moving into, so the next pass
              finds it already specified. A ticket with no real seam needs none.
            """;

        /// <summary>What it says now: two bullets in its place.</summary>
        public const string NewCutBullets = """
            - Decide whether to cut at all. The default is not to: a ticket that sits
              in one area of the code, or that one session can carry, is implemented
              whole, with the plan written on it (files by path, the pattern to copy,
              the shape of the tests). Cut only along a real seam: pieces in different
              areas of the code, or of different risk (needs the simulator vs. docs
              only), that can each be built, verified and merged on their own. Never one
              task per acceptance criterion. If two pieces edit the same file, they are
              one task. Tasks and bugs are never cut.
            - When you cut, make as few tasks as the seams allow. Three or fewer is
              the norm, and more needs the reason written on the ticket. Each task is a
              session's worth of work, complete on its own. Tests, docs and style-guide
              updates go in the task they describe, and the last task opens the pull
              request. A closeout is never a task of its own. Create each with parentKey
              set to this ticket and move it into the column this ticket is leaving.
              Chain with depends only where the order is real.
            """;

        /// <summary>The whole final-analysis prompt as it stands today, a frozen copy of <see cref="SeedPlaybookDefaultPrompt.Analysis"/>.</summary>
        public const string Stock = """
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
            - Cut the plan into tasks, one seam to a task and one acceptance criterion to
              a task: the files it touches, by path; the pattern file to copy; the shape
              of its tests - each task complete on its own, a single session's work.
              Create each with parentKey set to this ticket, then move it into the column
              this ticket is leaving, not the one it is moving into, so the next pass
              finds it already specified. A ticket with no real seam needs none.
            - Keep this ticket's own description a brief: what done means and why, not
              the plan. The plan lives on the tasks it was cut into, or, where it needed
              none, is what is already written above.
            - Say what you checked and what you could not check. The reasoning belongs on
              the ticket, not in a session log nobody can read afterwards.

            Write no implementation code and make no commit in this increment. Nothing here
            changes the tree.
            """;

        /// <summary>The same prompt with the one bullet replaced by two.</summary>
        public static string Replaced =>
            Stock.Replace(OldCutBullet, NewCutBullets, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DefaultPrompt alone, not Prompt: there is no row's own wording to
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
