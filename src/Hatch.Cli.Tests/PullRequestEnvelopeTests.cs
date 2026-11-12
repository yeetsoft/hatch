namespace Hatch.Cli.Tests;

/// <summary>
/// The writer and reader on their own, outside the corpus: round-tripping,
/// replacing an envelope already in a body, and the truncation a chain too
/// large for <see cref="PullRequestEnvelopeWriter.GitHubBodyLimit"/> forces.
/// </summary>
public sealed class PullRequestEnvelopeTests
{
    private static PullRequestEnvelope Envelope(params PullRequestEnvelopeIssue[] chain) =>
        new(PullRequestEnvelope.CurrentVersion, "hatch.example.com", "AER", chain);

    [Fact]
    public void Replace_OnAnEmptyBody_RoundTripsTheChainInOrder()
    {
        var chain = new[]
        {
            new PullRequestEnvelopeIssue("AER-13", "A task", Type: "task", ParentKey: "AER-12"),
            new PullRequestEnvelopeIssue("AER-12", "A story", Type: "story"),
        };
        var envelope = Envelope(chain);

        var body = PullRequestEnvelopeWriter.Replace("", envelope);

        Assert.True(PullRequestEnvelopeReader.TryRead(body, out var read, out var reason), reason);
        Assert.Equal(envelope.Version, read!.Version);
        Assert.Equal(envelope.Instance, read.Instance);
        Assert.Equal(envelope.Project, read.Project);
        Assert.Equal(envelope.Chain, read.Chain);
    }

    [Fact]
    public void Replace_WithAnOversizeChain_TruncatesLongestDescriptionsFirst_UnderTheLimit()
    {
        const int limit = 2000;
        var chain = new[]
        {
            new PullRequestEnvelopeIssue("AER-13", "A task", Description: new string('a', 1500)),
            new PullRequestEnvelopeIssue("AER-12", "A story", Description: new string('b', 300)),
        };
        var envelope = Envelope(chain);

        var body = PullRequestEnvelopeWriter.Replace("", envelope, limit);

        Assert.True(body.Length <= limit);
        Assert.True(PullRequestEnvelopeReader.TryRead(body, out var read, out var reason), reason);
        var task = Assert.Single(read!.Chain, i => i.Key == "AER-13");
        Assert.True(task.Truncated);
        var story = read.Chain.Single(i => i.Key == "AER-12");
        Assert.False(story.Truncated);
        Assert.Equal(300, story.Description!.Length);
    }

    [Fact]
    public void Replace_OnABodyWithAnOlderEnvelope_ReplacesOnlyThatSpan()
    {
        var before = Envelope(new PullRequestEnvelopeIssue("AER-12", "A story"));
        var original = "Some notes above.\n\n" + PullRequestEnvelopeWriter.Replace("", before) + "\n\nSome notes below.";

        var after = Envelope(new PullRequestEnvelopeIssue("AER-12", "A renamed story"));
        var replaced = PullRequestEnvelopeWriter.Replace(original, after);

        Assert.StartsWith("Some notes above.\n\n", replaced);
        Assert.EndsWith("\n\nSome notes below.", replaced);
        Assert.True(PullRequestEnvelopeReader.TryRead(replaced, out var read, out var reason), reason);
        Assert.Equal("A renamed story", read!.Chain.Single().Title);
    }

    [Fact]
    public void Replace_WithNoExistingEnvelope_AppendsAfterABlankLine()
    {
        var envelope = Envelope(new PullRequestEnvelopeIssue("AER-12", "A story"));

        var body = PullRequestEnvelopeWriter.Replace("Some notes.", envelope);

        Assert.StartsWith("Some notes.\n\n" + PullRequestEnvelopeMarkers.Start, body);
    }

    [Fact]
    public void TryRead_OnABodyWithNoMarkers_Fails()
    {
        Assert.False(PullRequestEnvelopeReader.TryRead("Ordinary pull request text.", out var envelope, out var reason));
        Assert.Null(envelope);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("AER-12 Auth: the first Admin", "[AER-12](https://hatch.example.com/issues/AER-12)", "AER-12", "hatch.example.com")]
    [InlineData("AER-12 Auth: the first Admin", "[AER-12](https://hatch.example.com/install/issues/AER-12)", "AER-12", "hatch.example.com/install")]
    [InlineData("AER-12 Auth: the first Admin", "[AER-12](https://hatch.example.com/apps/hatch/issues/AER-12)", "AER-12", "hatch.example.com")]
    [InlineData("AER-12 Auth: the first Admin", "[AER-12](https://hatch.example.com/install/apps/hatch/issues/AER-12)", "AER-12", "hatch.example.com/install")]
    public void TryReadVersionZero_OnTheRootShapeAndTheOldOne_ReadsTheKeyAndInstance(string title, string body, string expectedKey, string expectedInstance)
    {
        Assert.True(PullRequestEnvelopeReader.TryReadVersionZero(title, body, out var key, out var instance));
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedInstance, instance);
    }

    [Fact]
    public void TryReadVersionZero_WhenTheBracketedKeyDisagreesWithTheTitle_Fails()
    {
        Assert.False(PullRequestEnvelopeReader.TryReadVersionZero(
            "AER-12 Auth: the first Admin",
            "[AER-13](https://hatch.example.com/apps/hatch/issues/AER-13)",
            out var key,
            out var instance));
        Assert.Null(key);
        Assert.Null(instance);
    }
}
