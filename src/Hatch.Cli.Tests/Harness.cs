using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

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

/// <summary>
/// A second agent, standing in for whatever one that is not Claude would be -
/// its own id, its own resume wording, its own stream format, its own hook
/// shape - so a call site that only works by accident of Claude's own
/// vocabulary leaking through <see cref="IAgentAdapter"/> fails under this one
/// instead of passing by coincidence.
/// </summary>
public sealed class FakeAdapter : IAgentAdapter
{
    public string Id => "fake-agent";

    public string DisplayName => "Fake Agent";

    public IHookOutputWriter HookOutput { get; } = new FakeHookOutputWriter();

    public IUsageProbe? Probe => null;

    public bool CanSpawn(out string refusal)
    {
        refusal = "";
        return true;
    }

    /// <summary>
    /// Set to make <see cref="CanDeny"/> refuse a row carrying at least one
    /// denied-tools rule, with this sentence; null, or a row with no rules at
    /// all, takes it.
    /// </summary>
    public string? CanDenyRefusal { get; set; }

    public bool CanDeny(IReadOnlyList<string> rules, out string refusal)
    {
        if (rules.Count > 0 && CanDenyRefusal is { } why)
        {
            refusal = why;
            return false;
        }

        refusal = "";
        return true;
    }

    public bool PromptOnStdin { get; set; } = true;

    public bool CanHook { get; set; } = true;

    /// <summary>Every request <see cref="Release"/> was called with, in order.</summary>
    public List<SessionRequest> Released { get; } = [];

    /// <summary>Makes <see cref="WriteHooks"/> return null, as a write that failed would.</summary>
    public bool HooksFail { get; set; }

    /// <summary>The child a spawn runs, where a test wants a real one; otherwise a name nothing answers to.</summary>
    public Func<SessionRequest, ProcessStartInfo>? Spawn { get; set; }

    /// <summary>Run inside <see cref="CreateReader"/>, where a reader that mints a session id would write it.</summary>
    public Action<RunFacts>? OnReader { get; set; }

    public ProcessStartInfo BuildHeadless(SessionRequest request) => Spawn?.Invoke(request) ?? new() { FileName = "fake-agent" };

    public ProcessStartInfo BuildAttached(SessionRequest request) => Spawn?.Invoke(request) ?? new() { FileName = "fake-agent" };

    public void Release(SessionRequest request) => Released.Add(request);

    public IAgentStreamReader CreateReader(string root, RunFacts facts, string model)
    {
        OnReader?.Invoke(facts);
        return new FakeStreamReader(facts, ResumeCommand);
    }

    public string? ReadClosing(string json) => null;

    public string ResumeCommand(string sessionId) => $"fake-agent --resume {sessionId}";

    public IAgentHooks? WrittenHooks { get; private set; }

    public IAgentHooks? WriteHooks(string temp, string key, string hatch, string? rtk) =>
        HooksFail ? null : WrittenHooks = SessionHooks.Write(temp, key, hatch, rtk);

    public UsageLimit.Recognised? Limit { get; set; }

    /// <summary>What <see cref="RecogniseLimit"/> was last asked about - the run's last words, as the increment settled on them.</summary>
    public string? LimitAskedAbout { get; private set; }

    public UsageLimit.Recognised? RecogniseLimit(string? said, DateTimeOffset endedAt, TimeZoneInfo? runnerZone = null)
    {
        LimitAskedAbout = said;
        return Limit;
    }

    /// <summary>Where <see cref="OutputLastMessagePath"/> answers from - null unless a test sets it.</summary>
    public string? OutputLastMessageFile { get; set; }

    public string? OutputLastMessagePath(SessionRequest request) => OutputLastMessageFile;
}

/// <summary>
/// The fake's own stream format - a line read back by nothing Claude ever
/// wrote: <c>FAKE-RESULT sessionId|title|summary|turns|costUsd|durationMs</c>,
/// pipe-delimited, with none of <see cref="StreamRender"/>'s stream-json.
/// </summary>
internal sealed class FakeStreamReader(RunFacts facts, Func<string, string> resumeCommand) : IAgentStreamReader
{
    private const string Prefix = "FAKE-RESULT ";

