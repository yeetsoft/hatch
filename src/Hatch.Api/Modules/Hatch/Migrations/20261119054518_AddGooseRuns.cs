using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddGooseRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GooseRuns",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirstRanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Sweeps = table.Column<int>(type: "integer", nullable: false),
                    LongestMs = table.Column<long>(type: "bigint", nullable: false),
                    Entries = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GooseRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GooseRuns_LastRanAt",
                schema: "hatch",
                table: "GooseRuns",
                column: "LastRanAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GooseRuns",
                schema: "hatch");
        }
    }
}
