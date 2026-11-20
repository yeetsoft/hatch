using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock ambition pass: what an agent is told when it is handed an
    /// <c>ambition</c> issue, a look at what the trunk holds today against the
    /// capabilities the ambition lists, and a choice of the next epic to carve.
    /// </summary>
    /// <remarks>
    /// <para>The row runs from the implementation column to itself. A pass is
    /// not a step in an issue's life: the ambition stays where it stands, so
    /// there is no column to move it to. The dispatcher learns to match that
    /// self-edge in a later change; until it does, the row is only listed on the
    /// Playbooks page. It is read-only by construction: <c>DeniedTools</c>
    /// takes away the tools that edit the tree and the git commands that move
    /// it, so a pass can judge and file but not change code. The list is the
    /// reflection row's own, copied as a literal.</para>
    ///
    /// <para>The column is found by its stock name, <c>In Progress</c>, the way
    /// <see cref="SeedReflectionPlaybook"/> finds <c>Breakdown</c> and
    /// <c>Backlog</c>, and one status row supplies both ends of the edge. The
    /// insert is guarded by <see cref="EfHatchPlaybook.Covers"/> spelled in SQL:
    /// a row already covering <c>ambition</c> on that edge, because its
    /// <c>Types</c> is empty or lists it, means nothing is seeded on top of it.
    /// <c>Shape</c> is left out of the guard on purpose, since ignoring it only
    /// ever seeds less. <c>Model</c> and <c>Effort</c> are the reflection row's.
    /// <see cref="Down"/> removes only a row still saying <see cref="Stock"/>
    /// and still without an operator's prompt; an edited one is somebody's
    /// wording. The prompt is a literal here and not a reference: a migration
    /// is frozen text, and an edit to a constant somewhere else must not change
    /// what this one did.</para>
    /// </remarks>
    public partial class SeedAmbitionPassPlaybook : Migration
    {
        /// <summary>The prompt as it is seeded, and so as an unedited row still is.</summary>
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, because the prompt is prose and prose has
            // apostrophes in it.
            migrationBuilder.Sql(
                $$"""
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Shape", "Prompt", "DefaultPrompt", "Model", "Effort", "DeniedTools", "CreatedAt", "UpdatedAt")
                SELECT s."Id", s."Id", 'ambition', 'any', NULL, $prompt${{Stock}}$prompt$, 'fable', 'max',
                       '["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]'::jsonb,
                       now(), now()
                  FROM hatch."Statuses" s
                 WHERE s."Name" = 'In Progress'
                   AND NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = s."Id" AND p."ToStatusId" = s."Id"
                       AND (p."Types" = '' OR 'ambition' = ANY(string_to_array(p."Types", ','))));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror of the guard: only a row on that edge still saying
            // exactly what this migration wrote goes. An edited one is
            // somebody's wording.
            migrationBuilder.Sql(
                $$"""
                DELETE FROM hatch."Playbooks" p
                 USING hatch."Statuses" s
                 WHERE s."Name" = 'In Progress'
                   AND p."FromStatusId" = s."Id" AND p."ToStatusId" = s."Id"
                   AND p."Types" = 'ambition'
                   AND p."Prompt" IS NULL
                   AND p."DefaultPrompt" = $prompt${{Stock}}$prompt$;
                """);
        }
    }
}
