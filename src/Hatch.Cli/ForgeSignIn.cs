using System.Collections.Concurrent;

namespace Hatch.Cli;

/// <summary>
/// What the loop knows about whether it is signed in to each checkout's forge: the
/// latest answer for every checkout asked (absent until one), when that answer was
/// given, and when the checkout was last asked at all. A reference on
/// <see cref="Runtime"/>, so a copy of it with a field changed still shares what
/// was learned.
/// </summary>
/// <remarks>
/// A question that came back with no answer - a host that could not be reached, a
/// check that ran out of time - stamps the time it was asked and leaves the old
/// answer standing, so a bad hour on the network neither blanks <c>works</c> nor
/// clears <c>fails</c>.
/// </remarks>
public sealed class ForgeSignInCache
{
    /// <summary>The least time between two questions to one checkout, whoever asks.</summary>
    public static readonly TimeSpan Between = TimeSpan.FromMinutes(1);

    /// <summary>How long a <c>works</c> is left alone at the top of a pass. A <c>fails</c> is asked every pass.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(30);

    private readonly Lock _gate = new();

    private readonly Dictionary<string, (ForgeSignInAnswer Answer, DateTimeOffset At)> _latest =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly Dictionary<string, DateTimeOffset> _asked =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// The latest answer for <paramref name="root"/>, however long ago - null for a checkout never
    /// answered. Unlike a question it does not expire: it is what the loop tells the board.
    /// </summary>
    public ForgeSignInAnswer? Latest(string root)
    {
        lock (_gate) return _latest.TryGetValue(Key(root), out var held) ? held.Answer : null;
    }

    /// <summary>The latest answer for every checkout under <paramref name="directory"/> that has one, such as a clone made after start.</summary>
    public IReadOnlyList<ForgeSignInAnswer> LatestUnder(string directory)
    {
        var prefix = Key(directory) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        lock (_gate) return [.. _latest.Where(p => p.Key.StartsWith(prefix, comparison)).Select(p => p.Value.Answer)];
    }

    /// <summary>
    /// Whether <paramref name="root"/> should be asked now: never inside <see cref="Between"/> of the
    /// last question, and at the top of a pass (<paramref name="passTop"/>) not while its answer is a
    /// <c>works</c> under <see cref="Keep"/> old.
    /// </summary>
    public bool Due(string root, DateTimeOffset now, bool passTop)
    {
        lock (_gate) return DueLocked(Key(root), now, passTop);
    }

    /// <summary>
    /// <see cref="Due"/>, and if so stamps the question now - one step, so two callers at once ask once.
    /// </summary>
    public bool TryBegin(string root, DateTimeOffset now, bool passTop)
    {
        var key = Key(root);
        lock (_gate)
        {
            if (!DueLocked(key, now, passTop)) return false;

            _asked[key] = now;
            return true;
        }
    }

    /// <summary>Keeps <paramref name="answer"/> unless it is no answer, which leaves what was held.</summary>
    public void Record(string root, ForgeSignInAnswer answer, DateTimeOffset now)
    {
        if (answer.State is null || Array.IndexOf(RunnerForge.Words, answer.State) < 0) return;

        lock (_gate) _latest[Key(root)] = (answer, now);
    }

    private bool DueLocked(string key, DateTimeOffset now, bool passTop)
    {
        if (_asked.TryGetValue(key, out var asked) && now - asked < Between) return false;
        if (passTop && _latest.TryGetValue(key, out var held)
            && held.Answer.State == ForgeSignInStates.Works && now - held.At < Keep)
            return false;

        return true;
    }

    private static string Key(string root) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

/// <summary>
/// A forge that asks again whether the runner is signed in whenever a call comes
/// back with a reason - a refusal is the likeliest sign a sign-in has gone. The
/// reasons are the forge's own words and are no judgement of them: the check
/// decides, and the question is asked at most once a minute. <see cref="IForge.LogAsync"/>
/// and <see cref="IForge.FileUrlAsync"/> carry no reason and never ask.
/// </summary>
/// <remarks>
/// The check goes through <see cref="Runtime.AskForgeAsync"/>, which asks the unwrapped
/// forge, so a check can never start a check.
/// </remarks>
internal sealed class WatchedForge(IForge inner, Runtime runtime, string path, string? canonical) : IForge
{
    public async Task<ForgeAnswer> ReadAsync(string sha, CancellationToken ct) =>
        await Watch(await inner.ReadAsync(sha, ct), a => a.Why, ct);

