using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectOnePullRequestPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnePullRequestPolicy",
                schema: "hatch",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "optional");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnePullRequestPolicyAt",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnePullRequestPolicyBy",
                schema: "hatch",
                table: "Projects",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnePullRequestPolicy",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OnePullRequestPolicyAt",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OnePullRequestPolicyBy",
                schema: "hatch",
                table: "Projects");
        }
    }
}
