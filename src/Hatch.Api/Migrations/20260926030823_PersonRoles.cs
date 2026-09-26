using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersonRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing row becomes a User first (default 1), so nobody is
            // ever Pending: an upgrade must not lock out the person running it.
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "People",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // IsAdmin true -> Admin (2); false stays User (1).
            migrationBuilder.Sql("UPDATE \"People\" SET \"Role\" = 2 WHERE \"IsAdmin\";");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "People");

            // The default was only there to fill existing rows. A row written
            // without a role is Pending in the CLR, and the database agrees.
            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "People",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "People",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Lossy by nature: Pending and User both become "not an admin".
            migrationBuilder.Sql("UPDATE \"People\" SET \"IsAdmin\" = TRUE WHERE \"Role\" = 2;");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "People");
        }
    }
}
