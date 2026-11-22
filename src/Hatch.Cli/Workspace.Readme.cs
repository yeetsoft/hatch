using System.Diagnostics;
using System.Text;

namespace Hatch.Cli;

/// <summary>
/// The one step <see cref="Leave"/> adds for a pull request's own branch: the
/// repository's <c>.hatch/README.md</c> kept at this build's text, so a
/// runner that writes the file once does not leave every other checkout it
/// touches behind it. See "Leaving the tree" in <c>docs/hatch.md</c>.
/// </summary>
/// <remarks>
/// Nothing here runs unless the increment moved the branch: a session that
/// only read adds nothing, and neither does a checkout with no branch, or
/// more than one, on origin. Where it does run, the write is never older than
/// what the trunk already holds - that case is the trunk's own merge forward
/// to settle, not a second copy pushed here to conflict with it.
/// </remarks>
public sealed partial class Workspace
{
    private static readonly HashSet<string> _readmeSaid = new(StringComparer.Ordinal);
    private static readonly object _readmeSaidLock = new();

    /// <summary>Clears what this process has already said about the readme file - a test's own fresh start.</summary>
    internal static void ForgetReadmeSaid()
    {
        lock (_readmeSaidLock) _readmeSaid.Clear();
    }

    /// <summary>Says <paramref name="reason"/> once per process, not once per checkout per increment: it is the same news every time.</summary>
    private void SayReadmeOnce(string reason)
    {
        lock (_readmeSaidLock)
        {
            if (!_readmeSaid.Add(reason)) return;
        }

        complain($"hatch:   {reason}");
    }

    /// <summary>
    /// Writes or replaces <c>.hatch/README.md</c> on the issue's one open
    /// branch, when the increment moved it and this build's text is newer than
    /// both copies - see the type remarks.
    /// </summary>
    private void KeepReadmeCurrent(string lowerKey, string trunk, string? recordedBranch, Carrying? carry, Action<string, bool> note)
    {
        if (carry is null) return;

        if (!Git("fetch", "--quiet", "--prune", "origin").Ok)
        {
            complain("hatch:   could not fetch from origin, so .hatch/README.md was not checked");
            return;
        }

        var (open, _) = Branched(lowerKey, trunk, recordedBranch);
        if (open.Count != 1) return;

        var branch = open[0];
        var tip = FullSha($"refs/remotes/origin/{branch}");
        if (tip is null) return;

        var trunkRef = $"refs/remotes/origin/{trunk}";
        var basis = carry.OriginTip ?? trunkRef;

        // --no-merges excludes the merge commit Enter makes forwarding a
        // stale branch; excluding the trunk's own tip besides excludes the
        // commits that merge carries in as its other parent, which are the
        // trunk's and not this session's. Without both, a session that only
        // read a stale branch would look like it moved it.
        var moved = Git("rev-list", "--no-merges", "--count", tip, $"^{basis}", $"^{trunkRef}");
        if (!(moved.Ok && int.TryParse(moved.Out.Trim(), out var movedCount) && movedCount > 0)) return;

        var branchBlob = ReadBlob(branch, ReadmeFile.Path);
        if (branchBlob.Outcome == BlobOutcome.Failed)
        {
            complain($"hatch:   .hatch/README.md could not be read on {branch} - {branchBlob.Why}");
            return;
        }

        var trunkBlob = ReadBlob(trunk, ReadmeFile.Path);
        if (trunkBlob.Outcome == BlobOutcome.Failed)
        {
            complain($"hatch:   .hatch/README.md could not be read on {trunk} - {trunkBlob.Why}");
            return;
        }

        var branchVerdict = ReadmeFile.Judge(Decoded(branchBlob));
        var trunkVerdict = ReadmeFile.Judge(Decoded(trunkBlob));

        if (branchVerdict == ReadmeVerdict.Foreign || trunkVerdict == ReadmeVerdict.Foreign)
        {
            SayReadmeOnce(ReadmeFile.ForeignSentence);
            return;
        }

        // Keep on either side covers "same", "newer", and "the trunk already
        // has it and the branch does not" - that last one is the trunk's own
        // merge forward to settle, not a write to make here.
        if (branchVerdict == ReadmeVerdict.Keep || trunkVerdict == ReadmeVerdict.Keep) return;

        WriteReadme(branch, tip, branchVerdict, carry.Signing, note);
    }

    private static string? Decoded(BlobRead blob) =>
        blob.Outcome == BlobOutcome.Found ? new UTF8Encoding(false).GetString(blob.Content!) : null;

