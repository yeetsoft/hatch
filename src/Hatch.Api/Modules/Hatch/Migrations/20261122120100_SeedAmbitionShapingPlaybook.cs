using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock row that shapes an ambition: what an agent is told when it is
    /// asked to turn a draft ambition into a title, a description and a table
    /// of capabilities.
    /// </summary>
    /// <remarks>
    /// <para>The row runs from <c>Breakdown</c> to <c>Backlog</c>, where a draft
    /// is turned into a specification, and is flagged <c>ShapesAmbition</c>, so
    /// the ordinary match never returns it: it sits beside the reflection row
    /// and the epic rows on the same edge without competing for
    /// <c>ambition</c>. The dispatcher learns to aim at the row in a later
    /// change; until it does, the row is only listed on the Playbooks page. It
    /// is read-only by construction: <c>DeniedTools</c> takes away the tools
    /// that edit the tree and the git commands that move it, so a session can
    /// judge and record but not change code. The list is the ambition pass
    /// row's own, copied as a literal, and so are <c>Model</c> and
    /// <c>Effort</c>.</para>
    ///
    /// <para>The columns are found by their stock names, the way
    /// <see cref="SeedReflectionPlaybook"/> finds them. The insert is guarded
    /// by <see cref="EfHatchPlaybook.Covers"/> spelled in SQL, narrowed to rows
    /// that shape an ambition: a flagged row already covering <c>ambition</c>
    /// on that edge, because its <c>Types</c> is empty or lists it, means
    /// nothing is seeded on top of it. A board missing either column gets
    /// nothing. <c>Shape</c> is left out of the guard on purpose, since
    /// ignoring it only ever seeds less. <see cref="Down"/> removes only a
    /// flagged row still saying <see cref="Stock"/> and still without an
    /// operator's prompt; an edited one is somebody's wording. The prompt is a
    /// literal here and not a reference: a migration is frozen text, and an
    /// edit to a constant somewhere else must not change what this one
    /// did.</para>
    /// </remarks>
    public partial class SeedAmbitionShapingPlaybook : Migration
    {
        /// <summary>The prompt as it is seeded, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are shaping a draft ambition. The prompt gives you what the person wrote: their words, the
            dreams they drew on and any files. You judge and you write. You never edit the tree, and your
            tools refuse if you try.

            - Read the draft's comments and events first. A previous run may have been aborted partway;
              continue it rather than starting again. Keep what the description already says in the
              ambition's shape, and add only what is missing.
            - Work from what the prompt's own section holds: the person's words, the dreams and the files.
            - Give the draft a title of a handful of words.
            - Write the description in this shape and no other: a summary of at most three short paragraphs
              before any heading; then `## What this is for`; then `## Capabilities`, a table with a number,
              what is true when it is done, and how to tell; then `## Principles`; then `## Out of scope`.
            - A capability says what is true, never what work to do. Its how-to-tell line is something a
              later session could check on the trunk. Each line is one line of at most 300 characters.
            - Add each capability as a row, in the table's order, with
              `hatch capability add <key> "<statement>" --how "<how to tell>"`.
            - Where the brief does not settle something that is the person's to choose, ask with `hatch ask`,
              one call per question, the choices named and one recommended. Do not guess, and do not fill a
              gap with a capability nobody asked for.
            - Record the dreams you drew on with `hatch api PUT /api/issues/<key>/dreams`.
            - List no epics and no work, and file nothing under the draft. Move it only to the column named
              at the end of this prompt, and never start it: starting an ambition is a person's.
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
                    ("FromStatusId", "ToStatusId", "Types", "Shape", "Prompt", "DefaultPrompt", "Model", "Effort", "DeniedTools", "ShapesAmbition", "CreatedAt", "UpdatedAt")
                SELECT f."Id", t."Id", 'ambition', 'any', NULL, $prompt${{Stock}}$prompt$, 'fable', 'max',
                       '["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]'::jsonb,
                       true, now(), now()
                  FROM hatch."Statuses" f, hatch."Statuses" t
                 WHERE f."Name" = 'Breakdown' AND t."Name" = 'Backlog'
                   AND NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id"
                       AND p."ShapesAmbition"
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
                 USING hatch."Statuses" f, hatch."Statuses" t
                 WHERE f."Name" = 'Breakdown' AND t."Name" = 'Backlog'
                   AND p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id"
                   AND p."Types" = 'ambition'
                   AND p."ShapesAmbition"
                   AND p."Prompt" IS NULL
                   AND p."DefaultPrompt" = $prompt${{Stock}}$prompt$;
                """);
        }
    }
}
