using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerTokenBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MaxTokens",
                schema: "hatch",
                table: "Runners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MaxTokensPerHour",
                schema: "hatch",
                table: "Runners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ThrottledUntil",
                schema: "hatch",
                table: "Runners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TokensLastHour",
                schema: "hatch",
                table: "Runners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TokensUsed",
                schema: "hatch",
                table: "Runners",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxTokens",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MaxTokensPerHour",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "ThrottledUntil",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "TokensLastHour",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "TokensUsed",
                schema: "hatch",
                table: "Runners");
        }
    }
}
