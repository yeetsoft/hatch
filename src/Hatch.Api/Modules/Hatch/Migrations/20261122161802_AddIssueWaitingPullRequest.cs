using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueWaitingPullRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WaitingPrAt",
                schema: "hatch",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrBody",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrBranch",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrRemote",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrRunner",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrSha",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrTitle",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingPrWhy",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaitingPrAt",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrBody",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrBranch",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrRemote",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrRunner",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrSha",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrTitle",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "WaitingPrWhy",
                schema: "hatch",
                table: "Issues");
        }
    }
}
