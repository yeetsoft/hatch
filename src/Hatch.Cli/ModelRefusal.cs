using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// Whether a session ended because the model it asked for could not be
/// spawned at all - a wrong or unreachable id - read from whatever the
/// session said on its way out, the same way <see cref="UsageLimit"/> reads a
/// usage limit rather than asking an API.
/// </summary>
public static partial class ModelRefusal
{
    /// <summary>
    /// The model a refusal sentence named, and the instant it resets where the
    /// sentence names one. <see cref="Model"/> is the sentence's own spelling,
    /// trimmed - the caller narrows by the alias it spawned, not by this
    /// string. Both null for a sentence that names no model.
    /// </summary>
    public readonly record struct Recognised(string? Model, DateTimeOffset? ResetAt);

    /// <summary>
    /// Whether <paramref name="said"/> is Claude naming a model it could not
    /// spawn - out of usage credits for that model, or the model unavailable
    /// outright. Null for anything else, including an ordinary error and a
    /// usage-limit sentence (<see cref="UsageLimit.Recognise"/>'s own words,
    /// never this one's).
    /// </summary>
    public static Recognised? Claude(string? said, DateTimeOffset endedAt, TimeZoneInfo? zone = null)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;

        var model = CreditsPattern().Match(said) is { Success: true } credits
            ? credits.Groups["model"].Value
            : UnavailablePattern().Match(said) is { Success: true } unavailable
                ? unavailable.Groups["model"].Value
                : null;

        if (model is null) return null;

        return new Recognised(model.Trim(), UsageLimit.ResetNamed(said, endedAt, zone));
    }

    /// <summary>
    /// Whether <paramref name="said"/> is Codex's own refusal of a model id a
    /// ChatGPT-account login cannot reach. Null for anything else.
    /// </summary>
    public static Recognised? Codex(string? said)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;

        var match = CodexPattern().Match(said);
        return match.Success ? new Recognised(match.Groups["model"].Value.Trim(), null) : null;
    }

    /// <summary>
    /// Whether <paramref name="said"/> is a local Ollama server saying it has
    /// no such model pulled - either of its two known wordings. Null for
    /// anything else, including a file or a command the shell could not find.
    /// </summary>
    public static Recognised? Ollama(string? said)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;

        var match = OllamaNamedPattern().Match(said);
        if (!match.Success) match = OllamaPullPattern().Match(said);

        return match.Success ? new Recognised(match.Groups["model"].Value.Trim(), null) : null;
    }

    // "API Error: Fable 5.1 requires usage credits. Switch to another model to
    // continue." - the model is whatever sits between the start of the line (or
    // the last ": " / ". ") and " requires usage credits", greedy so the leading
    // ".*" gives up the longest prefix that still lets the rest match.
    [GeneratedRegex(@"(?:.*[:.]\s)?(?<model>[^\n]+?)\s+requires usage credits\b[^\n]*?Switch to another model",
        RegexOptions.IgnoreCase)]
    private static partial Regex CreditsPattern();

    // "There's an issue with the selected model (<id>). It may not exist or
    // you may not have access to it." - deliberately not anchored on "There's",
    // which varies by apostrophe and is not part of what names the model.
    [GeneratedRegex(@"issue with the selected model\s*\((?<model>[^)]+)\)\.?\s*It may not exist",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnavailablePattern();

    [GeneratedRegex(@"the\s+[\x27\x22\x60](?<model>[^\x27\x22\x60]+)[\x27\x22\x60]\s+model is not supported when using Codex with a ChatGPT account",
        RegexOptions.IgnoreCase)]
    private static partial Regex CodexPattern();

    [GeneratedRegex(@"model\s+[\x27\x22\x60](?<model>[^\x27\x22\x60\r\n]+)[\x27\x22\x60]\s+not found",
        RegexOptions.IgnoreCase)]
    private static partial Regex OllamaNamedPattern();

    [GeneratedRegex(@"[\x27\x22\x60](?<model>[^\x27\x22\x60\r\n]+)[\x27\x22\x60]\s+not found,\s*try pulling it first",
        RegexOptions.IgnoreCase)]
    private static partial Regex OllamaPullPattern();
}
