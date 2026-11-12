using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Modules.Hatch.Migrations
{
    /// <summary>
    /// Tells the stored playbooks the address the API answers at now: every
    /// <c>/api/hatch/</c> in a playbook's text becomes <c>/api/</c>.
    /// </summary>
    /// <remarks>
    /// <para>The seeding migrations are frozen, so the database still told a
    /// session to call <c>/api/hatch/...</c>. The old prefix redirects, but an
    /// agent should be told the true address. The trailing slash leaves
    /// <c>/api/hatch-revision</c> alone.</para>
    ///
    /// <para>The change is semantically neutral, so an operator's own
    /// <c>Prompt</c> is rewritten along with <c>DefaultPrompt</c>, and
    /// <c>UpdatedAt</c> is not touched. <c>Down</c> is a no-op: the new
    /// text cannot be told apart from text written that way on purpose.</para>
    /// </remarks>
    public partial class RewriteApiPrefixInPlaybooks : Migration
    {
        /// <summary>The statement <c>Up</c> runs, public so a test can read what it touches.</summary>
        public const string Rewrite = """
            UPDATE hatch."Playbooks"
               SET "Prompt" = REPLACE("Prompt", '/api/hatch/', '/api/'),
                   "DefaultPrompt" = REPLACE("DefaultPrompt", '/api/hatch/', '/api/');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Rewrite);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
