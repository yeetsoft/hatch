namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="Program.Usage"/> is what a person reads at a terminal, and what a
/// session reads when nothing more specific was told to it. Nothing else keeps
/// it honest: a command added to <see cref="Program.Commands"/> and dispatched
/// in <c>Program</c>'s own switch can still be missing from here, silently,
/// until somebody who already knew the name went looking for it.
///
/// So every name in <see cref="Program.Commands"/> - <see cref="Program.Internal"/>
/// included, since <c>Usage</c> lists both today and carries no exception for
/// either - is checked against the printed block itself.
/// </summary>
public sealed class ProgramUsageTests
{
    [Fact]
    public void Every_command_the_cli_answers_to_is_named_in_the_help()
    {
        var help = string.Join('\n', Program.Usage);

        // Word-bounded rather than a bare Contains, and looked for as "hatch
        // <command>" rather than the bare name - a word in a note can otherwise
        // stand in for the line, the way "verdict" appears in "a check
        // session's own verdict", or "comment" would be satisfied by
        // "hatch comments".
        var missing = Program.Commands
            .Where(command => !Names(help, "hatch " + command))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"Program.Usage does not list: {string.Join(", ", missing)}. " +
            "Add a \"hatch <command>\" line for each.");
    }

    /// <summary>
    /// True when <paramref name="phrase"/> appears in <paramref name="text"/> as
    /// a whole word on each side - neither touching a letter, a digit or a
    /// hyphen. The hyphen is what separates <c>hatch work</c> from
    /// <c>hatch go-to-work</c>.
    /// </summary>
    private static bool Names(string text, string phrase)
    {
        for (var at = text.IndexOf(phrase, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(phrase, at + 1, StringComparison.Ordinal))
        {
            var before = at == 0 || !Part(text[at - 1]);
            var afterAt = at + phrase.Length;
            var after = afterAt == text.Length || !Part(text[afterAt]);
            if (before && after) return true;
        }

        return false;

        static bool Part(char c) => char.IsLetterOrDigit(c) || c == '-';
    }
}
