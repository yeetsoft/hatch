using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerFailures",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Where = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Why = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BeganAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerFailures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerFailures_ApiKeyId_EndedAt_Id",
                schema: "hatch",
                table: "RunnerFailures",
                columns: new[] { "ApiKeyId", "EndedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerFailures_ApiKeyId_Where_Kind",
                schema: "hatch",
                table: "RunnerFailures",
                columns: new[] { "ApiKeyId", "Where", "Kind" },
                unique: true,
                filter: "\"EndedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerFailures",
                schema: "hatch");
        }
    }
}
