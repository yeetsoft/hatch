namespace Hatch.Cli.Tests;

/// <summary>
/// Codex's own <c>--json</c> events, read back: the same two facts <see
/// cref="StreamRenderTests"/> checks for Claude, out of a different vocabulary.
/// </summary>
public sealed class CodexStreamReaderTests
{
    private static (RunFacts Facts, List<string> Lines) Play(string model, params string[] events)
    {
        var facts = new RunFacts();
        var reader = new CodexStreamReader("/tmp/checkout", facts, model);
        var lines = events.SelectMany(reader.Read).ToList();
        return (facts, lines);
    }

    [Fact]
    public void The_session_id_arrives_off_thread_started()
    {
        var (facts, lines) = Play("gpt-5-codex", Fixtures.CodexThreadStarted("abc-123"));

        Assert.Equal("abc-123", facts.SessionId);
        Assert.Contains(lines, l => l == "hatch: session abc-123");
        Assert.Contains(lines, l => l.Contains("abc-123", StringComparison.Ordinal) && l.Contains("join it with", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItemStarted_IsOneLineNamingTheFieldWorthALine()
    {
        var (_, lines) = Play("gpt-5-codex", Fixtures.CodexItemStarted("command_execution", command: "make test-hatch"));
        Assert.Contains(lines, l => l == "  ⏺ command_execution  make test-hatch");
    }

    [Fact]
    public void AnItemStarted_WithNoFieldOfItsOwn_NamesOnlyTheType()
    {
        var (_, lines) = Play("gpt-5-codex", Fixtures.CodexItemStarted("agent_message"));
        Assert.Contains(lines, l => l == "  ⏺ agent_message");
    }

    [Fact]
    public void ACompletedItem_DrawsNothingWhenItSucceeded()
    {
        var (_, lines) = Play("gpt-5-codex", Fixtures.CodexItemCompleted("command_execution", status: "completed", aggregatedOutput: "fine"));
        Assert.Empty(lines);
    }

    [Fact]
    public void ACompletedCommand_ThatFailed_DrawsItsAggregatedOutput()
    {
        var (_, lines) = Play(
            "gpt-5-codex",
            Fixtures.CodexItemCompleted("command_execution", status: "failed", aggregatedOutput: "it went wrong"));
        Assert.Contains(lines, l => l == "  ✗ it went wrong");
    }

    [Fact]
    public void AFailedFileChange_DrawsEveryPathJoined()
    {
        var (_, lines) = Play(
            "gpt-5-codex",
            Fixtures.CodexItemCompleted("file_change", status: "failed", changedPaths: ["a.txt", "b.txt"]));
        Assert.Contains(lines, l => l == "  ✗ a.txt, b.txt");
    }

    [Fact]
    public void AFailedMcpToolCall_PrefersTheErrorMessageOverTheTypeName()
    {
        var (_, withError) = Play(
            "gpt-5-codex",
            Fixtures.CodexItemCompleted("mcp_tool_call", status: "failed", errorMessage: "tool blew up"));
        Assert.Contains(withError, l => l == "  ✗ tool blew up");

        var (_, withoutError) = Play("gpt-5-codex", Fixtures.CodexItemCompleted("mcp_tool_call", status: "failed"));
        Assert.Contains(withoutError, l => l == "  ✗ mcp_tool_call");
    }

    [Fact]
    public void AFailedCollabToolCall_NamesOnlyTheType()
    {
        var (_, lines) = Play("gpt-5-codex", Fixtures.CodexItemCompleted("collab_tool_call", status: "failed"));
        Assert.Contains(lines, l => l == "  ✗ collab_tool_call");
    }

    [Fact]
    public void AnErrorTypedItem_DrawsItsMessage()
    {
        var (_, lines) = Play("gpt-5-codex", Fixtures.CodexItemCompleted("error", status: "failed", message: "could not reach the tool"));
        Assert.Contains(lines, l => l == "  ✗ could not reach the tool");
    }

    [Fact]
    public void TheLastAgentMessage_OverwritesLastAssistantText()
    {
        var (facts, _) = Play(
            "gpt-5-codex",
            Fixtures.CodexItemCompleted("agent_message", text: "first"),
            Fixtures.CodexItemCompleted("agent_message", text: "second"));

        Assert.Equal("second", facts.LastAssistantText);
    }

    [Fact]
    public void TurnCompleted_BuildsTheFourTokenKindsAndOneModelUse()
    {
        var (facts, _) = Play(
            "gpt-5-codex",
            Fixtures.CodexTurnCompleted(inputTokens: 1_000, cachedInputTokens: 200, cacheWriteInputTokens: 50, outputTokens: 300, reasoningOutputTokens: 20));

        Assert.NotNull(facts.Result);
        var model = Assert.Single(facts.Result.Models!);
        Assert.Equal("gpt-5-codex", model.Model);
        Assert.Equal(800, model.InputTokens); // 1_000 - 200 cached
        Assert.Equal(320, model.OutputTokens); // 300 + 20 reasoning
        Assert.Equal(50, model.CacheCreationTokens);
        Assert.Equal(200, model.CacheReadTokens);
        Assert.Null(model.CostUsd);
        Assert.Equal(1, facts.Result.Turns);
        Assert.Null(facts.Result.CostUsd);
        Assert.False(facts.Result.IsError);
        Assert.Equal(0, facts.Result.DurationMs);
    }

    [Fact]
    public void TurnCompleted_DefaultsReasoningOutputTokensToZeroWhenAbsent()
    {
        var (facts, _) = Play("gpt-5-codex", Fixtures.CodexTurnCompleted(outputTokens: 300, reasoningOutputTokens: null));

        var model = Assert.Single(facts.Result!.Models!);
        Assert.Equal(300, model.OutputTokens);
    }

    [Fact]
    public void TurnCompleted_AccumulatesRunningTotalsAcrossSeveralTurns()
    {
        var (facts, _) = Play(
            "gpt-5-codex",
            Fixtures.CodexTurnCompleted(inputTokens: 100, outputTokens: 50),
            Fixtures.CodexTurnCompleted(inputTokens: 10, outputTokens: 5));

        var model = Assert.Single(facts.Result!.Models!);
        Assert.Equal(110, model.InputTokens);
        Assert.Equal(55, model.OutputTokens);
        Assert.Equal(2, facts.Result.Turns);
    }

    [Fact]
    public void TurnCompleted_ParsesTheWorkLogFenceOffTheFinalAgentMessage()
    {
        var (facts, _) = Play(
            "gpt-5-codex",
            Fixtures.CodexItemCompleted("agent_message", text: "```work-log\nA title\n\nA summary.\n```"),
            Fixtures.CodexTurnCompleted());

        Assert.Equal("A title", facts.Result!.Title);
        Assert.Equal("A summary.", facts.Result.Summary);
    }

    [Fact]
    public void TurnCompleted_LiftsTheReleaseNoteFenceBesideTheWorkLog()
    {
        var (facts, _) = Play(
            "gpt-5-codex",
            Fixtures.CodexItemCompleted("agent_message", text: "```release-note\nA note.\n```\n```work-log\nA title\n```"),
            Fixtures.CodexTurnCompleted());

        Assert.Equal("A note.", facts.ReleaseNote);
        Assert.Equal("A title", facts.Result!.Title);
    }

    [Fact]
    public void TurnFailed_LandsItsTextOnBothResultTextAndLastAssistantText()
    {
        var (facts, _) = Play("gpt-5-codex", Fixtures.CodexTurnFailed("unexpected status 401 Unauthorized"));

        Assert.Equal("unexpected status 401 Unauthorized", facts.ResultText);
        Assert.Equal("unexpected status 401 Unauthorized", facts.LastAssistantText);
        Assert.Null(facts.Result);
    }

    [Fact]
    public void StandaloneError_LandsItsTextOnBothFields_AndTheLastOneWins()
    {
        var (facts, _) = Play(
            "gpt-5-codex",
            Fixtures.CodexError("Reconnecting... 1/5"),
            Fixtures.CodexError("Reconnecting... 2/5"));

        Assert.Equal("Reconnecting... 2/5", facts.ResultText);
        Assert.Equal("Reconnecting... 2/5", facts.LastAssistantText);
    }

    [Fact]
    public void Usage_IsAlwaysEmpty_AndUsageReadAt_IsAlwaysNull()
    {
        var reader = new CodexStreamReader("/tmp/checkout", new RunFacts(), "gpt-5-codex");
        Assert.Empty(reader.Usage);
        Assert.Null(reader.UsageReadAt);
    }
}
