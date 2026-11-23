using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Services.Settings;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Tests.Common;

/// <summary>
/// An opened secret never prints its value: not as text, not as JSON, not
/// inside a record that is logged. <see cref="OpenedSecret.Reveal"/> is the one
/// way out, so the tests that need the value call it, and a test that passes
/// only because everything is empty would fail the checks that it is there.
/// </summary>
public class OpenedSecretTests
{
    private const string Value = "sk-opened-secret-value";

    [Fact]
    public void ToString_Is_The_Placeholder()
    {
        var secret = OpenedSecret.From(Value)!;

        Assert.Equal("[secret]", secret.ToString());
        Assert.Equal("[secret]", $"{secret}");
        Assert.DoesNotContain(Value, string.Concat("token=", secret));
    }

    [Fact]
    public void Json_Of_A_Secret_Alone_Is_The_Placeholder()
    {
        var json = JsonSerializer.Serialize(OpenedSecret.From(Value));

        Assert.Equal("\"[secret]\"", json);
    }

    [Fact]
    public void Json_Inside_A_Record_Is_The_Placeholder_Under_Any_Options()
    {
        var holder = new Holder(OpenedSecret.From(Value));

        foreach (var options in new[]
        {
            new JsonSerializerOptions(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower },
        })
        {
            var json = JsonSerializer.Serialize(holder, options);
            Assert.DoesNotContain(Value, json);
            Assert.Contains("[secret]", json);
        }
    }

    [Fact]
    public void Reading_One_From_Json_Is_Refused() =>
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<OpenedSecret>("\"anything\""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Blank_Is_Not_A_Secret(string? plaintext) => Assert.Null(OpenedSecret.From(plaintext));

    [Fact]
    public void Reveal_Gives_Back_What_Went_In() => Assert.Equal(Value, OpenedSecret.From(Value)!.Reveal());

    [Fact]
    public void Trim_Drops_Surrounding_Whitespace_And_Stays_A_Secret()
    {
        var trimmed = OpenedSecret.From($"  {Value}\n")!.Trim();

        Assert.Equal(Value, trimmed.Reveal());
        Assert.Equal("[secret]", trimmed.ToString());
    }

    [Fact]
    public void Equality_Is_By_Reference()
    {
        var one = OpenedSecret.From(Value)!;
        var other = OpenedSecret.From(Value)!;

        Assert.NotEqual(one, other);
        Assert.Same(one, one);
    }

    [Fact]
    public void A_Snapshot_Holding_All_Five_Secrets_Gives_None_Of_Them_Away()
    {
        string[] sentinels =
        [
            "sentinel-google-client-secret",
            "sentinel-anthropic-api-key",
            "sentinel-claude-subscription-token",
            "sentinel-codex-api-key",
            "sentinel-xai-api-key",
        ];
        var snapshot = new SiteSettingsSnapshot(
            TimeZone: "America/New_York",
            GoogleClientId: "client-id",
            GoogleClientSecret: OpenedSecret.From(sentinels[0]),
            GoogleOAuthRedirectUri: null,
            AnthropicApiKey: OpenedSecret.From(sentinels[1]),
            ClaudeSubscriptionToken: OpenedSecret.From(sentinels[2]),
            CodexApiKey: OpenedSecret.From(sentinels[3]),
            XaiApiKey: OpenedSecret.From(sentinels[4]),
            LocalPersonName: null,
            RunnerDefaultMaxTokens: null,
            RunnerDefaultMaxTokensPerHour: null);

        var logger = new CapturingLogger();
        logger.LogInformation("{Snapshot}", snapshot);

        var printed = snapshot.ToString();
        var json = JsonSerializer.Serialize(snapshot);
        var logged = string.Join("\n", logger.Lines);

        foreach (var sentinel in sentinels)
        {
            Assert.DoesNotContain(sentinel, printed);
            Assert.DoesNotContain(sentinel, json);
            Assert.DoesNotContain(sentinel, logged);
        }

        // The checks above only mean something if the values are really there.
        Assert.Equal(sentinels[0], snapshot.GoogleClientSecret!.Reveal());
        Assert.Equal(sentinels[1], snapshot.AnthropicApiKey!.Reveal());
        Assert.Equal(sentinels[2], snapshot.ClaudeSubscriptionToken!.Reveal());
        Assert.Equal(sentinels[3], snapshot.CodexApiKey!.Reveal());
        Assert.Equal(sentinels[4], snapshot.XaiApiKey!.Reveal());
        Assert.Contains("[secret]", printed);
        Assert.NotEmpty(logger.Lines);
    }

    private sealed record Holder(OpenedSecret? Secret);

    /// <summary>Hand-rolled, as in the other tests here: the formatted line, nothing more.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");
    }
}
