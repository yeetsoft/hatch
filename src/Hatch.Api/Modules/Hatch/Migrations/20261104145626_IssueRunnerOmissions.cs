using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class IssueRunnerOmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueRunnerOmissions",
                schema: "hatch",
                columns: table => new
                {
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    RunnerName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    By = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueRunnerOmissions", x => new { x.IssueId, x.RunnerName });
                    table.ForeignKey(
                        name: "FK_IssueRunnerOmissions_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueRunnerOmissions",
                schema: "hatch");
        }
    }
}
