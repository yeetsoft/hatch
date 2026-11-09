using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class RunnerSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerSessions",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Where = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Host = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Line = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Under = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MaxRuns = table.Column<int>(type: "integer", nullable: true),
                    MaxSpend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxTokens = table.Column<long>(type: "bigint", nullable: true),
                    MaxTokensPerHour = table.Column<long>(type: "bigint", nullable: true),
                    UntilAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Runs = table.Column<int>(type: "integer", nullable: true),
                    Spent = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    TokensUsed = table.Column<long>(type: "bigint", nullable: true),
                    TokensLastHour = table.Column<long>(type: "bigint", nullable: true),
                    ExhaustedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ThrottledUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Usage = table.Column<string>(type: "jsonb", nullable: true),
                    UsageReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Remotes = table.Column<string>(type: "text", nullable: true),
                    Clones = table.Column<bool>(type: "boolean", nullable: true),
                    Mine = table.Column<bool>(type: "boolean", nullable: true),
                    Standing = table.Column<bool>(type: "boolean", nullable: true),
                    Agent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Revision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: true),
                    BuiltAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceRevision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SourceSequence = table.Column<int>(type: "integer", nullable: true),
                    Projects = table.Column<string>(type: "text", nullable: true),
                    RefusesDangerous = table.Column<bool>(type: "boolean", nullable: true),
                    OrderKind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    OrderKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OrderedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    OrderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerSessions_ApiKeyId_LastSeenAt",
                schema: "hatch",
                table: "RunnerSessions",
                columns: new[] { "ApiKeyId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerSessions_ApiKeyId_Where",
                schema: "hatch",
                table: "RunnerSessions",
                columns: new[] { "ApiKeyId", "Where" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerSessions",
                schema: "hatch");
        }
    }
}
