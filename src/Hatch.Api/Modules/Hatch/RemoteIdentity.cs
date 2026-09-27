using System.Text.RegularExpressions;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The one rule for when two spellings of a remote are the same repository,
/// and the only place it lives - nothing in the CLI or the browser
/// canonicalises anything of its own, so two callers can never come to
/// disagree about whether two remotes match.
///
/// <c>https://host/owner/repo.git</c>, <c>ssh://git@host/owner/repo</c>,
/// <c>git@host:owner/repo.git</c> and <c>host/owner/repo/</c> all fold to
/// <c>host/owner/repo</c>: scheme and user info dropped, a port kept, a
/// trailing <c>.git</c> and a trailing slash dropped, the whole lowercased. A
/// local path (<c>/…</c>, <c>file://…</c>) folds to its full path with case
/// intact - a Unix path is case-sensitive, and folding it would silently
/// merge two different repositories on disk.
/// </summary>
public static class RemoteIdentity
{
    /// <summary>
    /// scp-like syntax - <c>git@host:owner/repo.git</c> - told apart from a URL
    /// whose colon is followed by <c>//</c> (<c>ssh://host:2222/…</c>) by the
    /// one thing that actually distinguishes them: what comes right after the
    /// colon.
    /// </summary>
    private static readonly Regex ScpLike = new(
        @"^(?:[^@/]+@)?(?<host>[^:/]+):(?!//)(?<path>.+)$",
        RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The canonical form of <paramref name="remote"/>, or the sentence
    /// refusing it - exactly one of the two is non-null.
    /// </summary>
    public static (string? Canonical, string? Error) Canonical(string remote)
    {
        var trimmed = remote.Trim();
        if (trimmed.Length == 0) return (null, "a remote can't be empty");

        // A local path. No case-folding - see the type's own summary.
        if (trimmed.StartsWith('/')) return (trimmed, null);

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile) return (uri.LocalPath, null);

            var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            return Fold(host, uri.AbsolutePath, remote);
        }

        var scp = ScpLike.Match(trimmed);
        if (scp.Success) return Fold(scp.Groups["host"].Value, scp.Groups["path"].Value, remote);

        // Bare host/owner/repo, no scheme and no colon - split on the first
        // slash.
        var slash = trimmed.IndexOf('/');
        if (slash >= 0) return Fold(trimmed[..slash], trimmed[(slash + 1)..], remote);

        return (null, Refusal(remote));
    }

    /// <summary>
    /// Host and path, folded into one string - or the refusal, when either
    /// side turned out to be nothing at all.
    /// </summary>
    private static (string? Canonical, string? Error) Fold(string host, string path, string original)
    {
        host = host.Trim().ToLowerInvariant();

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        path = path.ToLowerInvariant();

        return host.Length == 0 || path.Length == 0
            ? (null, Refusal(original))
            : ($"{host}/{path}", null);
    }

    private static string Refusal(string remote) =>
        $"a remote is a URL, an scp-like address (git@host:owner/repo), or a local path - not \"{remote}\"";
}
