namespace Hatch.Cli;

/// <summary>
/// Everything a command needs that is not one of its own flags: where Hatch is,
/// where the tree is, what this runner is called, and how to spawn a session.
/// </summary>
/// <param name="Heartbeat">
/// How often a claim says it is still here. Null takes the server's TTL, which
/// is what every real run does; a test sets it so a heartbeat happens inside a
/// test rather than in five minutes' time.
/// </param>
public sealed record Runtime(
    Settings Settings,
    Board Board,
    ISessionRunner Sessions,
    Terminal Say,
    string Root,
    string RunnerName,
    string Where,
    string TempDirectory,
    IReadOnlyList<CheckoutEntry> Checkouts,
    TimeSpan? Heartbeat = null)
{
    /// <summary>The clock, so a test can put the loop at a particular hour.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// What a pinned readout would draw, kept current whether or not a
    /// terminal is actually drawing one - see <see cref="ReadoutState"/>.
    /// </summary>
    public ReadoutState Readout { get; init; } = new();

    /// <summary>
    /// Where a checkout's chosen name is recorded - <see cref="RunnerNames.Record"/>'s
    /// file, beside <see cref="Settings.UserConfigPath"/>. Replaceable so a test
    /// reads and writes its own temp file rather than the machine's.
    /// </summary>
    public string RunnersPath { get; init; } = RunnerNames.Record.DefaultPath();

    /// <summary>
    /// Where the night's running totals are handed from one incarnation of the
    /// loop to the next. Set by the supervisor in <c>scripts/hatch.sh</c> or
    /// <c>scripts/hatch.ps1</c> and by nobody else, so its absence is how the
    /// runner knows there is no supervisor standing over it and therefore
    /// nothing to restart it.
    /// </summary>
    public string? NightStatePath { get; init; }

    public int OffsetMinutes => Board.OffsetMinutes(Clock.GetLocalNow());

    public Picker Picker() => new(Board, RunnerName, Say, Checkouts, Root, Settings.BaseBranch, Settings.Workspace, MakeClone);

    public Idle Idle(bool mine = false) => new(Board, Say, Checkouts, Settings.Workspace is not null, mine);

    public Increment Increment() => new(Board, Sessions, Settings, Say, Checkouts, Readout, TempDirectory);

    /// <summary>
    /// This process, on the board: where it says it is alive and reads back
    /// what it has been asked to do. Named by <see cref="RunnerName"/>, which
    /// is the same string its claims carry - a runner has one identity, and the
    /// row is keyed on it.
    /// </summary>
    public Runners Runners() => new(Board.Client, RunnerName);

    /// <summary>
    /// The same board, quiet - what the idle poll reads with, since it says
    /// nothing about weather today, the way a claim's own heartbeat does not
    /// either. See <see cref="HatchClient.Quiet"/>.
    /// </summary>
    public Board QuietBoard => new(Board.Client.Quiet());

    /// <summary>
    /// How a checkout is made current between increments - one path, and the
    /// base branch to reset it to. Replaceable so a test can assert the order a
    /// pass does things in - the claim, then the reset, then the spawn - without
    /// a remote to fetch from.
    /// </summary>
    public Func<string, string?, IWorkspace> Workspace { get; init; } = (_, _) =>
        throw new InvalidOperationException("no workspace was configured");

    /// <summary>
    /// Where the build on a branch's tip is read from - the checkout's path, then
    /// the board's canonical identity for the repository, null where the project
    /// binds none. Replaceable so a test can say what a build came to without a
    /// forge to ask.
    /// </summary>
    public Func<string, string?, IForge> Forge { get; init; } = (_, _) =>
        throw new InvalidOperationException("no forge was configured");

    /// <summary>
    /// How a repository this runner has no checkout of is cloned into <see
    /// cref="Hatch.Cli.Settings.Workspace"/> - remote, then path. Replaceable
    /// so a test can assert what a clone was asked for without a network to
    /// clone from.
    /// </summary>
    public Func<string, string, IClone> MakeClone { get; init; } = (_, _) =>
        throw new InvalidOperationException("no clone seam was configured");

    /// <summary>
    /// How the loop reads its own source. Replaceable for the same reason
    /// <see cref="Workspace"/> is: a test says the loop changed underneath
    /// itself without having to change the files it is running from.
    /// </summary>
    public Func<ISelf> Self { get; init; } = () =>
        throw new InvalidOperationException("no source reader was configured");

    /// <summary>
    /// A fresh <see cref="Board"/> named for a runner this run was not started
    /// as - what <c>--repo</c> rebuilds when it changes <see cref="RunnerName"/>
    /// after this client already exists. Replaceable so a test can keep talking
    /// to its own stub wire rather than a real one.
    /// </summary>
    public Func<string, Board> NewBoard { get; init; } = _ =>
        throw new InvalidOperationException("no board factory was configured");

    /// <summary>The real ones, over whichever checkout each call names.</summary>
    public Runtime WithGit() => this with
    {
        Workspace = (path, baseBranch) => new Workspace(path, baseBranch, Say.Line, Say.Complain),
        Forge = (path, canonical) => new GhForge(path, canonical),
        Self = () => new LoopSource(Root),
        NewBoard = runnerName => new Board(new HatchClient(Settings, runnerName, onWaiting: Say.Complain)),
        MakeClone = (remote, path) => new GitClone(remote, path),
    };
}
