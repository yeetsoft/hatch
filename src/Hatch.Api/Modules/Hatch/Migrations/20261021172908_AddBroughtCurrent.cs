using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddBroughtCurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BroughtCurrentAt",
                schema: "hatch",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BroughtCurrentTrunkSha",
                schema: "hatch",
                table: "Issues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BroughtCurrentAt",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "BroughtCurrentTrunkSha",
                schema: "hatch",
                table: "Issues");
        }
    }
}
