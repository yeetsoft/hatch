namespace Hatch.Cli;

/// <summary>
/// The loop's look at what is in review: for each issue there, whether its
/// branch on origin still merges with the trunk, reported to the board.
/// </summary>
/// <remarks>
/// <para>It asks <c>git ls-remote</c> and fetches only on change. An idle loop
/// that fetched every interval all night is the thing the loop does not do -
/// see <see cref="Workspace.Prepare"/> - and an in-review column is mostly
/// pull requests that have not moved, so what moved is what one cheap call per
/// checkout says. A checkout is fetched once however many of its issues moved.</para>
///
/// <para>An issue is <em>unchanged</em>, and costs nothing, when either this
/// runner has already checked exactly what origin holds for it and the board took
/// the verdict, or the board's stored verdict names the one branch and both shas
/// origin holds now. The first is what stops a merged branch - which has no branch
/// sha on its verdict, and is the commonest thing in the column - from being
/// fetched every interval; the second is what stops a restarted runner
/// fetching everything once for nothing.</para>
///
/// <para>Nothing here can fail a pass. Every way it goes wrong is one line for
/// the terminal, said again only if it changes, and the issue is asked again next
/// interval because nothing is remembered until the board has taken the verdict.
/// It runs between passes on one thread, so it never overlaps a session.</para>
/// </remarks>
public sealed class ReviewPoll
{
    private readonly Dictionary<(string Path, string Key), string> _checked = [];
    private readonly Dictionary<(string Path, string Key), string> _told = [];
    private readonly Dictionary<string, string> _complained = [];
    private DateTimeOffset? _last;

    /// <summary>What one issue needs in one checkout: its key, and the verdict the board holds for it there.</summary>
    private sealed record Item(string Key, MergeCheckDto? Stored, bool Shared);

    private sealed record Target(Checkouts.Polled Where, List<Item> Items);

    /// <summary>Whether an interval has gone by since the last poll - and starts the next one's clock.</summary>
    public bool Due(DateTimeOffset now, int intervalSeconds)
    {
        if (_last is { } last && now - last < TimeSpan.FromSeconds(intervalSeconds)) return false;

        _last = now;
        return true;
    }

    public async Task RunAsync(Runtime runtime, CancellationToken ct)
    {
        try
        {
            await PollAsync(runtime, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The last resort. A poll is housekeeping: nothing it does is worth
            // a night.
            Complain("poll", $"hatch: the review poll failed - {e.Message}", runtime.Say);
        }
    }

    private async Task PollAsync(Runtime runtime, CancellationToken ct)
    {
        var say = runtime.Say;

        IReadOnlyList<ReviewEntryDto> entries;
        try
        {
            entries = await runtime.Board.ReviewAsync(runtime.Checkouts, ct);
        }
        catch (HatchException e)
        {
            Complain("board", $"hatch: what is in review could not be read, so no branch was checked - {e.Message}", say);
            return;
        }

        Recovered("board");

        var targets = new Dictionary<string, Target>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var served in Checkouts.Served(entry.Repositories, runtime.Checkouts, runtime.Settings.BaseBranch))
            {
                if (served.Remote is null)
                {
                    Complain($"remote:{served.Path}",
                        $"hatch: {Name(served.Path)} has no origin remote, so a verdict there has nothing to be reported under - not checking it", say);
                    continue;
                }

                var stored = entry.Checks.FirstOrDefault(c =>
                    served.Canonical is null ? c.Remote == served.Remote : c.Canonical == served.Canonical);

                if (!targets.TryGetValue(served.Path, out var target))
                    targets[served.Path] = target = new Target(served, []);
                target.Items.Add(new Item(entry.Key, stored, entry.Repositories.Count > 1));
            }
        }

