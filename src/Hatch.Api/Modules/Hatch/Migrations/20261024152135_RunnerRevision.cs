using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BuiltAt",
                schema: "hatch",
                table: "Runners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Revision",
                schema: "hatch",
                table: "Runners",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                schema: "hatch",
                table: "Runners",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuiltAt",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Revision",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Sequence",
                schema: "hatch",
                table: "Runners");
        }
    }
}
