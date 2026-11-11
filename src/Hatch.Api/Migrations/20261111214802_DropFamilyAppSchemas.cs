using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropFamilyAppSchemas : Migration
    {
        private static readonly string[] FamilySchemas = ["gather", "photos", "quill", "storage"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Gather, Photos, Quill and Storage modules are gone from the
            // code (HA-2224), and their data goes with them: a schema each,
            // dropped with whatever is in it. IF EXISTS because a fresh
            // install never had them.
            foreach (var schema in FamilySchemas)
                migrationBuilder.Sql($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");

            // The two Immich settings were only Photos' to read. The API key
            // is a secret at rest, so its row goes rather than sitting
            // unread.
            migrationBuilder.Sql(
                "DELETE FROM \"SiteSettings\" WHERE \"Key\" IN ('ImmichBaseUrl', 'ImmichApiKey');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Puts the empty schemas back so an older build finds its
            // namespaces. The tables, the rows in them and the two Immich
            // settings do not come back: Up dropped the data, and a restore
            // is from the pg_dump taken before upgrading.
            foreach (var schema in FamilySchemas)
                migrationBuilder.Sql($"CREATE SCHEMA IF NOT EXISTS \"{schema}\";");
        }
    }
}