        foreach (var (path, target) in targets)
        {
            ct.ThrowIfCancellationRequested();
            await CheckoutAsync(runtime, path, target, ct);
        }
    }

    private async Task CheckoutAsync(Runtime runtime, string path, Target target, CancellationToken ct)
    {
        var say = runtime.Say;
        var workspace = runtime.Workspace(path, target.Where.BaseBranch);

        var heads = workspace.Heads();
        if (heads is null)
        {
            Complain($"heads:{path}", $"hatch: {Name(path)}: origin did not answer, so what is in review there was not checked", say);
            return;
        }

        if (!heads.Shas.TryGetValue(heads.Trunk, out var trunkSha))
        {
            Complain($"heads:{path}", $"hatch: {Name(path)}: origin has no {heads.Trunk}, so what is in review there was not checked", say);
            return;
        }

        Recovered($"heads:{path}");

        var moved = new List<(Item Item, string Fingerprint)>();
        foreach (var item in target.Items)
        {
            var candidates = heads.Shas
                .Where(h => h.Key != heads.Trunk && Branches.Names(h.Key, item.Key))
                .OrderBy(h => h.Key, StringComparer.Ordinal)
                .ToList();

            // The trunk, then each candidate's name and sha: what origin holds
            // that could change the answer, and nothing else.
            var fingerprint = string.Join('\n', [trunkSha, .. candidates.Select(h => $"{h.Key}:{h.Value}")]);

            if (_checked.TryGetValue((path, item.Key), out var seen) && seen == fingerprint) continue;
            if (Vouches(item.Stored, heads.Trunk, trunkSha, candidates)) continue;

            moved.Add((item, fingerprint));
        }

        if (moved.Count == 0) return;

        // Once for the checkout, whatever number of its issues moved.
        if (!workspace.Fetch())
        {
            Complain($"fetch:{path}", $"hatch: {Name(path)}: could not fetch from origin, so what is in review there was not checked", say);
            return;
        }

        Recovered($"fetch:{path}");

        foreach (var (item, fingerprint) in moved)
        {
            var subject = $"check:{path}:{item.Key}";

            var verdict = workspace.Check(item.Key);
            if (verdict is null)
            {
                Complain(subject, $"hatch: {item.Key}: whether its branch merges cleanly with {heads.Trunk} could not be worked out in {Name(path)}", say);
                continue;
            }

            var request = new MergeCheckRequest(
                target.Where.Remote!, verdict.Trunk, verdict.TrunkSha, verdict.Kind,
                verdict.Branch, verdict.BranchSha, verdict.Files, runtime.RunnerName);

            try
            {
                await runtime.Board.MergeCheckAsync(item.Key, request, ct);
            }
            catch (HatchException e)
            {
                // Not remembered, so the board is asked again next interval.
                Complain(subject, $"hatch: {item.Key}: the board did not take the verdict on its branch - {e.Message}", say);
                continue;
            }

            Recovered(subject);
            _checked[(path, item.Key)] = fingerprint;

            var now = Describe(verdict.Kind, verdict.Files);
            var before = _told.TryGetValue((path, item.Key), out var told) ? told
                : item.Stored is null ? null
                : Describe(item.Stored.Verdict, item.Stored.Files);
            _told[(path, item.Key)] = now;

            if (before != now) say.Line(Sentence(item, verdict, path));
        }
    }

    /// <summary>
    /// Whether the board's own verdict already stands for what origin holds: a
    /// clean or conflicted one naming the one branch, at the sha it stands at now
    /// and against the trunk as it stands now.
    /// </summary>
    private static bool Vouches(
        MergeCheckDto? stored, string trunk, string trunkSha, IReadOnlyList<KeyValuePair<string, string>> candidates) =>
        stored is { Verdict: MergeVerdicts.Clean or MergeVerdicts.Conflicted }
        && candidates.Count == 1
        && stored.Trunk == trunk
        && stored.TrunkSha == trunkSha
        && stored.Branch == candidates[0].Key
        && stored.BranchSha == candidates[0].Value;

    private static string Describe(string kind, IEnumerable<string> files) =>
        $"{kind}\n{string.Join('\n', files.Order(StringComparer.Ordinal))}";

    private static string Sentence(Item item, Verdict verdict, string path)
    {
        var where = item.Shared ? $" in {Name(path)}" : "";
        return verdict.Kind switch
        {
            MergeVerdicts.Conflicted =>
                $"hatch: {item.Key} conflicts with {verdict.Trunk} ({verdict.Files.Count} file{(verdict.Files.Count == 1 ? "" : "s")}){where}",
            MergeVerdicts.Clean => $"hatch: {item.Key} merges cleanly with {verdict.Trunk}{where}",
            MergeVerdicts.Ambiguous => $"hatch: {item.Key} has more than one unmerged branch on origin{where}",
            _ => $"hatch: {item.Key} has no unmerged branch on origin{where}",
        };
    }

    private static string Name(string path) => Path.GetFileName(path.TrimEnd('/', '\\'));

    /// <summary>A failure, said once until it changes or goes away: a poll that fails every interval all night is one line and not four hundred.</summary>
    private void Complain(string subject, string line, Terminal say)
    {
        if (_complained.TryGetValue(subject, out var last) && last == line) return;

        _complained[subject] = line;
        say.Complain(line);
    }

    private void Recovered(string subject) => _complained.Remove(subject);
}
