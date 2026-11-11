using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// The last <c>capacity</c> lines of something, kept as they arrive: stderr and
/// the non-stream half of stdout are read on two threads beside the process, so
/// the buffer takes its own lock.
/// </summary>
internal sealed class Tail(int capacity = Scrub.MaxLines)
{
    private readonly Queue<string> _lines = new();
    private readonly object _gate = new();

    public void Add(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > capacity) _lines.Dequeue();
        }
    }

    public List<string> ToList()
    {
        lock (_gate) return [.. _lines];
    }
}

/// <summary>
/// What a session said on its way out is the CLI's own text, and it goes onto a
/// ticket: so keys and bearer values are masked, a line is capped, and nothing
/// in it can close the comment's code fence.
/// </summary>
public static partial class Scrub
{
    public const int MaxLines = 40;
    public const int MaxLineLength = 200;
    public const string Marker = "[redacted]";

    /// <summary>
    /// At most the last <see cref="MaxLines"/> lines, each masked, then capped,
    /// then dropped if blank. Masked before capped so a key that straddles the
    /// cap is never half shown.
    /// </summary>
    public static List<string> Lines(IEnumerable<string> lines)
    {
        var kept = new List<string>();
        foreach (var line in lines.TakeLast(MaxLines))
        {
            var clean = Cap(Mask(line)).Replace("```", "'''").TrimEnd();
            if (clean.Length > 0) kept.Add(clean);
        }

        return kept;
    }

    /// <summary>The kept lines as the comment shows them, in a fence they cannot close.</summary>
    public static string Fence(string kept) => $"The CLI said:\n\n```\n{kept}\n```";

    private static string Mask(string line)
    {
        line = HatchKey().Replace(line, Marker);
        line = Bearer().Replace(line, Marker);
        line = Anthropic().Replace(line, Marker);
        line = OpenAi().Replace(line, Marker);
        return Xai().Replace(line, Marker);
    }

    private static string Cap(string line) =>
        line.Length <= MaxLineLength ? line : string.Concat(line.AsSpan(0, MaxLineLength), "…");

    [GeneratedRegex(@"hatch_ak_[A-Za-z0-9_-]+")]
    private static partial Regex HatchKey();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_-]{8,}")]
    private static partial Regex Anthropic();

    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{20,}")]
    private static partial Regex OpenAi();

    [GeneratedRegex(@"xai-[A-Za-z0-9_-]{20,}")]
    private static partial Regex Xai();
}
