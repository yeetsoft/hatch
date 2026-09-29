using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class TrunkBuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrunkBuilds",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Remote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Canonical = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Trunk = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ShaSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Failing = table.Column<string>(type: "jsonb", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Runner = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CheckedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    BugIssueId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrunkBuilds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrunkBuilds_Issues_BugIssueId",
                        column: x => x.BugIssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrunkBuilds_BugIssueId",
                schema: "hatch",
                table: "TrunkBuilds",
                column: "BugIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_TrunkBuilds_Canonical_Trunk",
                schema: "hatch",
                table: "TrunkBuilds",
                columns: new[] { "Canonical", "Trunk" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrunkBuilds",
                schema: "hatch");
        }
    }
}
