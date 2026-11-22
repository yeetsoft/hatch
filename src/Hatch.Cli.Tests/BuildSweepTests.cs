using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>BuildStore.Sweep</c> - keeps the builds a machine can still need, removes
/// the rest, and never touches anything outside <c>versions/</c>.
/// </summary>
public sealed class BuildSweepTests : IDisposable
{
    private const string One = "https://one.example";
    private const string Two = "https://two.example";

    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);
    private static readonly string C = new('c', 40);
    private static readonly string D = new('d', 40);
    private static readonly string E = new('e', 40);

    private readonly string root = Directory.CreateTempSubdirectory("hatch-sweep-").FullName;
    private readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    private BuildStore Store() => new(root);

    private string Versions => Path.Combine(root, "versions");

    private string Folder(string sha) => Path.Combine(Versions, sha);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Make(string sha)
    {
        Directory.CreateDirectory(Folder(sha));
        var binary = Path.Combine(Folder(sha), BuildStore.BinaryName);
        File.WriteAllText(binary, "a build");
        return binary;
    }

    private string RecordOf(string origin, params string[] shas)
    {
        var store = Store();
        foreach (var sha in shas) Assert.True(store.Record(origin, sha));
        return Directory.GetFiles(Path.Combine(root, "boards"))
            .Single(f => store.Read(origin) is not null && File.ReadAllText(f).Contains(origin));
    }

    private static string Lease(string folder, string sha) => Path.Combine(folder, $"{sha}.in-use-{Guid.NewGuid():N}");

    private bool Has(string sha) => Directory.Exists(Folder(sha));

    // ---- What is kept ----

    [Fact]
    public void Keeps_current_and_previous_of_two_boards_and_removes_the_rest()
    {
        foreach (var sha in new[] { A, B, C, D, E }) Make(sha);
        RecordOf(One, A, B);
        RecordOf(Two, C);

        var result = Store().Sweep(now, []);

        Assert.True(Has(A) && Has(B) && Has(C));
        Assert.False(Has(D) || Has(E));
        Assert.Equal(new SweepResult(2, 0), result);
        Assert.Empty(Directory.GetDirectories(Versions, ".old-*"));
    }

    [Fact]
    public void A_removed_build_is_no_longer_found()
    {
        Make(A);
        Make(B);
        RecordOf(One, A);

        Store().Sweep(now, []);

        Assert.NotNull(Store().Find(A));
        Assert.Null(Store().Find(B));
    }

    [Fact]
    public void A_board_record_that_is_thirty_one_days_old_stops_counting()
    {
        Make(A);
        Make(B);
        var record = RecordOf(One, A);
        File.SetLastWriteTimeUtc(record, now.UtcDateTime - TimeSpan.FromDays(31));
        RecordOf(Two, B);

        Store().Sweep(now, []);

        Assert.False(Has(A));
        Assert.True(Has(B));
    }

    [Fact]
    public void A_touched_board_record_counts_again()
    {
        Make(A);
        var record = RecordOf(One, A);
        File.SetLastWriteTimeUtc(record, now.UtcDateTime - TimeSpan.FromDays(31));

        Store().Touch(One, now);
        Store().Sweep(now, []);

        Assert.True(Has(A));
    }

    [Fact]
    public void Touch_makes_no_record_for_a_board_that_has_none()
    {
        Store().Touch(One, now);

        Assert.False(Directory.Exists(Path.Combine(root, "boards")));
    }

    [Fact]
    public void A_record_that_cannot_be_read_removes_no_build()
    {
        Make(A);
        Make(B);
        var record = RecordOf(One, A);
        File.WriteAllText(record, "not json at all");

        var result = Store().Sweep(now, []);

        Assert.True(Has(A) && Has(B));
        Assert.Equal(2, result.Failed);
    }

    [Fact]
    public void A_build_a_running_path_is_in_is_kept()
    {
        var binary = Make(A);
        Make(B);
        RecordOf(One, C);

        Store().Sweep(now, [binary]);

        Assert.True(Has(A));
        Assert.False(Has(B));
    }

    [Fact]
    public void Where_what_is_running_cannot_be_found_out_no_build_is_removed()
    {
        Make(A);
        Make(B);
        RecordOf(One, A);

        var result = Store().Sweep(now, null);

        Assert.True(Has(A) && Has(B));
        Assert.Equal(new SweepResult(0, 1), result);
    }

    // ---- Leases ----

    [Fact]
    public void A_held_lease_keeps_its_build()
    {
        Make(A);
        Make(B);
        RecordOf(One, C);

        using (Store().Hold(A))
        {
            Store().Sweep(now, []);

            Assert.True(Has(A));
            Assert.False(Has(B));
            Assert.Single(Directory.GetFiles(Versions));
        }

        Assert.Empty(Directory.GetFiles(Versions));
    }

    [Fact]
    public void A_lease_taken_after_the_first_look_still_keeps_its_build()
    {
        Make(A);
        IDisposable? lease = null;

        try
        {
            var result = Store().Sweep(now, [], beforeRemoval: sha => lease = Store().Hold(sha));

            Assert.True(Has(A));
            Assert.Equal(new SweepResult(0, 0), result);
        }
        finally
        {
            lease?.Dispose();
        }
    }

    [Fact]
    public void A_record_written_after_the_first_look_still_keeps_its_build()
    {
        Make(A);

        var result = Store().Sweep(now, [], beforeRemoval: sha => RecordOf(One, sha));

        Assert.True(Has(A));
        Assert.Equal(new SweepResult(0, 0), result);
    }

    [Fact]
    public void A_lease_with_no_holder_is_deleted_and_protects_nothing()
    {
        Make(A);
        var lease = Lease(Versions, A);
        File.WriteAllText(lease, "");
        RecordOf(One, C);

        Store().Sweep(now, []);

        Assert.False(File.Exists(lease));
        Assert.False(Has(A));
    }

    [Fact]
    public void A_lease_for_a_build_that_is_not_there_is_harmless()
    {
        using var lease = Store().Hold(A);
        RecordOf(One, B);

        var result = Store().Sweep(now, []);

        Assert.Equal(new SweepResult(0, 0), result);
        Assert.Single(Directory.GetFiles(Versions));
    }

    [Fact]
    public void Holding_what_is_not_a_sha_makes_no_file()
    {
        using var lease = Store().Hold("../x");

        Assert.False(Directory.Exists(Versions));
    }

    // ---- Downloads and leftovers ----

    [Fact]
    public void An_unfinished_download_goes_at_twenty_five_hours_and_stays_at_twenty_three()
    {
        var old = Path.Combine(Versions, ".tmp-" + Guid.NewGuid());
        var young = Path.Combine(Versions, ".tmp-" + Guid.NewGuid());
        foreach (var path in new[] { old, young })
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, BuildStore.BinaryName), "half");
        }

        Directory.SetLastWriteTimeUtc(old, now.UtcDateTime - TimeSpan.FromHours(25));
        Directory.SetLastWriteTimeUtc(young, now.UtcDateTime - TimeSpan.FromHours(23));

        Store().Sweep(now, []);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(young));
    }

    [Fact]
    public void A_folder_left_aside_is_removed()
    {
        var aside = Path.Combine(Versions, ".old-" + Guid.NewGuid());
        Directory.CreateDirectory(aside);
        File.WriteAllText(Path.Combine(aside, BuildStore.BinaryName), "gone");

        var result = Store().Sweep(now, []);

        Assert.False(Directory.Exists(aside));
        Assert.Equal(new SweepResult(1, 0), result);
    }

    // ---- What is never touched ----

    [Fact]
    public void Nothing_outside_versions_is_touched_and_neither_are_odd_names()
    {
        Make(A);
        RecordOf(One, A);
        var beside = Path.Combine(root, "other");
        Directory.CreateDirectory(beside);
        File.WriteAllText(Path.Combine(beside, "x"), "x");
        File.WriteAllText(Path.Combine(root, "config"), "x");

        var notASha = Path.Combine(Versions, "notes");
        var shortSha = Path.Combine(Versions, new string('a', 39));
        var upper = Path.Combine(Versions, new string('A', 40));
        var dotted = Path.Combine(Versions, ".keep");
        foreach (var path in new[] { notASha, shortSha, upper, dotted }) Directory.CreateDirectory(path);
        var strayFile = Path.Combine(Versions, B);
        File.WriteAllText(strayFile, "a file named like a build");
        var strayLease = Path.Combine(Versions, "xyz.in-use-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(strayLease, "");

        Store().Sweep(now, []);

        Assert.True(File.Exists(Path.Combine(beside, "x")) && File.Exists(Path.Combine(root, "config")));
        Assert.All(new[] { notASha, shortSha, upper, dotted }, p => Assert.True(Directory.Exists(p), p));
        Assert.True(File.Exists(strayFile) && File.Exists(strayLease));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "boards")));
    }

    [Fact]
    public void The_launchers_own_folder_is_never_removed()
    {
        var own = Make(A);
        Make(B);
        RecordOf(One, C);

        Store().Sweep(now, [], own);

        Assert.True(Has(A));
        Assert.False(Has(B));
    }

    [SkippableFact]
    public void A_link_is_left_alone_and_what_it_points_at_too()
    {
        Skip.If(OperatingSystem.IsWindows(), "a link needs a privilege there");

        var outside = Directory.CreateTempSubdirectory("hatch-sweep-outside-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(outside, "keep"), "x");
            Directory.CreateDirectory(Versions);
            Directory.CreateSymbolicLink(Folder(A), outside);
            RecordOf(One, C);

            Store().Sweep(now, []);

            Assert.True(Directory.Exists(Folder(A)));
            Assert.True(File.Exists(Path.Combine(outside, "keep")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void A_sweep_with_no_versions_folder_does_nothing()
    {
        Assert.Equal(default, Store().Sweep(now, []));
        Assert.False(Directory.Exists(Versions));
    }

    // ---- A delete that fails ----

    [Fact]
    public void A_delete_that_fails_is_counted_and_tried_again_by_the_next_sweep()
    {
        Make(A);
        Make(B);
        RecordOf(One, A);

        var first = Store().Sweep(now, [], remove: _ => throw new IOException("busy"));

        Assert.Equal(new SweepResult(0, 1), first);
        Assert.Null(Store().Find(B));
        Assert.Single(Directory.GetDirectories(Versions, ".old-*"));

        var second = Store().Sweep(now, []);

        Assert.Equal(new SweepResult(1, 0), second);
        Assert.Empty(Directory.GetDirectories(Versions, ".old-*"));
        Assert.True(Has(A));
    }

    // ---- A real process ----

    [SkippableFact]
    public void A_build_a_process_is_running_stays_until_it_ends()
    {
        Skip.IfNot(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "run by hand nowhere else");

        var source = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "ping.exe")
            : "/bin/sleep";
        Skip.IfNot(File.Exists(source), source + " is not here");

        Directory.CreateDirectory(Folder(A));
        var binary = Path.Combine(Folder(A), BuildStore.BinaryName);
        File.Copy(source, binary);
        RecordOf(One, C);

        var start = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in OperatingSystem.IsWindows() ? new[] { "-n", "60", "127.0.0.1" } : ["60"])
            start.ArgumentList.Add(arg);
        start.RedirectStandardOutput = true;

        using var process = Process.Start(start)!;
        try
        {
            if (OperatingSystem.IsLinux()) WaitUntilSeen(process, Folder(A));

            var kept = Store().Sweep(now, RunningBuilds.Paths());

            Assert.True(Has(A), "a running build was removed");
            Assert.NotNull(Store().Find(A));
            Assert.Equal(0, kept.Removed);
            if (OperatingSystem.IsWindows()) Assert.Equal(1, kept.Failed);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        // Windows lets go of the image a moment after the process is gone.
        for (var i = 0; i < 100 && Has(A); i++)
        {
            Store().Sweep(now, RunningBuilds.Paths());
            if (Has(A)) Thread.Sleep(50);
        }

        Assert.False(Has(A), "the build was not removed once nothing ran it");
    }

    // A scan right after Process.Start can miss the child before /proc catches up with it
    // (or, on a busy box, drop it from a short-lived listing error); wait for the real scan
    // to see it rather than asserting against whatever the first scan happened to return.
    private static void WaitUntilSeen(Process process, string folder)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var attempts = 0;
        var seen = Array.Empty<string>() as IReadOnlyCollection<string>;
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            attempts++;

            try
            {
                seen = RunningBuilds.Paths();
                last = null;
            }
            catch (Exception e)
            {
                seen = Array.Empty<string>();
                last = e;
            }

            if (seen.Any(path => path.StartsWith(folder, StringComparison.Ordinal)
                                  && (path.Length == folder.Length || path[folder.Length] == Path.DirectorySeparatorChar)))
            {
                Console.WriteLine($"WaitUntilSeen: {process.Id} seen after {attempts} attempt(s)");
                return;
            }

            Thread.Sleep(20);
        }

        var target = new FileInfo($"/proc/{process.Id}/exe").LinkTarget;
        throw new TimeoutException(
            $"never saw pid {process.Id} (exe -> {target ?? "unknown"}, has exited: {process.HasExited}) among {seen.Count} path(s) from the last scan"
            + (last is null ? "" : $"; the last scan threw {last}"));
    }
}
