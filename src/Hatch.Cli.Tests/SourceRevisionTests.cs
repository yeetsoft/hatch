using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// Whether the CLI can answer "what is the newest commit under my own
/// source" out of a real checkout - the narrower check HA-1473 adds beside
/// the board's own revision, scoped to src/Hatch.Cli and src/Hatch.Contracts.
/// </summary>
public sealed class SourceRevisionTests : IDisposable
{
    private static readonly string[] Paths = ["src/Hatch.Cli", "src/Hatch.Contracts"];

    private readonly string _root = Directory.CreateTempSubdirectory("hatch-source-").FullName;

    public SourceRevisionTests()
    {
        Git(_root, "init", "--quiet", "-b", "main");
        Git(_root, "config", "user.name", "Hatch Test");
        Git(_root, "config", "user.email", "hatch-test@example.invalid");
        Git(_root, "config", "commit.gpgsign", "false");

        Put("src/Hatch.Cli/GoToWork.cs", "// the loop\n");
        Commit("the loop");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The operating system's to tidy.
        }
        catch (UnauthorizedAccessException)
        {
            // Git marks its objects read-only on some platforms.
        }
    }

    private void Put(string relative, string body)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, body);
    }

    private void Commit(string message)
    {
        Git(_root, "add", "--all");
        Git(_root, "commit", "--quiet", "--message", message);
    }

    [Fact]
    public void Matches_rev_parse_and_rev_list_against_the_same_root()
    {
        var expectedSha = Git(_root, "rev-parse", "HEAD").Trim();
        var expectedSequence = int.Parse(Git(_root, "rev-list", "--count", "HEAD").Trim());

        var info = new GitSourceRevision(_root).Newest(Paths);

        Assert.NotNull(info);
        Assert.Equal(expectedSha, info!.Sha);
        Assert.Equal(expectedSequence, info.Sequence);
    }

    [Fact]
    public void A_commit_under_an_unrelated_path_does_not_move_the_answer()
    {
        var before = new GitSourceRevision(_root).Newest(Paths);

        Put("docs/plan.md", "# plan\n");
        Commit("the plan");

        var after = new GitSourceRevision(_root).Newest(Paths);

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_root_that_is_not_a_git_repository_answers_null()
    {
        var bare = Directory.CreateTempSubdirectory("hatch-source-bare-").FullName;
        try
        {
            Assert.Null(new GitSourceRevision(bare).Newest(Paths));
        }
        finally
        {
            try
            {
                Directory.Delete(bare, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void A_commit_under_either_path_alone_still_answers()
    {
        Put("src/Hatch.Contracts/Dtos.cs", "// the wire\n");
        Commit("the wire");

        var expectedSha = Git(_root, "rev-parse", "HEAD").Trim();

        var info = new GitSourceRevision(_root).Newest(Paths);

        Assert.NotNull(info);
        Assert.Equal(expectedSha, info!.Sha);
    }

    private static string Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} in {dir}: {stderr}");
        return stdout;
    }
}
