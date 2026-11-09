using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <summary>
    /// Drops <c>ApiKeys.Scopes</c>. A key minted with no scopes was inert and
    /// now reaches every route that accepts a key, so existing keys are widened
    /// by this migration: revoke any that should not be.
    /// </summary>
    public partial class DropApiKeyScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scopes",
                table: "ApiKeys");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Scopes",
                table: "ApiKeys",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }
    }
}
