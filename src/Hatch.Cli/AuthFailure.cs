using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// Whether a session ended because its agent is signed out of its account
/// rather than because of anything on the ticket - read from whatever the
/// session said on its way out, the same way <see cref="ModelRefusal"/> reads
/// a refused model rather than asking an API.
/// </summary>
public static partial class AuthFailure
{
    /// <summary>
    /// The sentence a sign-in failure was recognised from: the first of
    /// <paramref name="said"/>'s lines that matched, through
    /// <see cref="Scrub.Lines"/> so a key in it is masked and its length is
    /// capped the same way a comment's would be.
    /// </summary>
    public readonly record struct Recognised(string Sentence);

    /// <summary>
    /// Whether <paramref name="said"/> is Claude saying its own account is
    /// signed out - an expired or unrefreshable OAuth session, a rejected API
    /// key, or its own prompt to log in again. Null for anything else,
    /// including an ordinary error and a usage limit.
    /// </summary>
    public static Recognised? Claude(string? said) => Recognise(said, ClaudePattern());

    /// <summary>
    /// Whether <paramref name="said"/> is Codex saying its own account is
    /// signed out. Null for anything else.
    /// </summary>
    public static Recognised? Codex(string? said) => Recognise(said, CodexPattern());

    private static Recognised? Recognise(string? said, Regex pattern)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;

        var hit = said.ReplaceLineEndings("\n").Split('\n').FirstOrDefault(line => pattern.IsMatch(line));
        if (hit is null) return null;

        var sentence = Scrub.Lines([hit]).FirstOrDefault();
        return sentence is { Length: > 0 } ? new Recognised(sentence) : null;
    }

    // HA-1622's own sentence - "Failed to authenticate: OAuth session expired
    // and could not be refreshed" - plus the API-key and /login wordings seen
    // alongside it.
    [GeneratedRegex(@"OAuth session expired|could not be refreshed|Invalid API key|Please run /login|Not logged in|authentication_error",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClaudePattern();

    [GeneratedRegex(@"Not logged in|codex login", RegexOptions.IgnoreCase)]
    private static partial Regex CodexPattern();

    // Grok gets no pattern yet: no real sentence naming a signed-out account
    // has been seen in the repo or its docs, the same posture ModelRefusal
    // takes for Grok's own unseen refusal - see docs/hatch.md's Grok section.
    // The default interface member answers null until one is.
}
