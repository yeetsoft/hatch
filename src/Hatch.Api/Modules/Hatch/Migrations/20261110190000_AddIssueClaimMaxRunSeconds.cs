using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A claim's own optional hard cap on its run, in seconds - see
    /// <see cref="Hatch.Api.Modules.Hatch.EfHatchIssue.ClaimMaxRunSeconds"/>.
    /// Nullable and defaultless: every existing claim reads blank until
    /// something sets one.
    /// </summary>
    public partial class AddIssueClaimMaxRunSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaimMaxRunSeconds",
                schema: "hatch",
                table: "Issues",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimMaxRunSeconds",
                schema: "hatch",
                table: "Issues");
        }
    }
}
