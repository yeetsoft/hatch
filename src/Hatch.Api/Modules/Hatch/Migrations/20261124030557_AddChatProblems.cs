using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChatProblems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatProblems",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeganAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sentence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatProblems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatProblems_Kind_Open",
                schema: "hatch",
                table: "ChatProblems",
                column: "Kind",
                unique: true,
                filter: "\"EndedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatProblems",
                schema: "hatch");
        }
    }
}
