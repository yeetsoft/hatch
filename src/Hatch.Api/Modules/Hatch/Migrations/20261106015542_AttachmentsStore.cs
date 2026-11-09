using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Images",
                schema: "hatch",
                newName: "Attachments",
                newSchema: "hatch");

            migrationBuilder.Sql(
                """ALTER TABLE "hatch"."Attachments" RENAME CONSTRAINT "PK_Images" TO "PK_Attachments";""");

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                schema: "hatch",
                table: "Attachments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Size",
                schema: "hatch",
                table: "Attachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                schema: "hatch",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                schema: "hatch",
                table: "Attachments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.Sql(
                """
                UPDATE "hatch"."Attachments"
                SET "FileName" = 'image.' || CASE "ContentType"
                        WHEN 'image/png' THEN 'png'
                        WHEN 'image/jpeg' THEN 'jpg'
                        WHEN 'image/gif' THEN 'gif'
                        WHEN 'image/webp' THEN 'webp'
                        ELSE 'bin'
                    END,
                    "Size" = octet_length("Bytes");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A row whose ContentType isn't one of the four images cannot go back
            // into a 64-char column - its row is deleted rather than its type
            // silently truncated.
            migrationBuilder.Sql(
                """
                DELETE FROM "hatch"."Attachments"
                WHERE "ContentType" NOT IN ('image/png', 'image/jpeg', 'image/gif', 'image/webp');
                """);

            migrationBuilder.DropColumn(
                name: "FileName",
                schema: "hatch",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Size",
                schema: "hatch",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                schema: "hatch",
                table: "Attachments");

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                schema: "hatch",
                table: "Attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.Sql(
                """ALTER TABLE "hatch"."Attachments" RENAME CONSTRAINT "PK_Attachments" TO "PK_Images";""");

            migrationBuilder.RenameTable(
                name: "Attachments",
                schema: "hatch",
                newName: "Images",
                newSchema: "hatch");
        }
    }
}
