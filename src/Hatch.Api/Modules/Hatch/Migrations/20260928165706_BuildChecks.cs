using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class BuildChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildChecks",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    Remote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Canonical = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ShaSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Failing = table.Column<string>(type: "jsonb", nullable: true),
                    PushedByIncrement = table.Column<bool>(type: "boolean", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Runner = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CheckedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildChecks_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildChecks_IssueId_Canonical",
                schema: "hatch",
                table: "BuildChecks",
                columns: new[] { "IssueId", "Canonical" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildChecks",
                schema: "hatch");
        }
    }
}
