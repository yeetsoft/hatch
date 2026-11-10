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

    /// <summary>
    /// How long a tool call may stay open before the runner stops saying it is
    /// still running, so a test can drive it in short spans. Null is the
    /// agent's own <see cref="IAgentAdapter.ToolCeiling"/>.
    /// </summary>
    public TimeSpan? ToolCeiling { get; init; }

    /// <summary>The model families a pinned id is judged against, so a test can run a second agent.</summary>
    public ModelFamilies Families { get; init; } = ModelFamilies.Default;

    /// <summary>The models this runner has stopped offering. A reference, so it survives <c>runtime with { ... }</c>.</summary>
    public ModelNarrowing Narrowing { get; init; } = new();

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
    /// The resolved active-or-named profile's name, not its file. Null with no
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
    /// directory rather than the machine's.
    /// </summary>
    public string ProfilesDirectory { get; init; } = Settings.ProfilesDirectory();

    /// <summary>
    /// Where the old per-checkout runner-name record lives. Replaceable so a
    /// test reads its own file rather than the machine's.
    /// </summary>
    public string RunnerRecordPath { get; init; } = RunnerNames.Record.DefaultPath();

    /// <summary>
    /// Where the night's running totals are handed from one incarnation of the
    /// loop to the next. Set by the supervisor in <c>scripts/hatch.sh</c> or
    /// <c>scripts/hatch.ps1</c> and by nobody else, so its absence is how the
    /// runner knows there is no supervisor standing over it and therefore
    /// nothing to restart it.
    /// </summary>
    public string? NightStatePath { get; init; }

    /// <summary>The file this run's log is being written to, or null where there is none - handed to the next incarnation in the night's state, so a restart appends to it.</summary>
    public string? LogPath { get; init; }

    /// <summary>The directory a runner set's host owns, from <c>HATCH_SET_DIR</c>; null outside a set.</summary>
    public string? SetDir { get; init; }

    /// <summary>This member's slot in a runner set, from <c>HATCH_SET_SLOT</c>; null for slot 1 and for a lone runner.</summary>
    public int? Slot { get; init; }

    /// <summary>What this runner advertises: detected once at start, merged with what it declares.</summary>
    public IReadOnlyList<string> Has { get; init; } = [];

    /// <summary>This process's own OS, arch, agent version, container, username and start time, read once at start.</summary>
    public MachineFacts Machine { get; init; } = MachineFacts.None;

    public int OffsetMinutes => Board.OffsetMinutes(Clock.GetLocalNow());

    public Picker Picker() => new(Board, RunnerName, Where, Say, Checkouts, Root, Settings.BaseBranch, Settings.Workspace, MakeClone, Settings.Agent, Families, Adapter, Narrowing, Clock);

    public Idle Idle(bool mine = false) => new(Board, Say, Checkouts, Settings.Workspace is not null, mine, RunnerName);

    public Increment Increment() => new(Board, Sessions, Adapter, Settings, Say, Checkouts, Readout, TempDirectory, Controls, Workspace, QuietBoard, Clock, ToolCeiling);

    /// <summary>
    /// This process, on the board: where it says it is alive and reads back
    /// what it has been asked to do. The key names the runner and the session
    /// is <see cref="Where"/>, the checkout; its claims carry <see cref="Where"/>
    /// too, since one runner's several sessions each hold at most one ticket
    /// at a different <see cref="Where"/>.
    /// </summary>
    public Runners Runners() => new(Board.Client);

    /// <summary>
    /// The models a beat advertises: <c>HATCH_MODELS</c> where it was set and
    /// validated for the agent the adapter is, else the adapter's own list,
    /// less any <see cref="Narrowing"/> in force. Null where that list is empty,
    /// so the beat leaves the row's models be - but empty, not null, where every
    /// model is narrowed, since an empty list clears the row's models and null
    /// would leave the stale ones.
    /// </summary>
    public IReadOnlyList<string>? AdvertisedModels() => Advertised(Settings, Adapter, Narrowing, Clock.GetUtcNow());

    /// <summary>
    /// <see cref="AdvertisedModels"/>'s rule without a <see cref="Runtime"/>, for
    /// the commands that read the queue but spawn nothing.
    /// </summary>
    public static IReadOnlyList<string>? Advertised(
        Settings settings, IAgentAdapter adapter, ModelNarrowing narrowing, DateTimeOffset now)
    {
        var chosen = settings.Models is not null && adapter.Id == settings.Agent ? settings.Models : adapter.Models;
        return chosen.Count > 0 ? narrowing.Offered(chosen, now) : null;
    }

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
    /// A fresh <see cref="Board"/> over the same key - what <c>--repo</c>
    /// rebuilds when it changes <see cref="Where"/> after this client already
    /// exists. Replaceable so a test can keep talking to its own stub wire
    /// rather than a real one.
    /// </summary>
    public Func<Board> NewBoard { get; init; } = () =>
        throw new InvalidOperationException("no board factory was configured");

    /// <summary>The real ones, over whichever checkout each call names.</summary>
    public Runtime WithGit() => this with
    {
        Workspace = (path, baseBranch) => new Workspace(path, baseBranch, Say.Line, Say.Complain),
        Forge = (path, canonical) => new GhForge(path, canonical),
        Self = () => new LoopSource(Root),
        SourceRevision = () => new GitSourceRevision(Root),
        NewBoard = () => new Board(new HatchClient(Settings, onWaiting: Say.Complain), refusesDangerous: Settings.RefusesDangerous)
        {
            Has = Has,
            Models = () => AdvertisedModels() ?? [],
            Agent = Adapter.Id,
        },
        MakeClone = (remote, path) => new GitClone(remote, path),
    };
}
