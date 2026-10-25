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
    IAgentAdapter Adapter,
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

    /// <summary>The model families a pinned id is judged against, so a test can run a second agent.</summary>
    public ModelFamilies Families { get; init; } = ModelFamilies.Default;

    /// <summary>
    /// This process's own build identity, read once off the entry assembly -
    /// see CliRevision.cs. Replaceable so a test can assert what a heartbeat
    /// sends without a stamped assembly to build.
    /// </summary>
    public ICliRevision CliRevision { get; init; } = new CliRevision();

    /// <summary>
    /// What a pinned readout would draw, kept current whether or not a
    /// terminal is actually drawing one - see <see cref="ReadoutState"/>.
    /// </summary>
    public ReadoutState Readout { get; init; } = new();

    /// <summary>
    /// What the keyboard has decided so far - always constructed, nothing has
    /// to ask who is listening, the same remark as <see cref="Readout"/>'s
    /// own. Keys off by default, which keeps every existing test compiling
    /// with the keyboard simply out of the picture.
    /// </summary>
    public Controls Controls { get; init; } = new(keysOn: false);

    /// <summary>
    /// Where a checkout's chosen name is recorded - <see cref="RunnerNames.Record"/>'s
    /// file, beside <see cref="Settings.UserConfigPath"/>. Replaceable so a test
    /// reads and writes its own temp file rather than the machine's.
    /// </summary>
    public string RunnersPath { get; init; } = RunnerNames.Record.DefaultPath();

    /// <summary>
    /// The resolved active-or-named profile's name, not its file: the key
    /// <see cref="Checkout.RunnerAsync"/> records the runner's name under, so
    /// two profiles serving one workspace do not share a name. Null with no
    /// profile.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// The pointer this run follows between increments. Null when
    /// <c>--profile</c> or <c>HATCH_PROFILE</c> pinned the run, and null by
    /// default so a test never reads the machine's own.
    /// </summary>
    public string? ActiveProfilePath { get; init; }

    /// <summary>
    /// Where the named profiles live. Replaceable so a test reads its own
    /// directory rather than the machine's, like <see cref="RunnersPath"/>.
    /// </summary>
    public string ProfilesDirectory { get; init; } = Settings.ProfilesDirectory();

    /// <summary>
    /// Where the night's running totals are handed from one incarnation of the
    /// loop to the next. Set by the supervisor in <c>scripts/hatch.sh</c> or
    /// <c>scripts/hatch.ps1</c> and by nobody else, so its absence is how the
    /// runner knows there is no supervisor standing over it and therefore
    /// nothing to restart it.
    /// </summary>
    public string? NightStatePath { get; init; }

    public int OffsetMinutes => Board.OffsetMinutes(Clock.GetLocalNow());

    public Picker Picker() => new(Board, RunnerName, Say, Checkouts, Root, Settings.BaseBranch, Settings.Workspace, MakeClone, Settings.Agent, Families, Adapter);

    public Idle Idle(bool mine = false) => new(Board, Say, Checkouts, Settings.Workspace is not null, mine);

    public Increment Increment() => new(Board, Sessions, Adapter, Settings, Say, Checkouts, Readout, TempDirectory, Controls, Workspace, QuietBoard, Clock);

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
    /// How the checkout's own git history is read, for the narrower check
    /// against the newest commit under its own source (src/Hatch.Cli,
    /// src/Hatch.Contracts) rather than the board. Replaceable so a test can
    /// say what the checkout's history looks like without a real repository
    /// to ask.
    /// </summary>
    public Func<ISourceRevision> SourceRevision { get; init; } = () =>
        throw new InvalidOperationException("no source revision reader was configured");

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
        SourceRevision = () => new GitSourceRevision(Root),
        NewBoard = runnerName => new Board(new HatchClient(Settings, runnerName, onWaiting: Say.Complain), Settings.Tags),
        MakeClone = (remote, path) => new GitClone(remote, path),
    };
}
