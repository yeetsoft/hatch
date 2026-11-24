using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChatListening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ListenFailingSince",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ListenLastRefusal",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ListeningSince",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListenFailingSince",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "ListenLastRefusal",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "ListeningSince",
                schema: "hatch",
                table: "ChatWorkspaces");
        }
    }
}
