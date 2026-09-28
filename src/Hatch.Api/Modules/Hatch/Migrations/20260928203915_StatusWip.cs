using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <remarks>
    /// <para>Joins the seed on name, the way <c>TheFlow</c> does: <c>In Progress</c>
    /// and <c>In Review</c> are the stock board's two WIP columns, and a board
    /// that has renamed either is one whose operator gets to decide for
    /// themselves rather than one this migration guesses at. Guarded the same way
    /// - <c>NOT "IsDeferred" AND NOT "IsTerminal"</c> - so it never writes a flag
    /// <c>WipController</c> would itself refuse.</para>
    ///
    /// <para>No limit row. A number is a judgement about how much the operator
    /// wants outstanding at once, and this migration has no opinion - the column
    /// is flagged and the WIP limit field on the Statuses page reads blank until
    /// somebody types one.</para>
    /// </remarks>
    public partial class StatusWip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWip",
                schema: "hatch",
                table: "Statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WipLimits",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Types = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Limit = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WipLimits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WipLimits_Types",
                schema: "hatch",
                table: "WipLimits",
                column: "Types",
                unique: true);

            migrationBuilder.Sql(
                """
                UPDATE hatch."Statuses" SET "IsWip" = true
                 WHERE "Name" IN ('In Progress', 'In Review') AND NOT "IsDeferred" AND NOT "IsTerminal";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WipLimits",
                schema: "hatch");

            migrationBuilder.DropColumn(
                name: "IsWip",
                schema: "hatch",
                table: "Statuses");
        }
    }
}
