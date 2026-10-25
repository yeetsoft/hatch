using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerReportedTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Host",
                schema: "hatch",
                table: "Runners",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Runs",
                schema: "hatch",
                table: "Runners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Spent",
                schema: "hatch",
                table: "Runners",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Host",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Runs",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Spent",
                schema: "hatch",
                table: "Runners");
        }
    }
}
