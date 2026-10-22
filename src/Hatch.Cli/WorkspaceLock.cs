namespace Hatch.Cli;

/// <summary>Who holds a workspace's lock, read back for a run that was refused it.</summary>
/// <remarks>Either field can be null on its own - the window between the pid file's write and the checkout file's.</remarks>
public sealed record WorkspaceHolder(int? Pid, string? Checkout);

/// <summary>
/// One lock per workspace, held for the whole night a loop serves it.
/// </summary>
/// <remarks>
/// <para>Modelled on <see cref="LoopLock"/> - a directory under the temporary
/// path, claimed atomically by a <c>pid</c> file <see cref="FileMode.CreateNew"/>
/// will not write twice - but for the workspace a run clones fresh checkouts
/// into rather than for one checkout already on disk: the thing two loops
/// would otherwise race to clone into at three in the morning has no lock of
/// its own yet, so the workspace directory carries one for the whole run.</para>
///
/// <para>A second file, <c>checkout</c>, follows the pid file once it is
/// written, holding the path of the standing checkout the run that took the
/// lock is serving - so a refusal can say not only who holds the workspace
/// but from where.</para>
/// </remarks>
public sealed class WorkspaceLock : IDisposable
{
    private readonly string _dir;
    private bool _held;

    private WorkspaceLock(string dir)
    {
        _dir = dir;
        _held = true;
    }

    /// <summary>Where the lock for this workspace lives.</summary>
    public static string PathFor(string workspace, string tempDirectory) =>
        Path.Combine(tempDirectory, $"hatch-workspace-{Checkout.Fingerprint(workspace)}.lock");

    /// <summary>
    /// Take it, or say who has it. <paramref name="alive"/> is how "is that pid
    /// still there" is asked, so a test can answer it without a process.
    /// </summary>
    /// <returns>The lock, or null - in which case <paramref name="holder"/> says who has it.</returns>
    public static WorkspaceLock? Take(
        string workspace, string checkout, string tempDirectory,
        out WorkspaceHolder? holder, Func<int, bool>? alive = null)
    {
        alive ??= LoopLock.StillRunning;
        var dir = PathFor(workspace, tempDirectory);
        holder = null;

        if (TryCreate(dir, checkout, out var taken)) return taken;

        // A lock whose owner is gone is litter, exactly as LoopLock.Take finds it.
        var read = ReadHolder(dir);
        if (read.Pid is { } owner && alive(owner))
        {
            holder = read;
            return null;
        }

        Console.Error.WriteLine($"hatch: clearing a stale lock left by pid {read.Pid?.ToString() ?? "?"}");
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Somebody else got there first, which the retry below will find out.
        }

        if (TryCreate(dir, checkout, out var second)) return second;

        // Two runs that both found the lock stale, and this is the one that
        // lost the race for it. Re-read rather than reuse the one above - the
        // winner's own pid and checkout are what belong in the refusal now.
        holder = ReadHolder(dir);
        return null;
    }

    /// <summary>
    /// The atomic half, plus the checkout written once the pid file is. A
    /// failure after the pid file exists leaves no half-written lock behind -
    /// best effort, since there is nothing further to do about one that will
    /// not go.
    /// </summary>
    private static bool TryCreate(string dir, string checkout, out WorkspaceLock? taken)
    {
        taken = null;
        try
        {
            Directory.CreateDirectory(dir);

            using (var pid = new FileStream(
                       Path.Combine(dir, "pid"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(pid))
            {
                writer.Write(Environment.ProcessId);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The pid file is somebody else's claim, not ours to touch.
            return false;
        }

        try
        {
            File.WriteAllText(Path.Combine(dir, "checkout"), checkout);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The pid file above is ours, written this call - leaving it
            // behind with no checkout would be a half-written lock nobody
            // else wrote either half of.
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                // Nothing further to do about a half-written lock that will not clear.
            }

            return false;
        }

        taken = new WorkspaceLock(dir);
        return true;
    }

    private static WorkspaceHolder ReadHolder(string dir) => new(LoopLock.ReadPid(dir), ReadCheckout(dir));

    private static string? ReadCheckout(string dir)
    {
        try
        {
            return File.ReadAllText(Path.Combine(dir, "checkout"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Missing (a FileNotFoundException, which is an IOException) is the
            // window between the pid file's write and this one.
            return null;
        }
    }

    /// <summary>What a run configured for this workspace is told, when another already holds it.</summary>
    public static string Refusal(string workspace, WorkspaceHolder holder)
    {
        var who = holder.Pid is { } pid ? $"pid {pid}" : "another run";
        var from = holder.Checkout is { } checkout ? $", from {checkout}" : "";
        return string.Join('\n',
            $"hatch: the workspace {workspace} is already served by {who}{from}.",
            "hatch: two loops would reset the same clones - give this checkout a workspace of its own with  hatch config --here --workspace <dir>");
    }

    /// <summary>What a run that only defaulted to this workspace is told - it serves its own checkouts and will not clone.</summary>
    public static string Note(string workspace, WorkspaceHolder holder)
    {
        var who = holder.Pid is { } pid ? $"pid {pid}" : "another run";
        var from = holder.Checkout is { } checkout ? $", from {checkout}" : "";
        return $"hatch: another run holds the default workspace {workspace} - {who}{from}; this run will not clone";
    }

    /// <summary>
    /// The one entry point both <c>go-to-work</c> and <c>work</c> call: take the
    /// workspace this runtime is configured for, or fall back to serving only
    /// the checkouts already on disk where the workspace was only a default.
    /// </summary>
    /// <returns>False only where a configured (not defaulted) workspace is already held - in which case <paramref name="refusal"/> says so.</returns>
    public static bool Serve(Runtime runtime, out Runtime served, out WorkspaceLock? held, out string refusal)
    {
        refusal = "";

        if (runtime.Settings.Workspace is not { } workspace)
        {
            served = runtime;
            held = null;
            return true;
        }

        var checkout = runtime.Checkouts.FirstOrDefault(c => c.Standing)?.Path ?? runtime.Root;
        held = Take(workspace, checkout, runtime.TempDirectory, out var holder);
        if (held is not null)
        {
            served = runtime;
            return true;
        }

        if (runtime.Settings.WorkspaceDefaulted)
        {
            // Two runs that never named a workspace share the default one, and
            // refusing the second would turn away a run that only wants to
            // serve its own checkouts. It serves what it has and does not
            // clone - the first run owns the directory clones land in, and
            // what is already cloned there is that run's to serve and lock.
            runtime.Say.Complain(Note(workspace, holder!));
            served = runtime with
            {
                Checkouts = runtime.Checkouts
                    .Where(c => c.Standing || !Checkouts.IsUnder(workspace, c.Path)).ToList(),
                Settings = runtime.Settings.WithWorkspace(null, defaulted: true),
            };
            return true;
        }

        refusal = Refusal(workspace, holder!);
        served = runtime;
        return false;
    }

    /// <summary>Give it up. Safe to call twice, which on an interrupt it will be.</summary>
    public void Dispose()
    {
        if (!_held) return;
        _held = false;

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Already gone, or not ours to remove. Either way there is nothing
            // useful to say on the way out of a run.
        }
    }
}
