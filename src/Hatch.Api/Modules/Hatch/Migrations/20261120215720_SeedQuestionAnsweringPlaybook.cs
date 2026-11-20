using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The stock question-answering row: what an agent is told when it is
    /// asked to answer one question raised on an issue under an
    /// <c>ambition</c>, from what the ambition, its earlier answers and the
    /// trunk already say.
    /// </summary>
    /// <remarks>
    /// <para>The row runs from the implementation column to itself and is
    /// flagged <c>AnswersQuestions</c>, so the ordinary match never returns it:
    /// it sits beside the ambition pass row on the same edge without
    /// competing for <c>ambition</c>. The dispatcher learns to aim at the row
    /// in a later change; until it does, the row is only listed on the
    /// Playbooks page. It is read-only by construction: <c>DeniedTools</c>
    /// takes away the tools that edit the tree and the git commands that move
    /// it, so a session can judge and answer but not change code. The list is
    /// the reflection row's own, copied as a literal.</para>
    ///
    /// <para>The column is found by its stock name, <c>In Progress</c>, the way
    /// <see cref="SeedAmbitionPassPlaybook"/> finds it, and one status row
    /// supplies both ends of the edge. The insert is guarded by
    /// <see cref="EfHatchPlaybook.Covers"/> spelled in SQL, narrowed to rows
    /// that answer questions: a flagged row already covering <c>ambition</c>
    /// on that edge, because its <c>Types</c> is empty or lists it, means
    /// nothing is seeded on top of it. The pass row on the same edge does not
    /// block it. <c>Shape</c> is left out of the guard on purpose, since
    /// ignoring it only ever seeds less. <see cref="Down"/> removes only a
    /// flagged row still saying <see cref="Stock"/> and still without an
    /// operator's prompt; an edited one is somebody's wording. The prompt is a
    /// literal here and not a reference: a migration is frozen text, and an
    /// edit to a constant somewhere else must not change what this one
    /// did.</para>
    /// </remarks>
    public partial class SeedQuestionAnsweringPlaybook : Migration
    {
        /// <summary>The prompt as it is seeded, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are answering one question asked on an issue under an ambition. The prompt names
            the question. You judge and you answer. You never edit the tree, and your tools refuse
            if you try.

            - Read the ambition's description, its answered questions with
              `hatch api GET /api/issues/<key>/questions?open=false`, the issue the question is on,
              and the repository at the trunk.
            - Answer only when one of those settles the question. A guess is not an answer. A
              choice that is the operator's stays the operator's.
            - Say where you found it: the file, the docs page, the principle, or the earlier answer.
            - The first line of the answer is the decision, in an option's label where one fits. The
              lines after it say where it was found.
            - If none of them settles the question, leave it open. Comment on its issue that the
              context does not settle it, and say what you read. Then stop.
            - Never answer a question at `hold`.
            - Move nothing. Never change an issue's column or its dates.
            - Name no board, repository, domain or person: nothing said here may be true of only
              the installation this happens to run on.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dollar-quoted, because the prompt is prose and prose has
            // apostrophes in it.
            migrationBuilder.Sql(
                $$"""
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Shape", "Prompt", "DefaultPrompt", "Model", "Effort", "DeniedTools", "AnswersQuestions", "CreatedAt", "UpdatedAt")
                SELECT s."Id", s."Id", 'ambition', 'any', NULL, $prompt${{Stock}}$prompt$, 'fable', 'max',
                       '["Edit","Write","NotebookEdit","Bash(git add *)","Bash(git commit *)","Bash(git push *)","Bash(git checkout *)","Bash(git switch *)","Bash(git reset *)","Bash(git restore *)","Bash(git stash *)","Bash(git merge *)","Bash(git rebase *)","Bash(git cherry-pick *)","Bash(git clean *)"]'::jsonb,
                       true, now(), now()
                  FROM hatch."Statuses" s
                 WHERE s."Name" = 'In Progress'
                   AND NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = s."Id" AND p."ToStatusId" = s."Id"
                       AND p."AnswersQuestions"
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
                   AND p."AnswersQuestions"
                   AND p."Prompt" IS NULL
                   AND p."DefaultPrompt" = $prompt${{Stock}}$prompt$;
                """);
        }
    }
}
