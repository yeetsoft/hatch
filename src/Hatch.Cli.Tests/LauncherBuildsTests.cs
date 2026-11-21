using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// A launcher that is handed a different build: it fetches it, checks it, runs
/// its next child from it, and on any failure carries on with the build it had.
/// </summary>
public sealed class LauncherBuildsTests : IDisposable
{
    private const string Own = "/somewhere/hatch";
    private const string Origin = "https://board.example";
    private const string DownloadPath = "/api/runner/download/linux-x64";

    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);
    private static readonly byte[] Bytes = "a build"u8.ToArray();

    private readonly string temp = Directory.CreateTempSubdirectory("hatch-launcher-builds-").FullName;
    private readonly string root = Directory.CreateTempSubdirectory("hatch-launcher-store-").FullName;
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 3, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        foreach (var directory in new[] { temp, root })
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
    }

    // ---- Fakes ----

    private sealed record Began(string Program, string[] Args, IReadOnlyDictionary<string, string> Env)
    {
        public string State => Env[Launcher.StateVariable];

        public string Dir => Path.GetDirectoryName(State)!;
    }

    private sealed class Running(Task<int> exited) : ILaunchedProcess
    {
        public Task<int> Exited => exited;

        public void Forward(PosixSignal signal) { }

        public void WaitForExit(TimeSpan limit) { }

        public void Dispose() { }
    }

    private sealed class Child : ILaunchedChild
    {
        public List<Began> Starts { get; } = [];

        /// <summary>What each child does, in order; the last one is repeated.</summary>
        public List<Func<Began, Task<int>>> Lives { get; } = [];

        public ILaunchedProcess Start(string program, string[] args, IReadOnlyDictionary<string, string> environment)
        {
            var began = new Began(program, args, new Dictionary<string, string>(environment));
            Starts.Add(began);
            return new Running(Lives[Math.Min(Starts.Count - 1, Lives.Count - 1)](began));
        }
    }

    private sealed class Fetch(string serves) : IBuildFetch
    {
        public int Calls;

        public Func<string, CancellationToken, Task<Download>>? Serve { get; set; }

        public async Task<Download> DownloadAsync(LauncherBoard board, string path, string destination, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return await (Serve ?? Good(serves))(destination, ct);
        }
    }

    private static Func<string, CancellationToken, Task<Download>> Good(string revision) => async (destination, _) =>
    {
        await File.WriteAllBytesAsync(destination, Bytes);
        return new Download(HttpStatusCode.OK, revision, Sha256Of(Bytes), Bytes.Length, null);
    };

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static BuildRequest Request(string revision, string origin = Origin) =>
        new(origin, revision, 4127, DownloadPath, Sha256Of(Bytes), "linux-x64");

    private static Func<Began, Task<int>> Exits(int code) => _ => Task.FromResult(code);

    /// <summary>Asks for a build the way a child does, then asks to be started again.</summary>
    private static Func<Began, Task<int>> Asks(BuildRequest request, string? state = null) => began =>
    {
        if (state is not null) File.WriteAllText(began.State, state);
        BuildHandoff.Write(began.Dir, request);
        return Task.FromResult(75);
    };

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "the launcher never did the thing the child was waiting for");
    }

    private LauncherBuilds Builds(
        Fetch fetch, BuildStore? store = null, Func<string, string, Task<string?>>? verify = null,
        string origin = Origin) =>
        new(() => new LauncherBoard(origin, null!), store ?? new BuildStore(root), fetch,
            verify ?? ((_, _) => Task.FromResult<string?>(null)), clock, TimeSpan.FromMilliseconds(10));

    private Task<int> Run(Child child, LauncherBuilds builds, LaunchSignals? signals = null) =>
        Launcher.RunAsync(
            ["go-to-work", "--max-runs", "3"], child, Own, signals ?? new LaunchSignals(), forwards: true,
            Launcher.Version, temp, _ => { }, builds);

    private string Kept(string sha) => Path.Combine(root, "versions", sha, BuildStore.BinaryName);

    private async Task Keep(string sha, string origin = Origin, bool current = false)
    {
        var store = new BuildStore(root);
        var result = await store.InstallAsync(
            sha, async path => { await File.WriteAllBytesAsync(path, Bytes); return null; },
            _ => Task.FromResult<string?>(null), default);
        Assert.Null(result.Failure);
        if (current) Assert.True(store.Record(origin, sha));
    }

    // ---- Moving to a new build ----

    [Fact]
    public async Task A_request_starts_the_next_child_from_the_stored_build_and_records_it_once_it_is_heard_from()
    {
        var store = new BuildStore(root);
        var fetch = new Fetch(A);
        var child = new Child
        {
            Lives =
            {
                Asks(Request(A), state: "carried"),
                async began =>
                {
                    Assert.Equal("carried", File.ReadAllText(began.State));
                    Assert.Null(store.Read(Origin)?.Current);

                    BuildHandoff.MarkAnswered(began.Dir, A);
                    await WaitFor(() => store.Read(Origin)?.Current == A);
                    return 0;
                },
            },
        };

        Assert.Equal(0, await Run(child, Builds(fetch, store)));

        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(Own, child.Starts[0].Program);
        Assert.Equal(Kept(A), child.Starts[1].Program);
        Assert.Equal(child.Starts[0].Args, child.Starts[1].Args);
        Assert.Equal(child.Starts[0].State, child.Starts[1].State);
        Assert.Equal(1, fetch.Calls);
        Assert.Equal(A, store.Read(Origin)!.Current);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
    }

    [Fact]
    public async Task A_build_the_child_is_heard_from_on_the_far_side_of_its_exit_is_still_recorded()
    {
        var store = new BuildStore(root);
        var child = new Child
        {
            Lives = { Asks(Request(A)), began => { BuildHandoff.MarkAnswered(began.Dir, A); return Task.FromResult(0); } },
        };

        await Run(child, Builds(new Fetch(A), store));

        Assert.Equal(A, store.Read(Origin)!.Current);
    }

    [Fact]
    public async Task A_build_already_kept_is_never_fetched()
    {
        await Keep(A);
        var fetch = new Fetch(A);
        var child = new Child { Lives = { Asks(Request(A)), Exits(0) } };

        await Run(child, Builds(fetch));

        Assert.Equal(0, fetch.Calls);
        Assert.Equal(Kept(A), child.Starts[1].Program);
    }

    [Fact]
    public async Task The_build_is_checked_where_it_was_fetched_and_not_yet_kept()
    {
        var checkedAt = new List<(string Binary, string Revision)>();
        var child = new Child { Lives = { Asks(Request(A)), Exits(0) } };

        await Run(child, Builds(new Fetch(A), verify: (binary, revision) =>
        {
            checkedAt.Add((binary, revision));
            return Task.FromResult<string?>(null);
        }));

        var (binary, revision) = Assert.Single(checkedAt);
        Assert.Equal(A, revision);
        Assert.NotEqual(Kept(A), binary);
        Assert.Contains(".tmp-", binary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_for_another_board_is_dropped_without_a_word()
    {
        var fetch = new Fetch(A);
        var child = new Child { Lives = { Asks(Request(A, "https://elsewhere.example")), Exits(0) } };

        await Run(child, Builds(fetch));

        Assert.Equal(0, fetch.Calls);
        Assert.Equal(Own, child.Starts[1].Program);
        Assert.False(child.Starts[1].Env.ContainsKey(BuildHandoff.NoteVariable));
        Assert.False(child.Starts[1].Env.ContainsKey(BuildHandoff.HoldVariable));
    }

    [Fact]
    public async Task A_board_is_the_same_board_whatever_its_case_and_trailing_slash()
    {
        var fetch = new Fetch(A);
        var child = new Child { Lives = { Asks(Request(A, "HTTPS://Board.Example/")), Exits(0) } };

        await Run(child, Builds(fetch));

        Assert.Equal(1, fetch.Calls);
        Assert.Equal(Kept(A), child.Starts[1].Program);
    }

    [Fact]
    public async Task A_launcher_that_cannot_say_which_board_it_is_for_drops_the_request()
    {
        var fetch = new Fetch(A);
        var builds = Builds(fetch) with { Board = () => null };
        var child = new Child { Lives = { Asks(Request(A)), Exits(0) } };

        await Run(child, builds);

        Assert.Equal(0, fetch.Calls);
        Assert.Equal(Own, child.Starts[1].Program);
    }

    [Fact]
    public async Task A_request_for_the_build_already_running_is_dropped()
    {
        await Keep(A, current: true);
        var fetch = new Fetch(A);
        var child = new Child { Lives = { Asks(Request(A)), Exits(0) } };

        await Run(child, Builds(fetch));

        Assert.Equal(Kept(A), child.Starts[0].Program);
        Assert.Equal(Kept(A), child.Starts[1].Program);
        Assert.Equal(0, fetch.Calls);
    }

    [Fact]
    public async Task With_no_hold_and_no_note_the_child_is_still_told_exactly_three_things()
    {
        var child = new Child { Lives = { Exits(0) } };

        await Run(child, Builds(new Fetch(A)));

        var env = Assert.Single(child.Starts).Env;
        Assert.Equal(3, env.Count);
        Assert.Equal("2", env[Launcher.VersionVariable]);
    }

    // ---- A fresh start ----

    [Fact]
    public async Task A_fresh_start_runs_the_recorded_build()
    {
        await Keep(A, current: true);
        var child = new Child { Lives = { Exits(0) } };

        await Run(child, Builds(new Fetch(A)));

        Assert.Equal(Kept(A), Assert.Single(child.Starts).Program);
    }

    [Fact]
    public async Task A_fresh_start_with_the_builds_deleted_by_hand_runs_its_own_file()
    {
        await Keep(A, current: true);
        Directory.Delete(Path.Combine(root, "versions"), recursive: true);
        var fetch = new Fetch(A);
        var child = new Child { Lives = { Exits(0) } };

        await Run(child, Builds(fetch));

        Assert.Equal(Own, Assert.Single(child.Starts).Program);
        Assert.Equal(0, fetch.Calls);
    }

    [Fact]
    public async Task A_fresh_start_with_nothing_recorded_runs_its_own_file()
    {
        var child = new Child { Lives = { Exits(0) } };

        await Run(child, Builds(new Fetch(A)));

        Assert.Equal(Own, Assert.Single(child.Starts).Program);
    }

    [Fact]
    public async Task A_child_on_the_launchers_own_file_that_ends_any_other_way_ends_the_launcher_as_today()
    {
        var child = new Child { Lives = { Exits(1) } };

        Assert.Equal(1, await Run(child, Builds(new Fetch(A))));
        Assert.Single(child.Starts);
    }

    [Fact]
    public async Task Two_launchers_over_one_folder_each_start_the_build_their_own_board_is_on()
    {
        const string other = "https://other.example";
        await Keep(A, current: true);
        await Keep(B, other, current: true);

        var first = new Child { Lives = { Exits(0) } };
        var second = new Child { Lives = { Exits(0) } };

        var runs = await Task.WhenAll(
            Run(first, Builds(new Fetch(A))), Run(second, Builds(new Fetch(B), origin: other)));

        Assert.All(runs, code => Assert.Equal(0, code));
        Assert.Equal(Kept(A), Assert.Single(first.Starts).Program);
        Assert.Equal(Kept(B), Assert.Single(second.Starts).Program);

        var store = new BuildStore(root);
        Assert.Equal(A, store.Read(Origin)!.Current);
        Assert.Equal(B, store.Read(other)!.Current);
    }

    // ---- Failing to move ----

    [Theory]
    [InlineData("fetch fails", "503")]
    [InlineData("wrong header", "another build")]
    [InlineData("wrong sha", "sha256")]
    [InlineData("verify fails", "did not report")]
    [InlineData("disk full", "disk full")]
    [InlineData("unwritable", "")]
    public async Task Any_failure_starts_the_build_it_had_with_a_hold_and_a_note_and_throws_nothing(string how, string why)
    {
        var fetch = new Fetch(A);
        var store = new BuildStore(root);
        Func<string, string, Task<string?>>? verify = null;

        switch (how)
        {
            case "fetch fails":
                fetch.Serve = (_, _) => Task.FromResult(new Download(HttpStatusCode.ServiceUnavailable, null, null, 0, "hatch: 503 downloading a build"));
                break;
            case "wrong header":
                fetch.Serve = Good(B);
                break;
            case "wrong sha":
                fetch.Serve = async (destination, _) =>
                {
                    await File.WriteAllBytesAsync(destination, Bytes);
                    return new Download(HttpStatusCode.OK, A, Sha256Of([1, 2, 3]), Bytes.Length, null);
                };
                break;
            case "verify fails":
                verify = (_, _) => Task.FromResult<string?>("hatch: the new build did not report revision aaaaaaaa");
                break;
            case "disk full":
                fetch.Serve = (_, _) => throw new IOException("disk full");
                break;
            case "unwritable":
                var file = Path.Combine(temp, "not-a-folder");
                File.WriteAllText(file, "");
                store = new BuildStore(file);
                break;
        }

        var child = new Child { Lives = { Asks(Request(A), state: "carried"), Exits(0) } };

        Assert.Equal(0, await Run(child, Builds(fetch, store, verify)));

        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(Own, child.Starts[1].Program);

        var env = child.Starts[1].Env;
        Assert.True(BuildHold.Parse(env[BuildHandoff.HoldVariable]).Holds(A, clock.GetUtcNow()));

        var note = env[BuildHandoff.NoteVariable];
        Assert.StartsWith("hatch: could not move to build aaaaaaaa - ", note, StringComparison.Ordinal);
        Assert.Contains(why, note, StringComparison.Ordinal);
        Assert.EndsWith("; carrying on with the build it has, asking again in 1m", note, StringComparison.Ordinal);

        Assert.Null(store.Find(A));
    }

    [Fact]
    public async Task What_the_night_had_is_there_for_the_build_it_carries_on_with()
    {
        var fetch = new Fetch(A) { Serve = (_, _) => throw new IOException("disk full") };
        string? seen = null;
        var child = new Child
        {
            Lives = { Asks(Request(A), state: "carried"), began => { seen = File.ReadAllText(began.State); return Task.FromResult(0); } },
        };

        await Run(child, Builds(fetch));

        Assert.Equal("carried", seen);
    }

    [Fact]
    public async Task A_request_for_a_held_build_is_not_fetched_and_says_nothing()
    {
        var fetch = new Fetch(A) { Serve = (_, _) => throw new IOException("disk full") };
        var child = new Child { Lives = { Asks(Request(A)), Asks(Request(A)), Exits(0) } };

        await Run(child, Builds(fetch));

        Assert.Equal(1, fetch.Calls);
        Assert.Equal(3, child.Starts.Count);
        Assert.True(child.Starts[2].Env.ContainsKey(BuildHandoff.HoldVariable));
        Assert.False(child.Starts[2].Env.ContainsKey(BuildHandoff.NoteVariable));
    }

    [Fact]
    public async Task A_second_failure_of_one_build_doubles_the_hold()
    {
        var fetch = new Fetch(A) { Serve = (_, _) => throw new IOException("disk full") };
        var child = new Child
        {
            Lives =
            {
                Asks(Request(A)),
                began =>
                {
                    // Past the first minute, so it is asked about again.
                    clock.Advance(TimeSpan.FromMinutes(2));
                    return Asks(Request(A))(began);
                },
                Exits(0),
            },
        };

        await Run(child, Builds(fetch));

        Assert.Equal(2, fetch.Calls);
        Assert.Contains("asking again in 2m", child.Starts[2].Env[BuildHandoff.NoteVariable], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(12, 60)]
    [InlineData(500, 60)]
    public void The_hold_doubles_from_a_minute_and_stops_at_an_hour(int failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), BuildHold.Backoff(failures));
    }

    // ---- A build that does not work ----

    [Fact]
    public async Task A_new_build_that_ends_before_it_is_heard_from_goes_back_with_the_night_and_is_never_asked_for_again()
    {
        var fetch = new Fetch(A);
        string? seen = null;
        var child = new Child
        {
            Lives =
            {
                Asks(Request(A), state: "carried"),
                began =>
                {
                    // What a child that ends without restarting does.
                    File.Delete(began.State);
                    return Task.FromResult(1);
                },
                began =>
                {
                    seen = File.ReadAllText(began.State);
                    return Asks(Request(A))(began);
                },
                Exits(0),
            },
        };

        Assert.Equal(0, await Run(child, Builds(fetch)));

        Assert.Equal(4, child.Starts.Count);
        Assert.Equal(Kept(A), child.Starts[1].Program);
        Assert.Equal(Own, child.Starts[2].Program);
        Assert.Equal("carried", seen);

        var hold = BuildHold.Parse(child.Starts[2].Env[BuildHandoff.HoldVariable]);
        Assert.True(hold.Holds(A, clock.GetUtcNow().AddYears(10)));
        Assert.Contains("aaaaaaaa", child.Starts[2].Env[BuildHandoff.NoteVariable], StringComparison.Ordinal);

        // Asked for once more, and not fetched.
        Assert.Equal(1, fetch.Calls);
        Assert.Equal(Own, child.Starts[3].Program);
        Assert.Null(new BuildStore(root).Read(Origin)?.Current);
    }

    [Fact]
    public async Task A_new_build_that_ends_the_same_way_after_it_is_heard_from_ends_the_launcher_as_today()
    {
        var child = new Child
        {
            Lives = { Asks(Request(A)), began => { BuildHandoff.MarkAnswered(began.Dir, A); return Task.FromResult(1); } },
        };

        Assert.Equal(1, await Run(child, Builds(new Fetch(A))));

        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(A, new BuildStore(root).Read(Origin)!.Current);
    }

    [Fact]
    public async Task A_new_build_that_restarts_before_it_is_heard_from_is_neither_held_nor_put_back()
    {
        var child = new Child
        {
            Lives = { Asks(Request(A), state: "carried"), Exits(75), Exits(0) },
        };

        Assert.Equal(0, await Run(child, Builds(new Fetch(A))));

        Assert.Equal(3, child.Starts.Count);
        Assert.Equal(Kept(A), child.Starts[2].Program);
        Assert.False(child.Starts[2].Env.ContainsKey(BuildHandoff.HoldVariable));
        Assert.False(child.Starts[2].Env.ContainsKey(BuildHandoff.NoteVariable));
    }

    [Fact]
    public async Task A_new_build_that_cannot_sign_before_it_is_heard_from_is_not_held_and_not_run_again()
    {
        var child = new Child { Lives = { Asks(Request(A), state: "carried"), Exits(78) } };

        Assert.Equal(78, await Run(child, Builds(new Fetch(A))));

        Assert.Equal(2, child.Starts.Count);
        Assert.False(child.Starts[1].Env.ContainsKey(BuildHandoff.HoldVariable));
    }

    [Fact]
    public async Task A_new_build_ended_by_a_signal_is_not_blamed()
    {
        var ends = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var signals = new LaunchSignals();
        var child = new Child { Lives = { Asks(Request(A)), _ => ends.Task } };

        var run = Run(child, Builds(new Fetch(A)), signals);
        await WaitFor(() => child.Starts.Count == 2);
        signals.Raise(PosixSignal.SIGINT);
        ends.SetResult(130);

        Assert.Equal(130, await run);
        Assert.Equal(2, child.Starts.Count);
    }

    [Fact]
    public async Task A_new_build_that_cannot_be_started_goes_back_the_same_way()
    {
        var starter = new Child { Lives = { Asks(Request(A)), Exits(0) } };
        var refusing = new RefusingStarter(starter, Kept(A));

        var code = await Launcher.RunAsync(
            ["go-to-work"], refusing, Own, new LaunchSignals(), true, Launcher.Version, temp, _ => { },
            Builds(new Fetch(A)));

        Assert.Equal(0, code);
        Assert.Equal([Own, Own], starter.Starts.Select(s => s.Program));
        Assert.Contains("could not be started", starter.Starts[1].Env[BuildHandoff.NoteVariable], StringComparison.Ordinal);
    }

    private sealed class RefusingStarter(ILaunchedChild inner, string refuses) : ILaunchedChild
    {
        public ILaunchedProcess Start(string program, string[] args, IReadOnlyDictionary<string, string> environment) =>
            program == refuses ? throw new System.ComponentModel.Win32Exception("not an executable") : inner.Start(program, args, environment);
    }

    // ---- A signal ----

    [Fact]
    public async Task A_signal_during_a_download_ends_the_night_and_starts_no_child()
    {
        var downloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetch = new Fetch(A)
        {
            Serve = async (_, ct) =>
            {
                downloading.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            },
        };
        var signals = new LaunchSignals();
        var child = new Child { Lives = { Asks(Request(A)), Exits(0) } };

        var run = Run(child, Builds(fetch), signals);
        await downloading.Task;
        signals.Raise(PosixSignal.SIGINT);

        Assert.Equal(0, await run);
        Assert.Single(child.Starts);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "versions")));
    }

    [Fact]
    public async Task A_signal_the_download_ignores_still_ends_the_night_and_starts_no_child()
    {
        var signals = new LaunchSignals();
        var fetch = new Fetch(A)
        {
            Serve = async (destination, _) =>
            {
                signals.Raise(PosixSignal.SIGTERM);
                await File.WriteAllBytesAsync(destination, Bytes);
                return new Download(HttpStatusCode.OK, A, Sha256Of(Bytes), Bytes.Length, null);
            },
        };
        var child = new Child { Lives = { Asks(Request(A)), Exits(0) } };

        Assert.Equal(0, await Run(child, Builds(fetch), signals));
        Assert.Single(child.Starts);
    }

    // ---- The handshake ----

    [Fact]
    public void A_request_round_trips_and_is_taken_once()
    {
        var request = Request(A);

        BuildHandoff.Write(temp, request);

        Assert.Equal(request, BuildHandoff.Take(temp));
        Assert.Null(BuildHandoff.Take(temp));
    }

    [Fact]
    public void The_last_request_written_wins()
    {
        BuildHandoff.Write(temp, Request(A));
        BuildHandoff.Write(temp, Request(B));

        Assert.Equal(B, BuildHandoff.Take(temp)!.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Origin\":")]
    [InlineData("{}")]
    [InlineData("null")]
    public void A_garbled_request_is_nothing_and_is_removed(string text)
    {
        File.WriteAllText(Path.Combine(temp, BuildHandoff.RequestFileName), text);

        Assert.Null(BuildHandoff.Take(temp));
        Assert.False(File.Exists(Path.Combine(temp, BuildHandoff.RequestFileName)));
    }

    [Fact]
    public void An_answer_round_trips_and_clearing_removes_it()
    {
        Assert.Null(BuildHandoff.Answered(temp));

        Assert.True(BuildHandoff.MarkAnswered(temp, A));
        Assert.Equal(A, BuildHandoff.Answered(temp));

        BuildHandoff.Write(temp, Request(A));
        BuildHandoff.Clear(temp);

        Assert.Null(BuildHandoff.Answered(temp));
        Assert.Null(BuildHandoff.Take(temp));
    }

    [Fact]
    public void A_hold_round_trips_through_its_variable()
    {
        var now = clock.GetUtcNow();
        var hold = new BuildHold();
        hold.Until(A, now.AddMinutes(5));
        hold.Never(B);

        var parsed = BuildHold.Parse(hold.Format(now));

        Assert.True(parsed.Holds(A, now));
        Assert.False(parsed.Holds(A, now.AddMinutes(6)));
        Assert.True(parsed.Holds(B, now.AddYears(50)));
        Assert.False(parsed.Holds(new string('c', 40), now));
    }

    [Fact]
    public void A_hold_that_has_run_out_is_left_out_of_the_variable()
    {
        var now = clock.GetUtcNow();
        var hold = new BuildHold();
        hold.Until(A, now.AddMinutes(1));

        Assert.NotNull(hold.Format(now));
        Assert.Null(hold.Format(now.AddMinutes(2)));
    }

    [Fact]
    public void A_shorter_hold_never_replaces_a_longer_one_and_never_replaces_never()
    {
        var now = clock.GetUtcNow();
        var hold = new BuildHold();
        hold.Until(A, now.AddHours(1));
        hold.Until(A, now.AddMinutes(1));
        hold.Never(B);
        hold.Until(B, now.AddMinutes(1));

        Assert.True(hold.Holds(A, now.AddMinutes(30)));
        Assert.True(hold.Holds(B, now.AddYears(50)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData(",,,")]
    [InlineData("=5")]
    [InlineData("abc=")]
    [InlineData("abc=-5")]
    [InlineData("abc=soon")]
    [InlineData("abc=99999999999999999999999999")]
    [InlineData("abc=1=2,,=,never")]
    public void Garbage_in_a_hold_is_an_empty_hold(string? text)
    {
        var hold = BuildHold.Parse(text);

        Assert.False(hold.Holds("abc", clock.GetUtcNow()));
        Assert.Null(hold.Format(clock.GetUtcNow()));
    }

    [Fact]
    public void A_hold_keeps_what_it_can_read_beside_what_it_cannot()
    {
        var hold = BuildHold.Parse("junk,abc=never,def=notanumber, ghi = never ");

        Assert.True(hold.Holds("abc", clock.GetUtcNow()));
        Assert.False(hold.Holds("def", clock.GetUtcNow()));
        Assert.True(hold.Holds("ghi", clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(1, "1m")]
    [InlineData(2, "2m")]
    [InlineData(60, "1h")]
    public void A_hold_reads_in_minutes_and_hours(int minutes, string words)
    {
        Assert.Equal(words, BuildHold.Words(TimeSpan.FromMinutes(minutes)));
    }
}
