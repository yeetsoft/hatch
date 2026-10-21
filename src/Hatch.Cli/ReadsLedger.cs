using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// What a session read, counted by path or endpoint: the tally behind a work log
/// entry's <see cref="WorkLogEntryRequest.Reads"/>, shared by every agent's stream
/// reader so the three of them sort, cap and recognise the same way.
/// </summary>
/// <remarks>
/// Keys are paths, endpoints and verbs only - never free text. Every recognizer
/// here returns a key or null, and a generic shell read (<c>cat</c>, <c>sed</c>,
/// <c>grep</c>) is left uncounted on purpose; see HA-1186.
/// </remarks>
internal sealed partial class ReadsLedger
{
    /// <summary>
    /// The most paths one entry carries. Mirrors <c>EfHatchWorkLogEntry.MaxReads</c> -
    /// the CLI does not reference the Api project - so both ends drop the same 51st path.
    /// </summary>
    public const int Max = 50;

    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Counts one read of <paramref name="path"/>; null, empty and whitespace are ignored.</summary>
    public void Add(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        _counts[path] = _counts.GetValueOrDefault(path) + 1;
    }

    /// <summary>The heaviest <see cref="Max"/> paths: count descending, then path ascending, ordinal.</summary>
    public IReadOnlyList<WorkLogReadDto> Ranked() =>
        _counts
            .Select(c => new WorkLogReadDto(c.Key, c.Value))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Path, StringComparer.Ordinal)
            .Take(Max)
            .ToList();

    /// <summary>
    /// The ledger key for a shell command that is a call to the hatch CLI, or null
    /// for anything else. <c>api</c> keys on the endpoint, query and fragment cut
    /// off; every other subcommand folds into <c>hatch &lt;verb&gt;</c>, so every
    /// ticket key lands in one bucket per verb.
    /// </summary>
    /// <remarks>
    /// A deliberate starts-with rule: the launcher must be the command's first
    /// word, so <c>bash scripts/hatch.sh</c> and <c>cd x &amp;&amp; hatch</c> are not
    /// recognised. The subcommand regex is what keeps free text out of the key.
    /// </remarks>
    internal static string? FromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var tokens = command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Unquote)
            .ToList();
        if (tokens.Count == 0) return null;

        var launcher = tokens[0].ToLowerInvariant();
        List<string> args;
        if (launcher is "hatch" or "./scripts/hatch.sh" or "scripts/hatch.sh" or @".\scripts\hatch.ps1")
            args = tokens.Skip(1).ToList();
        else if (launcher is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe")
        {
            var script = tokens.FindIndex(t => t.EndsWith("hatch.ps1", StringComparison.OrdinalIgnoreCase));
            if (script < 0) return null;
            args = tokens.Skip(script + 1).ToList();
        }
        else
            return null;

        if (args.Count == 0) return null;

        // A verb ending a statement (`hatch show; rm -rf /`) is still that verb.
        var verb = args[0].Split([';', '|', '&'])[0];
        if (!Subcommand().IsMatch(verb)) return null;

        if (verb != "api") return "hatch " + verb;

        if (args.Count < 3) return null;

        var path = args[2];
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];

        return Endpoint().IsMatch(path) ? path : null;
    }

    /// <summary>
    /// The ledger key for a fetched URL: scheme, host, port when not the default,
    /// and path. The query, the fragment and any <c>user:password@</c> are dropped.
    /// </summary>
    internal static string? FromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme is not ("http" or "https")) return null;

        // Not GetLeftPart(Path), which keeps the user info.
        return $"{u.Scheme}://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{u.AbsolutePath}";
    }

    private static string Unquote(string token)
    {
        if (token.Length > 0 && token[0] is '\'' or '"') token = token[1..];
        if (token.Length > 0 && token[^1] is '\'' or '"') token = token[..^1];
        return token;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]*$")]
    private static partial Regex Subcommand();

    [GeneratedRegex(@"^/[^\s'""`;|&<>()$]*$")]
    private static partial Regex Endpoint();
}
