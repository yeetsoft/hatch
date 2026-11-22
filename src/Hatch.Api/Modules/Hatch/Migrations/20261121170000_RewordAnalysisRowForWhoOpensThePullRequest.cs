using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the final-analysis row who opens the pull request: the last task, when
    /// the ticket's tasks share one branch (<c>hatch one-pr</c>), and otherwise each
    /// task, which records its own with <c>hatch pr</c>.
    /// </summary>
    /// <remarks>
    /// <para>One bullet of one row changes. Like <see cref="RewordLaterRowsForTheSummary"/>
    /// this guards and writes <c>DefaultPrompt</c> alone: an operator's own
    /// <c>Prompt</c> is left untouched and <c>UpdatedAt</c> never moves - learning a
    /// default is not an edit. The guard is a frozen copy of that migration's
    /// <see cref="RewordLaterRowsForTheSummary.AnalysisReplaced"/>, which is what the
    /// live row reads; a row somebody has reworded since matches nothing and is left
    /// alone.</para>
    /// </remarks>
    public partial class RewordAnalysisRowForWhoOpensThePullRequest : Migration
    {
        /// <summary>The final-analysis row's task-cutting bullet, as the live default text says it.</summary>
        public const string OldBullet = """
            - When you cut, make as few tasks as the seams allow. Three or fewer is
              the norm, and more needs the reason written on the ticket. Each task is a
              session's worth of work, complete on its own. Tests, docs and style-guide
              updates go in the task they describe, and the last task opens the pull
              request. A closeout is never a task of its own. Create each with parentKey
              set to this ticket and move it into the column this ticket is leaving.
              Chain with depends only where the order is real.
            """;

        /// <summary>What it says now: the last task opens the one pull request only when the tasks share a branch.</summary>
        public const string NewBullet = """
            - When you cut, make as few tasks as the seams allow. Three or fewer is
              the norm, and more needs the reason written on the ticket. Each task is a
              session's worth of work, complete on its own. Tests, docs and style-guide
              updates go in the task they describe. When this ticket's tasks share one
              branch (`hatch one-pr`), the last task opens the one pull request;
              otherwise each task opens its own pull request and records it on itself
              with `hatch pr`. A closeout is never a task of its own. Create each with
              parentKey set to this ticket and move it into the column this ticket is
              leaving. Chain with depends only where the order is real.
            """;

        /// <summary>The whole final-analysis prompt as it stands today: a frozen copy of
        /// <see cref="RewordLaterRowsForTheSummary.AnalysisReplaced"/>, byte for byte.</summary>
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
            - Keep this ticket's own description a brief: what done means and why, not
              the plan. The plan lives on the tasks it was cut into, or, where it needed
              none, is what is already written above. The summary at the top stays
              first and stays plain - sharpen what follows it, never write over it.
              Title each task cut from this ticket in one short line naming the change.
            - Say what you checked and what you could not check. The reasoning belongs on
              the ticket, not in a session log nobody can read afterwards.

            Write no implementation code and make no commit in this increment. Nothing here
            changes the tree.
            """;

        /// <summary>The same prompt with the task-cutting bullet reworded.</summary>
        public static string AnalysisReplaced =>
            AnalysisStock.Replace(OldBullet, NewBullet, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DefaultPrompt alone: no row's own Prompt to disturb and no UpdatedAt to
            // bump - learning a default is not an edit.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{AnalysisReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{AnalysisStock}}$old$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{AnalysisStock}}$old$
                 WHERE "DefaultPrompt" = $new${{AnalysisReplaced}}$new$;
                """);
        }
    }
}
