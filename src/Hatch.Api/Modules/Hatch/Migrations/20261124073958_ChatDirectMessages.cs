using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class ChatDirectMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RoomId",
                schema: "hatch",
                table: "ChatMessages",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "LinkId",
                schema: "hatch",
                table: "ChatMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefusal",
                schema: "hatch",
                table: "ChatLinks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRefusalAt",
                schema: "hatch",
                table: "ChatLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RunnerFailures",
                schema: "hatch",
                table: "ChatLinks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_LinkId_Kind_Subject",
                schema: "hatch",
                table: "ChatMessages",
                columns: new[] { "LinkId", "Kind", "Subject" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChatMessages_OneDestination",
                schema: "hatch",
                table: "ChatMessages",
                sql: "(\"RoomId\" IS NULL) <> (\"LinkId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ChatLinks_LinkId",
                schema: "hatch",
                table: "ChatMessages",
                column: "LinkId",
                principalSchema: "hatch",
                principalTable: "ChatLinks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ChatLinks_LinkId",
                schema: "hatch",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_LinkId_Kind_Subject",
                schema: "hatch",
                table: "ChatMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChatMessages_OneDestination",
                schema: "hatch",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "LinkId",
                schema: "hatch",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "LastRefusal",
                schema: "hatch",
                table: "ChatLinks");

            migrationBuilder.DropColumn(
                name: "LastRefusalAt",
                schema: "hatch",
                table: "ChatLinks");

            migrationBuilder.DropColumn(
                name: "RunnerFailures",
                schema: "hatch",
                table: "ChatLinks");

            // A direct message has no room to go back to.
            migrationBuilder.Sql("DELETE FROM hatch.\"ChatMessages\" WHERE \"RoomId\" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "RoomId",
                schema: "hatch",
                table: "ChatMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
