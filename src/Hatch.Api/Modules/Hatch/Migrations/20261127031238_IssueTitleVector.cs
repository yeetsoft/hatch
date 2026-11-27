using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class IssueTitleVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "TitleVector",
                schema: "hatch",
                table: "Issues",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "to_tsvector('english', \"Title\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_TitleVector",
                schema: "hatch",
                table: "Issues",
                column: "TitleVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Issues_TitleVector",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "TitleVector",
                schema: "hatch",
                table: "Issues");
        }
    }
}