    public long TokensSoFar => 0;

    public int Requests => 0;

    public long PeakContextTokens => 0;

    public IReadOnlyList<UsageWindow> Usage => [];

    public DateTimeOffset? UsageReadAt => null;

    public IEnumerable<string> Read(string raw)
    {
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal)) yield break;

        var fields = raw[Prefix.Length..].Split('|');
        var sessionId = fields[0];
        var title = fields[1];
        var summary = fields[2];
        var turns = int.Parse(fields[3], CultureInfo.InvariantCulture);
        var costUsd = decimal.Parse(fields[4], CultureInfo.InvariantCulture);
        var durationMs = long.Parse(fields[5], CultureInfo.InvariantCulture);

        facts.SessionId = sessionId;
        facts.CostUsd = costUsd;
        facts.Result = new WorkLogEntryRequest(
            SessionId: sessionId,
            StartedAt: default,
            EndedAt: default,
            DurationMs: durationMs,
            Title: title,
            Summary: summary,
            IsError: false,
            Turns: turns,
            CostUsd: costUsd,
            Models: null);

        yield return $"fake-agent: done in {turns} turns";
        yield return $"hatch:   join it with  {resumeCommand(sessionId)}";
    }
}

/// <summary>
/// The fake's own hook-answer shape - a <c>Kind</c>/<c>Text</c> record, plainly
/// serialised, obviously not <see cref="StopBlock"/>/<see cref="PostToolUseOutput"/>
/// (neither "decision" nor "hookSpecificOutput" appear in it) and never
/// mentioning Claude.
/// </summary>
internal sealed class FakeHookOutputWriter : IHookOutputWriter
{
    public string WriteStop(string text) => JsonSerializer.Serialize(new FakeHookLine("stop", text));

    public string WritePostToolUse(string text) => JsonSerializer.Serialize(new FakeHookLine("post-tool-use", text));
}

