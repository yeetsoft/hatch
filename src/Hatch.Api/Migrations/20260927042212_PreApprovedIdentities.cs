using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreApprovedIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_Provider_Subject",
                table: "ExternalIdentities");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "ExternalIdentities",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastSignInAt",
                table: "ExternalIdentities",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_Email_Unclaimed",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "Email" },
                unique: true,
                filter: "\"Subject\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_Subject",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "Subject" },
                unique: true,
                filter: "\"Subject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An unclaimed identity cannot exist under the old, non-null shape.
            migrationBuilder.Sql("DELETE FROM \"ExternalIdentities\" WHERE \"Subject\" IS NULL;");

            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_Provider_Email_Unclaimed",
                table: "ExternalIdentities");

            migrationBuilder.DropIndex(
                name: "IX_ExternalIdentities_Provider_Subject",
                table: "ExternalIdentities");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "ExternalIdentities",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastSignInAt",
                table: "ExternalIdentities",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentities_Provider_Subject",
                table: "ExternalIdentities",
                columns: new[] { "Provider", "Subject" },
                unique: true);
        }
    }
}
