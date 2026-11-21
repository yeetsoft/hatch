using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChangeEventId",
                schema: "hatch",
                table: "WorkLogEntries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChangeOutcomes",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    EpicId = table.Column<long>(type: "bigint", nullable: false),
                    ChangeEventId = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Why = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeOutcomes_Issues_EpicId",
                        column: x => x.EpicId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChangeOutcomes_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkLogEntries_ChangeEventId",
                schema: "hatch",
                table: "WorkLogEntries",
                column: "ChangeEventId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeOutcomes_EpicId",
                schema: "hatch",
                table: "ChangeOutcomes",
                column: "EpicId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeOutcomes_IssueId_ChangeEventId_EpicId",
                schema: "hatch",
                table: "ChangeOutcomes",
                columns: new[] { "IssueId", "ChangeEventId", "EpicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeOutcomes",
                schema: "hatch");

            migrationBuilder.DropIndex(
                name: "IX_WorkLogEntries_ChangeEventId",
                schema: "hatch",
                table: "WorkLogEntries");

            migrationBuilder.DropColumn(
                name: "ChangeEventId",
                schema: "hatch",
                table: "WorkLogEntries");
        }
    }
}