    public Task<string?> LogAsync(FailingCheck check, CancellationToken ct) => inner.LogAsync(check, ct);

    public async Task<PullRequestAnswer> ReadPullRequestAsync(string url, CancellationToken ct) =>
        await Watch(await inner.ReadPullRequestAsync(url, ct), a => a.Why, ct);

    public async Task<PullRequestListAnswer> ReadOpenPullRequestsAsync(CancellationToken ct) =>
        await Watch(await inner.ReadOpenPullRequestsAsync(ct), a => a.Why, ct);

    public async Task<AssociationAnswer> ReadAssociationAsync(long number, CancellationToken ct) =>
        await Watch(await inner.ReadAssociationAsync(number, ct), a => a.Why, ct);

    public async Task<MergeAnswer> MergeAsync(string url, string sha, CancellationToken ct) =>
        await Watch(await inner.MergeAsync(url, sha, ct), a => a.Why, ct);

    public async Task<PullRequestBodyAnswer> ReadBodyAsync(string url, CancellationToken ct) =>
        await Watch(await inner.ReadBodyAsync(url, ct), a => a.Why, ct);

    public async Task<BodyEditAnswer> EditBodyAsync(string url, string body, CancellationToken ct) =>
        await Watch(await inner.EditBodyAsync(url, body, ct), a => a.Why, ct);

    // A conflict is the forge answering: it has read the branch and said no, which a refused sign-in cannot.
    public async Task<UpdateAnswer> UpdateAsync(long number, string sha, CancellationToken ct) =>
        await Watch(await inner.UpdateAsync(number, sha, ct), a => a.Outcome == UpdateOutcomes.Conflicted ? null : a.Why, ct);

    public async Task<RerunAnswer> RerunAsync(FailingCheck check, CancellationToken ct) =>
        await Watch(await inner.RerunAsync(check, ct), a => a.Why, ct);

    public Task<string?> FileUrlAsync(string filePath, string trunk, CancellationToken ct) => inner.FileUrlAsync(filePath, trunk, ct);

    public Task<ForgeSignInAnswer> CheckSignInAsync(TimeSpan limit, CancellationToken ct) => inner.CheckSignInAsync(limit, ct);

    private async Task<T> Watch<T>(T answer, Func<T, string?> why, CancellationToken ct)
    {
        if (why(answer) is { Length: > 0 }) await runtime.AskForgeAsync(path, canonical, ct);
        return answer;
    }
}

public sealed partial record Runtime
{
    /// <summary>
    /// How a checkout is asked whether this runner is signed in to its forge - path, then the board's
    /// canonical identity for the repository. Replaceable so a test can say what a machine answered
    /// without a <c>gh</c> to run. Null, the default, is the real thing: <see cref="MakeForge"/>'s own
    /// check, which is never the wrapped <see cref="Forge"/> - a record's property initialiser cannot
    /// reach <c>this</c>, so the default cannot be written here.
    /// </summary>
    public Func<string, string?, CancellationToken, Task<ForgeSignInAnswer>>? ForgeProbe { get; init; }

    /// <summary>
    /// The forge's answer to whether this runner is signed in to <paramref name="canonical"/>, held and
    /// asked at most once a minute per checkout. Returns the held answer without asking inside that minute.
    /// With <paramref name="passTop"/> it also leaves a <c>works</c> under thirty minutes old alone. An answer
    /// of nothing - a host that could not be reached - is not kept, and the old one stands.
    /// </summary>
    public async Task<ForgeSignInAnswer?> AskForgeAsync(
        string path, string? canonical, CancellationToken ct, bool passTop = false)
    {
        if (!ForgeCache.TryBegin(path, Clock.GetUtcNow(), passTop)) return ForgeCache.Latest(path);

        var answer = ForgeProbe is { } probe
            ? await probe(path, canonical, ct)
            : await MakeForge(path, canonical).CheckSignInAsync(GhForge.Limit, ct);
        ForgeCache.Record(path, answer, Clock.GetUtcNow());
        return ForgeCache.Latest(path);
    }

