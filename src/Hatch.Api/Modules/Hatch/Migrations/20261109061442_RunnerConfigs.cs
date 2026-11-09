using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerConfigs",
                schema: "hatch",
                columns: table => new
                {
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tags = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Under = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MaxRuns = table.Column<int>(type: "integer", nullable: true),
                    MaxSpend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxTokens = table.Column<long>(type: "bigint", nullable: true),
                    MaxTokensPerHour = table.Column<long>(type: "bigint", nullable: true),
                    UntilAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerConfigs", x => x.ApiKeyId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerConfigs",
                schema: "hatch");
        }
    }
}
