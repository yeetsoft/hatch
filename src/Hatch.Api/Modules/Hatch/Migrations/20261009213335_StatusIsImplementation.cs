using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <remarks>
    /// Adds the column and ticks nothing - no seed, unlike
    /// <c>20261001150651_AgentFiles.cs</c>. A board with nothing ticked keeps
    /// today's measured implementation column, and that is the whole guarantee
    /// that this change alters no existing board until an operator ticks one.
    /// </remarks>
    public partial class StatusIsImplementation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsImplementation",
                schema: "hatch",
                table: "Statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsImplementation",
                schema: "hatch",
                table: "Statuses");
        }
    }
}
