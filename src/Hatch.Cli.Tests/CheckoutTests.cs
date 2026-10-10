using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// One loop per checkout, and two loops in two checkouts - which is the line
/// that lets a second one start at all.
/// </summary>
public sealed class CheckoutTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-lock-").FullName;

    private string Tree(string name)
    {
        var path = Path.Combine(_temp, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Two_checkouts_on_one_machine_both_run()
    {
        var first = LoopLock.Take(Tree("one"), _temp, out _);
        var second = LoopLock.Take(Tree("two"), _temp, out var refusal);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("", refusal);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Two_loops_in_one_checkout_do_not()
    {
        var tree = Tree("one");
        using var held = LoopLock.Take(tree, _temp, out _);
        var second = LoopLock.Take(tree, _temp, out var refusal);

        Assert.NotNull(held);
        Assert.Null(second);

        // The pid, so the second loop can go and find the first, and the
        // checkout, because "already running here" is ambiguous the moment there
        // are two heres.
        Assert.Contains($"pid {Environment.ProcessId}", refusal, StringComparison.Ordinal);
        Assert.Contains(tree, refusal, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Two_spellings_of_one_path_are_one_checkout()
    {
        var tree = Tree("Hatch");
        var lowered = Path.Combine(_temp, "hatch");

        // Only where the filesystem folds case, which is a property of the disk
        // and not of this code. Where it does not, the two spellings really are
        // two directories and keeping them apart is the right answer.
        Skip.IfNot(Directory.Exists(lowered), "this filesystem is case-sensitive, so there is one spelling");

        Assert.Equal(Checkout.Fingerprint(tree), Checkout.Fingerprint(lowered));

        using var held = LoopLock.Take(tree, _temp, out _);
        Assert.Null(LoopLock.Take(lowered, _temp, out var refusal));
        Assert.Contains("already running", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_written_the_long_way_round_is_the_same_checkout()
    {
        var tree = Tree("one");
        var roundabout = Path.Combine(_temp, "two", "..", "one");
        Directory.CreateDirectory(Path.Combine(_temp, "two"));

        Assert.Equal(Checkout.Fingerprint(tree), Checkout.Fingerprint(roundabout));
    }

    [SkippableFact]
    public void A_symlinked_checkout_is_the_tree_it_points_at()
    {
        var tree = Tree("one");
        var link = Path.Combine(_temp, "link");

        try
        {
            Directory.CreateSymbolicLink(link, tree);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Skip.If(true, "this platform will not make a symlink without a privilege");
        }

        Assert.Equal(Checkout.Fingerprint(tree), Checkout.Fingerprint(link));
    }

    [Fact]
    public void A_lock_whose_owner_is_gone_is_cleared_and_taken()
    {
        var tree = Tree("one");
        using var abandoned = LoopLock.Take(tree, _temp, out _);
        Assert.NotNull(abandoned);

        // The pid file is still there and the process behind it is not - a
        // machine that rebooted, or a kill -9. That is litter, not a lock.
        using var taken = LoopLock.Take(tree, _temp, out var refusal, alive: _ => false);

        Assert.NotNull(taken);
        Assert.Equal("", refusal);
    }

    [Fact]
    public void A_released_lock_is_taken_again()
    {
        var tree = Tree("one");
        LoopLock.Take(tree, _temp, out _)!.Dispose();

        using var again = LoopLock.Take(tree, _temp, out _);
        Assert.NotNull(again);
    }

    // ---- What the board calls this runner ----

    private string RunnersPath => Path.Combine(_temp, "runners");

    [Fact]
    public async Task HATCH_RUNNER_wins()
    {
        Assert.Equal(
            "the-box",
            await Checkout.RunnerAsync("the-box", "kestrel", "/Users/x/code/Hatch", null, default, RunnersPath));
    }

    [Fact]
    public async Task A_long_configured_runner_loses_its_head_rather_than_its_tail()
    {
        var deep = "/" + string.Join('/', Enumerable.Repeat("a-directory-with-a-long-name", 20)) + "/Hatch";
        var runner = await Checkout.RunnerAsync(deep, "kestrel", "/Users/x/code/Hatch", null, default, RunnersPath);

        // The server refuses a longer one, and a refused claim is a loop that
        // cannot start.
        Assert.Equal(ClaimRequest.MaxRunnerLength, runner.Length);

        // The end of a path is the part that names a checkout, so that is the
        // part that survives.
        Assert.StartsWith("...", runner, StringComparison.Ordinal);
        Assert.EndsWith("/Hatch", runner, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fresh_choice_is_recorded_and_a_later_run_repeats_it()
    {
        var tree = Tree("one");

        var first = await Checkout.RunnerAsync(null, "kestrel", tree, null, default, RunnersPath);
        var second = await Checkout.RunnerAsync(null, "kestrel", tree, null, default, RunnersPath);

        Assert.Equal(first, second);
        Assert.Contains(first, RunnerNames.All);
        Assert.Contains($"{Checkout.Canonical(tree)}={first}", File.ReadAllLines(RunnersPath));
    }

    [Fact]
    public async Task A_recorded_name_beats_a_fresh_choice()
    {
        var tree = Tree("one");
        RunnerNames.Record.Set(RunnersPath, Checkout.Canonical(tree), "Some Recorded Name");

        Assert.Equal("Some Recorded Name", await Checkout.RunnerAsync(null, "kestrel", tree, null, default, RunnersPath));
    }

    [Fact]
    public async Task Two_spellings_of_one_path_read_one_record()
    {
        var tree = Tree("one");
        var trailingSlash = tree + Path.DirectorySeparatorChar;

        var chosen = await Checkout.RunnerAsync(null, "kestrel", tree, null, default, RunnersPath);
        var again = await Checkout.RunnerAsync(null, "kestrel", trailingSlash, null, default, RunnersPath);

        Assert.Equal(chosen, again);
        Assert.Single(RunnerNames.Record.Read(RunnersPath));
    }

    [Fact]
    public async Task Two_checkouts_on_one_machine_never_share_a_name_even_hashing_to_the_same_slot()
    {
        var one = await Checkout.RunnerAsync(null, "kestrel", Tree("one"), null, default, RunnersPath);
        var two = await Checkout.RunnerAsync(null, "kestrel", Tree("two"), null, default, RunnersPath);

        Assert.NotEqual(one, two);
    }

    [Fact]
    public async Task A_name_held_by_a_live_runner_elsewhere_is_skipped()
    {
        var tree = Tree("one");
        var wouldChoose = RunnerNames.Choose(
            "kestrel", Checkout.Canonical(tree), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var wire = new Wire();
        wire.Json("GET", "/api/hatch/runners", new[]
        {
            new RunnerDto(
                wouldChoose, "loop", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null,
                "running", null, null, null, null, 90, [], null, null, null, "elsewhere:/some/other/tree"),
        });

        var client = new HatchClient(new Settings { Base = "https://hatch.example", Key = "hatch_ak_test" }, "lookup", wire);
        var board = new Board(client);

        var chosen = await Checkout.RunnerAsync(null, "kestrel", tree, board, default, RunnersPath);

        Assert.NotEqual(wouldChoose, chosen);
    }

    [Fact]
    public async Task The_local_record_is_the_backstop_when_the_board_cannot_be_reached()
    {
        var tree = Tree("one");
        var client = new HatchClient(new Settings { Base = "https://hatch.example", Key = "hatch_ak_test" }, "lookup", new Wire());
        var board = new Board(client);

        // No rule stubbed on the wire, so the read 404s the way an unreachable
        // or too-old Hatch would - and a name is still chosen rather than the
        // whole command failing over it.
        var chosen = await Checkout.RunnerAsync(null, "kestrel", tree, board, default, RunnersPath);

        Assert.Contains(chosen, RunnerNames.All);
    }

    // ---- What a runner serves ----

    [Fact]
    public void A_checkout_with_no_origin_declares_standing_and_no_remote()
    {
        var tree = Tree("no-origin");
        Git(tree, "init", "--quiet");

        Assert.True(Checkouts.TryDiscover(tree, [], null, out var checkouts, out _, out _));

        var entry = Assert.Single(checkouts);
        Assert.Equal(tree, entry.Path);
        Assert.True(entry.Standing);
        Assert.Null(entry.Remote);
    }

    [Fact]
    public void A_named_checkout_with_an_origin_reads_it()
    {
        var tree = Tree("with-origin");
        Git(tree, "init", "--quiet");
        Git(tree, "remote", "add", "origin", "https://example.test/named.git");

        Assert.True(Checkouts.TryDiscover(null, [tree], null, out var checkouts, out _, out _));

        var entry = Assert.Single(checkouts);
        Assert.Equal(tree, entry.Path);
        Assert.False(entry.Standing);
        Assert.Equal("https://example.test/named.git", entry.Remote);
    }

    [Fact]
    public void A_named_checkout_with_no_origin_is_refused_naming_it()
    {
        var tree = Tree("no-origin-named");
        Git(tree, "init", "--quiet");

        Assert.False(Checkouts.TryDiscover(null, [tree], null, out var checkouts, out _, out var refusal));

        Assert.Empty(checkouts);
        Assert.Contains(tree, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_path_that_does_not_exist_is_refused_before_anything_else()
    {
        var missing = Path.Combine(_temp, "does-not-exist");

        Assert.False(Checkouts.TryDiscover(null, [missing], null, out var checkouts, out _, out var refusal));

        Assert.Empty(checkouts);
        Assert.Contains(missing, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_path_with_no_git_is_refused_naming_it()
    {
        var notAcheckout = Tree("plain-directory");

        Assert.False(Checkouts.TryDiscover(null, [notAcheckout], null, out var checkouts, out _, out var refusal));

        Assert.Empty(checkouts);
        Assert.Contains(notAcheckout, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_standing_checkout_named_again_is_one_entry()
    {
        var tree = Tree("standing-again");
        Git(tree, "init", "--quiet");
        Git(tree, "remote", "add", "origin", "https://example.test/standing.git");

        Assert.True(Checkouts.TryDiscover(tree, [tree], null, out var checkouts, out _, out _));

        Assert.Single(checkouts);
    }

    [Fact]
    public void A_named_path_spelled_twice_is_one_entry()
    {
        var tree = Tree("one");
        Git(tree, "init", "--quiet");
        Git(tree, "remote", "add", "origin", "https://example.test/one.git");
        var roundabout = Path.Combine(_temp, "two", "..", "one");
        Directory.CreateDirectory(Path.Combine(_temp, "two"));

        Assert.True(Checkouts.TryDiscover(null, [tree, roundabout], null, out var checkouts, out _, out _));

        Assert.Single(checkouts);
    }

    // ---- HA-19: a workspace of clones ----

    [Fact]
    public void PathFor_and_CanonicalFor_round_trip_a_plain_canonical()
    {
        var path = Checkouts.PathFor("/clones", "example.com/owner/repo");

        Assert.Equal(Path.Combine("/clones", "example.com", "owner", "repo"), path);
        Assert.Equal("example.com/owner/repo", Checkouts.CanonicalFor("/clones", path));
    }

    [Fact]
    public void PathFor_and_CanonicalFor_round_trip_a_port()
    {
        var path = Checkouts.PathFor("/clones", "example.test:8443/owner/repo");

        Assert.Equal(Path.Combine("/clones", "example.test_8443", "owner", "repo"), path);
        Assert.Equal("example.test:8443/owner/repo", Checkouts.CanonicalFor("/clones", path));
    }

    [Fact]
    public void A_workspace_finds_checkouts_at_different_depths_and_leaves_a_plain_directory_a_stray()
    {
        var workspace = Tree("workspace");

        // Shallow: directly under the workspace.
        var shallow = Path.Combine(workspace, "example.test", "owner", "shallow");
        Directory.CreateDirectory(shallow);
        Git(shallow, "init", "--quiet");
        Git(shallow, "remote", "add", "origin", "https://example.test/owner/shallow.git");

        // Deep: nested further under a top-level entry that is not itself a checkout.
        var deep = Path.Combine(workspace, "example.test", "owner", "nested", "deep");
        Directory.CreateDirectory(deep);
        Git(deep, "init", "--quiet");
        Git(deep, "remote", "add", "origin", "https://example.test/owner/deep.git");

        // A stray: a top-level entry holding no checkout anywhere beneath it.
        var stray = Path.Combine(workspace, "not-a-checkout");
        Directory.CreateDirectory(Path.Combine(stray, "just-a-file"));

        Assert.True(Checkouts.TryDiscover(null, [], workspace, out var checkouts, out var strays, out var refusal));
        Assert.Equal("", refusal);

        Assert.Contains(checkouts, c => c.Path == shallow && c.Remote == "https://example.test/owner/shallow.git");
        Assert.Contains(checkouts, c => c.Path == deep && c.Remote == "https://example.test/owner/deep.git");
        Assert.Equal(2, checkouts.Count);

        var strayFound = Assert.Single(strays);
        Assert.Equal(Path.Combine(workspace, "not-a-checkout"), strayFound);
    }

    [Fact]
    public void A_workspace_that_overlaps_a_named_checkout_is_refused_naming_both()
    {
        var workspace = Tree("workspace");
        var nested = Path.Combine(workspace, "a-checkout");
        Directory.CreateDirectory(nested);
        Git(nested, "init", "--quiet");
        Git(nested, "remote", "add", "origin", "https://example.test/a.git");

        Assert.False(Checkouts.TryDiscover(null, [nested], workspace, out var checkouts, out var strays, out var refusal));

        Assert.Empty(checkouts);
        Assert.Empty(strays);
        Assert.Contains(workspace, refusal, StringComparison.Ordinal);
        Assert.Contains(nested, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureWorkspace_makes_the_directory_and_proves_it_writable()
    {
        var path = Path.Combine(_temp, "brand-new", "workspace");

        Assert.True(Checkouts.EnsureWorkspace(path, out var refusal));
        Assert.Equal("", refusal);
        Assert.True(Directory.Exists(path));
    }

    private static void Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo { FileName = "git", WorkingDirectory = dir, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        process.WaitForExit();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // The operating system's to tidy.
        }
    }
}
