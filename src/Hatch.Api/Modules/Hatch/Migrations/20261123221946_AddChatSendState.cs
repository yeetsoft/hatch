using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSendState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailStreak",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HoldUntil",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefusal",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRefusalAt",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendFailingSince",
                schema: "hatch",
                table: "ChatWorkspaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefusal",
                schema: "hatch",
                table: "ChatRooms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendFailingSince",
                schema: "hatch",
                table: "ChatRooms",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailStreak",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "HoldUntil",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "LastRefusal",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "LastRefusalAt",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "SendFailingSince",
                schema: "hatch",
                table: "ChatWorkspaces");

            migrationBuilder.DropColumn(
                name: "LastRefusal",
                schema: "hatch",
                table: "ChatRooms");

            migrationBuilder.DropColumn(
                name: "SendFailingSince",
                schema: "hatch",
                table: "ChatRooms");
        }
    }
}
