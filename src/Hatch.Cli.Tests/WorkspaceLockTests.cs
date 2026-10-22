namespace Hatch.Cli.Tests;

/// <summary>
/// One lock per workspace - the directory a run clones fresh checkouts into,
/// rather than a checkout already on disk.
/// </summary>
public sealed class WorkspaceLockTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-workspace-lock-").FullName;

    private string Tree(string name)
    {
        var path = Path.Combine(_temp, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void PathFor_is_a_fingerprinted_name_under_the_temp_directory()
    {
        var workspace = Tree("workspace");
        var path = WorkspaceLock.PathFor(workspace, _temp);

        Assert.Equal(_temp, Path.GetDirectoryName(path));
        Assert.Matches("^hatch-workspace-[0-9a-f]{12}\\.lock$", Path.GetFileName(path));
    }

    [Fact]
    public void Two_spellings_of_one_workspace_give_one_path()
    {
        var workspace = Tree("one");
        var roundabout = Path.Combine(_temp, "two", "..", "one");
        Directory.CreateDirectory(Path.Combine(_temp, "two"));

        Assert.Equal(WorkspaceLock.PathFor(workspace, _temp), WorkspaceLock.PathFor(roundabout, _temp));
    }

    [Fact]
    public void Take_records_the_pid_and_the_checkout()
    {
        var workspace = Tree("workspace");
        var checkout = Tree("checkout");

        using var held = WorkspaceLock.Take(workspace, checkout, _temp, out var holder);

        Assert.NotNull(held);
        Assert.Null(holder);

        var dir = WorkspaceLock.PathFor(workspace, _temp);
        Assert.Equal(Environment.ProcessId.ToString(), File.ReadAllText(Path.Combine(dir, "pid")).Trim());
        Assert.Equal(checkout, File.ReadAllText(Path.Combine(dir, "checkout")));
    }

    [Fact]
    public void A_second_take_of_the_same_workspace_is_refused()
    {
        var workspace = Tree("workspace");
        var checkout = Tree("checkout");

        using var first = WorkspaceLock.Take(workspace, checkout, _temp, out _);
        var second = WorkspaceLock.Take(workspace, Tree("other"), _temp, out var holder);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.NotNull(holder);
        Assert.Equal(Environment.ProcessId, holder!.Pid);
        Assert.Equal(checkout, holder.Checkout);
    }

    [Fact]
    public void A_lock_whose_owner_is_gone_is_cleared_and_taken()
    {
        var workspace = Tree("workspace");
        using var abandoned = WorkspaceLock.Take(workspace, Tree("checkout"), _temp, out _);
        Assert.NotNull(abandoned);

        // Not capturing Console.Error: it is process-wide and xunit runs
        // classes in parallel, so asserting the take succeeds is the proof.
        using var taken = WorkspaceLock.Take(workspace, Tree("checkout-2"), _temp, out var holder, alive: _ => false);

        Assert.NotNull(taken);
        Assert.Null(holder);
    }

    [Fact]
    public void Two_different_workspaces_take_two_locks_at_once()
    {
        using var first = WorkspaceLock.Take(Tree("one"), Tree("checkout-one"), _temp, out _);
        using var second = WorkspaceLock.Take(Tree("two"), Tree("checkout-two"), _temp, out _);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void A_released_lock_is_taken_again_and_dispose_is_safe_twice()
    {
        var workspace = Tree("workspace");
        var held = WorkspaceLock.Take(workspace, Tree("checkout"), _temp, out _)!;
        held.Dispose();
        held.Dispose();

        using var again = WorkspaceLock.Take(workspace, Tree("checkout-2"), _temp, out _);
        Assert.NotNull(again);
    }

    [Fact]
    public void Refusal_names_the_pid_the_checkout_and_the_fix()
    {
        var text = WorkspaceLock.Refusal("/a/workspace", new WorkspaceHolder(4242, "/a/checkout"));

        Assert.Contains("pid 4242", text, StringComparison.Ordinal);
        Assert.Contains("/a/checkout", text, StringComparison.Ordinal);
        Assert.Contains("hatch config --here --workspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Refusal_degrades_with_no_pid_and_no_checkout()
    {
        var text = WorkspaceLock.Refusal("/a/workspace", new WorkspaceHolder(null, null));

        Assert.Contains("another run", text, StringComparison.Ordinal);
        Assert.DoesNotContain(", from", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_says_this_run_will_not_clone()
    {
        var text = WorkspaceLock.Note("/a/workspace", new WorkspaceHolder(4242, "/a/checkout"));

        Assert.Contains("this run will not clone", text, StringComparison.Ordinal);
        Assert.Contains("pid 4242", text, StringComparison.Ordinal);
        Assert.Contains("/a/checkout", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_degrades_with_no_pid_and_no_checkout()
    {
        var text = WorkspaceLock.Note("/a/workspace", new WorkspaceHolder(null, null));

        Assert.Contains("another run", text, StringComparison.Ordinal);
        Assert.DoesNotContain(", from", text, StringComparison.Ordinal);
    }

    // ---- Serve ----

    [Fact]
    public void Serve_with_no_workspace_passes_the_runtime_through()
    {
        using var h = new Harness();

        var ok = WorkspaceLock.Serve(h.Runtime, out var served, out var held, out _);

        Assert.True(ok);
        Assert.Null(held);
        Assert.Same(h.Runtime, served);
    }

    [Fact]
    public void Serve_takes_a_free_workspace()
    {
        using var h = new Harness();
        var ws = Path.Combine(h.Temp, "ws");
        var runtime = h.Runtime with { Settings = h.Runtime.Settings.WithWorkspace(ws, defaulted: false) };

        var ok = WorkspaceLock.Serve(runtime, out var served, out var held, out var refusal);

        Assert.True(ok);
        Assert.NotNull(held);
        Assert.Equal("", refusal);
        Assert.Equal(ws, served.Settings.Workspace);

        held!.Dispose();
    }

    [Fact]
    public void Serve_refuses_a_configured_workspace_already_held()
    {
        using var h = new Harness();
        var ws = Path.Combine(h.Temp, "ws");
        using var other = WorkspaceLock.Take(ws, h.Root, h.Temp, out _);
        var runtime = h.Runtime with { Settings = h.Runtime.Settings.WithWorkspace(ws, defaulted: false) };

        var ok = WorkspaceLock.Serve(runtime, out _, out var held, out var refusal);

        Assert.False(ok);
        Assert.Null(held);
        Assert.Contains("hatch config --here --workspace", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Serve_falls_back_when_the_defaulted_workspace_is_already_held()
    {
        using var h = new Harness();
        var ws = Path.Combine(h.Temp, "ws");
        var outside = Path.Combine(h.Temp, "outside");
        var inside = Path.Combine(ws, "clone");
        using var other = WorkspaceLock.Take(ws, h.Root, h.Temp, out _);

        var runtime = h.Runtime with
        {
            Settings = h.Runtime.Settings.WithWorkspace(ws, defaulted: true),
            Checkouts = [.. h.Runtime.Checkouts, new CheckoutEntry(outside, null, Standing: false), new CheckoutEntry(inside, null, Standing: false)],
        };

        var ok = WorkspaceLock.Serve(runtime, out var served, out var held, out _);

        Assert.True(ok);
        Assert.Null(held);
        Assert.Null(served.Settings.Workspace);
        Assert.Contains("this run will not clone", string.Join('\n', h.Say.Complained), StringComparison.Ordinal);
        Assert.Contains(served.Checkouts, c => c.Path == h.Root);
        Assert.Contains(served.Checkouts, c => c.Path == outside);
        Assert.DoesNotContain(served.Checkouts, c => c.Path == inside);
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
