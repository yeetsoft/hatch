using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock row that works out changes: what an agent is told when it is
    /// asked to work out what one change to an ambition does to its epics in
    /// flight.
    /// </summary>
    /// <remarks>
    /// <para>The row runs from the implementation column to itself and is
    /// flagged <c>WorksOutChanges</c>, so the ordinary match never returns it:
    /// it sits beside the ambition pass row and the answering row on the same
    /// edge without competing for <c>ambition</c>. The dispatcher learns to aim
    /// at the row in a later change; until it does, the row is only listed on
    /// the Playbooks page. It is read-only by construction: <c>DeniedTools</c>
    /// takes away the tools that edit the tree and the git commands that move
    /// it, so a session can judge and record but not change code. The list is
    /// the reflection row's own, copied as a literal.</para>
    ///
    /// <para>The column is found by its stock name, <c>In Progress</c>, the way
    /// <see cref="SeedAmbitionPassPlaybook"/> finds it, and one status row
    /// supplies both ends of the edge. The insert is guarded by
    /// <see cref="EfHatchPlaybook.Covers"/> spelled in SQL, narrowed to rows
    /// that work out changes: a flagged row already covering <c>ambition</c>
    /// on that edge, because its <c>Types</c> is empty or lists it, means
    /// nothing is seeded on top of it. The pass row and the answering row on
    /// the same edge do not block it. <c>Shape</c> is left out of the guard on
    /// purpose, since ignoring it only ever seeds less. <see cref="Down"/>
    /// removes only a flagged row still saying <see cref="Stock"/> and still
    /// without an operator's prompt; an edited one is somebody's wording. The
    /// prompt is a literal here and not a reference: a migration is frozen
    /// text, and an edit to a constant somewhere else must not change what
    /// this one did.</para>
    /// </remarks>
    public partial class SeedWorksOutChangesPlaybook : Migration
    {
        /// <summary>The prompt as it is seeded, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are working out what one change to an ambition does to its epics in flight. The prompt
            names the ambition, the change and the epics. You judge and you record. You never edit the
            tree, and your tools refuse if you try.

            - Read the ambition, the change, and each named epic's brief with `hatch show`.
            - Give each named epic exactly one outcome, with one sentence of why:
              - `carries_on`: the change touches what the epic serves, and the epic's work still serves
                the ambition as it now reads.
              - `not_touched`: nothing the epic serves was changed.
              - `serves_nothing`: nothing the ambition now asks for is served by what the epic builds.
            - For an epic that carries on, comment on that epic with what the change now asks of it.
            - Record nothing for an epic the prompt did not name.
            - Move nothing, file nothing, and change no capability and no link. Shelving an epic is a
              person's press.
            - Name no board, repository, domain or person: nothing said here may be true of only the
              installation this happens to run on.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, because the prompt is prose and prose has
            // apostrophes in it.
            migrationBuilder.Sql(
                $$"""
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Shape", "Prompt", "DefaultPrompt", "Model", "Effort", "DeniedTools", "WorksOutChanges", "CreatedAt", "UpdatedAt")
                SELECT s."Id", s."Id", 'ambition', 'any', NULL, $prompt${{Stock}}$prompt$, 'fable', 'max',
                       '["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]'::jsonb,
                       true, now(), now()
                  FROM hatch."Statuses" s
                 WHERE s."Name" = 'In Progress'
                   AND NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = s."Id" AND p."ToStatusId" = s."Id"
                       AND p."WorksOutChanges"
                       AND (p."Types" = '' OR 'ambition' = ANY(string_to_array(p."Types", ','))));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a flagged row on that edge still
            // saying exactly what this migration wrote goes. An edited one is
            // somebody's wording.
            migrationBuilder.Sql(
                $$"""
                DELETE FROM hatch."Playbooks" p
                 USING hatch."Statuses" s
                 WHERE s."Name" = 'In Progress'
                   AND p."FromStatusId" = s."Id" AND p."ToStatusId" = s."Id"
                   AND p."Types" = 'ambition'
                   AND p."WorksOutChanges"
                   AND p."Prompt" IS NULL
                   AND p."DefaultPrompt" = $prompt${{Stock}}$prompt$;
                """);
        }
    }
}
