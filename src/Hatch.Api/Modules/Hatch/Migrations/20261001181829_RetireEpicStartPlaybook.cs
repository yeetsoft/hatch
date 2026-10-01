using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// The epic-start playbook - "you are starting an epic, which means
    /// choosing what starts" - is retired wherever the board now does what it
    /// said: HA-113's hop pulls an epic across the column before the WIP
    /// section with no session, so nothing is left to dispatch an agent there
    /// to pick a story.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Up"/> deletes only a row that still reads
    /// <see cref="Stock"/> byte for byte, scoped to <c>Types = 'epic'</c> and to
    /// a move that crosses into the WIP section - the same move the hop now
    /// carries for free (<c>Dispatch.HopKind</c>'s <c>epic</c> arm). A board
    /// with no WIP section turned on deletes nothing: the hop never fires
    /// there either, so the seeded row is still the only thing that dispatches
    /// an epic out of "To Do", and removing it would leave that board with no
    /// playbook at all. An edited row, or a stock row on a move the section
    /// does not touch, is left alone, the same call every migration here makes
    /// about a row somebody has changed.</para>
    ///
    /// <para>No <c>Shape</c> guard is needed, unlike <see cref="VerifyEpicPlaybook"/>'s
    /// insert: every row seeded before <c>PlaybookShape</c> (20260930171416)
    /// existed - including this one - and defaults to <c>'any'</c>, and this
    /// migration deletes on <c>Prompt</c> and <c>Types</c> alone, the same two
    /// columns <see cref="RewordReviewPlaybook"/> already guards its own
    /// update on, so <c>Shape</c> carries no distinguishing information
    /// here.</para>
    ///
    /// <para><see cref="Down"/> is not quite the mirror of a delete: there is
    /// no row left to restore the text onto, so it inserts the stock row back
    /// at the column before the first flagged, non-deferred, non-terminal
    /// column - the same landmark <see cref="VerifyEpicPlaybook"/> measures its
    /// own insert against - and only where no row already covers an epic on
    /// that move, so running <see cref="Down"/> twice, or on a board somebody
    /// has since given its own epic-start row, inserts nothing a second
    /// time.</para>
    /// </remarks>
    public partial class RetireEpicStartPlaybook : Migration
    {
        /// <summary>The prompt as the seed wrote it, and so as an unedited row still is.</summary>
        public const string Stock = """
            You are starting an epic, which means choosing what starts - not building it.

            - Read the epic, its children, and what has already happened to it. A previous
              run may have been aborted partway; continue it rather than starting again.
            - If it has no stories under it, it reached this column too early. Say so on the
              ticket, leave it where it is, and stop.
            - Choose the story that unblocks the most of the rest, respecting ready dates.
            - Leave that story where it is. The next increment picks it up on its own
              merits, and moving it now would claim work nobody has started.
            - Comment on the epic saying which story is next and why that one.

            Write no implementation code in this increment.
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only a row that still says Stock, word for word, scoped to an
            // epic and to a move that now crosses into the WIP section - the
            // same move the hop carries with no session. A board with no WIP
            // section turned on, or whose epic-start row sits on a move the
            // section does not touch, keeps the row unchanged.
            migrationBuilder.Sql(
                $$"""
                DELETE FROM hatch."Playbooks" p
                 USING hatch."Statuses" f, hatch."Statuses" t
                 WHERE p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id"
                   AND p."Types" = 'epic' AND p."Prompt" = $prompt${{Stock}}$prompt$
                   AND NOT (f."IsWip" AND NOT f."IsDeferred" AND NOT f."IsTerminal")
                   AND t."IsWip" AND NOT t."IsDeferred" AND NOT t."IsTerminal";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // There is no row left to put the text back onto, so this inserts
            // one - at the column before the first flagged, non-deferred,
            // non-terminal column - and only where no row already covers an
            // epic on that move.
            migrationBuilder.Sql(
                $$"""
                WITH board AS (
                    SELECT "Id", "IsWip", "IsTerminal", ROW_NUMBER() OVER (ORDER BY "SortOrder", "Id") AS n
                      FROM hatch."Statuses"
                     WHERE NOT "IsDeferred"
                ),
                target AS (
                    SELECT b."Id", b.n
                      FROM board b
                     WHERE b."IsWip" AND NOT b."IsTerminal"
                     ORDER BY b.n
                     LIMIT 1
                ),
                feeder AS (
                    SELECT b."Id"
                      FROM board b, target t
                     WHERE b.n = t.n - 1
                )
                INSERT INTO hatch."Playbooks"
                    ("FromStatusId", "ToStatusId", "Types", "Prompt", "Model", "Effort", "CreatedAt", "UpdatedAt")
                SELECT f."Id", t."Id", 'epic', $prompt${{Stock}}$prompt$, 'opus', 'high', now(), now()
                  FROM feeder f, target t
                 WHERE NOT EXISTS (
                    SELECT 1 FROM hatch."Playbooks" p
                     WHERE p."FromStatusId" = f."Id" AND p."ToStatusId" = t."Id" AND p."Types" = 'epic');
                """);
        }
    }
}
