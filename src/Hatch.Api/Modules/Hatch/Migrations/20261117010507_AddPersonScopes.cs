using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonScopeProjects",
                schema: "hatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonScopeProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonScopeProjects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "hatch",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonScopes",
                schema: "hatch",
                columns: table => new
                {
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonScopes", x => x.PersonId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonScopeProjects_PersonId_ProjectId",
                schema: "hatch",
                table: "PersonScopeProjects",
                columns: new[] { "PersonId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonScopeProjects_ProjectId",
                schema: "hatch",
                table: "PersonScopeProjects",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonScopeProjects",
                schema: "hatch");

            migrationBuilder.DropTable(
                name: "PersonScopes",
                schema: "hatch");
        }
    }
}
