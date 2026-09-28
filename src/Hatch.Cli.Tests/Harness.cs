using System.Diagnostics;

namespace Hatch.Cli.Tests;

/// <summary>
/// The claude CLI, replaced by whatever the test wants it to be: a run that
/// finishes, one that fails, one that sits there long enough for a heartbeat to
/// be refused underneath it.
/// </summary>
public sealed class FakeSessions : ISessionRunner
{
    /// <summary>Every spawn, in order - so a test can assert that there was not one.</summary>
    public List<SessionRequest> Spawned { get; } = [];

    /// <summary>Every attached spawn.</summary>
    public List<SessionRequest> Attached { get; } = [];

    /// <summary>Set once the session is under way, so a test can act while it runs.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Why there is nothing to spawn, when that is the path under test. Null is a CLI that is there.</summary>
    public string? Missing { get; set; }

    /// <summary>
    /// Waits for a session to be under way, and fails saying so rather than
    /// hanging when none ever is. A bare await on <see cref="Started"/> never
    /// returns if the thing under test declines to spawn, and it takes the rest
    /// of its class down with it.
    /// </summary>
    public async Task StartedWithin(int withinMs = 10_000)
    {
        try
        {
            await Started.Task.WaitAsync(TimeSpan.FromMilliseconds(withinMs));
        }
        catch (TimeoutException)
        {
            Assert.Fail($"waited {withinMs}ms for a session to start, and none did");
        }
    }

    /// <summary>What a session does. The default emits a few events and finishes well.</summary>
    public Func<SessionRequest, Action<string>?, CancellationToken, Task<SessionResult>> Behaviour { get; set; } =
        (_, onLine, _) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.ToolUse("Bash", "make test-api"));
            onLine?.Invoke(Fixtures.Result(said: "```work-log\nDid a thing\n\nIn detail.\n```"));
            return Task.FromResult(new SessionResult(0, ""));
        };

    /// <summary>A session that runs until its token is cancelled, which is what a lost lease does to one.</summary>
    public static Func<SessionRequest, Action<string>?, CancellationToken, Task<SessionResult>> UntilStopped(
        Action? onceRunning = null) =>
        async (_, onLine, ct) =>
        {
            onLine?.Invoke(Fixtures.Init());
            onLine?.Invoke(Fixtures.ToolUse("Bash", "make test-api"));
            onceRunning?.Invoke();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                // Killed, which is the outcome under test. A killed run reports
                // nothing, exactly as a TERM'd CLI does not get to write a
                // result event.
                return new SessionResult(143, "");
            }

            return new SessionResult(0, "");
        };

    public Task<SessionResult> RunAsync(SessionRequest request, Action<string>? onLine, CancellationToken ct)
    {
        Spawned.Add(request);
        Started.TrySetResult();
        return Behaviour(request, onLine, ct);
    }

    public Task<int> AttachAsync(SessionRequest request, CancellationToken ct)
    {
        Attached.Add(request);
        Started.TrySetResult();
        return Task.FromResult(0);
    }

    public bool CanSpawn(out string refusal)
    {
        refusal = Missing ?? "";
        return Missing is null;
    }
}

/// <summary>The tree between increments, without a remote to fetch from.</summary>
/// <remarks>
/// A factory rather than one <see cref="IWorkspace"/>, since <see
/// cref="Runtime.Workspace"/> takes a path and a base branch and can be asked
/// for more than one checkout in a pass. <see cref="Prepared"/> is the paths
/// asked for, in order - both the count a single-checkout test wants and the
/// sequence a multi-checkout one does.
/// </remarks>
public sealed class FakeWorkspace
{
    /// <summary>What any checkout not named in <see cref="AnswerFor"/> resets to.</summary>
    public Reset Answer { get; set; } = Reset.Ready;

    /// <summary>What one particular checkout resets to, overriding <see cref="Answer"/>.</summary>
    public Dictionary<string, Reset> AnswerFor { get; } = [];

    /// <summary>Every path a pass asked to be made current, in the order it asked.</summary>
    public List<string> Prepared { get; } = [];

