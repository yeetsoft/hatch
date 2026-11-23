using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatRooms",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkspaceId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Changelog = table.Column<bool>(type: "boolean", nullable: false),
                    Questions = table.Column<bool>(type: "boolean", nullable: false),
                    Problems = table.Column<bool>(type: "boolean", nullable: false),
                    UnownedProblems = table.Column<bool>(type: "boolean", nullable: false),
                    AllProjects = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatRooms_ChatWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "hatch",
                        principalTable: "ChatWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatWorkspaceProjects",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkspaceId = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatWorkspaceProjects", x => x.Id);
                    table.UniqueConstraint("AK_ChatWorkspaceProjects_WorkspaceId_ProjectId", x => new { x.WorkspaceId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_ChatWorkspaceProjects_ChatWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "hatch",
                        principalTable: "ChatWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatWorkspaceProjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "hatch",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatRoomProjects",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<int>(type: "integer", nullable: false),
                    WorkspaceId = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRoomProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatRoomProjects_ChatRooms_RoomId",
                        column: x => x.RoomId,
                        principalSchema: "hatch",
                        principalTable: "ChatRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatRoomProjects_ChatWorkspaceProjects_WorkspaceId_ProjectId",
                        columns: x => new { x.WorkspaceId, x.ProjectId },
                        principalSchema: "hatch",
                        principalTable: "ChatWorkspaceProjects",
                        principalColumns: new[] { "WorkspaceId", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomProjects_RoomId_ProjectId",
                schema: "hatch",
                table: "ChatRoomProjects",
                columns: new[] { "RoomId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomProjects_WorkspaceId_ProjectId",
                schema: "hatch",
                table: "ChatRoomProjects",
                columns: new[] { "WorkspaceId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_WorkspaceId_ExternalId",
                schema: "hatch",
                table: "ChatRooms",
                columns: new[] { "WorkspaceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatWorkspaceProjects_ProjectId",
                schema: "hatch",
                table: "ChatWorkspaceProjects",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatRoomProjects",
                schema: "hatch");

            migrationBuilder.DropTable(
                name: "ChatRooms",
                schema: "hatch");

            migrationBuilder.DropTable(
                name: "ChatWorkspaceProjects",
                schema: "hatch");
        }
    }
}
