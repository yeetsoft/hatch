using Hatch.Api.Tests.Hatch;
using System.Text.RegularExpressions;

namespace Hatch.Api.Tests;

/// <summary>
/// The api image's <c>pg_dump</c> and the db image's server have to be the
/// same Postgres major: <c>pg_dump</c> refuses to dump a server newer than
/// itself, so a bump that touches one Dockerfile and not the other leaves
/// every backup failing.
///
/// <para>Without this test the only signal of that drift is a backup that
/// fails later, long after the change that caused it. This reads both files
/// and fails the build instead, naming both and both majors.</para>
///
/// <para>Needs no database - it reads two text files from the repository.</para>
/// </summary>
public class PgDumpVersionTests
{
    // The minor (`18.4`) is deliberately not captured: only the major has to agree.
    private static readonly Regex DbPattern = new(@"^FROM\s+postgres:(\d+)", RegexOptions.Multiline);

    // Anchored to non-comment lines: Dockerfile.api explains the pin in a comment
    // that also says `postgresql-client-18`, and an unanchored pattern would stay
    // green if the real apt-get install line were changed or removed.
    private static readonly Regex ApiPattern = new(@"^[^#\r\n]*postgresql-client-(\d+)", RegexOptions.Multiline);

    [Fact]
    public async Task TheApiImagesPgDumpAndTheDbImagesServerAreTheSamePostgresMajor()
    {
        var root = MinimalDatabases.RepositoryRoot();
        var dbText = await File.ReadAllTextAsync(Path.Combine(root, "containers", "hatch-db", "Dockerfile.db"));
        var apiText = await File.ReadAllTextAsync(Path.Combine(root, "src", "Hatch.Api", "Dockerfile.api"));

        var db = DbPattern.Match(dbText);
        var api = ApiPattern.Match(apiText);
        Assert.True(db.Success, $"containers/hatch-db/Dockerfile.db has no line matching {DbPattern}; this test expects a `FROM postgres:<major>` line.");
        Assert.True(api.Success, $"src/Hatch.Api/Dockerfile.api has no line matching {ApiPattern}; this test expects a non-comment line installing `postgresql-client-<major>`.");

        Assert.True(
            api.Groups[1].Value == db.Groups[1].Value,
            $"src/Hatch.Api/Dockerfile.api installs postgresql-client-{api.Groups[1].Value} but containers/hatch-db/Dockerfile.db is FROM postgres:{db.Groups[1].Value} - pg_dump refuses a server newer than itself; bump both files together.");
    }
}
