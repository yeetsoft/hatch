using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CapabilityLinks",
                schema: "hatch",
                columns: table => new
                {
                    CapabilityId = table.Column<long>(type: "bigint", nullable: false),
                    IssueId = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityLinks", x => new { x.CapabilityId, x.IssueId });
                    table.ForeignKey(
                        name: "FK_CapabilityLinks_Capabilities_CapabilityId",
                        column: x => x.CapabilityId,
                        principalSchema: "hatch",
                        principalTable: "Capabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapabilityLinks_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "hatch",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityLinks_IssueId",
                schema: "hatch",
                table: "CapabilityLinks",
                column: "IssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapabilityLinks",
                schema: "hatch");
        }
    }
}
