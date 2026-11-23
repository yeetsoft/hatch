using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatMessages",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<int>(type: "integer", nullable: false),
                    WorkspaceId = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SentRevision = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PendingSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DroppedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRefusal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatRooms_RoomId",
                        column: x => x.RoomId,
                        principalSchema: "hatch",
                        principalTable: "ChatRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatWorkspaceProjects_WorkspaceId_ProjectId",
                        columns: x => new { x.WorkspaceId, x.ProjectId },
                        principalSchema: "hatch",
                        principalTable: "ChatWorkspaceProjects",
                        principalColumns: new[] { "WorkspaceId", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatSubjects",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSubjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_PendingSince",
                schema: "hatch",
                table: "ChatMessages",
                column: "PendingSince",
                filter: "\"DroppedAt\" IS NULL AND \"SentRevision\" < \"Revision\"");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_RoomId_Kind_Subject",
                schema: "hatch",
                table: "ChatMessages",
                columns: new[] { "RoomId", "Kind", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_WorkspaceId_ProjectId",
                schema: "hatch",
                table: "ChatMessages",
                columns: new[] { "WorkspaceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSubjects_Kind_Subject",
                schema: "hatch",
                table: "ChatSubjects",
                columns: new[] { "Kind", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages",
                schema: "hatch");

            migrationBuilder.DropTable(
                name: "ChatSubjects",
                schema: "hatch");
        }
    }
}
