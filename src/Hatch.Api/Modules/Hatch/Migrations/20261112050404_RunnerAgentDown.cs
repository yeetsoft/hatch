using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerAgentDown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentDownKind",
                schema: "hatch",
                table: "RunnerSessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentDownLine",
                schema: "hatch",
                table: "RunnerSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgentDownUntil",
                schema: "hatch",
                table: "RunnerSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentDownKind",
                schema: "hatch",
                table: "LegacyRunners",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentDownLine",
                schema: "hatch",
                table: "LegacyRunners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgentDownUntil",
                schema: "hatch",
                table: "LegacyRunners",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentDownKind",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "AgentDownLine",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "AgentDownUntil",
                schema: "hatch",
                table: "RunnerSessions");

            migrationBuilder.DropColumn(
                name: "AgentDownKind",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "AgentDownLine",
                schema: "hatch",
                table: "LegacyRunners");

            migrationBuilder.DropColumn(
                name: "AgentDownUntil",
                schema: "hatch",
                table: "LegacyRunners");
        }
    }
}
