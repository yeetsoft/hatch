using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChangelogTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangelogTasks",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    SourceIssueId = table.Column<long>(type: "bigint", nullable: true),
                    Canonical = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangelogTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangelogTasks_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChangelogTasks_Issues_SourceIssueId",
                        column: x => x.SourceIssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangelogTasks_Canonical",
                schema: "hatch",
                table: "ChangelogTasks",
                column: "Canonical");

            migrationBuilder.CreateIndex(
                name: "IX_ChangelogTasks_IssueId",
                schema: "hatch",
                table: "ChangelogTasks",
                column: "IssueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangelogTasks_SourceIssueId",
                schema: "hatch",
                table: "ChangelogTasks",
                column: "SourceIssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangelogTasks",
                schema: "hatch");
        }
    }
}
