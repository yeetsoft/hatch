using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class PlaybookProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybookProposals",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    IssueId = table.Column<long>(type: "bigint", nullable: true),
                    Author = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    State = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybookProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybookProposals_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlaybookProposalChanges",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProposalId = table.Column<int>(type: "integer", nullable: false),
                    PlaybookId = table.Column<int>(type: "integer", nullable: true),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    Patch = table.Column<string>(type: "jsonb", nullable: false),
                    BasedOnUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybookProposalChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybookProposalChanges_PlaybookProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "hatch",
                        principalTable: "PlaybookProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaybookProposalChanges_Playbooks_PlaybookId",
                        column: x => x.PlaybookId,
                        principalSchema: "hatch",
                        principalTable: "Playbooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookProposalChanges_PlaybookId",
                schema: "hatch",
                table: "PlaybookProposalChanges",
                column: "PlaybookId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookProposalChanges_ProposalId",
                schema: "hatch",
                table: "PlaybookProposalChanges",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookProposals_IssueId",
                schema: "hatch",
                table: "PlaybookProposals",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookProposals_State_CreatedAt",
                schema: "hatch",
                table: "PlaybookProposals",
                columns: new[] { "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaybookProposalChanges",
                schema: "hatch");

            migrationBuilder.DropTable(
                name: "PlaybookProposals",
                schema: "hatch");
        }
    }
}
