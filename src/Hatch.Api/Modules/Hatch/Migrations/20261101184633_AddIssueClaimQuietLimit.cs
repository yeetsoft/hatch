using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// A claim's own optional quiet window, in seconds - see
    /// <see cref="Hatch.Api.Modules.Hatch.EfHatchIssue.ClaimQuietLimitSeconds"/>.
    /// Nullable and defaultless: every existing claim reads blank until
    /// something sets one.
    /// </summary>
    public partial class AddIssueClaimQuietLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClaimQuietLimitSeconds",
                schema: "hatch",
                table: "Issues",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimQuietLimitSeconds",
                schema: "hatch",
                table: "Issues");
        }
    }
}
