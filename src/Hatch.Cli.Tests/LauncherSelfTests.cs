using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Cli.Tests;

/// <summary>
/// A launcher that replaces its own file with a newer build it has stored. Every
/// file here lives under a temporary folder; nothing touches the <c>hatch</c> on
/// this machine's own <c>PATH</c>, and the real <c>execve</c> is never called.
/// </summary>
public sealed class LauncherSelfTests : IDisposable
{
    private const string Origin = "https://board.example";
    private const string Other = "https://other.example";
    private const string DownloadPath = "/api/runner/download/linux-x64";

    private static readonly string Old = new('1', 40);
    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);
    private static readonly string C = new('c', 40);

    private readonly string temp = Directory.CreateTempSubdirectory("hatch-self-night-").FullName;
    private readonly string root = Directory.CreateTempSubdirectory("hatch-self-store-").FullName;
    private readonly string bin = Directory.CreateTempSubdirectory("hatch-self-bin-").FullName;
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 3, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, VersionReport> reports = [];
    private readonly List<string> said = [];

    public LauncherSelfTests()
    {
        File.WriteAllText(Own, TextOf(Old));
        reports[TextOf(Old)] = new VersionReport(Old, 100, 2);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { temp, root, bin })
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string Own => Path.Combine(bin, BuildStore.BinaryName);

    private static string TextOf(string sha) => $"build {sha}";

    // ---- Fakes ----

    private sealed class Exec(bool replaces) : IExec
    {
        public List<(string Path, string[] Args, IReadOnlyDictionary<string, string> Added)> Calls { get; } = [];

        public Action<string>? OnCall { get; set; }

        public bool Replace(string path, string[] args, IReadOnlyDictionary<string, string> added, out string? failure)
        {
            failure = replaces ? null : "fake exec failed";
            Calls.Add((path, args, new Dictionary<string, string>(added)));
            OnCall?.Invoke(path);
            return replaces;
        }
    }

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

        public List<Func<Began, Task<int>>> Lives { get; } = [];

        public ILaunchedProcess Start(string program, string[] args, IReadOnlyDictionary<string, string> environment)
        {
            var began = new Began(program, args, new Dictionary<string, string>(environment));
            Starts.Add(began);
            return new Running(Lives[Math.Min(Starts.Count - 1, Lives.Count - 1)](began));
        }
    }

    private sealed class Fetch : IBuildFetch
    {
        public HashSet<string> Broken { get; } = [];

        public async Task<Download> DownloadAsync(LauncherBoard board, string path, string destination, CancellationToken ct)
        {
            var revision = Revision;
            if (Broken.Contains(revision)) return new Download(HttpStatusCode.InternalServerError, null, null, 0, "hatch: the board refused");

            var bytes = Encoding.UTF8.GetBytes(TextOf(revision));
            await File.WriteAllBytesAsync(destination, bytes, ct);
            return new Download(HttpStatusCode.OK, revision, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length, null);
        }

        /// <summary>The build the next download is of.</summary>
        public string Revision { get; set; } = A;
    }

    private static Func<Began, Task<int>> Exits(int code) => _ => Task.FromResult(code);

    private static BuildRequest Request(string revision) =>
        new(Origin, revision, 4127, DownloadPath,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(TextOf(revision)))), "linux-x64");

    private static Func<Began, Task<int>> Asks(string revision, string? state = null) => began =>
    {
        if (state is not null) File.WriteAllText(began.State, state);
        BuildHandoff.Write(began.Dir, Request(revision));
        return Task.FromResult(75);
    };

    // ---- Set-up ----

    private SelfUpdate Self(
        Exec? exec = null, bool renameAside = false, bool handoff = false, string? resume = null,
        int sequence = 100, string? ownPath = null, Func<string, Task<VersionReport?>>? read = null) =>
        new(ownPath ?? Own, Old, sequence, new BuildStore(root),
            read ?? (path => Task.FromResult(Report(path))), exec ?? new Exec(true), renameAside, Say,
            handoff, resume);

    private void Say(string line)
    {
        lock (said) said.Add(line);
    }

    private VersionReport? Report(string path) =>
        reports.GetValueOrDefault(File.ReadAllText(path).Trim());

    /// <summary>Keeps a build as the checks would have, says what it reports, and names it current for a board.</summary>
    private async Task Keep(string sha, int sequence, string? origin = Origin, int launcher = 3)
    {
        reports[TextOf(sha)] = new VersionReport(sha, sequence, launcher);

        var store = new BuildStore(root);
        var result = await store.InstallAsync(
            sha, async path => { await File.WriteAllTextAsync(path, TextOf(sha)); return null; },
            _ => Task.FromResult<string?>(null), default);
        Assert.Null(result.Failure);
        if (origin is not null) Assert.True(store.Record(origin, sha));
    }

    private string Kept(string sha) => Path.Combine(root, "versions", sha, BuildStore.BinaryName);

    private LauncherBuilds Builds(Fetch fetch) =>
        new(() => new LauncherBoard(Origin, null!), new BuildStore(root), fetch,
            (_, _) => Task.FromResult<string?>(null), clock, TimeSpan.FromMilliseconds(10));

    private Task<int> Run(Child child, SelfUpdate? self, Fetch? fetch = null, int version = 3, string[]? args = null) =>
        Launcher.RunAsync(
            args ?? ["go-to-work", "--max-runs", "3"], child, Own, new LaunchSignals(), forwards: true, version,
            temp, said.Add, Builds(fetch ?? new Fetch()), self);

    private string OwnText() => File.ReadAllText(Own);

    private string[] BinEntries() => Directory.GetFileSystemEntries(bin).Select(Path.GetFileName).Order().ToArray()!;

    // ---- Which build is newer ----

    [Fact]
    public void A_higher_sequence_is_newer_and_an_equal_or_lower_one_is_not()
    {
        VersionReport R(string sha, int sequence) => new(sha, sequence, 3);

        Assert.Equal(A, SelfUpdate.Choose(100, Old, [new("a", A, R(A, 101))])!.Value.Sha);
        Assert.Null(SelfUpdate.Choose(100, Old, [new("a", A, R(A, 100))]));
        Assert.Null(SelfUpdate.Choose(100, Old, [new("a", A, R(A, 99))]));
        Assert.Null(SelfUpdate.Choose(100, Old, []));
    }

    [Fact]
    public void The_newest_of_several_wins_and_a_tie_goes_to_the_lower_sha()
    {
        VersionReport R(string sha, int sequence) => new(sha, sequence, 3);

        Assert.Equal(B, SelfUpdate.Choose(100, Old, [new("a", A, R(A, 101)), new("b", B, R(B, 150))])!.Value.Sha);
        Assert.Equal(A, SelfUpdate.Choose(100, Old, [new("b", B, R(B, 150)), new("a", A, R(A, 150))])!.Value.Sha);
    }

    [Fact]
    public void A_build_that_cannot_say_what_it_is_is_never_chosen()
    {
        Assert.Null(SelfUpdate.Choose(100, Old, [new("a", A, null)]));
        Assert.Null(SelfUpdate.Choose(100, Old, [new("a", A, new VersionReport(B, 500, 3))]));
        Assert.Null(SelfUpdate.Choose(100, Old, [new("old", Old, new VersionReport(Old, 500, 3))]));
    }

    // ---- At a start ----

    [Fact]
    public async Task A_newer_stored_build_becomes_the_file_at_a_start_and_the_person_sees_one_line()
    {
        await Keep(A, 150);
        var exec = new Exec(true);
        var child = new Child { Lives = { Exits(0) } };

        var code = await Run(child, Self(exec));

        Assert.Equal(0, code);
        Assert.Equal(TextOf(A), OwnText());
        Assert.Equal(File.ReadAllBytes(Kept(A)), File.ReadAllBytes(Own));
        Assert.Equal([$"hatch: updated itself to build {A[..8]}"], said);

        // In its own place, with the same arguments and nothing else changed.
        var (path, args, added) = Assert.Single(exec.Calls);
        Assert.Equal(Own, path);
        Assert.Equal(["go-to-work", "--max-runs", "3"], args);
        Assert.Equal("1", added[SelfUpdate.HandoffVariable]);
        Assert.DoesNotContain(SelfUpdate.ResumeVariable, added.Keys);

        // Before anything else: no child, no night's directory.
        Assert.Empty(child.Starts);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
        Assert.Equal([BuildStore.BinaryName], BinEntries());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(99)]
    public async Task An_equal_or_lower_stored_build_changes_nothing(int sequence)
    {
        await Keep(A, sequence);
        var exec = new Exec(true);
        var child = new Child { Lives = { Exits(0) } };

        Assert.Equal(0, await Run(child, Self(exec)));

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Empty(exec.Calls);
        Assert.Empty(said);
        Assert.Single(child.Starts);
    }

    [Fact]
    public async Task A_board_that_rolls_back_moves_the_child_and_leaves_the_launcher()
    {
        // The launcher is at 100; the board's recorded build is older.
        await Keep(A, 60);
        var child = new Child { Lives = { Exits(0) } };

        await Run(child, Self());

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Equal(Kept(A), Assert.Single(child.Starts).Program);
    }

    [Fact]
    public async Task With_two_boards_on_different_builds_the_launcher_becomes_the_newer()
    {
        await Keep(A, 120, Origin);
        await Keep(B, 180, Other);

        await Run(new Child { Lives = { Exits(0) } }, Self(new Exec(true)));

        Assert.Equal(TextOf(B), OwnText());
    }

    [Fact]
    public async Task A_launcher_started_by_another_updating_does_not_look_again_or_start_a_chain()
    {
        await Keep(A, 150);
        var exec = new Exec(true);
        var child = new Child { Lives = { Exits(0) } };

        await Run(child, Self(exec, handoff: true));

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Empty(exec.Calls);
        Assert.Empty(said);
        Assert.Single(child.Starts);
    }

    [Fact]
    public async Task On_Windows_a_start_renames_the_running_file_aside_and_starts_the_new_one_once()
    {
        await Keep(A, 150);
        var exec = new Exec(true);
        var child = new Child { Lives = { Exits(7) } };

        var code = await Run(child, Self(exec, renameAside: true));

        // The new file, started once, with its own exit code passed on.
        Assert.Equal(7, code);
        var started = Assert.Single(child.Starts);
        Assert.Equal(Own, started.Program);
        Assert.Equal(["go-to-work", "--max-runs", "3"], started.Args);
        Assert.Equal("1", started.Env[SelfUpdate.HandoffVariable]);
        Assert.DoesNotContain(Launcher.StateVariable, started.Env.Keys);
        Assert.Empty(exec.Calls);

        Assert.Equal(TextOf(A), OwnText());
        var aside = Assert.Single(BinEntries(), name => name.Contains(".old-", StringComparison.Ordinal));
        Assert.Equal(TextOf(Old), File.ReadAllText(Path.Combine(bin, aside)));
        Assert.Equal([$"hatch: updated itself to build {A[..8]}"], said);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
    }

    [Fact]
    public async Task The_file_left_aside_is_removed_at_a_later_start_and_one_in_use_is_skipped_silently()
    {
        await Keep(A, 150);
        await Run(new Child { Lives = { Exits(0) } }, Self(renameAside: true));
        var aside = Directory.GetFiles(bin, "*.old-*").Single();

        // A later start: nothing is running the old file any more.
        said.Clear();
        await Run(new Child { Lives = { Exits(0) } }, Self(handoff: true));

        Assert.False(File.Exists(aside));
        Assert.Empty(said);

        if (!OperatingSystem.IsWindows()) return;

        // Where a running file cannot be deleted, it is skipped, not an error.
        var busy = Own + ".old-1-busy";
        File.WriteAllText(busy, "busy");
        using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
            Self().Sweep();

        Assert.True(File.Exists(busy));
        Assert.Empty(said);
    }

    [Fact]
    public void A_half_written_copy_is_swept_once_it_is_old_and_never_before()
    {
        var fresh = Path.Combine(bin, ".hatch-new-1-fresh");
        var stale = Path.Combine(bin, ".hatch-new-2-stale");
        File.WriteAllText(fresh, "x");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-3));

        Self().Sweep();

        Assert.True(File.Exists(fresh));
        Assert.False(File.Exists(stale));
    }

    // ---- Between children ----

    /// <summary>What the second child found in the night's state file, which is gone once the night ends.</summary>
    private string? seenState;

    private async Task<(Child Child, Exec Exec, Fetch Fetch)> ChangeOfBuild(bool replaces, bool renameAside, int sequence = 150)
    {
        reports[TextOf(A)] = new VersionReport(A, sequence, 3);

        var fetch = new Fetch { Revision = A };
        var exec = new Exec(replaces);
        var child = new Child
        {
            Lives =
            {
                Asks(A, state: "carried"),
                began => { seenState = File.ReadAllText(began.State); return Task.FromResult(0); },
            },
        };

        var code = await Run(child, Self(exec, renameAside), fetch);
        Assert.Equal(0, code);
        return (child, exec, fetch);
    }

    [Fact]
    public async Task On_Linux_and_macOS_a_change_of_build_replaces_the_process_with_the_night_left_for_it()
    {
        var (child, exec, _) = await ChangeOfBuild(replaces: true, renameAside: false);

        Assert.Equal(TextOf(A), OwnText());
        Assert.Equal([BuildStore.BinaryName], BinEntries());
        Assert.Single(child.Starts);

        var (path, args, added) = Assert.Single(exec.Calls);
        Assert.Equal(Own, path);
        Assert.Equal(["go-to-work", "--max-runs", "3"], args);
        Assert.Equal("1", added[SelfUpdate.HandoffVariable]);
        Assert.DoesNotContain(Launcher.StateVariable, added.Keys);

        // The night's directory is the next one's: still there, with its state.
        var directory = added[SelfUpdate.ResumeVariable];
        Assert.Equal(child.Starts[0].Dir, directory);
        Assert.Equal("carried", File.ReadAllText(child.Starts[0].State));

        var resume = LauncherResume.Read(directory)!;
        Assert.Equal(Kept(A), resume.Program);
        Assert.Equal(A, resume.Trial);
        Assert.Equal(Origin, resume.TrialOrigin);
        Assert.Equal([$"hatch: updated itself to build {A[..8]}"], said);
    }

    [Fact]
    public async Task The_next_child_starts_as_it_would_have_on_the_far_side()
    {
        // The first process: a failed ask first, so there is a hold to carry, then a good one.
        var fetch = new Fetch { Revision = C };
        fetch.Broken.Add(C);
        reports[TextOf(A)] = new VersionReport(A, 150, 3);
        var exec = new Exec(true);
        var first = new Child
        {
            Lives =
            {
                Asks(C, state: "night one"),
                began =>
                {
                    fetch.Revision = A;
                    return Asks(A)(began);
                },
            },
        };

        Assert.Equal(0, await Run(first, Self(exec), fetch));
        Assert.Equal(2, first.Starts.Count);
        Assert.Equal(Own, first.Starts[1].Program);
        var directory = Assert.Single(exec.Calls).Added[SelfUpdate.ResumeVariable];
        var held = first.Starts[1].Env[BuildHandoff.HoldVariable];
        Assert.Contains(C, held, StringComparison.Ordinal);

        // The new process: told where the night is, and that it is the one in its place.
        string? found = null;
        var second = new Child { Lives = { began => { found = File.ReadAllText(began.State); return Task.FromResult(0); } } };
        said.Clear();
        var code = await Run(second, Self(new Exec(true), handoff: true, resume: directory, sequence: 150));

        Assert.Equal(0, code);
        var began = Assert.Single(second.Starts);
        Assert.Equal(first.Starts[0].State, began.State);
        Assert.Equal("night one", found);
        Assert.Equal(held, began.Env[BuildHandoff.HoldVariable]);
        Assert.DoesNotContain(SelfUpdate.HandoffVariable, began.Env.Keys);
        Assert.DoesNotContain(SelfUpdate.ResumeVariable, began.Env.Keys);
        Assert.Empty(said);

        // Same build on trial, and it is the one that is run.
        Assert.Equal(Kept(A), began.Program);
        Assert.False(File.Exists(Path.Combine(directory, LauncherResume.FileName)));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task A_trial_that_fails_after_the_swap_still_has_the_state_to_fall_back_to()
    {
        var exec = new Exec(true);
        reports[TextOf(A)] = new VersionReport(A, 150, 3);
        var first = new Child { Lives = { Asks(A, state: "before") } };
        await Run(first, Self(exec), new Fetch { Revision = A });
        var directory = Assert.Single(exec.Calls).Added[SelfUpdate.ResumeVariable];

        // The trial ends badly before it is heard from, having spoiled the state.
        var second = new Child
        {
            Lives =
            {
                began => { File.WriteAllText(began.State, "spoiled"); return Task.FromResult(1); },
                began => { Assert.Equal("before", File.ReadAllText(began.State)); return Task.FromResult(0); },
            },
        };

        Assert.Equal(0, await Run(second, Self(new Exec(true), handoff: true, resume: directory, sequence: 150)));

        Assert.Equal(2, second.Starts.Count);
        Assert.Equal(Kept(A), second.Starts[0].Program);
        Assert.Contains(A, second.Starts[1].Env[BuildHandoff.HoldVariable], StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_Windows_a_change_of_build_renames_aside_and_carries_on_as_it_was()
    {
        var (child, exec, _) = await ChangeOfBuild(replaces: true, renameAside: true);

        Assert.Empty(exec.Calls);
        Assert.Equal(TextOf(A), OwnText());
        Assert.Single(BinEntries(), name => name.Contains(".old-", StringComparison.Ordinal));

        // The same process went on to the next child, on the same state.
        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(Kept(A), child.Starts[1].Program);
        Assert.Equal(child.Starts[0].State, child.Starts[1].State);
        Assert.Equal("carried", seenState);
        Assert.Equal([$"hatch: updated itself to build {A[..8]}"], said);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
    }

    [Fact]
    public async Task A_launcher_already_as_new_as_the_build_it_stored_leaves_its_file_alone()
    {
        var (child, exec, _) = await ChangeOfBuild(replaces: true, renameAside: false, sequence: 100);

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Empty(exec.Calls);
        Assert.Empty(said);
        Assert.Equal(2, child.Starts.Count);
    }

    [Fact]
    public async Task An_exec_that_fails_carries_on_in_the_same_process_with_one_line()
    {
        var (child, exec, _) = await ChangeOfBuild(replaces: false, renameAside: false);

        Assert.Single(exec.Calls);
        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(Kept(A), child.Starts[1].Program);
        Assert.Equal("carried", seenState);
        Assert.Equal(2, said.Count);
        Assert.StartsWith("hatch: updated itself", said[0], StringComparison.Ordinal);
        Assert.StartsWith("hatch: could not update itself", said[1], StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
    }

    [Fact]
    public async Task A_missing_or_garbled_record_is_a_fresh_start_in_a_new_directory()
    {
        var elsewhere = Path.Combine(temp, "hatch-night-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, LauncherResume.FileName), "{ not json");

        foreach (var named in new[] { Path.Combine(temp, "hatch-night-gone"), elsewhere })
        {
            var child = new Child { Lives = { Exits(0) } };

            await Run(child, Self(handoff: true, resume: named));

            var began = Assert.Single(child.Starts);
            Assert.NotEqual(named, began.Dir);
            Assert.Equal(Own, began.Program);
        }
    }

    [Fact]
    public void A_record_only_reads_from_a_directory_a_launcher_made()
    {
        var other = Path.Combine(temp, "somewhere-else");
        Directory.CreateDirectory(other);
        Assert.True(LauncherResume.Write(other, new LauncherResume(Own, null, Own, null, null, null, null, null, false), null, out _));

        Assert.Null(LauncherResume.Read(other));
    }

    [Fact]
    public void A_record_round_trips_with_the_state_set_aside()
    {
        var directory = Path.Combine(temp, "hatch-night-roundtrip");
        Directory.CreateDirectory(directory);
        var resume = new LauncherResume(
            Kept(A), A, Own, null, A, Origin, $"{C}=never", $"{C}=2", false);

        Assert.True(LauncherResume.Write(directory, resume, new Dictionary<string, byte[]> { ["night.json"] = [1, 2, 3] }, out _));

        var read = LauncherResume.Read(directory)!;
        Assert.Equal(resume with { HasAside = true }, read);
        Assert.Equal(2, read.ParseFailed()[C]);
        Assert.Equal([1, 2, 3], read.ReadAside(directory)!["night.json"]);

        LauncherResume.Remove(directory);
        Assert.Null(LauncherResume.Read(directory));
        Assert.False(Directory.Exists(Path.Combine(directory, "aside")));
    }

    // ---- What it will and will not do ----

    [Fact]
    public async Task A_copy_that_does_not_report_the_revision_asked_for_replaces_nothing()
    {
        await Keep(A, 150);

        // The stored build answers; the copy beside the launcher does not.
        var self = Self(read: path => Task.FromResult(
            Path.GetDirectoryName(path) == bin ? new VersionReport(B, 150, 3) : Report(path)));

        var child = new Child { Lives = { Exits(0) } };
        Assert.Equal(0, await Run(child, self));

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Equal([BuildStore.BinaryName], BinEntries());
        Assert.Single(child.Starts);
        Assert.Single(said);
        Assert.StartsWith("hatch: could not update itself", said[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_copy_that_will_not_run_replaces_nothing_and_leaves_no_litter()
    {
        await Keep(A, 150);
        var self = Self(read: path => Path.GetDirectoryName(path) == bin
            ? throw new System.ComponentModel.Win32Exception("not a program")
            : Task.FromResult(Report(path)));

        var child = new Child { Lives = { Exits(0) } };
        Assert.Equal(0, await Run(child, self));

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Equal([BuildStore.BinaryName], BinEntries());
        Assert.Single(child.Starts);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_never_ends_the_night_and_says_why_once()
    {
        await Keep(A, 150);
        var gone = Path.Combine(bin, "no-such-folder", BuildStore.BinaryName);
        var self = Self(ownPath: gone);

        var child = new Child { Lives = { Asks(A), Exits(0) } };
        var code = await Launcher.RunAsync(
            ["go-to-work"], child, Own, new LaunchSignals(), forwards: true, 3, temp, said.Add,
            Builds(new Fetch { Revision = A }), self);

        Assert.Equal(0, code);

        // At the start and again at the change of build, and still one line.
        Assert.Equal(2, child.Starts.Count);
        Assert.Single(said);
        Assert.StartsWith("hatch: could not update itself", said[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_swap_that_fails_after_the_old_file_moved_puts_it_back()
    {
        await Keep(A, 150);
        var self = Self(renameAside: true);
        var candidate = (await self.FindNewerAsync())!.Value;

        // Something takes the path in the instant between the two moves.
        var run = Self(renameAside: true, read: path =>
        {
            var report = Report(path);

            // The copy checked out: now make the second move impossible.
            if (Path.GetDirectoryName(path) == bin) File.Delete(path);

            return Task.FromResult(report);
        });

        Assert.False(await run.SwapAsync(candidate));

        Assert.Equal(TextOf(Old), OwnText());
        Assert.Equal([BuildStore.BinaryName], BinEntries());
        Assert.Single(said);
    }

    [Fact]
    public async Task A_second_failure_in_one_process_says_nothing_more()
    {
        await Keep(A, 150);
        var self = Self(ownPath: Path.Combine(bin, "no-such-folder", BuildStore.BinaryName));
        var candidate = (await self.FindNewerAsync())!.Value;

        Assert.False(await self.SwapAsync(candidate));
        Assert.False(await self.SwapAsync(candidate));

        Assert.Single(said);
    }

    [Fact]
    public async Task A_link_on_the_path_is_followed_and_left_as_it_was()
    {
        await Keep(A, 150);
        var real = Path.Combine(Directory.CreateTempSubdirectory("hatch-self-real-").FullName, BuildStore.BinaryName);
        File.WriteAllText(real, TextOf(Old));
        var link = Path.Combine(bin, "hatch-link");

        try { File.CreateSymbolicLink(link, real); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return; // This account may not make links.
        }

        try
        {
            var own = SelfUpdate.Resolve(link);
            Assert.Equal(Path.GetFullPath(real), Path.GetFullPath(own));

            var self = Self(ownPath: own);
            Assert.True(await self.SwapAsync((await self.FindNewerAsync())!.Value));

            Assert.Equal(TextOf(A), File.ReadAllText(real));
            Assert.NotNull(File.ResolveLinkTarget(link, returnFinalTarget: false));
            Assert.Equal(TextOf(A), File.ReadAllText(link));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(real)!, recursive: true); }
            catch (IOException) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_launchers_replacing_the_same_file_at_once_leave_one_good_file_and_no_litter(bool renameAside)
    {
        await Keep(A, 150, Origin);
        await Keep(B, 160, Other);

        for (var round = 0; round < 50; round++)
        {
            File.WriteAllText(Own, TextOf(Old));
            foreach (var left in Directory.GetFiles(bin, "*.old-*")) File.Delete(left);

            var one = Self(renameAside: renameAside);
            var two = Self(renameAside: renameAside);
            var a = new SelfUpdate.Candidate(Kept(A), A, new VersionReport(A, 150, 3));
            var b = new SelfUpdate.Candidate(Kept(B), B, new VersionReport(B, 160, 3));

            var gate = new ManualResetEventSlim();
            var first = Task.Run(() => { gate.Wait(); return one.SwapAsync(a); });
            var second = Task.Run(() => { gate.Wait(); return two.SwapAsync(b); });
            gate.Set();
            await Task.WhenAll(first, second);

            Assert.True(
                OwnText() == TextOf(A) || OwnText() == TextOf(B),
                $"round {round}: {OwnText()} / {string.Join(" | ", said)} / {string.Join(",", BinEntries())}");
            Assert.Empty(Directory.GetFiles(bin, ".hatch-new-*"));
            Assert.True(await first || await second);
        }
    }

    // ---- What never replaces itself ----

    private sealed class Stamp(string revision, int sequence) : ICliRevision
    {
        public string Revision => revision;
        public int Sequence => sequence;
        public DateTimeOffset? BuiltAt => null;
        public bool IsDevelopment => revision == CliRevision.Development;
    }

    [Fact]
    public void A_build_nobody_stamped_never_replaces_itself()
    {
        var store = new BuildStore(root);

        Assert.Null(SelfUpdate.Real(new Stamp(CliRevision.Development, 0), Own, store, said.Add, take: false));
        Assert.Null(SelfUpdate.Real(new Stamp(CliRevision.Development, 4000), Own, store, said.Add, take: false));
        Assert.Null(SelfUpdate.Real(new Stamp(Old, 0), Own, store, said.Add, take: false));
        Assert.Null(SelfUpdate.Real(new Stamp(Old, 100), null, store, said.Add, take: false));
        Assert.NotNull(SelfUpdate.Real(new Stamp(Old, 100), Own, store, said.Add, take: false));
    }

    [Fact]
    public void The_image_the_scripts_and_a_named_binary_are_never_launchers_so_never_replace_themselves()
    {
        // `scripts/hatch.sh`, `scripts/hatch.ps1`, a file named by HATCH_RUNNER_BIN and
        // every member of a set run under a state file; the image's build is unstamped.
        var under = new Dictionary<string, string?> { [Launcher.StateVariable] = "/tmp/night.json" };
        Assert.False(Launcher.Applies("go-to-work", [], under, builtAsLauncher: true));
        Assert.False(Launcher.Applies("do-my-work", [], under, builtAsLauncher: true));
        Assert.False(Launcher.Applies("go-to-work", [], new Dictionary<string, string?>(), builtAsLauncher: false));
    }

    [Fact]
    public void The_two_variables_are_the_launchers_and_are_never_given_to_a_child()
    {
        Assert.Equal("HATCH_LAUNCHER_HANDOFF", SelfUpdate.HandoffVariable);
        Assert.Equal("HATCH_LAUNCHER_RESUME", SelfUpdate.ResumeVariable);
    }

    // ---- Older children, newer launcher ----

    [Fact]
    public async Task A_child_older_than_the_launcher_runs_and_the_night_carries_on()
    {
        // A kept build that reports launcher version 2 and an older sequence.
        await Keep(A, 60, launcher: 2);
        var exec = new Exec(true);
        var child = new Child { Lives = { Exits(75), Exits(0) } };

        var code = await Run(child, Self(exec), version: Launcher.Version);

        Assert.Equal(0, code);
        Assert.Equal(2, child.Starts.Count);
        Assert.Equal(Kept(A), child.Starts[0].Program);
        Assert.Equal(Launcher.Version.ToString(), child.Starts[0].Env[Launcher.VersionVariable]);
        Assert.Empty(exec.Calls);
        Assert.Equal(TextOf(Old), OwnText());
    }

    [Fact]
    public async Task A_launcher_with_nothing_to_replace_itself_with_behaves_exactly_as_before()
    {
        var child = new Child { Lives = { Exits(0) } };

        Assert.Equal(0, await Run(child, null));

        Assert.Empty(said);
        Assert.Equal(TextOf(Old), OwnText());
    }
}
