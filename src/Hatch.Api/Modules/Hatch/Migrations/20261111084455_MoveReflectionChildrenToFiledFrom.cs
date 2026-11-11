using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Moves every issue filed under a reflection to the new shape: linked to
    /// the reflection by <c>FiledFromId</c>, and taken out from under it.
    /// </summary>
    /// <remarks>
    /// <para>The rule is "children of reflections", never a list of keys: every
    /// issue whose <c>ParentId</c> is an issue of type <c>reflection</c>, in
    /// every project and in every column, closed ones included. Direct children
    /// only - a grandchild's parent is a story or a task, not a reflection. For
    /// each one it sets <c>FiledFromId</c> to the reflection, clears
    /// <c>ParentId</c> and stamps <c>UpdatedAt</c>, as an API parent change
    /// does, and writes the two events such an edit would have written:
    /// <c>filed_from_changed</c> and <c>parent_changed</c>, with payloads of
    /// display keys exactly as <c>IssuesController</c> writes them. A child
    /// that was already linked to some other issue records that issue's key in
    /// <c>from</c>; one already linked to the reflection itself writes only
    /// <c>parent_changed</c>, since nothing about its link changed. The actor
    /// is the literal of <see cref="Hatch.Api.Modules.Hatch.ReflectionCadenceJob.Actor"/>,
    /// the board, which this migration cannot reference from SQL.</para>
    ///
    /// <para>Running it twice writes nothing the second time, by construction:
    /// the move clears <c>ParentId</c>, so the selector finds no child of a
    /// reflection left to move and no event is written twice.</para>
    ///
    /// <para>Pre-approval travels by parentage alone, so a child that inherited
    /// it from a pre-approved reflection no longer does once it has no parent.
    /// That is the rule of the new shape, not something this migration patches
    /// around.</para>
    ///
    /// <para>It names no issue, project or installation, and it closes nothing:
    /// a reflection left with no children is for a person to close.</para>
    /// </remarks>
    public partial class MoveReflectionChildrenToFiledFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One statement, so the three data-modifying CTEs read the same
            // snapshot: "moved" is the pre-update rows, which is how the old
            // FiledFromId is still there to be recorded in each event.
            migrationBuilder.Sql("""
            WITH moved AS (
                SELECT c."Id" AS child,
                       c."FiledFromId" AS old_from,
                       p."Id" AS parent,
                       pp."Key" || '-' || p."Number"::text AS parent_key,
                       CASE WHEN o."Id" IS NULL THEN NULL
                            ELSE op."Key" || '-' || o."Number"::text END AS old_key
                FROM hatch."Issues" c
                JOIN hatch."Issues" p ON p."Id" = c."ParentId" AND p."Type" = 'reflection'
                JOIN hatch."Projects" pp ON pp."Id" = p."ProjectId"
                LEFT JOIN hatch."Issues" o ON o."Id" = c."FiledFromId"
                LEFT JOIN hatch."Projects" op ON op."Id" = o."ProjectId"
            ), filed_from_events AS (
                INSERT INTO hatch."IssueEvents" ("IssueId", "Actor", "Kind", "Payload", "At")
                SELECT m.child, 'the board', 'filed_from_changed',
                       jsonb_build_object('from', m.old_key, 'to', m.parent_key), now()
                FROM moved m
                WHERE m.old_from IS DISTINCT FROM m.parent
            ), parent_events AS (
                INSERT INTO hatch."IssueEvents" ("IssueId", "Actor", "Kind", "Payload", "At")
                SELECT m.child, 'the board', 'parent_changed',
                       jsonb_build_object('from', m.parent_key, 'to', NULL::text), now()
                FROM moved m
            )
            UPDATE hatch."Issues" i
            SET "FiledFromId" = m.parent, "ParentId" = NULL, "UpdatedAt" = now()
            FROM moved m
            WHERE i."Id" = m.child;
            """);
        }

        /// <summary>
        /// Left empty on purpose. The move is a reshaping the events record;
        /// reversing it by guessing which <c>FiledFromId</c> rows were this
        /// migration's would re-parent issues a person has since linked.
        /// </summary>
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