    /// <summary>
    /// The commit itself, with no working tree and no index of the checkout: a
    /// temporary index builds the one-file tree, and a temporary file holds
    /// the bytes <c>hash-object</c> reads - both removed when this returns.
    /// </summary>
    private void WriteReadme(string branch, string tip, ReadmeVerdict verdict, CommitSigning signing, Action<string, bool> note)
    {
        var contentPath = Path.GetTempFileName();
        // Not GetTempFileName: that creates an empty file, and an empty file
        // is not a valid index for read-tree to open - a name with nothing
        // there yet is what makes it write a fresh one.
        var indexPath = Path.Combine(Path.GetTempPath(), $"hatch-readme-index-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(contentPath, new UTF8Encoding(false).GetBytes(ReadmeFile.Text));

            var hashed = GitReal(null, "hash-object", "-w", "--no-filters", contentPath);
            if (!hashed.Ok)
            {
                note($".hatch/README.md could not be committed on {branch} - {hashed.Why}", true);
                return;
            }

            var blob = hashed.Out.Trim();
            var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexPath };

            var read = GitReal(env, "read-tree", tip);
            if (!read.Ok)
            {
                note($".hatch/README.md could not be committed on {branch} - {read.Why}", true);
                return;
            }

            var staged = GitReal(env, "update-index", "--add", "--cacheinfo", $"100644,{blob},{ReadmeFile.Path}");
            if (!staged.Ok)
            {
                note($".hatch/README.md could not be committed on {branch} - {staged.Why}", true);
                return;
            }

            var written = GitReal(env, "write-tree");
            if (!written.Ok)
            {
                note($".hatch/README.md could not be committed on {branch} - {written.Why}", true);
                return;
            }

            var tree = written.Out.Trim();
            var tipTree = Git("rev-parse", "--verify", "--quiet", $"{tip}^{{tree}}");
            if (tipTree.Ok && tipTree.Out.Trim() == tree) return;

            var verb = verdict == ReadmeVerdict.Write ? "write" : "replace";
            var message = $"Readme: {verb} .hatch/README.md at version {ReadmeFile.Version}";

            // A failed -S is a bad note, never a fallback to an unsigned
            // commit: the gate ahead of this already refused an increment
            // that could not sign on a project that requires it, so Fails-and-
            // not-Allows below is only defensive.
            Ran committed;
            if (signing.State == Signing.Works)
                committed = GitReal(null, "commit-tree", tree, "-p", tip, "-S", "-m", message);
            else if (signing.Allows)
                committed = GitReal(null, "commit-tree", tree, "-p", tip, "-m", message);
            else
            {
                SayReadmeOnce("this git cannot sign .hatch/README.md's commit, and this project does not allow an unsigned one - nothing was written");
                return;
            }

            if (!committed.Ok)
            {
                note($".hatch/README.md could not be committed on {branch} - {committed.Why}", true);
                return;
            }

            var sha = committed.Out.Trim();
            var push = GitReal(null, "push", "--quiet", "origin", $"{sha}:refs/heads/{branch}");
            if (!push.Ok)
            {
                note($"the push of .hatch/README.md to {branch} was refused - {push.Why}; the branch is as it was", true);
                return;
            }

            if (FullSha($"refs/remotes/origin/{branch}") != sha) Fetch();

            var verbPast = verdict == ReadmeVerdict.Write ? "written" : "replaced";
            note($".hatch/README.md was {verbPast} on {branch} (version {ReadmeFile.Version}), now at {sha[..Math.Min(9, sha.Length)]}", false);
        }
        finally
        {
            File.Delete(contentPath);
            File.Delete(indexPath);
        }
    }

    /// <summary>
    /// Git spawned for real, with its own environment on top of what this
    /// process inherits - the temporary-index commands above need
    /// <c>GIT_INDEX_FILE</c>, which neither the <c>git</c> nor the
    /// <c>gitBytes</c> test seam can carry. Always a real process, signed the
    /// way every other call here is: a test's own workspace runs real git for
    /// these even where it fakes the rest.
    /// </summary>
    private Ran GitReal(IReadOnlyDictionary<string, string>? env, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in Signed(args, start)) start.ArgumentList.Add(arg);
        if (env is not null) foreach (var (name, value) in env) start.Environment[name] = value;

        try
        {
            using var process = Process.Start(start);
            if (process is null) return new Ran(-1, "", "");

            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new Ran(process.ExitCode, stdout, stderr.GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new Ran(-1, "", "");
        }
    }
}
