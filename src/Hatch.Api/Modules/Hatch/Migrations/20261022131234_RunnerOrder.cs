using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderKey",
                schema: "hatch",
                table: "Runners",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderKind",
                schema: "hatch",
                table: "Runners",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OrderedAt",
                schema: "hatch",
                table: "Runners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderedBy",
                schema: "hatch",
                table: "Runners",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderKey",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "OrderKind",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "OrderedAt",
                schema: "hatch",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "OrderedBy",
                schema: "hatch",
                table: "Runners");
        }
    }
}