    /// <summary>
    /// Run at the moment a reset is asked for, so a test can look at what had
    /// already happened by then. The order is the acceptance criterion: a ticket
    /// is claimed before the fetch, not after it.
    /// </summary>
    public Action? Watching { get; set; }

    /// <summary>
    /// Everything a pass asked of any checkout, in order and in one list -
    /// <c>prepare</c>, <c>enter</c>, <c>plan</c>, <c>leave</c> and <c>return</c>,
    /// each with the path - so the order between them can be asserted, and not
    /// only the order within each.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>What a checkout not named in <see cref="EntryFor"/> finds: no branch, and a name to cut.</summary>
    public Func<string, string, BranchEntry>? Entry { get; set; }

    /// <summary>One checkout's own answer to branch entry.</summary>
    public Dictionary<string, BranchEntry> EntryFor { get; } = [];

    /// <summary>The lines <see cref="IWorkspace.Leave"/> answers with, for every checkout.</summary>
    public List<string> LeaveNotes { get; } = [];

    /// <summary>What <c>Heads</c> answers for a checkout - null, like an origin that did not answer, unless a test says.</summary>
    public Dictionary<string, RemoteHeads?> HeadsFor { get; } = [];

    /// <summary>What <c>Fetch</c> answers, for every checkout.</summary>
    public bool FetchAnswer { get; set; } = true;

    /// <summary>What <c>Check</c> answers for one issue in one checkout. Absent is a check that could not be made.</summary>
    public Dictionary<(string Path, string Key), Verdict?> Verdicts { get; } = [];

    /// <summary>What <c>Leave</c> hands back as the verdict on origin's branch, per checkout.</summary>
    public Dictionary<string, Verdict?> FoundFor { get; } = [];

    /// <summary>Each <c>Leave</c>, with whether the pull request was to be synced.</summary>
    public List<(string Path, string Key, bool Sync)> Left { get; } = [];

    /// <summary>What each <c>Enter</c> was given as the answer to a which-branch question.</summary>
    public List<string?> Answers { get; } = [];

    /// <summary>Whether <see cref="IWorkspace.Prepare"/> was told to stash, per call.</summary>
    public List<bool> Stashed { get; } = [];

    public IWorkspace For(string path, string? baseBranch) => new Bound(this, path);

    private sealed class Bound(FakeWorkspace owner, string path) : IWorkspace
    {
        public Reset Prepare(bool stash = true)
        {
            owner.Prepared.Add(path);
            owner.Calls.Add($"prepare {path}");
            owner.Stashed.Add(stash);
            owner.Watching?.Invoke();
            return owner.AnswerFor.GetValueOrDefault(path, owner.Answer);
        }

        public BranchEntry Enter(string key, string title, string? answer)
        {
            owner.Calls.Add($"enter {path}");
            owner.Answers.Add(answer);
            return Answer(key, title);
        }

        public BranchEntry Plan(string key, string title, string? answer)
        {
            owner.Calls.Add($"plan {path}");
            return Answer(key, title) is var entry
                ? new BranchEntry
                {
                    Path = entry.Path, Kind = entry.Kind, Branch = entry.Branch, Sha = entry.Sha, Ahead = entry.Ahead,
                    Merge = entry.Merge, Conflicted = entry.Conflicted, Candidates = entry.Candidates,
                    Merged = entry.Merged, Cut = entry.Cut, Planned = true,
                }
                : entry;
        }

        public Leaving Leave(string key, bool syncPullRequest)
        {
            owner.Calls.Add($"leave {path}");
            owner.Left.Add((path, key, syncPullRequest));
            return new Leaving(path, [.. owner.LeaveNotes], owner.FoundFor.GetValueOrDefault(path));
        }

        public RemoteHeads? Heads()
        {
            owner.Calls.Add($"heads {path}");
            return owner.HeadsFor.GetValueOrDefault(path);
        }

        public bool Fetch()
        {
            owner.Calls.Add($"fetch {path}");
            return owner.FetchAnswer;
        }

