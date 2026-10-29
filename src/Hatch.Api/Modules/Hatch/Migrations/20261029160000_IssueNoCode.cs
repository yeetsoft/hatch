using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class IssueNoCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NoCode",
                schema: "hatch",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NoCodeAt",
                schema: "hatch",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoCode",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "NoCodeAt",
                schema: "hatch",
                table: "Issues");
        }
    }
}
