using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuePreviousTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousTitles",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Every issue's previous titles, from its own retitled events,
            // oldest-replaced first - the same order Retitle() builds them in
            // going forward. For each title it once carried, the latest
            // occurrence (by At, then Id) decides whether it is still a
            // previous title at all: one equal to the issue's current title,
            // or blank, is dropped. A line break is folded to a space, the
            // same rule Retitle() applies.
            migrationBuilder.Sql("""
            UPDATE hatch."Issues" i
            SET "PreviousTitles" = agg."PreviousTitles"
            FROM (
                SELECT per_title."IssueId",
                       string_agg(per_title."Title", E'\n' ORDER BY per_title."LastAt", per_title."LastId") AS "PreviousTitles"
                FROM (
                    SELECT DISTINCT ON (e."IssueId", regexp_replace(e."Payload"->>'from', '[\r\n]+', ' ', 'g'))
                           e."IssueId" AS "IssueId",
                           regexp_replace(e."Payload"->>'from', '[\r\n]+', ' ', 'g') AS "Title",
                           e."At" AS "LastAt",
                           e."Id" AS "LastId"
                    FROM hatch."IssueEvents" e
                    JOIN hatch."Issues" cur ON cur."Id" = e."IssueId"
                    WHERE e."Kind" = 'retitled'
                      AND regexp_replace(e."Payload"->>'from', '[\r\n]+', ' ', 'g') <> ''
                      AND regexp_replace(e."Payload"->>'from', '[\r\n]+', ' ', 'g') <> cur."Title"
                    ORDER BY e."IssueId", regexp_replace(e."Payload"->>'from', '[\r\n]+', ' ', 'g'), e."At" DESC, e."Id" DESC
                ) per_title
                GROUP BY per_title."IssueId"
            ) agg
            WHERE agg."IssueId" = i."Id";
            """);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "hatch",
                table: "Issues",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', \"Title\"), 'A') || setweight(to_tsvector('english', \"Description\"), 'B') || setweight(to_tsvector('english', \"PreviousTitles\"), 'B')",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldComputedColumnSql: "setweight(to_tsvector('english', \"Title\"), 'A') || setweight(to_tsvector('english', \"Description\"), 'B')",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousTitles",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "hatch",
                table: "Issues",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', \"Title\"), 'A') || setweight(to_tsvector('english', \"Description\"), 'B')",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldComputedColumnSql: "setweight(to_tsvector('english', \"Title\"), 'A') || setweight(to_tsvector('english', \"Description\"), 'B') || setweight(to_tsvector('english', \"PreviousTitles\"), 'B')",
                oldStored: true);
        }
    }
}
