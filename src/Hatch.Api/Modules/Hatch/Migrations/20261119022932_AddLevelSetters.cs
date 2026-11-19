using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelSetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreApprovalSetBy",
                schema: "hatch",
                table: "Issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrioritySetBy",
                schema: "hatch",
                table: "Issues",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreApprovalSetBy",
                schema: "hatch",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "PrioritySetBy",
                schema: "hatch",
                table: "Issues");
        }
    }
}