        public Verdict? Check(string key)
        {
            owner.Calls.Add($"check {path} {key}");
            return owner.Verdicts.GetValueOrDefault((path, key));
        }

        public void Return() => owner.Calls.Add($"return {path}");

        private BranchEntry Answer(string key, string title) =>
            owner.EntryFor.TryGetValue(path, out var one) ? one
            : owner.Entry?.Invoke(path, key)
              ?? new BranchEntry { Path = path, Kind = BranchKind.None, Cut = Branches.Cut(key, title) };
    }
}

/// <summary>
/// The forge, without one to ask: a test says what the build on a sha came to,
/// and reads back every question. Silent until told - a forge that cannot answer
/// and does not say why, so a test that is not about builds hears nothing of them.
/// </summary>
public sealed class FakeForge
{
    /// <summary>Every read asked for, as <c>path sha</c>, in order.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>Every log asked for, by the check's name.</summary>
    public List<string> Logs { get; } = [];

    /// <summary>What every read answers, unless <see cref="ReadFor"/> says otherwise.</summary>
    public ForgeAnswer Answer { get; set; } = new(null, null);

    /// <summary>What a read of one sha answers.</summary>
    public Dictionary<string, ForgeAnswer> ReadFor { get; } = [];

    /// <summary>What a read answers, given the path and the sha - for a test that has to say something different the second time.</summary>
    public Func<string, string, ForgeAnswer?>? Reading { get; set; }

    /// <summary>The excerpt a failing check's log gives, by the check's name. Absent is no log.</summary>
    public Dictionary<string, string> Excerpts { get; } = [];

    /// <summary>Every canonical identity a forge was made for, in order.</summary>
    public List<string?> Canonicals { get; } = [];

    public IForge For(string path, string? canonical)
    {
        Canonicals.Add(canonical);
        return new Bound(this, path);
    }

    private sealed class Bound(FakeForge owner, string path) : IForge
    {
        public Task<ForgeAnswer> ReadAsync(string sha, CancellationToken ct)
        {
            owner.Reads.Add($"{path} {sha}");
            return Task.FromResult(
                owner.Reading?.Invoke(path, sha) ?? owner.ReadFor.GetValueOrDefault(sha, owner.Answer));
        }

        public Task<string?> LogAsync(FailingCheck check, CancellationToken ct)
        {
            owner.Logs.Add(check.Name);
            return Task.FromResult(owner.Excerpts.GetValueOrDefault(check.Name));
        }
    }
}

/// <summary>
/// A clone, without a network to make one from: a test sets what each attempt
/// answers, and reads back every remote and path asked for.
/// </summary>
public sealed class FakeClone
{
    /// <summary>Every clone asked for, in order.</summary>
    public List<(string Remote, string Path)> Requested { get; } = [];

    /// <summary>What every attempt answers with. Null - the default - is success.</summary>
    public string? Error { get; set; }

    /// <summary>One remote's own answer, overriding <see cref="Error"/> for it alone.</summary>
    public Dictionary<string, string?> ErrorFor { get; } = [];

    /// <summary>Run at the moment a clone is attempted, so a test can look at what had already happened by then.</summary>
    public Action? Watching { get; set; }

    public Func<string, string, IClone> Factory => (remote, path) => new Bound(this, remote, path);

    private sealed class Bound(FakeClone owner, string remote, string path) : IClone
    {
        public string? Run()
        {
            owner.Requested.Add((remote, path));
            owner.Watching?.Invoke();
            return owner.ErrorFor.TryGetValue(remote, out var specific) ? specific : owner.Error;
        }
    }
}

/// <summary>
/// The loop's own source, without a tree to change: a test sets what the next
/// read answers, at the moment a reset would have pulled it.
/// </summary>
public sealed class FakeSelf : ISelf
{
    /// <summary>What the loop is made of, until a test says it is made of something else.</summary>
    public SelfPrint Print { get; set; } = Of(("src/Hatch.Cli/GoToWork.cs", "before"));

    /// <summary>How many times the loop read itself.</summary>
    public int Taken { get; private set; }

