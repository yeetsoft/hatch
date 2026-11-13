using System.Globalization;

namespace Hatch.Cli;

/// <summary>One commit that landed on the trunk, as the prompt shows it.</summary>
public sealed record TrunkCommit(string Sha, string Date, string Subject, string Body);

/// <summary>The commits to one path since a moment, newest first.</summary>
public sealed record PathCommits(string Path, IReadOnlyList<TrunkCommit> Commits);

/// <summary>The commits to the paths a brief names, in one checkout, labelled by its remote where there is more than one.</summary>
public sealed record RepoSince(string? Where, IReadOnlyList<PathCommits> Paths);

/// <summary>What has landed on the trunk, to the files a brief names, since the brief was written.</summary>
public sealed partial class Workspace
{
    public const int MaxCommitsPerPath = 5;
    public const int MaxPaths = 10;
    public const int MaxBodyChars = 600;

    /// <summary>
    /// For each of <paramref name="paths"/>, the commits that landed on origin's
    /// trunk after <paramref name="at"/>, newest first - best-effort, empty
    /// wherever git cannot say. A path git has no history for is not an error:
    /// it returns no commits, which is how a word that is not a path drops out.
    /// </summary>
    /// <remarks>
    /// The boundary is the last first-parent commit at or before <paramref name="at"/>,
    /// the moment something landed rather than when it was written, so a branch
    /// whose commits are older than the brief but merged after it still shows.
    /// </remarks>
    public IReadOnlyList<PathCommits> Since(DateTimeOffset at, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || BaseBranch() is not { Length: > 0 } trunk) return [];

        var tip = $"refs/remotes/origin/{trunk}";
        var boundary = Git("rev-list", "-1", "--first-parent", $"--before={at.ToString("o", CultureInfo.InvariantCulture)}", tip);
        if (!boundary.Ok) return [];

        var range = boundary.Out.Trim() is { Length: > 0 } below ? $"{below}..{tip}" : tip;

        var found = new List<PathCommits>();
        foreach (var path in paths)
        {
            if (found.Count >= MaxPaths) break;

            var log = Git("log", range, "--no-merges", "-n", MaxCommitsPerPath.ToString(CultureInfo.InvariantCulture),
                "--format=%x1e%h%x1f%cI%x1f%s%x1f%b", "--", $":(literal){path}");
            if (!log.Ok) continue;

            var commits = log.Out.Split('\u001e', StringSplitOptions.RemoveEmptyEntries)
                .Select(Commit)
                .OfType<TrunkCommit>()
                .ToList();
            if (commits.Count > 0) found.Add(new PathCommits(path, commits));
        }

        return found;
    }

    private static TrunkCommit? Commit(string record)
    {
        var fields = record.Split('\u001f');
        if (fields.Length < 3 || fields[0].Trim() is not { Length: > 0 } sha) return null;

        var date = fields[1].Length >= 10 ? fields[1][..10] : fields[1];
        var body = fields.Length > 3 ? fields[3].Trim() : "";
        if (body.Length > MaxBodyChars) body = body[..MaxBodyChars].TrimEnd() + "...";

        return new TrunkCommit(sha, date, fields[2].Trim(), body);
    }
}
