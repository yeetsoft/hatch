using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the rows that sharpen, implement, close out and verify a ticket to
    /// keep its summary at the top, to title the tasks they cut in a few words and
    /// to open their comments with one plain sentence saying what landed, the way
    /// <c>docs/issues.md</c>'s "How a ticket reads" spells out.
    /// </summary>
    /// <remarks>
    /// <para>Four rows change, each independently guarded and independently
    /// reversible, the way <see cref="RewordBreakdownRowsForTitleAndSummary"/> touches
    /// three of its own. Each keeps its existing bullet's subject and gains the
    /// rule in the same place. Like that migration this guards and writes
    /// <c>DefaultPrompt</c> alone on all four rows: an operator's own
    /// <c>Prompt</c> is left untouched and <c>UpdatedAt</c> never moves -
    /// learning a default is not an edit.</para>
    ///
    /// <para>None of the three live rows still reads as it was seeded, so their
    /// guards are frozen copies of the live text, not of
    /// <see cref="SeedPlaybookDefaultPrompt"/>. The epic-verification row has
    /// never been reworded, so its guard is a frozen copy of
    /// <see cref="SeedPlaybookDefaultPrompt.EpicVerification"/>.</para>
    /// </remarks>
    public partial class RewordLaterRowsForTheSummary : Migration
    {
        /// <summary>The final-analysis row's description bullet, as the live default text says it.</summary>
        public const string OldAnalysisBullet = """
            - Keep this ticket's own description a brief: what done means and why, not
              the plan. The plan lives on the tasks it was cut into, or, where it needed
              none, is what is already written above.
            """;

        /// <summary>What it says now: the summary stays first and each task cut is titled in one short line.</summary>
        public const string NewAnalysisBullet = """
            - Keep this ticket's own description a brief: what done means and why, not
              the plan. The plan lives on the tasks it was cut into, or, where it needed
              none, is what is already written above. The summary at the top stays
              first and stays plain - sharpen what follows it, never write over it.
              Title each task cut from this ticket in one short line naming the change.
            """;

        /// <summary>The whole final-analysis prompt as it stands today: a frozen copy of
        /// <see cref="RewordFinalAnalysisPlaybook.Replaced"/>, byte for byte, which is what the
        /// live row reads - not <see cref="SeedPlaybookDefaultPrompt.Analysis"/>, which is
        /// frozen at the pre-reword text.</summary>
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
              none, is what is already written above.
            - Say what you checked and what you could not check. The reasoning belongs on
              the ticket, not in a session log nobody can read afterwards.

            Write no implementation code and make no commit in this increment. Nothing here
            changes the tree.
            """;

        /// <summary>The same prompt with the description bullet reworded.</summary>
        public static string AnalysisReplaced =>
            AnalysisStock.Replace(OldAnalysisBullet, NewAnalysisBullet, StringComparison.Ordinal);

        /// <summary>The implementation row's comment bullet, as the live default text says it.</summary>
        public const string OldImplementationBullet = """
            - Comment the summary a reviewer needs: the branch, the sha, what changed, what
              to look at first, what did not land, and anything you are unsure about. If you
              opened a pull request, record it on the issue with hatch pr.
            """;

        /// <summary>What it says now: the comment opens with one sentence saying what landed.</summary>
        public const string NewImplementationBullet = """
            - Comment the summary a reviewer needs, opening with one sentence saying
              what landed, then the branch, the sha, what changed, what to look at
              first, what did not land, and anything you are unsure about. If you
              opened a pull request, record it on the issue with hatch pr.
            """;

        /// <summary>The whole implementation prompt as it stands today: the live text, which
        /// carries <see cref="PushWorkThatIsNotGreen"/> and <see cref="DeclareNoCode"/>'s rewording of
        /// later bullets and so is neither <see cref="RewordImplementationPlaybook.Replaced"/> nor
        /// <see cref="SeedPlaybookDefaultPrompt.Implementation"/>.</summary>
        public const string ImplementationStock = """
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
            - Green before pushed: lint, build, tests. A change to a screen is rendered in
              the prescribed headless driver (docs/browser-testing.md) and what was seen
              goes on the ticket; never claim a screen works from the build alone. Work
              that is not green still leaves this machine - a branch nobody else can see
              is work the next runner starts again from nothing.
              - If the checks cannot run here at all (a toolchain this machine lacks),
                push, open the pull request as usual, and say on the ticket which check
                was not run and why. The pull request's own checks are the gate.
              - If something is red and you cannot fix it, commit what you have, push it,
                comment what is red, what you tried and where you would look next, and
                stop, leaving the ticket where it is. Never put a branch you know is red
                up for review.
            - Check the acceptance criteria one at a time and say which are met. A criterion
              you cannot verify is one to say out loud you cannot verify, not one to quietly
              count.
            - Commit and push, the subject in house style: an area, then what changed, as a
              sentence. If there is no code change, make no commit and push no branch: say
              what you found in a comment, then run `hatch pr <key> --none "<reason>"` so
              the board knows nothing is coming.
            - Comment the summary a reviewer needs: the branch, the sha, what changed, what
              to look at first, what did not land, and anything you are unsure about. If you
              opened a pull request, record it on the issue with hatch pr.
            - Move the child tasks you worked along with the issue. A task left standing in
              the column it started in reads as work nobody did.
            """;

        /// <summary>The same prompt with the comment bullet reworded.</summary>
        public static string ImplementationReplaced =>
            ImplementationStock.Replace(OldImplementationBullet, NewImplementationBullet, StringComparison.Ordinal);

        /// <summary>The closeout row's verdict bullet, as the live default text says it.</summary>
        public const string OldCloseoutBullet = """
            - Every criterion met: comment what landed, where to look and what was
              checked, and move the issue on. Only the operator decides that something
              shipped.
            """;

        /// <summary>What it says now: the verdict opens with one sentence, then names each child.</summary>
        public const string NewCloseoutBullet = """
            - Every criterion met: comment the verdict, opening with one sentence
              saying whether the ticket does what it asked, then each child, where to
              look and what was checked - and move the issue on. Only the operator
              decides that something shipped.
            """;

        /// <summary>The whole closeout prompt as it stands today: <see cref="SeedPlaybookDefaultPrompt.Closeout"/>
        /// with every <c>/api/hatch/</c> rewritten to <c>/api/</c>
        /// (<see cref="RewriteApiPrefixInPlaybooks"/>), copied from the live row.</summary>
        public const string CloseoutStock = """
            Every child below is closed. This increment checks that what was done is what
            this issue asked for, and writes no implementation code.

            - Read the issue's own acceptance criteria, then each child - its description,
              its comments, and the pull request recorded on it (hatch show <key>, and
              hatch api GET /api/issues/<key> for the rest).
            - Check the criteria one at a time, naming the child and the commit or pull
              request that met each. A criterion that cannot be verified is one to say so
              about, not one to quietly count.
            - A gap becomes a new child task under this issue, filed with parentKey, not a
              fix made here - which puts the issue back to having an open child, so the
              next pass pulls the new task and the ceremony runs again when it lands.
              docs/hatch-planning.md has the shape.
            - Every criterion met: comment what landed, where to look and what was
              checked, and move the issue on. Only the operator decides that something
              shipped.
            """;

        /// <summary>The same prompt with the verdict bullet reworded.</summary>
        public static string CloseoutReplaced =>
            CloseoutStock.Replace(OldCloseoutBullet, NewCloseoutBullet, StringComparison.Ordinal);

        /// <summary>The epic-verification row's verdict bullet, as the stock text says it.</summary>
        public const string OldVerificationBullet = """
            - Comment the verdict on the epic: each criterion, whether it holds, and the
              evidence - a path, a test, a command and what it printed.
            """;

        /// <summary>What it says now: the verdict opens with one sentence, then keeps each criterion's evidence.</summary>
        public const string NewVerificationBullet = """
            - Comment the verdict on the epic, opening with one sentence saying
              whether the epic does what it asked, then each criterion, whether it
              holds, and the evidence - a path, a test, a command and what it
              printed.
            """;

        /// <summary>The whole epic-verification prompt: a frozen copy of
        /// <see cref="SeedPlaybookDefaultPrompt.EpicVerification"/>, byte for byte, since nothing
        /// has ever reworded it. This install has no row of its own carrying it
        /// (<see cref="VerifyEpicPlaybook"/> found the closeout row already covering an epic
        /// and wrote nothing), so the statement guarded on it is a no-op here and live
        /// wherever that text is.</summary>
        public const string VerificationStock = """
            You are verifying an epic: checking what its description promised against what the
            trunk now does. Every story filed under it has shipped or been shelved, and this is
            the last look before the operator decides whether to close it. You do not close it.

            - Read the epic, its children and what has already happened to it - its comments
              and its events. A previous run may have been aborted partway; continue it rather
              than starting again.
            - Check each acceptance criterion in the epic's description against the trunk as
              origin has it, one at a time. Read the repository; where a criterion is testable,
              build and run the tests with the repository's own commands. A criterion you could
              not check is one to say out loud you could not check, not one to count as holding.
            - Comment the verdict on the epic: each criterion, whether it holds, and the
              evidence - a path, a test, a command and what it printed.
            - A criterion that a child still open, or a child in a deferred column, was filed
              for is not a gap. Name that child rather than filing its work a second time:
              shelving work is the operator's call, and it has been made.
            - If every criterion holds, or the only ones that do not were shelved with a
              deferred child, move the epic to the column named at the end of this prompt, and
              say in the verdict which criteria were shelved.
            - Otherwise file each gap under the epic - one story or bug per gap, with parentKey
              set to the epic, saying what is wrong or missing, with acceptance criteria
              somebody else could check - and leave the epic in the column it is in. The verdict
              names each issue you filed and the criterion it answers. A gap you cannot turn
              into a ticket is a question: ask it, and stop.
            - Never move the epic, or anything under it, into a terminal column. Only the
              operator decides that something shipped.

            Write no code, make no commit and push nothing. If the branch section names a
            branch to cut, you need none: nothing you do changes the tree.
            """;

        /// <summary>The same prompt with the verdict bullet reworded.</summary>
        public static string VerificationReplaced =>
            VerificationStock.Replace(OldVerificationBullet, NewVerificationBullet, StringComparison.Ordinal);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Four independent guarded statements, one per row, DefaultPrompt alone
            // on each: no row's own Prompt to disturb and no UpdatedAt to bump -
            // learning a default is not an edit.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{AnalysisReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{AnalysisStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{ImplementationReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{ImplementationStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{CloseoutReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{CloseoutStock}}$old$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{VerificationReplaced}}$new$
                 WHERE "DefaultPrompt" = $old${{VerificationStock}}$old$;
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
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{ImplementationStock}}$old$
                 WHERE "DefaultPrompt" = $new${{ImplementationReplaced}}$new$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{CloseoutStock}}$old$
                 WHERE "DefaultPrompt" = $new${{CloseoutReplaced}}$new$;
                """);
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $old${{VerificationStock}}$old$
                 WHERE "DefaultPrompt" = $new${{VerificationReplaced}}$new$;
                """);
        }
    }
}
