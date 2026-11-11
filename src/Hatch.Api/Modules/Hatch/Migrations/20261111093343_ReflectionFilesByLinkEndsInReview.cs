using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the reflection row to file by <c>filedFromKey</c> rather than by
    /// <c>parentKey</c>, to write a hypothesis that measures itself on its own
    /// date rather than waiting on the next reflection, and to end the row in
    /// the review column rather than Backlog, so declaring no code there closes
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>Two independently guarded halves, the way <see cref="TheFlow"/>
    /// and the reword migrations before it each guard their own column. The
    /// reword half guards and writes <c>DefaultPrompt</c> alone, only where it
    /// is byte-identical to <see cref="Stock"/> - a frozen copy of
    /// <see cref="ReflectionFilesTrackerGaps.Replaced"/> - the way
    /// <see cref="RewordReflectionPlaybook"/> does: an edited row's own
    /// <c>Prompt</c> is never read or written, and <c>UpdatedAt</c> does not
    /// move. The retarget half guards on routing alone: a row still on
    /// <c>Breakdown</c> to <c>Backlog</c> for <c>reflection</c> has not been
    /// moved by an operator, so it is moved to the review column, measured the
    /// way <see cref="SeedConflictPlaybook"/> measures it - the non-deferred
    /// column immediately left of the first terminal one - and guarded by
    /// <c>NOT EXISTS</c> against the same
    /// (<c>FromStatusId</c>, <c>ToStatusId</c>, <c>Types</c>, <c>Shape</c>,
    /// <c>ProjectId</c>) unique index <see cref="SeedReflectionPlaybook"/>
    /// guards its insert with, so retargeting one row never collides with
    /// another already sitting on that transition. Neither half touches the
    /// other's columns, so a row an operator retargeted but never reworded
    /// still gets the new wording, and one reworded but never retargeted still
    /// moves.</para>
    /// </remarks>
    public partial class ReflectionFilesByLinkEndsInReview : Migration
    {
        /// <summary>The whole reflection prompt as it stands today, a frozen copy of <see cref="ReflectionFilesTrackerGaps.Replaced"/>.</summary>
        public const string Stock = """
            You are reflecting on how this board has been run, not on any one ticket on it.

            - Read this ticket's own comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Read the window this ticket's own brief names - the thirty days before it was
              filed, where it names none. Read it through the folds, not the rows: spend by
              playbook, flow by column, the timeline, the queue, utilization, the activity
              trail, and which playbooks spoke in it.
            - Read the hypotheses the reflections before this one left open. List the
              project's reflections with GET /api/hatch/issues?type=reflection&projectId=,
              the project being this ticket's own, and leave this ticket out. List the
              children of each with parentKey, and read every one whose description carries
              a "## Hypothesis" block and whose read-after date has passed, except one an
              earlier reflection has already commented a verdict of confirmed or refuted on.
              Compare its metric over the baseline window and over the after window - the
              same length as the baseline, ending on the read-after date - through the same
              folds the block names. Call it confirmed, refuted or unreadable (the metric is
              no longer produced, or the after window holds too little to say), and comment
              the verdict on the hypothesis issue.
            - Rewrite this ticket's description as the report, in this shape and no other:
              Window; What ran and what it cost; Where the time went; Findings, each with
              its evidence; Recommendations, each either filed as an issue under this
              reflection or written as an instruction for a person; Hypotheses read, each
              confirmed, refuted or unreadable with its numbers and its key, and written as
              "None" with the reason where the project has no earlier reflection or none of
              their hypotheses is due, never left out; Hypotheses filed, each as the block
              below; What reflection could not see.
            - File each recommendation that is a change to make as an issue under this
              reflection, in the project it reflects on - an epic, a story, a task or a bug,
              whichever its size calls for. Propose code changes only where that project's
              own repository is this one, told by the tree this session stands in rather
              than by name; where it is not, write the finding as an instruction for a
              person instead. File each hypothesis the same way, with a readyAt set to its
              read-after date, so it waits until then, and this block in its description:

              ## Hypothesis
              - Metric: the fold and the series, named so it can be read again
              - Baseline: the window, from and to, and the value over it
              - Change: what is expected to move, and the key of the issue that carries it
              - Read after: the date, the same as its readyAt
            - A refuted hypothesis whose change landed as code gets an issue filed under this
              reflection to revert or amend it, where that project's own repository is this
              one, and an instruction for a person where it is not. A confirmed one is
              reported and left for a person to close.
            - Where the project reflected on binds no repository at all, say so under "What
              reflection could not see" as an instrumentation request, rather than filing
              work against code nobody can reach. Where an instrumentation gap lies in the
              tracker itself rather than in that project's own repository - something the
              tracker does not record or expose - name it under "What reflection could not
              see" as well, and also file it as an issue, with no parent, in the project
              that GET /api/hatch/projects flags with "bindsTracker": true, if the board has
              one. Where none is flagged, leave it in the report as prose.
            - Name no board, repository, domain or person: nothing said here may be true of
              only the installation this happens to run on.
            - You judge and you file; you never edit the tree. Move this ticket to the
              column named at the end of this prompt once the report is written and its
              issues are filed.
            """;

        /// <summary>What it says now: files by filedFromKey, writes a self-measuring hypothesis, and declares no code rather than naming a column.</summary>
        public const string Replaced = """
            You are reflecting on how this board has been run, not on any one ticket on it.

            - Read this ticket's own comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Read the window this ticket's own brief names - the thirty days before it was
              filed, where it names none. Read it through the folds, not the rows: spend by
              playbook, flow by column, the timeline, the queue, utilization, the activity
              trail, and which playbooks spoke in it.
            - Read the hypotheses earlier reflections filed. List the project's reflections
              with GET /api/hatch/issues?type=reflection&projectId=, this ticket's own
              project, leaving this ticket out. List each one's filed issues with
              GET /api/hatch/issues?filedFromKey=, and read every one whose description
              carries a "## Hypothesis" block. Report the verdict already commented on it.
              Where its read-after date has passed and it carries no verdict, measure it
              yourself: compare its metric over the baseline window and the after window -
              the same length, ending on the read-after date - through the same folds the
              block names. Call it confirmed, refuted or unreadable (the metric is no longer
              produced, or the after window holds too little to say), and comment the
              verdict on the hypothesis issue.
            - Rewrite this ticket's description as the report, in this shape and no other:
              Window; What ran and what it cost; Where the time went; Findings, each with
              its evidence; Recommendations, each either filed as an issue or written as an
              instruction for a person; Hypotheses read, each confirmed, refuted or
              unreadable with its numbers and its key, and written as "None" with the reason
              where the project has no earlier reflection or none of their hypotheses is
              due, never left out; Hypotheses filed, each as the block below; What
              reflection could not see.
            - File every recommendation that is a change to make, and every hypothesis, as
              an issue with filedFromKey set to this reflection and no parent, in the
              project it reflects on - epic, story, task or bug, whichever its size calls
              for. An epic, with its stories under it, is filed the same way: only the top
              issue carries the link. Propose code changes only where that project's own
              repository is this one, told by the tree this session stands in rather than
              by name; otherwise write the finding as an instruction for a person.
            - File each hypothesis with a readyAt set to its read-after date, a dependency
              (hatch depends <hypothesis key> <key>) on the issue its Change line names, and
              this in its description:

              ## Hypothesis
              - Metric: the fold and the series, named so it can be read again
              - Baseline: the window, from and to, and the value over it
              - Change: what is expected to move, and the key of the issue that carries it
              - Read after: the date, the same as its readyAt

              ## How to read it
              - Compare the metric above over the baseline window and the after window - the
                same length, ending on the read-after date - through the same folds.
              - Call it confirmed, refuted or unreadable (the metric is no longer produced,
                or the after window holds too little to say), and comment the verdict with
                the numbers.
              - Declare no code: this ticket measures, it does not implement.

              so the session that takes it on can measure it without the reflection.
            - A refuted hypothesis whose change landed as code gets an issue filed with
              filedFromKey set to the hypothesis to revert or amend it, where that project's
              own repository is this one, and an instruction for a person where it is not.
            - Where the project reflected on binds no repository at all, say so under "What
              reflection could not see" as an instrumentation request, rather than filing
              work against code nobody can reach. Where an instrumentation gap lies in the
              tracker itself rather than in that project's own repository - something the
              tracker does not record or expose - name it under "What reflection could not
              see" as well, and also file it as an issue, with filedFromKey set to this
              reflection and no parent, in the project that GET /api/hatch/projects flags
              with "bindsTracker": true, if the board has one. Where none is flagged, leave
              it in the report as prose.
            - Name no board, repository, domain or person: nothing said here may be true of
              only the installation this happens to run on.
            - You judge and you file; you never edit the tree. Once the report is written
              and its issues are filed, declare no code with hatch pr <key> --none
              "<reason>", naming the report as the reason, and move this ticket to the
              column named at the end of this prompt.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DefaultPrompt alone, the way every reword before this one does
            // it: no row's own wording to disturb, no UpdatedAt to bump.
            migrationBuilder.Sql(
                $$"""
                UPDATE hatch."Playbooks"
                   SET "DefaultPrompt" = $new${{Replaced}}$new$
                 WHERE "DefaultPrompt" = $old${{Stock}}$old$;
                """);

            // Routing alone: a row still exactly on Breakdown -> Backlog for
            // reflection has not been moved by an operator, so it moves to the
            // review column - measured the way SeedConflictPlaybook measures
            // it, the non-deferred column immediately left of the first
            // terminal one - guarded against the same unique index
            // SeedReflectionPlaybook's own insert guards against, so this
            // never collides with a row already sitting on that transition.
            migrationBuilder.Sql("""
            WITH board AS (
                SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                  FROM hatch."Statuses"
                 WHERE NOT "IsDeferred"
            ),
            review AS (
                SELECT b."Id"
                  FROM board b
                 WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
            )
            UPDATE hatch."Playbooks" p
               SET "ToStatusId" = review."Id"
              FROM hatch."Statuses" f, hatch."Statuses" t, review
             WHERE f."Name" = 'Breakdown' AND t."Name" = 'Backlog'
               AND p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id"
               AND p."Types" = 'reflection'
               AND NOT EXISTS (
                SELECT 1 FROM hatch."Playbooks" o
                 WHERE o."FromStatusId" = p."FromStatusId" AND o."ToStatusId" = review."Id"
                   AND o."Types" = p."Types" AND o."Shape" = p."Shape"
                   AND o."ProjectId" IS NOT DISTINCT FROM p."ProjectId");
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

            migrationBuilder.Sql("""
            WITH board AS (
                SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                  FROM hatch."Statuses"
                 WHERE NOT "IsDeferred"
            ),
            review AS (
                SELECT b."Id"
                  FROM board b
                 WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
            )
            UPDATE hatch."Playbooks" p
               SET "ToStatusId" = t."Id"
              FROM hatch."Statuses" f, hatch."Statuses" t, review
             WHERE f."Name" = 'Breakdown' AND t."Name" = 'Backlog'
               AND p."FromStatusId" = f."Id" AND p."ToStatusId" = review."Id"
               AND p."Types" = 'reflection'
               AND NOT EXISTS (
                SELECT 1 FROM hatch."Playbooks" o
                 WHERE o."FromStatusId" = p."FromStatusId" AND o."ToStatusId" = t."Id"
                   AND o."Types" = p."Types" AND o."Shape" = p."Shape"
                   AND o."ProjectId" IS NOT DISTINCT FROM p."ProjectId");
            """);
        }
    }
}
