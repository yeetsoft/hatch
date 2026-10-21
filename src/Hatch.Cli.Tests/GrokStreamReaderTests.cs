using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// Grok Build's own <c>streaming-json</c> and single-object <c>json</c>, read
/// back into the same <see cref="RunFacts"/> either way.
/// </summary>
public sealed class GrokStreamReaderTests
{
    private static (RunFacts Facts, GrokStreamReader Reader) New()
    {
        var facts = new RunFacts();
        return (facts, new GrokStreamReader("/tmp/checkout", facts));
    }

    [Fact]
    public void TheSessionId_IsMintedBeforeAnyLine_AndReplacedByEnd()
    {
        var (facts, reader) = New();

        var minted = facts.SessionId;
        Assert.False(string.IsNullOrEmpty(minted));
        Assert.True(Guid.TryParse(minted, out _));

        reader.Read(Fixtures.GrokEnd(sessionId: "abc123"));

        Assert.Equal("abc123", facts.SessionId);
        Assert.NotEqual(minted, facts.SessionId);
    }

    [Fact]
    public void Usage_DrivesTheFourTokenKinds_DedupedByMessageId_AndExcludesReasoning()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokUsage(
            messageId: "resp_1", inputTokens: 100, outputTokens: 50, cacheReadTokens: 10, cacheCreationTokens: 5, reasoningTokens: 999));
        reader.Read(Fixtures.GrokUsage(
            messageId: "resp_1", inputTokens: 100, outputTokens: 50, cacheReadTokens: 10, cacheCreationTokens: 5, reasoningTokens: 999));

        Assert.Equal(165, reader.TokensSoFar); // 100+50+10+5, never the 999 reasoning tokens
        Assert.Equal(1, reader.Requests);
        Assert.Equal(115, reader.PeakContextTokens); // 100+5+10, output excluded
    }

    [Fact]
    public void Usage_CountsEveryLine_WhenMessageIdIsAbsent()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokUsage(messageId: null, inputTokens: 10, outputTokens: 10));
        reader.Read(Fixtures.GrokUsage(messageId: null, inputTokens: 10, outputTokens: 10));

        Assert.Equal(2, reader.Requests);
        Assert.Equal(40, reader.TokensSoFar);
    }

    [Fact]
    public void ALaterFence_BeatsAnEarlierQuotedOne()
    {
        var (facts, reader) = New();

        reader.Read(Fixtures.GrokText("```work-log\nAn earlier quoted title\n\nIgnored.\n```"));
        reader.Read(Fixtures.GrokToolCall());
        reader.Read(Fixtures.GrokToolUpdate());
        reader.Read(Fixtures.GrokText("```work-log\nThe real title\n\nThe real summary.\n```"));
        reader.Read(Fixtures.GrokEnd());

        Assert.Equal("The real title", facts.Result!.Title);
        Assert.Equal("The real summary.", facts.Result.Summary);
    }

    [Fact]
    public void AFence_InAnEarlierMessage_DescribesTheRowWhenTheFinalMessageHasNone()
    {
        var (facts, reader) = New();

        reader.Read(Fixtures.GrokText("```work-log\nMiddle title\n\n"));
        reader.Read(Fixtures.GrokText("Middle body.\n```"));
        reader.Read(Fixtures.GrokToolCall());
        reader.Read(Fixtures.GrokToolUpdate());
        reader.Read(Fixtures.GrokText("Done."));
        reader.Read(Fixtures.GrokEnd());

        Assert.Equal("Middle title", facts.Result!.Title);
        Assert.Equal("Middle body.", facts.Result.Summary);
    }

    [Fact]
    public void CostUsd_IsNullWithNoCostReported()
    {
        var (facts, reader) = New();

        reader.Read(Fixtures.GrokEnd());

        Assert.Null(facts.CostUsd);
        Assert.Null(facts.Result!.CostUsd);
    }

    [Fact]
    public void CostUsd_IsExactFromTicks_AndModelsFollowTheirOwnCostUsd()
    {
        var (facts, reader) = New();

        var modelUsage = new Dictionary<string, object>
        {
            ["grok-4.7"] = new
            {
                inputTokens = 7210,
                outputTokens = 1893,
                cacheReadInputTokens = 41000,
                cacheCreationInputTokens = 0,
                modelCalls = 7,
                costUSD = 0.01268905m,
            },
        };

        reader.Read(Fixtures.GrokEnd(modelUsage: modelUsage, totalCostUsdTicks: 126890500, totalCostUsd: 0.01268905m));

        Assert.Equal(0.01268905m, facts.CostUsd);
        Assert.Equal(0.01268905m, facts.Result!.CostUsd);

        var model = Assert.Single(facts.Result.Models!);
        Assert.Equal(0.01268905m, model.CostUsd);
    }

    [Fact]
    public void WithNoModelUsage_OneRowKeyedGrok_CarriesTheTopLevelUsagesCounts()
    {
        var (facts, reader) = New();

        var usage = new
        {
            input_tokens = 500,
            output_tokens = 100,
            cache_read_input_tokens = 20,
            cache_creation_input_tokens = 5,
        };

        reader.Read(Fixtures.GrokEnd(usage: usage, totalCostUsd: 0.02m));

        var model = Assert.Single(facts.Result!.Models!);
        Assert.Equal("grok", model.Model);
        Assert.Equal(500, model.InputTokens);
        Assert.Equal(100, model.OutputTokens);
        Assert.Equal(5, model.CacheCreationTokens);
        Assert.Equal(20, model.CacheReadTokens);
        Assert.Equal(0.02m, model.CostUsd);
    }

    [Fact]
    public void AnError_LandsItsTextOnBothFields_AndResultStaysNullWithNoEnd()
    {
        var (facts, reader) = New();

        reader.Read(Fixtures.GrokError("rate limited"));

        Assert.Equal("rate limited", facts.ResultText);
        Assert.Equal("rate limited", facts.LastAssistantText);
        Assert.Null(facts.Result);
    }

    [Fact]
    public void TheQuietJsonObject_ReadsToTheSameFactsAsTheStream()
    {
        var modelUsage = new Dictionary<string, object>
        {
            ["grok-4.7"] = new
            {
                inputTokens = 7210,
                outputTokens = 1893,
                cacheReadInputTokens = 41000,
                cacheCreationInputTokens = 0,
                modelCalls = 7,
                costUSD = 0.01268905m,
            },
        };
        const string fence = "```release-note\nA note.\n```\n```work-log\nA title\n\nA summary.\n```";

        var (streamFacts, streamReader) = New();
        streamReader.Read(Fixtures.GrokUsage(inputTokens: 7210, outputTokens: 1893, cacheReadTokens: 41000, cacheCreationTokens: 0));
        streamReader.Read(Fixtures.GrokText(fence));
        streamReader.Read(Fixtures.GrokEnd(modelUsage: modelUsage, totalCostUsdTicks: 126890500, totalCostUsd: 0.01268905m));

        var (jsonFacts, jsonReader) = New();
        jsonReader.Read(Fixtures.GrokJson(
            text: fence, inputTokens: 7210, outputTokens: 1893, cacheReadTokens: 41000, cacheCreationTokens: 0,
            modelUsage: modelUsage, totalCostUsdTicks: 126890500, totalCostUsd: 0.01268905m));

        Assert.Equal("abc123", streamFacts.SessionId);
        Assert.Equal("abc123", jsonFacts.SessionId);
        Assert.Equal(streamFacts.CostUsd, jsonFacts.CostUsd);
        Assert.Equal(streamReader.TokensSoFar, jsonReader.TokensSoFar);
        Assert.Equal(streamReader.Requests, jsonReader.Requests);
        Assert.Equal(streamReader.PeakContextTokens, jsonReader.PeakContextTokens);
        Assert.Equal("A note.", streamFacts.ReleaseNote);
        Assert.Equal("A note.", jsonFacts.ReleaseNote);
        Assert.Equal(streamFacts.Result!.Title, jsonFacts.Result!.Title);
        Assert.Equal(streamFacts.Result.Summary, jsonFacts.Result.Summary);
        Assert.Equal(streamFacts.Result.CostUsd, jsonFacts.Result.CostUsd);

        var streamModel = Assert.Single(streamFacts.Result.Models!);
        var jsonModel = Assert.Single(jsonFacts.Result.Models!);
        Assert.Equal(streamModel.CostUsd, jsonModel.CostUsd);
    }

    [Fact]
    public void AnUnknownType_AndANonJsonLine_DoNotThrow()
    {
        var (_, reader) = New();

        var ex2 = Record.Exception(() => reader.Read(JsonSerializer.Serialize(new { type = "some_future_type" })).ToList());
        var ex3 = Record.Exception(() => reader.Read("not json at all").ToList());

        Assert.Null(ex2);
        Assert.Null(ex3);
    }

    [Fact]
    public void Usage_IsAlwaysEmpty_AndUsageReadAt_IsAlwaysNull()
    {
        var (_, reader) = New();

        Assert.Empty(reader.Usage);
        Assert.Null(reader.UsageReadAt);
    }

    [Fact]
    public void TheBanner_IsTheFirstThingOut_OfTheVeryFirstReadCall()
    {
        var (facts, reader) = New();

        var lines = reader.Read(Fixtures.GrokUsage()).ToList();

        Assert.Equal($"hatch: session {facts.SessionId}", lines[0]);
        Assert.Equal($"hatch:   join it with  grok --resume {facts.SessionId}", lines[1]);
    }

    [Fact]
    public void AToolCall_DrawsOneLine_OffItsRawInput()
    {
        var (_, reader) = New();
        reader.Read(Fixtures.GrokUsage()); // skip the banner

        var lines = reader.Read(Fixtures.GrokToolCall()).ToList();

        Assert.Contains("  ⏺ read_file  src/main.rs", lines);
    }

    [Fact]
    public void AFailedToolCallUpdate_DrawsItsRawOutput()
    {
        var (_, reader) = New();
        reader.Read(Fixtures.GrokUsage()); // skip the banner

        var lines = reader.Read(Fixtures.GrokToolUpdate(status: "failed")).ToList();

        Assert.Contains("  ✗ {\"lines\":42}", lines);
    }

    [Fact]
    public void ASucceededToolCallUpdate_DrawsNothing()
    {
        var (_, reader) = New();
        reader.Read(Fixtures.GrokUsage()); // skip the banner

        var lines = reader.Read(Fixtures.GrokToolUpdate(status: "completed")).ToList();

        Assert.Empty(lines);
    }

    [Fact]
    public void Text_BufferedAcrossAToolCall_FlushesIntact()
    {
        var (_, reader) = New();
        reader.Read(Fixtures.GrokUsage()); // skip the banner

        reader.Read(Fixtures.GrokText("Hello "));
        reader.Read(Fixtures.GrokText("world"));
        var lines = reader.Read(Fixtures.GrokToolCall()).ToList();

        Assert.Equal(["", "Hello world", "  ⏺ read_file  src/main.rs"], lines);
    }

    [Fact]
    public void Thought_FiresExactlyAt3000AccumulatedChars_AndNotBefore()
    {
        var (_, reader) = New();
        reader.Read(Fixtures.GrokUsage()); // skip the banner

        var short2999 = reader.Read(Fixtures.GrokThought(new string('x', 2999))).ToList();
        Assert.Empty(short2999);

        var at3000 = reader.Read(Fixtures.GrokThought("x")).ToList();
        Assert.Equal(["  ✻ thinking…"], at3000);
    }

    // ---- What the session read ----

    [Fact]
    public void ReadFile_CountsItsTargetFile_RepoRelative()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokToolCall("read_file", new { target_file = "/tmp/checkout/src/foo.rs" })).ToList();

        Assert.Equal([("src/foo.rs", 1)], reader.Reads.Select(r => (r.Path, r.Count)));
    }

    [Fact]
    public void AToolCall_AndItsUpdate_CountOnce()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokToolCall("read_file", new { target_file = "a.rs" })).ToList();
        reader.Read(Fixtures.GrokToolUpdate()).ToList();

        Assert.Equal([("a.rs", 1)], reader.Reads.Select(r => (r.Path, r.Count)));
    }

    [Fact]
    public void AnyTool_WithAHatchCommand_CountsByVerbOrEndpoint()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokToolCall("shell", new { command = "hatch show HA-1" })).ToList();
        reader.Read(Fixtures.GrokToolCall("shell", new { command = "hatch api GET /x?y=1" }, toolCallId: "call_2")).ToList();

        Assert.Equal(
            [("/x", 1), ("hatch show", 1)],
            reader.Reads.Select(r => (r.Path, r.Count)).OrderBy(r => r.Path, StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnrecognisedToolName_WithAUrlOrPathField_IsNotCounted()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokToolCall("grep", new { url = "https://example.test/x", path = "src" })).ToList();

        Assert.Empty(reader.Reads);
    }

    [Fact]
    public void AnUnrecognisedCommand_IsNotCounted()
    {
        var (_, reader) = New();

        reader.Read(Fixtures.GrokToolCall("shell", new { command = "cat foo.txt" })).ToList();

        Assert.Empty(reader.Reads);
    }

    [Fact]
    public void FiftyOneDistinctPaths_KeepTheHeaviestFifty()
    {
        var (_, reader) = New();

        for (var i = 0; i < 51; i++)
            for (var n = 0; n <= i; n++)
                reader.Read(Fixtures.GrokToolCall("read_file", new { target_file = $"src/file{i:D2}.rs" }, toolCallId: $"call_{i}_{n}")).ToList();

        var reads = reader.Reads;

        Assert.Equal(ReadsLedger.Max, reads.Count);
        Assert.Equal(51, reads[0].Count);
        Assert.DoesNotContain(reads, r => r.Path == "src/file00.rs");
    }
}
