using System.Net;
using System.Text.Json;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>hatch inbox</c>, which a session's hooks call: what it prints for each
/// hook, when it asks, and the one thing it must never do, which is fail.
/// </summary>
public sealed class InboxCommandTests : IDisposable
{
    private const string Deliver = "/api/hatch/issues/AER-1/messages/deliver";

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 3, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _h = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("hatch-inbox-test-").FullName;

    private string Stamp => Path.Combine(_dir, "inbox.stamp");
    private string Clamp => Path.Combine(_dir, "clamp.json");

    public void Dispose()
    {
        _h.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private InboxCommand Command(TimeSpan? limit = null) =>
        new(_h.Board, _h.Say, new StringReader("{\"tool_name\":\"Bash\"}"), new FrozenClock(Now))
        {
            Limit = limit ?? TimeSpan.FromSeconds(InboxCommand.CallSeconds),
        };

    private Task<int> Step(params string[] more) =>
        Command().RunAsync(["AER-1", "--hook", "post-tool-use", "--stamp", Stamp, .. more], default);

    private Task<int> StepClamped() =>
        Step("--clamp", Clamp);

    private Task<int> Stop() => Command().RunAsync(["AER-1", "--hook", "stop"], default);

    private Task<int> StopClamped() =>
        Command().RunAsync(["AER-1", "--hook", "stop", "--clamp", Clamp], default);

    private void Waiting(params CommentDto[] messages) => _h.Wire.Json("POST", Deliver, messages);

    private void Touched(TimeSpan ago)
    {
        File.WriteAllBytes(Stamp, []);
        File.SetLastWriteTimeUtc(Stamp, Now.UtcDateTime - ago);
    }

    private void Crossed() => File.WriteAllText(Clamp, "{\"tokens\":1200000,\"requests\":42}");

    // ---- The throttle ----

    [Fact]
    public async Task A_fresh_stamp_means_no_request_is_made()
    {
        Waiting(Fixtures.Message(7));
        Touched(TimeSpan.FromSeconds(3));

        Assert.Equal(0, await Step());

        Assert.Empty(_h.Wire.Calls);
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task A_stale_stamp_means_a_request_is_made_and_the_stamp_is_touched()
    {
        Waiting(Fixtures.Message(7));
        Touched(TimeSpan.FromSeconds(InboxCommand.ThrottleSeconds + 1));

        await Step();

        Assert.Equal(1, _h.Wire.Count("POST", Deliver));
        Assert.Equal(Now.UtcDateTime, File.GetLastWriteTimeUtc(Stamp));
    }

    [Fact]
    public async Task A_missing_stamp_means_a_request_is_made_and_the_stamp_is_made()
    {
        Waiting();

        await Step();

        Assert.Equal(1, _h.Wire.Count("POST", Deliver));
        Assert.True(File.Exists(Stamp));
    }

    [Fact]
    public async Task Two_steps_close_together_ask_once()
    {
        Waiting();

        await Step();
        await Step();

        Assert.Equal(1, _h.Wire.Count("POST", Deliver));
    }

    [Fact]
    public async Task Stop_ignores_the_stamp()
    {
        Waiting(Fixtures.Message(7));
        Touched(TimeSpan.Zero);

        await Stop();

        Assert.Equal(1, _h.Wire.Count("POST", Deliver));
    }

    [Fact]
    public async Task Stop_asks_every_time()
    {
        Waiting();

        await Stop();
        await Stop();

        Assert.Equal(2, _h.Wire.Count("POST", Deliver));
    }

    // ---- HA-222: the clamp ----

    [Fact]
    public async Task A_crossed_clamp_is_delivered_at_the_next_step_even_with_a_fresh_stamp()
    {
        Crossed();
        Touched(TimeSpan.FromSeconds(3));
        Waiting();

        Assert.Equal(0, await StepClamped());

        var text = JsonDocument.Parse(Assert.Single(_h.Say.Said)).RootElement
            .GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
        Assert.Contains(Prompt.WrapUp("AER-1"), text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.ChangeExtension(Clamp, ".delivered")));
    }

    [Fact]
    public async Task A_crossed_clamp_is_delivered_on_stop_too()
    {
        Crossed();
        Waiting();

        Assert.Equal(0, await StopClamped());

        var reason = JsonDocument.Parse(Assert.Single(_h.Say.Said)).RootElement.GetProperty("reason").GetString()!;
        Assert.Contains(Prompt.WrapUp("AER-1"), reason, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.ChangeExtension(Clamp, ".delivered")));
    }