internal sealed record FakeHookLine(string Kind, string Text);

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

    /// <summary>Whether a sha is on the trunk in one checkout. Absent is not on the trunk.</summary>
    public Dictionary<(string Path, string Sha), bool> OnTrunkFor { get; } = [];

    /// <summary>What <c>Check</c> answers for one issue in one checkout. Absent is a check that could not be made.</summary>
    public Dictionary<(string Path, string Key), Verdict?> Verdicts { get; } = [];

    /// <summary>What <c>Sync</c> answers for one issue in one checkout. Absent is a branch already current.</summary>
    public Dictionary<(string Path, string Key), Synced> SyncedFor { get; } = [];

    /// <summary>What <c>Leave</c> hands back as the verdict on origin's branch, per checkout.</summary>
    public Dictionary<string, Verdict?> FoundFor { get; } = [];

    /// <summary>Each <c>Leave</c>, with whether the pull request was to be synced.</summary>
    public List<(string Path, string Key, bool Sync)> Left { get; } = [];

    /// <summary>What each <c>Enter</c> was given as the answer to a which-branch question.</summary>
    public List<string?> Answers { get; } = [];

    /// <summary>Whether <see cref="IWorkspace.Prepare"/> was told to stash, per call.</summary>
    public List<bool> Stashed { get; } = [];

    /// <summary>
    /// Thrown out of <see cref="IWorkspace.Prepare"/> the next time it is
    /// called, instead of answering - what a git command failing outright, and
    /// not merely coming back <see cref="Reset.Never"/>, looks like.
    /// </summary>
    public Exception? PrepareThrows { get; set; }

    /// <summary>The same, for <see cref="IWorkspace.Enter"/>.</summary>
    public Exception? EnterThrows { get; set; }

    /// <summary>What <c>PushForLimit</c> answers, per checkout - nothing to push unless a test says.</summary>
    public Dictionary<string, LimitPushed> PushFor { get; } = [];

    /// <summary>Every <c>PushForLimit</c>, as <c>path key</c>, in order.</summary>
    public List<string> Pushed { get; } = [];

    /// <summary>What <c>DiffStat</c> answers, per checkout - null unless a test says.</summary>
    public Dictionary<string, string?> DiffStatFor { get; } = [];

    /// <summary>What <c>ReadBlob</c> answers, per checkout/trunk/path - absent unless a test says.</summary>
    public Dictionary<(string Path, string Trunk, string BlobPath), BlobRead> BlobsFor { get; } = [];

    /// <summary>What <c>TreeUnchanged</c> answers, for every checkout - clean unless a test says otherwise.</summary>
    public bool TreeUnchangedAnswer { get; set; } = true;

    /// <summary>What <c>Presence</c> answers for one issue in one checkout. Absent is no branch on origin at all.</summary>
    public Dictionary<(string Path, string Key), BranchPresence> PresenceFor { get; } = [];

    public IWorkspace For(string path, string? baseBranch) => new Bound(this, path);

    private sealed class Bound(FakeWorkspace owner, string path) : IWorkspace
    {
        public Reset Prepare(bool stash = true)
        {
            owner.Prepared.Add(path);
            owner.Calls.Add($"prepare {path}");
            owner.Stashed.Add(stash);
            owner.Watching?.Invoke();
            if (owner.PrepareThrows is { } thrown) throw thrown;
            return owner.AnswerFor.GetValueOrDefault(path, owner.Answer);
        }

        public BranchEntry Enter(string key, string title, string? answer, bool trunkLanding = false)
        {
            owner.Calls.Add($"enter {path}");
            owner.Answers.Add(answer);
            if (owner.EnterThrows is { } thrown) throw thrown;
            return Answer(key, title, trunkLanding);
        }

        public BranchEntry Plan(string key, string title, string? answer, bool trunkLanding = false)
        {
            owner.Calls.Add($"plan {path}");
            return Answer(key, title, trunkLanding) is var entry
                ? new BranchEntry
                {
                    Path = entry.Path, Kind = entry.Kind, Branch = entry.Branch, Sha = entry.Sha, Ahead = entry.Ahead,
                    Merge = entry.Merge, Conflicted = entry.Conflicted, Candidates = entry.Candidates,
                    Merged = entry.Merged, Cut = entry.Cut, Planned = true,
                    Trunk = entry.Trunk, TrunkLanding = entry.TrunkLanding,
                }
                : entry;
        }

        public BranchPresence Presence(string key)
        {
            owner.Calls.Add($"presence {path} {key}");
            return owner.PresenceFor.GetValueOrDefault((path, key));
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

        public (string? Full, bool OnTrunk) ResolveOnTrunk(string sha, string trunk)
        {
            owner.Calls.Add($"resolve {path} {sha}");
            return (sha, owner.OnTrunkFor.GetValueOrDefault((path, sha)));
        }

        public Verdict? Check(string key, string? playbookFilePath = null)
        {
            owner.Calls.Add($"check {path} {key}");
            return owner.Verdicts.GetValueOrDefault((path, key));
        }

        public Synced Sync(string key)
        {
            owner.Calls.Add($"sync {path} {key}");
            return owner.SyncedFor.GetValueOrDefault((path, key), new Synced(SyncOutcome.Current));
        }

        public void Return() => owner.Calls.Add($"return {path}");

        public LimitPushed PushForLimit(string key, string title)
        {
            owner.Calls.Add($"push {path}");
            owner.Pushed.Add($"{path} {key}");
            return owner.PushFor.GetValueOrDefault(path, new LimitPushed(LimitPush.Nothing, null, null, null));
        }

        public string? DiffStat(string branch)
        {
            owner.Calls.Add($"diff {path}");
            return owner.DiffStatFor.GetValueOrDefault(path);
        }

        public bool TreeUnchanged(string head)
        {
            owner.Calls.Add($"tree-unchanged {path}");
            return owner.TreeUnchangedAnswer;
        }

        public BlobRead ReadBlob(string trunk, string blobPath)
        {
            owner.Calls.Add($"read-blob {path} {trunk} {blobPath}");
            return owner.BlobsFor.GetValueOrDefault((path, trunk, blobPath), new BlobRead(BlobOutcome.Absent, null, null));
        }

        private BranchEntry Answer(string key, string title, bool trunkLanding) =>
            owner.EntryFor.TryGetValue(path, out var one) ? one
            : owner.Entry?.Invoke(path, key)
              ?? new BranchEntry { Path = path, Kind = BranchKind.None, Cut = Branches.Cut(key, title), TrunkLanding = trunkLanding };
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

    /// <summary>Every pull request asked for, as <c>path url</c>, in order.</summary>
    public List<string> PullRequestReads { get; } = [];

    /// <summary>What every pull request read answers, unless <see cref="PullRequestFor"/> says otherwise.</summary>
    public PullRequestAnswer PullRequestAnswer { get; set; } = new(null, null);

    /// <summary>What a read of one pull request's url answers.</summary>
    public Dictionary<string, PullRequestAnswer> PullRequestFor { get; } = [];

    /// <summary>Every merge asked for, with the sha it carried, in order.</summary>
    public List<(string Path, string Url, string Sha)> MergeCalls { get; } = [];

    /// <summary>What every merge attempt answers, unless <see cref="MergeFor"/> says otherwise.</summary>
    public MergeAnswer MergeAnswer { get; set; } = new(false, null);

    /// <summary>What a merge of one pull request's url answers.</summary>
    public Dictionary<string, MergeAnswer> MergeFor { get; } = [];

    /// <summary>Every update asked for, with the number and sha it carried, in order.</summary>
    public List<(string Path, long Number, string Sha)> UpdateCalls { get; } = [];

    /// <summary>What every update attempt answers, unless <see cref="UpdateFor"/> says otherwise.</summary>
    public UpdateAnswer UpdateAnswer { get; set; } = new(null, null);

    /// <summary>What an update of one pull request's number answers.</summary>
    public Dictionary<long, UpdateAnswer> UpdateFor { get; } = [];

    /// <summary>Every re-run asked for, with the job id it carried (null for a check with none), in order.</summary>
    public List<(string Path, long? JobId)> RerunCalls { get; } = [];

    /// <summary>What every re-run attempt answers, unless <see cref="RerunFor"/> says otherwise.</summary>
    public RerunAnswer RerunAnswer { get; set; } = new(null, null);

    /// <summary>What a re-run of one job id answers.</summary>
    public Dictionary<long, RerunAnswer> RerunFor { get; } = [];

    /// <summary>Every file link asked for, as the path, the file and the trunk, in order.</summary>
    public List<(string Path, string FilePath, string Trunk)> FileUrlCalls { get; } = [];

    /// <summary>What every file link answers, unless <see cref="FileUrlFor"/> says otherwise. Null is no link.</summary>
    public string? FileUrl { get; set; }

    /// <summary>What the link for one file path answers.</summary>
    public Dictionary<string, string?> FileUrlFor { get; } = [];

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

        public Task<PullRequestAnswer> ReadPullRequestAsync(string url, CancellationToken ct)
        {
            owner.PullRequestReads.Add($"{path} {url}");
            return Task.FromResult(owner.PullRequestFor.GetValueOrDefault(url, owner.PullRequestAnswer));
        }

        public Task<MergeAnswer> MergeAsync(string url, string sha, CancellationToken ct)
        {
            owner.MergeCalls.Add((path, url, sha));
            return Task.FromResult(owner.MergeFor.GetValueOrDefault(url, owner.MergeAnswer));
        }

        public Task<UpdateAnswer> UpdateAsync(long number, string sha, CancellationToken ct)
        {
            owner.UpdateCalls.Add((path, number, sha));
            return Task.FromResult(owner.UpdateFor.GetValueOrDefault(number, owner.UpdateAnswer));
        }

        public Task<RerunAnswer> RerunAsync(FailingCheck check, CancellationToken ct)
        {
            owner.RerunCalls.Add((path, check.JobId));
            return Task.FromResult(check.JobId is { } job
                ? owner.RerunFor.GetValueOrDefault(job, owner.RerunAnswer)
                : owner.RerunAnswer);
        }

        public Task<string?> FileUrlAsync(string filePath, string trunk, CancellationToken ct)
        {
            owner.FileUrlCalls.Add((path, filePath, trunk));
            return Task.FromResult(owner.FileUrlFor.GetValueOrDefault(filePath, owner.FileUrl));
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

    /// <summary>
    /// What <see cref="HatchClient.Send"/> measures its own retry window and
    /// waits against - real time unless a test hands it a fake one, which is
    /// what lets a test script a call that fails once or twice before it
    /// answers without actually spending the backoff's own seconds on it.
    /// </summary>
    public TimeProvider Clock { get; }

    public Harness(TimeSpan? heartbeat = null, TimeProvider? clock = null)
    {
        Temp = Directory.CreateTempSubdirectory("hatch-test-").FullName;
        Root = Path.Combine(Temp, "checkout");
        Directory.CreateDirectory(Path.Combine(Root, ".git"));

        Clock = clock ?? TimeProvider.System;
        var settings = new Settings { Base = "https://hatch.example", Key = "hatch_ak_test", HeartbeatSeconds = 0 };
        _client = new HatchClient(settings, "test:/checkout", Wire, Clock);

        // The default checkout's trunk is already settled as passed, at the
        // sha PollTests' own Heads() helper gives "main" - so a test that
        // never mentions a trunk build gets none of the poll's new trunk half:
        // it is vouched for on sight and costs nothing further. A test about
        // the trunk half itself overrides this with its own stub.
        Wire.Json("GET", "/api/hatch/trunk-builds", new[] { Fixtures.TrunkBuild() });

        // The default checkout's playbook file is read fresh off
        // Workspace.HeadsFor on every request rather than fixed at setup, so
        // it is always already current at whatever sha the trunk holds - a
        // test that moves the trunk sha mid-test still finds it current and
        // reads nothing further, the free pass the trunk-builds stub above
        // gives by a fixed sha instead. A test about the playbook file itself
        // overrides this with its own stub.
        Wire.Serve("GET", "/api/hatch/playbook-files", _ =>
        {
            var files = Workspace.HeadsFor.GetValueOrDefault(Root)?.TrunkSha is { } sha
                ? new[] { Fixtures.PlaybookFile(sha: sha) }
                : [];
            return JsonSerializer.Serialize(new PlaybookFilesDto(".hatch/playbooks.yaml", files), Fixtures.Json);
        });

        Runtime = new Runtime(
            Settings: settings,
            Board: new Board(_client),
            Sessions: Sessions,
            Adapter: new ClaudeAdapter(),
            Say: Say,
            Root: Root,
            RunnerName: "test:/checkout",
            Where: "test:/checkout",
            TempDirectory: Temp,
            Checkouts: [new CheckoutEntry(Root, "https://example.test/repo.git", Standing: true)],
            Heartbeat: heartbeat ?? Beat)
        {
            Workspace = (path, baseBranch) => Workspace.For(path, baseBranch),
            Forge = (path, canonical) => Forge.For(path, canonical),
            Self = () => Self,
            NewBoard = runnerName => new Board(new HatchClient(settings, runnerName, Wire, Clock)),
            MakeClone = Clone.Factory,
            Clock = Clock,
            // A temp file rather than the real Settings.UserConfigPath() -
            // otherwise every test that takes --repo would read and write the
            // machine actually running the suite.
            RunnersPath = Path.Combine(Temp, "runners"),
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
