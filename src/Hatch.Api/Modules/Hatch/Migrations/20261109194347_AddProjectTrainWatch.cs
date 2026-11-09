using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTrainWatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrainStalledDriver",
                schema: "hatch",
                table: "Projects",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TrainWatchedIssueId",
                schema: "hatch",
                table: "Projects",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrainWatchedSince",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrainStalledDriver",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TrainWatchedIssueId",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TrainWatchedSince",
                schema: "hatch",
                table: "Projects");
        }
    }
}
