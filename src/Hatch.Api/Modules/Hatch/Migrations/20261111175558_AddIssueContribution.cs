using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueContribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contributions",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    PullRequestUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HeadBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AuthorLogin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorAssociation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FiledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FiledBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    OriginInstance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginProjectKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    OriginKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EnvelopeVersion = table.Column<int>(type: "integer", nullable: true),
                    EnvelopeNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contributions_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_IssueId",
                schema: "hatch",
                table: "Contributions",
                column: "IssueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_OriginInstance_OriginKey",
                schema: "hatch",
                table: "Contributions",
                columns: new[] { "OriginInstance", "OriginKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_PullRequestUrl",
                schema: "hatch",
                table: "Contributions",
                column: "PullRequestUrl",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contributions",
                schema: "hatch");
        }
    }
}
