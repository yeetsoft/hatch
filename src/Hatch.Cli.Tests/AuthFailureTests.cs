namespace Hatch.Cli.Tests;

/// <summary>
/// Reading a signed-out account out of the sentence each agent leaves behind -
/// the reported lines, and the variations on them the story asks for.
/// </summary>
public sealed class AuthFailureTests
{
    private const string ClaudeOAuth = "Failed to authenticate: OAuth session expired and could not be refreshed";
    private const string ClaudeApiKey = "Invalid API key · please run /login";
    private const string ClaudeLoginPrompt = "Please run /login";
    private const string ClaudeNotLoggedIn = "Not logged in";
    private const string ClaudeAuthError = "authentication_error: could not verify credentials";
    private const string CodexNotLoggedIn = "Not logged in";
    private const string CodexLoginPrompt = "run `codex login` to authenticate";

    [Theory]
    [InlineData(ClaudeOAuth)]
    [InlineData(ClaudeApiKey)]
    [InlineData(ClaudeLoginPrompt)]
    [InlineData(ClaudeNotLoggedIn)]
    [InlineData(ClaudeAuthError)]
    public void Claude_reads_its_own_sign_in_sentences(string said)
    {
        var recognised = AuthFailure.Claude(said);

        Assert.NotNull(recognised);
        Assert.Equal(said, recognised!.Value.Sentence);
    }

    [Fact]
    public void Claude_reads_the_sentence_out_of_multi_line_stderr()
    {
        var said = $"some earlier line\n{ClaudeOAuth}\nsome later line";

        var recognised = AuthFailure.Claude(said);

        Assert.NotNull(recognised);
        Assert.Equal(ClaudeOAuth, recognised!.Value.Sentence);
    }

    [Fact]
    public void Claude_masks_a_key_in_the_sentence_it_returns()
    {
        var said = $"Invalid API key hatch_ak_abc123 - {ClaudeLoginPrompt}";

        var recognised = AuthFailure.Claude(said);

        Assert.NotNull(recognised);
        Assert.DoesNotContain("hatch_ak_abc123", recognised!.Value.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CodexNotLoggedIn)]
    [InlineData(CodexLoginPrompt)]
    public void Codex_reads_its_own_sign_in_sentences(string said)
    {
        var recognised = AuthFailure.Codex(said);

        Assert.NotNull(recognised);
        Assert.Equal(said, recognised!.Value.Sentence);
    }

    public static IEnumerable<object?[]> NotAnAuthFailure => new[]
    {
        new object?[] { "the build failed" },
        new object?[] { "You've hit your session limit · resets 7:40pm (America/New_York)" },
        new object?[] { "Fable 5.1 requires usage credits. Switch to another model to continue." },
        new object?[] { "" },
        new object?[] { "   " },
        new object?[] { null },
    };

    [Theory]
    [MemberData(nameof(NotAnAuthFailure))]
    public void Claude_answers_null_for_a_usage_limit_a_model_refusal_an_ordinary_failure_and_blank(string? said) =>
        Assert.Null(AuthFailure.Claude(said));

    [Theory]
    [MemberData(nameof(NotAnAuthFailure))]
    public void Codex_answers_null_for_a_usage_limit_a_model_refusal_an_ordinary_failure_and_blank(string? said) =>
        Assert.Null(AuthFailure.Codex(said));

    [Fact]
    public void Ollama_has_no_recogniser_and_answers_null()
    {
        var adapter = new OllamaAdapter();

        Assert.Null(adapter.RecogniseAuthFailure(ClaudeOAuth));
    }

    [Fact]
    public void Grok_takes_the_interface_default_and_answers_null()
    {
        IAgentAdapter adapter = new GrokAdapter();

        Assert.Null(adapter.RecogniseAuthFailure(ClaudeOAuth));
    }
}
