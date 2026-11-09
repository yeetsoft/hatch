namespace Hatch.Cli.Tests;

/// <summary>
/// Reading which model a refusal sentence named out of the one sentence each
/// agent leaves behind - the reported lines, and the variations on them the
/// story asks for.
/// </summary>
public sealed class ModelRefusalTests
{
    private static readonly DateTimeOffset EndedAt = DateTimeOffset.Parse("2026-09-28T22:00:00+00:00");

    private const string CreditsSentence = "Fable 5.1 requires usage credits. Switch to another model to continue.";
    private const string UnavailableStraight =
        "There's an issue with the selected model (claude-opus-9). It may not exist or you may not have access to it.";
    private const string UnavailableCurly =
        "There’s an issue with the selected model (claude-opus-9). It may not exist or you may not have access to it.";
    private const string CodexSentence = "the 'gpt-7-nova' model is not supported when using Codex with a ChatGPT account";
    private const string OllamaOlder = "model \"llama3:70b\" not found, try pulling it first";
    private const string OllamaNewer = "model 'llama3:70b' not found";

    [Fact]
    public void Claude_reads_the_credits_sentence_and_its_prefixed_form()
    {
        var plain = ModelRefusal.Claude(CreditsSentence, EndedAt);
        Assert.NotNull(plain);
        Assert.Equal("Fable 5.1", plain!.Value.Model);
        Assert.Null(plain.Value.ResetAt);

        var prefixed = ModelRefusal.Claude("API Error: " + CreditsSentence, EndedAt);
        Assert.NotNull(prefixed);
        Assert.Equal("Fable 5.1", prefixed!.Value.Model);
    }

    [Theory]
    [InlineData(UnavailableStraight)]
    [InlineData(UnavailableCurly)]
    public void Claude_reads_the_unavailable_sentence_with_either_apostrophe(string said)
    {
        var refusal = ModelRefusal.Claude(said, EndedAt);

        Assert.NotNull(refusal);
        Assert.Equal("claude-opus-9", refusal!.Value.Model);
        Assert.Null(refusal.Value.ResetAt);
    }

    [Fact]
    public void Claude_reads_an_undated_reset_appended_to_the_credits_sentence()
    {
        var refusal = ModelRefusal.Claude(CreditsSentence + " · resets 7:40pm (America/New_York)", EndedAt);

        Assert.NotNull(refusal);
        Assert.Equal("Fable 5.1", refusal!.Value.Model);
        Assert.Equal(DateTimeOffset.Parse("2026-09-28T23:40:00+00:00"), refusal.Value.ResetAt);
    }

    [Fact]
    public void Claude_reads_a_dated_reset_appended_to_the_credits_sentence()
    {
        var refusal = ModelRefusal.Claude(CreditsSentence + " · resets Oct 5 at 7:40pm (America/New_York)", EndedAt);

        Assert.NotNull(refusal);
        Assert.Equal("Fable 5.1", refusal!.Value.Model);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T23:40:00+00:00"), refusal.Value.ResetAt);
    }

    [Theory]
    [InlineData(CodexSentence)]
    [InlineData("The 'gpt-7-nova' model is not supported when using Codex with a ChatGPT account")]
    public void Codex_reads_the_unsupported_model_sentence_regardless_of_case(string said)
    {
        var refusal = ModelRefusal.Codex(said);

        Assert.NotNull(refusal);
        Assert.Equal("gpt-7-nova", refusal!.Value.Model);
        Assert.Null(refusal.Value.ResetAt);
    }

    [Theory]
    [InlineData(OllamaOlder)]
    [InlineData(OllamaNewer)]
    public void Ollama_reads_either_wording(string said)
    {
        var refusal = ModelRefusal.Ollama(said);

        Assert.NotNull(refusal);
        Assert.Equal("llama3:70b", refusal!.Value.Model);
        Assert.Null(refusal.Value.ResetAt);
    }

    public static IEnumerable<object?[]> NotAModelRefusal => new[]
    {
        new object?[] { "You've hit your session limit · resets 7:40pm (America/New_York)" },
        new object?[] { "You're out of usage credits" },
        new object?[] { "stream disconnected" },
        new object?[] { "\"foo.txt\" not found" },
        new object?[] { "" },
        new object?[] { "   " },
        new object?[] { null },
    };

    [Theory]
    [MemberData(nameof(NotAModelRefusal))]
    public void Claude_answers_null_for_an_ordinary_error_a_usage_limit_and_blank(string? said) =>
        Assert.Null(ModelRefusal.Claude(said, EndedAt));

    [Theory]
    [MemberData(nameof(NotAModelRefusal))]
    public void Codex_answers_null_for_an_ordinary_error_a_usage_limit_and_blank(string? said) =>
        Assert.Null(ModelRefusal.Codex(said));

    [Theory]
    [MemberData(nameof(NotAModelRefusal))]
    public void Ollama_answers_null_for_an_ordinary_error_a_usage_limit_and_blank(string? said) =>
        Assert.Null(ModelRefusal.Ollama(said));

    [Fact]
    public void Each_recogniser_answers_null_for_the_other_two_sentences()
    {
        Assert.Null(ModelRefusal.Claude(CodexSentence, EndedAt));
        Assert.Null(ModelRefusal.Claude(OllamaOlder, EndedAt));

        Assert.Null(ModelRefusal.Codex(CreditsSentence));
        Assert.Null(ModelRefusal.Codex(OllamaOlder));

        // The one that matters: codex --oss runs Ollama under Codex's own CLI,
        // so OllamaAdapter overrides Codex's recogniser rather than inheriting
        // it - a slip back to the base implementation would read Codex's own
        // sentence as if a local server had said it.
        Assert.Null(ModelRefusal.Ollama(CodexSentence));
        Assert.Null(ModelRefusal.Ollama(CreditsSentence));
    }
}
