namespace Hatch.Cli.Tests;

/// <summary>
/// <c>cli-reference.md</c> is prose that describes every command a session may
/// type, and nothing but the dispatch keeps it honest: a renamed command or a
/// flag added to a usage block would leave the page silently stale.
///
/// So the page is read here, against <see cref="Program.Commands"/> for every
/// command, and against each command's own <c>*Usage</c> array for every
/// flag in it. <see cref="Program.Internal"/> commands (<c>inbox</c> and
/// <c>runner-credential</c>) are a session's own plumbing, not something a
/// session types, so neither they nor their flags are checked here.
/// </summary>
public sealed class CliReferenceDocsTests
{
    private static readonly Dictionary<string, string[]> UsageByCommand = new()
    {
        ["config"] = ConfigCommand.ConfigUsage,
        ["use"] = UseCommand.UseUsage,
        ["standby"] = StandbyCommand.StandbyUsage,
        ["board"] = BoardCommands.BoardUsage,
        ["next"] = BoardCommands.NextUsage,
        ["queue"] = BoardCommands.QueueUsage,
        ["changelog"] = ChangelogCommand.ChangelogUsage,
        ["runners"] = RunnersCommand.RunnersUsage,
        ["show"] = IssueCommands.ShowUsage,
        ["comments"] = IssueCommands.CommentsUsage,
        ["start"] = IssueCommands.StartUsage,
        ["move"] = IssueCommands.MoveUsage,
        ["comment"] = IssueCommands.CommentUsage,
        ["pr"] = IssueCommands.PrUsage,
        ["landed"] = IssueCommands.LandedUsage,
        ["verdict"] = IssueCommands.VerdictUsage,
        ["depends"] = DependsCommand.DependsUsage,
        ["one-pr"] = OnePrCommand.OnePrUsage,
        ["ask"] = QuestionCommands.AskUsage,
        ["questions"] = QuestionCommands.QuestionsUsage,
        ["answer"] = QuestionCommands.AnswerUsage,
        ["api"] = ApiCommand.ApiUsage,
        ["work"] = WorkCommand.WorkUsage,
        ["go-to-work"] = GoToWorkCommand.GoToWorkUsage,
        ["do-my-work"] = GoToWorkCommand.GoToWorkUsage,
    };

    private static readonly System.Text.RegularExpressions.Regex Flag =
        new(@"(?<![\w-])--[a-z][a-z0-9-]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    [Fact]
    public void Every_command_a_session_may_type_is_named_on_the_page()
    {
        var doc = File.ReadAllText(Doc());

        var missing = Program.Commands.Except(Program.Internal)
            .Where(command => !Names(doc, command)).ToArray();

        Assert.True(missing.Length == 0,
            $"cli-reference.md does not name: {string.Join(", ", missing)}. " +
            "Add a row to ## Every command and a ### section for each.");
    }

    /// <summary>
    /// The audit: a command that reaches neither side of the map could slip
    /// past the flag check below by having no usage block to read at all, and
    /// an entry that names no real command would check nothing.
    /// </summary>
    [Fact]
    public void The_map_covers_every_command_and_nothing_else()
    {
        var commands = Program.Commands.Except(Program.Internal).ToArray();

        var uncovered = commands.Where(command => !UsageByCommand.ContainsKey(command)).ToArray();
        Assert.True(uncovered.Length == 0,
            $"UsageByCommand has no entry for: {string.Join(", ", uncovered)}.");

        var bogus = UsageByCommand.Keys.Where(command => !Program.Commands.Contains(command)).ToArray();
        Assert.True(bogus.Length == 0,
            $"UsageByCommand names a command that does not exist: {string.Join(", ", bogus)}.");
    }

    [Fact]
    public void Every_flag_in_a_commands_usage_block_is_on_the_page()
    {
        var doc = File.ReadAllText(Doc());

        var missing = new List<string>();
        foreach (var (command, usage) in UsageByCommand)
        {
            var text = string.Join('\n', usage);
            var flags = Flag.Matches(text).Select(m => m.Value).Distinct();
            foreach (var flag in flags)
            {
                if (!Names(doc, flag)) missing.Add($"{command} {flag}");
            }
        }

        Assert.True(missing.Count == 0,
            $"cli-reference.md does not name: {string.Join(", ", missing)}. " +
            "Add each flag to its command's usage block on the page.");
    }

    /// <summary>
    /// True when <paramref name="word"/> appears in <paramref name="text"/> as
    /// a whole word - neither side touching a letter, a digit or a hyphen. The
    /// hyphen is what separates <c>work</c> from <c>go-to-work</c>, and
    /// <c>--mine</c> from <c>--mine-only</c>.
    /// </summary>
    private static bool Names(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            var before = at == 0 || !Part(text[at - 1]);
            var afterAt = at + word.Length;
            var after = afterAt == text.Length || !Part(text[afterAt]);
            if (before && after) return true;
        }

        return false;

        static bool Part(char c) => char.IsLetterOrDigit(c) || c == '-';
    }

    /// <summary>
    /// Walks up from the test binary to the repository, the way
    /// <c>DocsContractTests</c> does, rather than assuming how deep
    /// <c>bin/Debug/net10.0</c> happens to be today.
    /// </summary>
    private static string Doc()
    {
        const string relative = "src/Hatch.Web/apps/hatch/docs/cli-reference.md";

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }
}