    /// <summary>A print over the named files and the hashes they are standing in for.</summary>
    public static SelfPrint Of(params (string Path, string Hash)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Hash, StringComparer.Ordinal);
        var ordered = map.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}");
        return new SelfPrint(map, string.Join('\n', ordered));
    }

    public SelfPrint Take()
    {
        Taken++;
        return Print;
    }
}

/// <summary>One test's runner: a stub wire, a stub session, a temporary tree.</summary>
public sealed class Harness : IDisposable
{
    /// <summary>
    /// Short enough that a heartbeat happens inside a test rather than in a
    /// minute's time. The interval a real run uses is the server's TTL divided
    /// down - see <see cref="Claim.Interval"/>, which has its own test.
    /// </summary>
    public static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(25);

    public Wire Wire { get; } = new();
    public FakeSessions Sessions { get; } = new();
    public Transcript Say { get; } = new();
    public string Root { get; }
    public string Temp { get; }

    private readonly HatchClient _client;

    public Harness(TimeSpan? heartbeat = null)
    {
        Temp = Directory.CreateTempSubdirectory("hatch-test-").FullName;
        Root = Path.Combine(Temp, "checkout");
        Directory.CreateDirectory(Path.Combine(Root, ".git"));

        var settings = new Settings { Base = "https://hatch.example", Key = "hatch_ak_test", HeartbeatSeconds = 0 };
        _client = new HatchClient(settings, "test:/checkout", Wire);

        Runtime = new Runtime(
            Settings: settings,
            Board: new Board(_client),
            Sessions: Sessions,
            Say: Say,
            Root: Root,
            RunnerName: "test:/checkout",
            TempDirectory: Temp,
            Checkouts: [new CheckoutEntry(Root, "https://example.test/repo.git", Standing: true)],
            Heartbeat: heartbeat ?? Beat)
        {
            Workspace = (path, baseBranch) => Workspace.For(path, baseBranch),
            Forge = (path, canonical) => Forge.For(path, canonical),
            Self = () => Self,
            NewBoard = runnerName => new Board(new HatchClient(settings, runnerName, Wire)),
            MakeClone = Clone.Factory,
        };
    }

    /// <summary>
    /// Where a night's totals would be handed on. Named but not written: the
    /// supervisor makes the path and the runner writes to it only when it is
    /// asking to come back.
    /// </summary>
    public string NightState => Path.Combine(Temp, "night.json");

    /// <summary>A loop that is being watched by a supervisor, and so may ask to restart.</summary>
    public Runtime Supervised => Runtime with { NightStatePath = NightState };

    /// <summary>The loop's own source, as the loop finds it.</summary>
    public FakeSelf Self { get; } = new();

    /// <summary>The tree, as the pass finds it. Ready unless a test says otherwise.</summary>
    public FakeWorkspace Workspace { get; } = new();

    /// <summary>Where the build on a tip is read from. Silent - it cannot answer, and says nothing - unless a test says what a build came to.</summary>
    public FakeForge Forge { get; } = new();

    /// <summary>What a clone into a workspace does, when a test configures one at all.</summary>
    public FakeClone Clone { get; } = new();

    public Runtime Runtime { get; }

    public Board Board => Runtime.Board;

    public HatchClient Client => _client;

    /// <summary>
    /// Waits for something to become true, or fails saying what it was waiting
    /// for. Everything here is a race by construction; a fixed sleep would be
    /// either slow or flaky and usually both.
    /// </summary>
    public static async Task Eventually(Func<bool> until, string what, int withinMs = 5_000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < withinMs)
        {
            if (until()) return;
            await Task.Delay(5);
        }

        Assert.Fail($"waited {withinMs}ms for {what}, and it did not happen");
    }

    public void Dispose()
    {
        _client.Dispose();
        Wire.Dispose();
        try
        {
            Directory.Delete(Temp, recursive: true);
        }
        catch (IOException)
        {
            // A temporary directory that will not go is the operating system's
            // to tidy, not a test failure.
        }
    }
}
