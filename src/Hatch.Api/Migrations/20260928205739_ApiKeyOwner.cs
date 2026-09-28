using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeyOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerPersonId",
                table: "ApiKeys",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_OwnerPersonId",
                table: "ApiKeys",
                column: "OwnerPersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeys_People_OwnerPersonId",
                table: "ApiKeys",
                column: "OwnerPersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_People_OwnerPersonId",
                table: "ApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_OwnerPersonId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "OwnerPersonId",
                table: "ApiKeys");
        }
    }
}
