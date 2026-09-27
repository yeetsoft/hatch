using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrantsCascadeWithPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuthGrants_People_PersonId",
                table: "AuthGrants");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthGrants_People_PersonId",
                table: "AuthGrants",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuthGrants_People_PersonId",
                table: "AuthGrants");

            migrationBuilder.AddForeignKey(
                name: "FK_AuthGrants_People_PersonId",
                table: "AuthGrants",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
