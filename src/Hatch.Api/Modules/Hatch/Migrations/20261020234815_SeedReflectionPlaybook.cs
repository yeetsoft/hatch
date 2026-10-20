using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock reflection playbook: what an agent is told when it is handed a
    /// <c>reflection</c> issue, a look back at how the board has been run rather
    /// than at any one ticket on it.
    /// </summary>
    /// <remarks>
    /// <para>The row sits on <c>Breakdown</c> to <c>Backlog</c>, where an issue
    /// is turned from a draft into a specification, and is read-only by
    /// construction: <c>DeniedTools</c> takes away the tools that edit the tree
    /// and the git commands that move it, so a reflection can judge and file but
    /// not change code. It is seeded for the reason the others are: a feature
    /// that does nothing until somebody fills in a table ships broken.</para>
    ///
    /// <para>The columns are found by name, the way the three
    /// <c>Breakdown</c> to <c>Backlog</c> rows seeded in 20260903204217 and
    /// repointed in 20260906201802 are, and the insert is guarded by
    /// <see cref="EfHatchPlaybook.Covers"/> spelled in SQL: a row already
    /// covering <c>reflection</c> on that transition, because its <c>Types</c> is
    /// empty or lists it, means nothing is seeded on top of it. <c>Shape</c> is
    /// left out of the guard on purpose - a reflection may be a leaf or a parent,
    /// and ignoring the shape only ever seeds less. <c>Model</c> and
    /// <c>Effort</c> are the stock epic-breakdown row's own. <see cref="Down"/>
    /// removes only a row still saying <see cref="Stock"/>; an edited one is
    /// somebody's wording. The prompt is a literal here and not a reference: a
    /// migration is frozen text, and an edit to a constant somewhere else must
    /// not change what this one did.</para>
    /// </remarks>
    public partial class SeedReflectionPlaybook : Migration
    {
        /// <summary>The prompt as it is seeded, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are reflecting on how this board has been run, not on any one ticket on it.

            - Read this ticket's own comments and events first. A previous run may have been
              aborted partway; continue it rather than starting again, and if it ran to
              completion and only the status was left behind, say so and move it on.
            - Read the window this ticket's own brief names - the thirty days before it was
              filed, where it names none. Read it through the folds, not the rows: spend by
              playbook, flow by column, the timeline, the queue, utilization, the activity
              trail, and which playbooks spoke in it.
            - Rewrite this ticket's description as the report, in this shape and no other:
              Window; What ran and what it cost; Where the time went; Findings, each with
              its evidence; Recommendations, each either filed as an issue under this
              reflection or written as an instruction for a person; Hypotheses, each with a
              metric, a baseline, the change expected and a date to read it after; What
              reflection could not see.
            - File each recommendation that is a change to make as an issue under this
              reflection, in the project it reflects on - an epic, a story, a task or a bug,
              whichever its size calls for. Propose code changes only where that project's
              own repository is this one, told by the tree this session stands in rather
              than by name; where it is not, write the finding as an instruction for a
              person instead. File each hypothesis the same way, with a readyAt set to its
              read-after date, so it waits until then.
            - Where the project reflected on binds no repository at all, say so under "What
              reflection could not see" as an instrumentation request, rather than filing
              work against code nobody can reach.
            - Name no board, repository, domain or person: nothing said here may be true of
              only the installation this happens to run on.
            - You judge and you file; you never edit the tree. Move this ticket to the
              column named at the end of this prompt once the report is written and its
              issues are filed.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, because the prompt is prose and prose has
            // apostrophes in it.
            migrationBuilder.Sql(
                $$"""
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Shape", "Prompt", "DefaultPrompt", "Model", "Effort", "DeniedTools", "CreatedAt", "UpdatedAt")
                SELECT f."Id", t."Id", 'reflection', 'any', NULL, $prompt${{Stock}}$prompt$, 'fable', 'max',
                       '["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]'::jsonb,
                       now(), now()
                  FROM hatch."Statuses" f, hatch."Statuses" t
                 WHERE f."Name" = 'Breakdown' AND t."Name" = 'Backlog'
                   AND NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id"
                       AND (p."Types" = '' OR 'reflection' = ANY(string_to_array(p."Types", ','))));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row on that transition still
            // saying exactly what this migration wrote goes. An edited one is
            // somebody's wording.
            migrationBuilder.Sql(
                $$"""
                DELETE FROM hatch."Playbooks" p
                 USING hatch."Statuses" f, hatch."Statuses" t
                 WHERE f."Name" = 'Breakdown' AND t."Name" = 'Backlog'
                   AND p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id"
                   AND p."Types" = 'reflection'
                   AND p."DefaultPrompt" = $prompt${{Stock}}$prompt$;
                """);
        }
    }
}
