using System.Globalization;
using System.Net;

namespace Hatch.Cli;

/// <summary>
/// A pull request nobody on the board records, opened from outside the loop: which project owns it, which
/// of them are passed over and why, and the filer that puts the rest in the review column.
/// </summary>
/// <remarks>
/// The rules live here as statics so <c>hatch queue</c> says exactly what the train will. The train's filer
/// is <see cref="OutsideFiler"/>, which only the elected driver of the project runs.
/// </remarks>
public static class OutsidePullRequests
{
    /// <summary>
    /// How old a pull request is before it is filed, so a person's own push and the pull request they open
    /// for it do not race the runner that would file it. Matches <c>MergeWork.SettleWindow</c> on the
    /// server, as <see cref="Poll.NoneWindow"/> does.
    /// </summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(10);

    /// <summary>How long a stranger's association is kept before it is read again, so a pull request nobody will file is not asked about every tick.</summary>
    public static readonly TimeSpan StrangerWindow = TimeSpan.FromMinutes(10);

    /// <summary>The forge's words for an author who may have a pull request filed for them.</summary>
    private static readonly string[] Trusted = ["MEMBER", "OWNER", "COLLABORATOR"];

    /// <summary>The project a checkout's repository belongs to for this purpose, and why.</summary>
    /// <param name="Project">The project that files.</param>
    /// <param name="BaseBranch">The branch a pull request must merge into: the repository's own binding, else the trunk the board reads for it.</param>
    /// <param name="Why">One sentence, for the description.</param>
    public sealed record Home(ProjectDto Project, string BaseBranch, string Why);

    /// <summary>
    /// The project that owns <paramref name="remote"/>'s repository: of those that bind it, the one whose
    /// primary repository it is, else the lowest id. Null where the board has told this runner nothing about
    /// the remote yet, or no project binds it - nothing is filed, and the next look asks again.
    /// </summary>
    /// <remarks>
    /// The canonical comes from the trunk rows, which spell the remote the way the poll reported it, since
    /// the canonicaliser is the server's. A repository's primary is the first of its project's list, the
    /// rule <c>ProjectRepositoryBinding</c> applies on the server.
    /// </remarks>
    public static Home? Resolve(string? remote, IReadOnlyList<TrunkBuildDto> trunks, IReadOnlyList<ProjectDto> projects)
    {
        if (remote is null) return null;

        var row = trunks.FirstOrDefault(t => t.Remote == remote);
        if (row is null) return null;

        var binding = projects
            .Select(p => (Project: p, Index: IndexOf(p, row.Canonical)))
            .Where(b => b.Index >= 0)
            .ToList();
        if (binding.Count == 0) return null;

        var primaries = binding.Where(b => b.Index == 0).OrderBy(b => b.Project.Id).ToList();
        var winner = primaries.Count > 0 ? primaries[0] : binding.OrderBy(b => b.Project.Id).First();

        var key = winner.Project.Key;
        var why = (winner.Index == 0, binding.Count) switch
        {
            (true, 1) => $"it is the primary repository of {key}",
            (true, var n) => $"it is the primary repository of {key}, of the {n} projects that bind it",
            (false, 1) => $"{key} binds it",
            (false, var n) => $"{n} projects bind it, none as its primary repository, and {key} has the lowest id",
        };

        var bound = winner.Project.Repositories[winner.Index].BaseBranch;
        return new Home(winner.Project, string.IsNullOrWhiteSpace(bound) ? row.Trunk : bound, why);
    }

