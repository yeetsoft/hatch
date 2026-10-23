using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the reflection row to file a tracker-instrumentation gap as an issue
    /// in the project the board flags as the tracker's own, instead of leaving it
    /// only in the report.
    /// </summary>
    /// <remarks>
    /// <para>A gap in what the tracker records or exposes is not work against the
    /// repository the reflected project binds, so the old text had it written up
    /// as prose and nothing more. The new text names it in the report as before and
    /// also files it, with no parent, in the project <c>GET /api/hatch/projects</c>
    /// flags <c>bindsTracker</c>, and leaves it as prose where none is flagged.</para>
    ///
    /// <para>Like <see cref="RewordReflectionPlaybook"/> this guards and writes
    /// <c>DefaultPrompt</c> alone: a row whose <c>DefaultPrompt</c> is
    /// byte-identical to <see cref="Stock"/> has nobody's wording in it, an edited
    /// row's <c>Prompt</c> is left alone, and <c>UpdatedAt</c> is not touched. The
    /// guard is on the immediately previous text, so a row that never took
    /// <see cref="RewordReflectionPlaybook"/> is left for it.</para>
    /// </remarks>
    public partial class ReflectionFilesTrackerGaps : Migration
    {
        /// <summary>The whole reflection prompt as it stands today, a frozen copy of <see cref="RewordReflectionPlaybook.Replaced"/>.</summary>
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
              work against code nobody can reach.
            - Name no board, repository, domain or person: nothing said here may be true of
              only the installation this happens to run on.
            - You judge and you file; you never edit the tree. Move this ticket to the
              column named at the end of this prompt once the report is written and its
              issues are filed.
            """;

        /// <summary>What it says now: a tracker-instrumentation gap is also filed in the project flagged as the tracker's own.</summary>
        public const string Replaced = """
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DefaultPrompt alone, the way the implementation reword does it:
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
