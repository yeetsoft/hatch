using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChatLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatLinks",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkspaceId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MemberName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecretHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    SecretExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatLinks_ChatWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "hatch",
                        principalTable: "ChatWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatLinkSends",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatLinkSends", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatLinks_SecretHash",
                schema: "hatch",
                table: "ChatLinks",
                column: "SecretHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatLinks_WorkspaceId_MemberId_Confirmed",
                schema: "hatch",
                table: "ChatLinks",
                columns: new[] { "WorkspaceId", "MemberId" },
                unique: true,
                filter: "\"ConfirmedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatLinks_WorkspaceId_PersonId",
                schema: "hatch",
                table: "ChatLinks",
                columns: new[] { "WorkspaceId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatLinkSends_PersonId_SentAt",
                schema: "hatch",
                table: "ChatLinkSends",
                columns: new[] { "PersonId", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatLinks",
                schema: "hatch");

            migrationBuilder.DropTable(
                name: "ChatLinkSends",
                schema: "hatch");
        }
    }
}