    private static int IndexOf(ProjectDto project, string canonical) =>
        project.Repositories.ToList().FindIndex(r => string.Equals(r.Canonical, canonical, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why this pull request is passed over without asking the forge anything more, or null where it is
    /// not - cheapest first. Whether the author is trusted is the one check left out, and the one that
    /// costs a read: see <see cref="Untrusted"/>.
    /// </summary>
    public static string? Classify(PullRequestAnswer pr, DateTimeOffset now, string expectedBase, IEnumerable<string> projectKeys)
    {
        if (pr.CreatedAt is not { } created) return "the forge did not say when it was opened";
        if (now - created < SettleWindow)
            return $"it was opened less than {SettleWindow.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes ago";

        if (pr.HeadBranch is not { Length: > 0 } head) return "the forge did not say which branch it comes from";
        if (Branches.BelongsToProject(head, projectKeys)) return $"its branch {head} is named for an issue on the board";

        if (pr.MergeState == MergeStates.Draft) return "it is a draft";
        if (pr.IsCrossRepository != false) return "it comes from a fork";

        if (!string.Equals(pr.Base, expectedBase, StringComparison.Ordinal))
            return $"it merges into {pr.Base ?? "an unknown branch"}, not {expectedBase}";

        return null;
    }

    /// <summary>Why an author is passed over, or null where the association is one a pull request is filed for.</summary>
    public static string? Untrusted(AssociationAnswer answer)
    {
        if (answer.Association is not { } association)
            return $"could not read who its author is - {answer.Why ?? "the forge said nothing"}";

        return Trusted.Contains(association, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"its author is {association.ToLowerInvariant()}, not a member, owner or collaborator";
    }

    /// <summary>The sentence a pass-over is said in.</summary>
    public static string Words(string url, long? number, string reason) =>
        $"hatch: pull request {(number is { } n ? $"#{n} " : "")}({url}) is not filed - {reason}";

    /// <summary>The description of the issue a pull request is filed as: a header, then the body exactly as written.</summary>
    public static string Describe(string url, PullRequestAnswer pr, string association, Home home) =>
        $"Filed from pull request #{pr.Number} ({url}) by @{pr.AuthorLogin} ({association}), head branch `{pr.HeadBranch}`, "
        + $"opened {pr.CreatedAt!.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC.\n\n"
        + $"Project: {home.Project.Key}, because {home.Why}.\n\n"
        + (pr.Body ?? "");

    /// <summary>
    /// The pull requests <c>hatch queue</c> would say are passed over, run inside a checkout of a bound
    /// repository: each open one no issue records that the filer would pass over, and why. Best effort -
    /// anything the forge or the board will not say is left out, as the attention footer is.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PassedOverAsync(
        Cli cli, CancellationToken ct)
    {
        var lines = new List<string>();
        try
        {
            var checkouts = cli.Checkouts.Where(c => c.Remote is not null).ToList();
            if (checkouts.Count == 0) return lines;

            var trunks = await cli.Board.TrunkBuildsAsync(ct);
            var projects = await cli.Board.ProjectsAsync(ct);
            var keys = projects.Select(p => p.Key).ToList();

            foreach (var checkout in checkouts)
            {
                if (Resolve(checkout.Remote, trunks, projects) is not { } home) continue;

                var canonical = trunks.First(t => t.Remote == checkout.Remote).Canonical;
                var forge = cli.Forge(checkout.Path, canonical);
                var list = await forge.ReadOpenPullRequestsAsync(ct);
                if (list.Open is null) continue;

                foreach (var (url, pr) in list.Open.OrderBy(p => p.Value.Number))
                {
                    if (pr.State != PullRequestStates.Open) continue;

                    var reason = Classify(pr, cli.Clock.GetUtcNow(), home.BaseBranch, keys);
                    if (reason is null && pr.Number is { } number)
                        reason = Untrusted(await forge.ReadAssociationAsync(number, ct));
                    if (reason is null) continue;

                    if ((await cli.Board.PullRequestRecordedAsync(url, ct)).Count > 0) continue;
                    lines.Add(Words(url, pr.Number, reason));
                }
            }
        }
        catch (Exception e) when (e is HatchException or HttpRequestException)
        {
            // Nothing here is worth failing a queue read for.
        }

        return lines;
    }
}

/// <summary>
/// Files an outside pull request as an issue in the review column - once, however many ticks and runners look at it.
/// </summary>
/// <remarks>
/// <para>In three writes, in this order, so a failure part-way leaves a visible issue and never a silent
/// record: the issue, then its contribution record (which is also what <c>GET /api/hatch/issues?pullRequestUrl=</c>
/// finds it by), then the move into review. The stage each pull request has reached is kept, so a later tick
/// resumes at the write that failed and never files a second issue. A restart forgets it: the half-filed
/// issue stays where it is, with the pull request's URL in its description.</para>
///
/// <para>Two runners that both think they drive the project, across an election hand-over, can both create
/// the issue; the board's record route refuses the second one's <c>PUT</c> with a <c>409</c> naming the
/// winner. The loser comments on its own orphan and says why. Closing it is a terminal move, which is the
/// operator's.</para>
///
/// <para>Each pass-over is said once per pull request per run, per reason - a reason that changes (young,
/// then a stranger) is said again.</para>
/// </remarks>
public sealed class OutsideFiler
{
    private enum Stage { Created, Recorded }

    private readonly HashSet<string> _settled = [];
    private readonly Dictionary<string, (string Key, Stage Stage, string Association)> _filing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Association, DateTimeOffset At)> _strangers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One look at every checkout this runner holds of a repository whose project it drives. <paramref name="lists"/>
    /// is the tick's own, so a path is listed at most once a tick.
    /// </summary>
    public async Task FileAsync(
        Runtime runtime, Action<string> complain, IReadOnlyList<ReviewCheckDto> review,
        Dictionary<string, PullRequestListAnswer> lists, CancellationToken ct)
    {
        // Nothing to look at before a heartbeat has said what this runner drives.
        if (runtime.Readout.DrivenProjects.Count == 0) return;

        var checkouts = runtime.Checkouts.Where(c => c.Remote is not null).ToList();
        if (checkouts.Count == 0) return;

        IReadOnlyList<TrunkBuildDto> trunks;
        IReadOnlyList<ProjectDto> projects;
        try
        {
            trunks = await runtime.QuietBoard.TrunkBuildsAsync(ct);
            projects = await runtime.QuietBoard.ProjectsAsync(ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: could not read what projects bind a repository, so no outside pull request is filed - {e.Message}");
            return;
        }

        var keys = projects.Select(p => p.Key).ToList();
        var recorded = review.Select(r => r.PullRequestUrl?.Trim().TrimEnd('/'))
            .Where(u => !string.IsNullOrEmpty(u)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var checkout in checkouts)
        {
            ct.ThrowIfCancellationRequested();

            if (OutsidePullRequests.Resolve(checkout.Remote, trunks, projects) is not { } home) continue;
            if (!runtime.Readout.Drives(home.Project.Key)) continue;

            var canonical = trunks.First(t => t.Remote == checkout.Remote).Canonical;
            var name = Path.GetFileName(checkout.Path.TrimEnd('/', '\\'));
            var forge = runtime.Forge(checkout.Path, canonical);

            if (!lists.TryGetValue(checkout.Path, out var list))
            {
                lists[checkout.Path] = list = await forge.ReadOpenPullRequestsAsync(ct);
                if (list.Open is null && list.Why is { } why) complain($"hatch: could not read pull requests in {name} - {why}");
            }

            if (list.Open is null) continue;

            foreach (var (url, pr) in list.Open.OrderBy(p => p.Value.Number))
            {
                ct.ThrowIfCancellationRequested();
                if (pr.State != PullRequestStates.Open || recorded.Contains(url)) continue;

                try
                {
                    await ConsiderAsync(runtime, complain, forge, home, keys, url, pr, ct);
                }
                catch (HatchException e)
                {
                    complain($"hatch: pull request ({url}) could not be filed - {e.Message}");
                }
            }
        }
    }

    private async Task ConsiderAsync(
        Runtime runtime, Action<string> complain, IForge forge, OutsidePullRequests.Home home, IReadOnlyList<string> keys,
        string url, PullRequestAnswer pr, CancellationToken ct)
    {
        // Half filed on an earlier tick: resumed, and not looked up again - the lookup would find it now.
        if (_filing.ContainsKey(url))
        {
            await FinishAsync(runtime, complain, url, pr, ct);
            return;
        }

        var now = runtime.Clock.GetUtcNow();
        var reason = OutsidePullRequests.Classify(pr, now, home.BaseBranch, keys);

        // Settled for this run: said once, and not asked about again until its reason changes.
        if (_settled.Contains(reason is null ? $"{url}|recorded" : $"{url}|{reason}")) return;

        // A pull request an issue already records is no business of this filer's, whatever it looks like.
        if ((await runtime.QuietBoard.PullRequestRecordedAsync(url, ct)).Count > 0)
        {
            _settled.Add($"{url}|{reason ?? "recorded"}");
            return;
        }

        string? association = null;
        if (reason is null)
        {
            if (pr.Number is not { } number)
            {
                reason = "the forge did not say its number";
            }
            else
            {
                var answer = _strangers.TryGetValue(url, out var known) && now - known.At < OutsidePullRequests.StrangerWindow
                    ? new AssociationAnswer(known.Association, null)
                    : await forge.ReadAssociationAsync(number, ct);

                reason = OutsidePullRequests.Untrusted(answer);
                association = answer.Association;

                if (reason is not null && association is not null) _strangers.TryAdd(url, (association, now));
                else _strangers.Remove(url);
            }
        }

        if (reason is null)
        {
            await FileOneAsync(runtime, complain, home, url, pr, association!, ct);
            return;
        }

        if (_settled.Add($"{url}|{reason}")) runtime.Say.Line(OutsidePullRequests.Words(url, pr.Number, reason));
    }

    private async Task FileOneAsync(
        Runtime runtime, Action<string> complain, OutsidePullRequests.Home home, string url, PullRequestAnswer pr,
        string association, CancellationToken ct)
    {
        var created = await runtime.QuietBoard.CreateIssueAsync(
            new IssueCreateRequest(
                home.Project.Id, "task", Title(pr), OutsidePullRequests.Describe(url, pr, association, home),
                null, null, null),
            ct);
        if (created is null) return;

        _filing[url] = (created.Key, Stage.Created, association);
        await FinishAsync(runtime, complain, url, pr, ct);
    }

    /// <summary>The writes after the issue exists, from whichever one has not been made.</summary>
    private async Task FinishAsync(
        Runtime runtime, Action<string> complain, string url, PullRequestAnswer pr, CancellationToken ct)
    {
        var (key, stage, association) = _filing[url];

        if (stage == Stage.Created)
        {
            var (origin, instance) = Origin(pr);
            (IssueDto? Issue, string? Conflict) put;
            try
            {
                put = await runtime.QuietBoard.ContributionAsync(
                    key,
                    new ContributionRequest(
                        url, pr.HeadBranch, pr.AuthorLogin, association, instance, origin?.Project, origin?.Key,
                        EnvelopeVersion: origin is null ? null : 0),
                    ct);
            }
            catch (HatchException e)
            {
                complain($"hatch: {key} - the board would not take the pull request's record - {e.Message}");
                return;
            }

            if (put.Conflict is { } conflict)
            {
                _filing.Remove(url);
                var reason = $"the board already holds it - {conflict}";
                if (_settled.Add($"{url}|{reason}"))
                    runtime.Say.Line(OutsidePullRequests.Words(url, pr.Number, reason));
                await runtime.QuietBoard.CommentAsync(key, $"Filed in error: {conflict}. Nothing was recorded on this issue.", ct);
                return;
            }

            _filing[url] = (key, Stage.Recorded, association);
        }

        IReadOnlyList<StatusDto> statuses;
        try
        {
            statuses = await runtime.QuietBoard.StatusesAsync(ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: {key} - could not read the columns to move it to review - {e.Message}");
            return;
        }

        if (Columns.AwaitingReview(statuses) is not { } review)
        {
            complain($"hatch: {key} - the board has no review column to put it in, so it stays where it was filed");
            return;
        }

        try
        {
            await runtime.QuietBoard.MoveAsync(key, review.Id, ct);
        }
        catch (HatchException e)
        {
            complain($"hatch: {key} - the board would not move it to {review.Name} - {e.Message}");
            return;
        }

        _filing.Remove(url);
        runtime.Say.Line($"hatch: {key} filed from pull request #{pr.Number} ({url}) -> {review.Name}");
    }

    /// <summary>The pull request's title as an issue's: at most what the board takes, and never empty.</summary>
    private static string Title(PullRequestAnswer pr)
    {
        var title = (pr.Title ?? "").Trim();
        if (title.Length == 0) return $"Pull request #{pr.Number}";
        return title.Length > 300 ? title[..300] : title;
    }

    /// <summary>Where the pull request says it came from, where its title and first line give a version-zero origin; nothing otherwise.</summary>
    private static ((string Project, string Key)? Origin, string? Instance) Origin(PullRequestAnswer pr)
    {
        if (!PullRequestEnvelopeReader.TryReadVersionZero(pr.Title ?? "", pr.Body ?? "", out var key, out var instance)
            || key is null) return (null, null);

        var dash = key.LastIndexOf('-');
        return dash <= 0 ? (null, null) : ((key[..dash], key), instance);
    }
}
