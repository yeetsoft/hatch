using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRunnersWorkContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RunnersWorkContributions",
                schema: "hatch",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RunnersWorkContributionsAt",
                schema: "hatch",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunnersWorkContributionsBy",
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
                name: "RunnersWorkContributions",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RunnersWorkContributionsAt",
                schema: "hatch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RunnersWorkContributionsBy",
                schema: "hatch",
                table: "Projects");
        }
    }
}
