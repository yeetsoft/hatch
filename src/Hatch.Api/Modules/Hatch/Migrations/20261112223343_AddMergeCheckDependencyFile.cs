using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddMergeCheckDependencyFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependencyFileContent",
                schema: "hatch",
                table: "MergeChecks",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DependencyFilePath",
                schema: "hatch",
                table: "MergeChecks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DependencyFileContent",
                schema: "hatch",
                table: "MergeChecks");

            migrationBuilder.DropColumn(
                name: "DependencyFilePath",
                schema: "hatch",
                table: "MergeChecks");
        }
    }
}
