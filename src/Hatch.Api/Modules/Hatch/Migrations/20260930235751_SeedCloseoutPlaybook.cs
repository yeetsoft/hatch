using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The point at which recursive execution starts working end to end: the
    /// stock implementation row narrows to <c>leaf</c>, and a new <c>parent</c>
    /// row carries the closeout prompt beside it.
    /// </summary>
    /// <remarks>
    /// <para>Before this migration the one row seeded onto the implementation
    /// -&gt; review transition with <c>Types = ''</c> (<c>Playbooks.cs</c>,
    /// 20260903204217) fired for every issue regardless of whether it had
    /// children. <see cref="EfHatchPlaybook.Covers"/> already tells a
    /// <c>leaf</c>-shaped row from a <c>parent</c>-shaped one and already
    /// prefers the more specific of two matches - this migration is the data
    /// that puts that machinery to work on this transition for the first time:
    /// narrowing the stock row to <c>leaf</c> so it only ever fires for a
    /// childless issue, and inserting a sibling that only ever fires for one
    /// with children, all of them closed (an issue's own gate keeps one with an
    /// open child off this transition at all - this migration does not touch
    /// that gate, only what happens once it has already passed).</para>
    ///
    /// <para>Both writes are guarded on the transition and on
    /// <c>Types = ''</c>, so the epic-specific row seeded beside the stock one
    /// is left alone: verifying that an epic's stories add up is already a
    /// reading-and-checking job regardless of shape, and narrowing it too would
    /// stop it firing for a childless epic. The narrow additionally requires
    /// <c>Shape = 'any'</c>, so an install that has already formed its own
    /// opinion about that row's shape is left alone, and the insert requires no
    /// row already exist for the transition and the <c>parent</c> shape - the
    /// same <c>NOT EXISTS</c> guard <see cref="SeedConflictPlaybook"/> uses, for
    /// the same reason: an operator who deletes or edits a seeded row has
    /// turned that piece off, and nothing here puts it back.</para>
    ///
    /// <para>The review column is measured in SQL the way
    /// <see cref="SeedConflictPlaybook"/> and <c>Columns.AwaitingReview</c>
    /// measure it: the non-deferred column immediately left of the first
    /// terminal one, in the board's own order. The implementation column is
    /// measured the way <c>Columns.Implementation</c> measures it: the
    /// non-deferred column whose own next non-deferred column is the review
    /// column. An install with no terminal column has no review column and
    /// this migration writes nothing. The prompt is a literal here and not a
    /// reference: a migration is frozen text, and an edit to a constant
    /// somewhere else must not change what this one wrote.</para>
    ///
    /// <para>Model and effort: <c>sonnet</c> and <c>medium</c> - the same pair
    /// the epic-closeout row beside it was seeded with (<c>Playbooks.cs</c>),
    /// because checking that a story's or a task's children add up to what was
    /// asked is the same reading-and-checking job as checking that an epic's
    /// do, just one altitude down. It writes no code and makes no judgement
    /// call finer than "does this match what was asked" - not a job that
    /// benefits from a heavier model or more effort than that.</para>
    /// </remarks>
    public partial class SeedCloseoutPlaybook : Migration
    {
        /// <summary>The closeout prompt as it is seeded, and so as an unedited row still is.</summary>
        public const string Closeout = """
            Every child below is closed. This increment checks that what was done is what
            this issue asked for, and writes no implementation code.

            - Read the issue's own acceptance criteria, then each child - its description,
              its comments, and the pull request recorded on it (hatch show <key>, and
              hatch api GET /api/hatch/issues/<key> for the rest).
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Narrow the stock implementation row to leaf. Guarded on the
            // transition, on Types = '' (this is the stock row, not the
            // epic-specific one beside it) and on Shape = 'any' (an install
            // that has already formed an opinion about this row's shape is
            // left alone).
            migrationBuilder.Sql(
                """
                WITH board AS (
                    SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                review AS (
                    SELECT b."Id", b.n
                      FROM board b
                     WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
                ),
                impl AS (
                    SELECT b."Id"
                      FROM board b, review r
                     WHERE b.n = r.n - 1
                )
                UPDATE hatch."Playbooks" p
                   SET "Shape" = 'leaf', "UpdatedAt" = now()
                  FROM impl i, review r
                 WHERE p."FromStatusId" = i."Id" AND p."ToStatusId" = r."Id"
                   AND p."Types" = '' AND p."Shape" = 'any';
                """);

            // 2) Insert the closeout row, once. Dollar-quoted, because the
            // prompt is prose and prose has apostrophes in it.
            migrationBuilder.Sql(
                $$"""
                WITH board AS (
                    SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                review AS (
                    SELECT b."Id", b.n
                      FROM board b
                     WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
                ),
                impl AS (
                    SELECT b."Id"
                      FROM board b, review r
                     WHERE b.n = r.n - 1
                )
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Shape", "Prompt", "Model", "Effort", "CreatedAt", "UpdatedAt")
                SELECT i."Id", r."Id", '', 'parent', $prompt${{Closeout}}$prompt$, 'sonnet', 'medium', now(), now()
                  FROM impl i, review r
                 WHERE NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = i."Id" AND p."ToStatusId" = r."Id"
                       AND p."Types" = '' AND p."Shape" = 'parent');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) Put the leaf back to any, but only while the closeout row
            // about to be removed below is still the one this migration
            // wrote - an edited or already-deleted one leaves the leaf as it
            // found it.
            migrationBuilder.Sql(
                $$"""
                WITH board AS (
                    SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                review AS (
                    SELECT b."Id", b.n
                      FROM board b
                     WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
                ),
                impl AS (
                    SELECT b."Id"
                      FROM board b, review r
                     WHERE b.n = r.n - 1
                )
                UPDATE hatch."Playbooks" p
                   SET "Shape" = 'any', "UpdatedAt" = now()
                  FROM impl i, review r
                 WHERE p."FromStatusId" = i."Id" AND p."ToStatusId" = r."Id"
                   AND p."Types" = '' AND p."Shape" = 'leaf'
                   AND EXISTS (
                      SELECT 1 FROM hatch."Playbooks" c
                       WHERE c."FromStatusId" = i."Id" AND c."ToStatusId" = r."Id"
                         AND c."Types" = '' AND c."Shape" = 'parent'
                         AND c."Prompt" = $prompt${{Closeout}}$prompt$);
                """);

            // 2) The mirror of the insert's guard: only a row still saying
            // exactly what this migration wrote goes. An edited one is
            // somebody's wording.
            migrationBuilder.Sql(
                $$"""
                WITH board AS (
                    SELECT "Id", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                review AS (
                    SELECT b."Id", b.n
                      FROM board b
                     WHERE b.n = (SELECT MIN(n) FROM board WHERE "IsTerminal") - 1
                ),
                impl AS (
                    SELECT b."Id"
                      FROM board b, review r
                     WHERE b.n = r.n - 1
                )
                DELETE FROM hatch."Playbooks" p
                 USING impl i, review r
                 WHERE p."FromStatusId" = i."Id" AND p."ToStatusId" = r."Id"
                   AND p."Types" = '' AND p."Shape" = 'parent'
                   AND p."Prompt" = $prompt${{Closeout}}$prompt$;
                """);
        }
    }
}