    /// <summary>
    /// Asks every checkout this run serves that is due, at once, so <see cref="ForgeAnswer"/> has
    /// something to say. The board is read for each repository's canonical identity only when some
    /// checkout is due, and a board that cannot be read means no identity - <c>gh</c> then resolves the
    /// repository from the remotes. Returns what each checkout answered, or held, with the checkouts
    /// that were not due left out.
    /// </summary>
    public async Task<IReadOnlyList<(string Path, ForgeSignInAnswer? Answer)>> AskForgesAsync(
        CancellationToken ct, bool passTop = false)
    {
        var now = Clock.GetUtcNow();
        var due = Checkouts.Where(c => ForgeCache.Due(c.Path, now, passTop)).ToList();
        if (due.Count == 0) return [];

        IReadOnlyList<ProjectDto> projects = [];
        try { projects = await QuietBoard.ProjectsAsync(ct); }
        catch (HatchException) { }

        return await Task.WhenAll(due.Select(async c => (
            c.Path, Answer: await AskForgeAsync(c.Path, CanonicalOf(c, projects), ct, passTop))));
    }

    /// <summary>
    /// The board's own identity for the repository a checkout is, matched from the checkout's remote to a
    /// project's bound remote. Null where no project binds it.
    /// </summary>
    public static string? CanonicalOf(CheckoutEntry checkout, IEnumerable<ProjectDto> projects) =>
        checkout.Remote is { } remote
            ? projects.SelectMany(p => p.Repositories).FirstOrDefault(r => r.Remote == remote)?.Canonical
            : null;

    /// <summary>
    /// The same match from a dispatch's own bindings, for the checkout an increment is about to run in.
    /// Null for a project that binds nothing, which is checked from the runner's own checkout.
    /// </summary>
    public static string? CanonicalOf(global::Hatch.Cli.Checkouts.Choice chosen, IReadOnlyList<WorkRepositoryDto> bound) =>
        chosen.Repositories.FirstOrDefault(r => r.Path == chosen.Root)?.Remote is { } remote
            ? bound.FirstOrDefault(r => r.Remote == remote)?.Canonical
            : null;

    /// <summary>
    /// Whether this runner is signed in to its forge, as the board is told on every beat: the worst over
    /// the checkouts it serves that have an answer, and any clone made under the workspace since. Null
    /// until one has - a runner with no checkout, or one whose forge has never been reachable. The reason
    /// is one line, masked and at most <see cref="RunnerForge.MaxWhyLength"/> characters, beside
    /// <c>fails</c> only.
    /// </summary>
    public (string State, string? Why)? ForgeAnswer()
    {
        var served = Checkouts.Select(c => ForgeCache.Latest(c.Path)).OfType<ForgeSignInAnswer>();
        var cloned = Settings.Workspace is { } workspace && !Settings.IsWorkspaceOff(workspace)
            ? ForgeCache.LatestUnder(workspace)
            : [];

        return FoldForge(served.Concat(cloned));
    }

    /// <summary>
    /// The worst of some sign-in answers, as the board is told: <c>fails</c>, then <c>works</c>. Null with
    /// no answer. The reason, beside <c>fails</c> only, is one line, masked and at most
    /// <see cref="RunnerForge.MaxWhyLength"/> characters.
    /// </summary>
    public static (string State, string? Why)? FoldForge(IEnumerable<ForgeSignInAnswer> answers)
    {
        var answer = RunnerForge.Worst(answers.Select(a => (a.State, a.Why)));
        if (answer is not { } worst) return null;

        // Scrub appends an ellipsis past its own cap, which would make 201.
        var first = (worst.Why ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        var why = worst.State == RunnerForge.Fails ? Scrub.Lines([first]).FirstOrDefault() : null;
        if (why is { Length: > RunnerForge.MaxWhyLength }) why = why[..RunnerForge.MaxWhyLength];
        return (worst.State, why);
    }

    /// <summary>
    /// One line for each served checkout where <c>gh</c> says it is not signed in, said at the start:
    /// what <c>gh</c> said, and that builds and pull requests will not be read or merged there. A checkout
    /// that is signed in, or could not be asked, is not mentioned.
    /// </summary>
    public async Task SayCannotReachForgeAsync(CancellationToken ct)
    {
        foreach (var (path, answer) in await AskForgesAsync(ct))
        {
            if (answer is { State: ForgeSignInStates.Fails })
                Say.Complain($"hatch: {path} - {answer.Why ?? "gh is not signed in"}, so builds and pull requests will not be read or merged there");
        }
    }
}