    [Fact]
    public async Task A_clamp_already_delivered_prints_nothing_new_for_it()
    {
        Crossed();
        File.WriteAllBytes(Path.ChangeExtension(Clamp, ".delivered"), []);
        Waiting();

        Assert.Equal(0, await StepClamped());
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task A_second_call_after_delivery_carries_only_an_ordinary_message_not_the_clamp_again()
    {
        Crossed();
        Waiting();

        await StepClamped();
        var first = JsonDocument.Parse(Assert.Single(_h.Say.Said)).RootElement
            .GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
        Assert.Contains("crossed its playbook's budget", first, StringComparison.Ordinal);

        _h.Wire.Replace("POST", Deliver, HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { Fixtures.Message(7, body: "second message") }, Fixtures.Json));
        Touched(TimeSpan.FromSeconds(InboxCommand.ThrottleSeconds + 1));

        await StepClamped();

        Assert.Equal(2, _h.Say.Said.Count);
        var second = JsonDocument.Parse(_h.Say.Said[1]).RootElement
            .GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
        Assert.DoesNotContain("crossed its playbook's budget", second, StringComparison.Ordinal);
        Assert.Contains("second message", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_clamp_file_behaves_exactly_as_without_the_flag()
    {
        Waiting(Fixtures.Message(7));
        Touched(TimeSpan.FromSeconds(InboxCommand.ThrottleSeconds + 1));

        Assert.Equal(0, await StepClamped());

        Assert.Single(_h.Say.Said);
        Assert.False(File.Exists(Path.ChangeExtension(Clamp, ".delivered")));
    }

    // ---- What it prints ----

    [Fact]
    public async Task A_message_at_a_step_is_printed_as_context_for_the_model()
    {
        Waiting(Fixtures.Message(7, body: "use the other table"));

        Assert.Equal(0, await Step());

        var output = Assert.Single(_h.Say.Said);
        var root = JsonDocument.Parse(output).RootElement;

        // The exact shape, because a hook that prints anything else is ignored
        // and the message would be marked read without the model ever seeing it.
        Assert.Equal(["hookSpecificOutput"], root.EnumerateObject().Select(p => p.Name));
        var inner = root.GetProperty("hookSpecificOutput");
        Assert.Equal(["hookEventName", "additionalContext"], inner.EnumerateObject().Select(p => p.Name));
        Assert.Equal("PostToolUse", inner.GetProperty("hookEventName").GetString());

        var text = inner.GetProperty("additionalContext").GetString()!;
        Assert.Contains("Nathan sent this to you on AER-1", text, StringComparison.Ordinal);
        Assert.Contains("while you were working", text, StringComparison.Ordinal);
        Assert.Contains("use the other table", text, StringComparison.Ordinal);
        Assert.Contains("say so on the ticket", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_at_the_stop_blocks_it_and_gives_the_reason()
    {
        Waiting(Fixtures.Message(7, body: "use the other table"));

        Assert.Equal(0, await Stop());

        var root = JsonDocument.Parse(Assert.Single(_h.Say.Said)).RootElement;
        Assert.Equal(["decision", "reason"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("block", root.GetProperty("decision").GetString());
        Assert.Contains("use the other table", root.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Several_messages_are_all_said_in_the_order_they_came_back()
    {
        Waiting(Fixtures.Message(7, body: "first"), Fixtures.Message(8, body: "second"));

        await Stop();

        var reason = JsonDocument.Parse(Assert.Single(_h.Say.Said)).RootElement.GetProperty("reason").GetString()!;
        Assert.True(reason.IndexOf("first", StringComparison.Ordinal) < reason.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_coming_back_prints_nothing_for_either_hook()
    {
        Waiting();

        await Step();
        await Stop();

        Assert.Empty(_h.Say.Said);
        Assert.Empty(_h.Say.Complained);
    }

    // ---- It never fails ----

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refusal_means_exit_zero_and_nothing_printed(HttpStatusCode code)
    {
        _h.Wire.Reply("POST", Deliver, code, "no");

        Assert.Equal(0, await Step());
        Assert.Equal(0, await Stop());

        Assert.Empty(_h.Say.Said);
        Assert.Empty(_h.Say.Complained);
    }

    [Fact]
    public async Task A_Hatch_that_does_not_have_the_route_is_skipped()
    {
        // Nothing scripted: the wire answers 404 for a route it has never heard of.
        Assert.Equal(0, await Step());
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_skipped()
    {
        _h.Wire.Reply("POST", Deliver, HttpStatusCode.OK, "<html>a proxy</html>");

        Assert.Equal(0, await Stop());
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task A_Hatch_that_never_answers_is_given_up_on()
    {
        using var slow = new HatchClient(
            new Settings { Base = "https://hatch.example", Key = "k", HeartbeatSeconds = 0 }, "x", new Hangs());
        var command = new InboxCommand(new Board(slow), _h.Say, new StringReader(""), new FrozenClock(Now))
        {
            Limit = TimeSpan.FromMilliseconds(50),
        };

        Assert.Equal(0, await command.RunAsync(["AER-1", "--hook", "stop"], default));
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task A_connection_that_never_happened_is_skipped()
    {
        // RetrySeconds: 0 - this is about what a connection failure that never
        // clears maps to, not about how long HatchClient.Send spends retrying
        // one, which HatchClientTests covers.
        using var down = new HatchClient(
            new Settings { Base = "https://hatch.example", Key = "k", HeartbeatSeconds = 0, RetrySeconds = 0 },
            "x", new Refuses());
        var command = new InboxCommand(new Board(down), _h.Say, new StringReader(""), new FrozenClock(Now));

        Assert.Equal(0, await command.RunAsync(["AER-1", "--hook", "stop"], default));
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task A_stamp_that_cannot_be_kept_skips_the_step_and_fails_nothing()
    {
        Waiting(Fixtures.Message(7));

        var code = await Command().RunAsync(
            ["AER-1", "--hook", "post-tool-use", "--stamp", Path.Combine(_dir, "nowhere", "inbox.stamp")], default);

        Assert.Equal(0, code);
        Assert.Empty(_h.Wire.Calls);
    }

    // ---- The arguments ----

    [Fact]
    public async Task It_reads_and_discards_what_the_hook_was_given()
    {
        var stdin = new StringReader("{\"anything\":true}");
        Waiting();

        await new InboxCommand(_h.Board, _h.Say, stdin, new FrozenClock(Now))
            .RunAsync(["AER-1", "--hook", "stop"], default);

        Assert.Equal(-1, stdin.Peek());
        Assert.Empty(_h.Say.Said);
    }

    [Fact]
    public async Task A_hook_it_does_not_know_is_refused_at_the_terminal()
    {
        Assert.Equal(1, await Command().RunAsync(["AER-1", "--hook", "pre-tool-use"], default));
        Assert.Empty(_h.Wire.Calls);
    }

    [Fact]
    public void It_is_internal_so_no_session_is_told_about_it()
    {
        Assert.Contains("inbox", Program.Internal);
        Assert.Contains("inbox", Program.Commands);
    }

    private sealed class Hangs : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class Refuses : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }
}
