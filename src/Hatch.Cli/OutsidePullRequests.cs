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
    /// Which parents each mirrored type may hang under. A copy of <c>EfHatchIssue.LegalParentTypes</c> in
    /// <c>Hatch.Api</c>, which this is a port of - the CLI cannot reference that assembly, so keep the two in
    /// step by hand. Nothing here ever mirrors as <c>reflection</c> (see <see cref="FoldType"/>), but the row
    /// is kept anyway since this is meant as a faithful copy.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> LegalParents = new Dictionary<string, string[]>
    {
        ["epic"] = ["epic", "reflection"],
        ["reflection"] = ["epic"],
        ["story"] = ["epic", "reflection"],
        ["task"] = ["story", "bug", "epic", "reflection"],
        ["bug"] = ["epic", "story", "reflection"],
    };

    private static readonly HashSet<string> MirrorableTypes = ["epic", "story", "task", "bug"];

    /// <summary>
    /// The type a mirrored issue is created as: <c>reflection</c> folds to <c>epic</c>, since a reflection is
    /// its own board's cadence artefact and has no standing as an ancestor elsewhere; anything this board does
    /// not know folds to <c>task</c>, the type that hangs under the most other types.
    /// </summary>
    public static string FoldType(string? type) =>
        type == "reflection" ? "epic" : type is not null && MirrorableTypes.Contains(type) ? type : "task";

    /// <summary>"an epic", "a story" - read on screen by the person who approves the mirrored issue.</summary>
    private static string Article(string noun) =>
        noun.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(noun[0])) ? "an" : "a";

    /// <summary>
    /// The nearest of <paramref name="nearestFirst"/> (a mirrored issue's key and its actual, already-folded
    /// type) that <paramref name="type"/> may legally hang under, and the sentence a description says when
    /// that is not the nearest one at all. Null, null for an empty chain or one where nothing is legal.
    /// </summary>
    public static (string? ParentKey, string? Note) ResolveParent(string type, IReadOnlyList<(string Key, string Type)> nearestFirst)
    {
        if (nearestFirst.Count == 0) return (null, null);

        var legal = LegalParents.TryGetValue(type, out var set) ? set : [];
        foreach (var (key, parentType) in nearestFirst)
        {
            if (!legal.Contains(parentType)) continue;

            var natural = nearestFirst[0];
            return key == natural.Key
                ? (key, null)
                : (key, $"Hangs under {key} rather than {natural.Key}, because {Article(type)} {type} does not "
                    + $"hang under {Article(natural.Type)} {natural.Type} here.");
        }

        var tried = string.Join(" or ", nearestFirst.Select(a => $"{Article(a.Type)} {a.Type}"));
        return (null, $"Hangs under no mirrored ancestor: {Article(type)} {type} does not hang under {tried} here.");
    }

    /// <summary>The description a mirrored ancestor's issue is created with: where it came from, the fold's note where there is one, then the originating description exactly, marked where the envelope says it was cut short.</summary>
    public static string MirrorDescription(string instance, string key, string? description, bool truncated, string? foldNote)
    {
        var header = $"Mirrored from {instance} {key}.";
        if (foldNote is not null) header += " " + foldNote;

        var body = description ?? "";
        if (truncated)
            body += (body.Length > 0 ? "\n\n" : "") + "_The originating description was cut short in the envelope._";

        return body.Length > 0 ? $"{header}\n\n{body}" : header;
    }

    /// <summary>The longest a mirrored issue's title is taken as, matching <c>EfHatchIssue.MaxTitleLength</c> - the CLI cannot reference it either.</summary>
    private const int MaxTitleLength = 300;

    public static string CapTitle(string title) => title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;

    /// <summary>What reading a pull request's body for a chain of ancestors, or the pre-envelope version-zero shape, settled on.</summary>
    public enum EnvelopeStatus
    {
        /// <summary>Neither an envelope nor the version-zero shape - an ordinary pull request's own.</summary>
        None,

        /// <summary>The pre-envelope shape: a title and a first line naming the issue it came from, and nothing else.</summary>
        VersionZero,

        /// <summary>An envelope, read in full.</summary>
        Read,

        /// <summary>An envelope was there, by its markers, but could not be read - <see cref="Note"/> says why.</summary>
        Unreadable,
    }

    /// <summary>
    /// What a pull request's body says about where it came from - see <see cref="ReadEnvelope"/>.
    /// </summary>
    /// <param name="Instance">The Hatch it was mirrored from, without a trailing slash. Set on every status but <see cref="EnvelopeStatus.None"/> and <see cref="EnvelopeStatus.Unreadable"/>.</param>
    /// <param name="OriginProjectKey">The project key of the pull request's own ticket on <paramref name="Instance"/>.</param>
    /// <param name="OriginKey">The pull request's own ticket's key on <paramref name="Instance"/>.</param>
    /// <param name="Version">The envelope's version, or 0 for the version-zero shape.</param>
    /// <param name="Note">Why an envelope could not be read, cut to what the contribution record takes. Null everywhere else.</param>
    /// <param name="OwnType">The folded type to create the pull request's own ticket as - only on <see cref="EnvelopeStatus.Read"/>.</param>
    /// <param name="OwnTitle">The title the envelope gives the pull request's own ticket - only on <see cref="EnvelopeStatus.Read"/>, and only when the chain says one.</param>
    /// <param name="Ancestors">The chain above the pull request's own ticket, root first - empty except on <see cref="EnvelopeStatus.Read"/>.</param>
    public sealed record EnvelopeOutcome(
        EnvelopeStatus Status, string? Instance, string? OriginProjectKey, string? OriginKey, int? Version,
        string? Note, string? OwnType, string? OwnTitle, IReadOnlyList<PullRequestEnvelopeIssue> Ancestors)
    {
        public static readonly EnvelopeOutcome None = new(EnvelopeStatus.None, null, null, null, null, null, null, null, []);
    }

    /// <summary>
    /// Where a pull request says it came from: an envelope first, by its markers, read with
    /// <see cref="PullRequestEnvelopeReader.TryRead"/>; else the pre-envelope version-zero shape
    /// (<see cref="PullRequestEnvelopeReader.TryReadVersionZero"/>); else nothing. An envelope the reader
    /// cannot make sense of, or whose own key this board could never record (no instance, or a key longer
    /// than <see cref="ContributionRequest.MaxOriginKeyLength"/> anywhere in the chain), reads as
    /// <see cref="EnvelopeStatus.Unreadable"/> rather than throwing - nothing here is worth failing a filing
    /// session over.
    /// </summary>
    public static EnvelopeOutcome ReadEnvelope(PullRequestAnswer pr)
    {
        var body = pr.Body ?? "";
        var hasMarker = body.Contains(PullRequestEnvelopeMarkers.Start, StringComparison.Ordinal)
            || body.Contains(PullRequestEnvelopeMarkers.End, StringComparison.Ordinal);

        if (!hasMarker)
        {
            if (PullRequestEnvelopeReader.TryReadVersionZero(pr.Title ?? "", body, out var versionZeroKey, out var versionZeroInstance)
                && versionZeroKey is { Length: > 0 } key && key.LastIndexOf('-') is > 0 and var dash)
                return new EnvelopeOutcome(
                    EnvelopeStatus.VersionZero, versionZeroInstance, key[..dash], key, 0, null, null, null, []);

            return EnvelopeOutcome.None;
        }

        if (!PullRequestEnvelopeReader.TryRead(body, out var envelope, out var reason) || envelope is null)
            return Unreadable(reason ?? "the envelope could not be read");

        if (envelope.Instance is not { Length: > 0 } rawInstance)
            return Unreadable("the envelope named no instance");

        if (envelope.Chain.Count == 0)
            return Unreadable("the envelope's chain is empty");

        if (envelope.Chain.Any(issue => issue.Key.Length > ContributionRequest.MaxOriginKeyLength))
            return Unreadable($"a key in the envelope's chain is longer than {ContributionRequest.MaxOriginKeyLength} characters");

        var own = envelope.Chain[0];
        var ownDash = own.Key.LastIndexOf('-');
        if (ownDash <= 0) return Unreadable($"\"{own.Key}\" is not an issue key");

        var instance = rawInstance.TrimEnd('/');
        var ancestors = envelope.Chain.Skip(1).Reverse().ToList();

        return new EnvelopeOutcome(
            EnvelopeStatus.Read, instance, own.Key[..ownDash], own.Key, envelope.Version, null,
            FoldType(own.Type), own.Title, ancestors);
    }

    private static EnvelopeOutcome Unreadable(string reason) =>
        new(EnvelopeStatus.Unreadable, null, null, null, null, Cap(reason, ContributionRequest.MaxEnvelopeNoteLength),
            null, null, []);

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>The project key before an issue key's last dash, or null where there is none to take.</summary>
    public static string? ProjectOf(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash <= 0 ? null : key[..dash];
    }

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
/// record: the issue, then its contribution record (which is also what <c>GET /api/issues?pullRequestUrl=</c>
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
    /// A mirrored ancestor created but not yet fully recorded and moved, keyed <c>instance|originKey</c> -
    /// consulted before the find-by-origin read, so a tick that is retrying never creates a second issue for
    /// one it already made. An entry is dropped the moment its move to review succeeds; a found ancestor
    /// (one some earlier tick, or another instance entirely, already finished) is never added at all, since
    /// nothing is left to resume on it.
    /// </summary>
    private readonly Dictionary<string, (string Key, string Type, Stage Stage)> _mirrors = new(StringComparer.Ordinal);

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
        var outcome = OutsidePullRequests.ReadEnvelope(pr);

        var type = "task";
        var title = Title(pr);
        string? parentKey = null;
        var description = OutsidePullRequests.Describe(url, pr, association, home);

        if (outcome.Status == OutsidePullRequests.EnvelopeStatus.Read)
        {
            IReadOnlyList<StatusDto> statuses;
            try
            {
                statuses = await runtime.QuietBoard.StatusesAsync(ct);
            }
            catch (HatchException e)
            {
                complain($"hatch: could not read the columns to mirror the chain {url} carries - {e.Message}");
                return;
            }

            // Root first, so each ancestor's parent is already resolved by the time it needs one.
            var nearestFirst = new List<(string Key, string Type)>();
            foreach (var ancestor in outcome.Ancestors)
            {
                var mirrored = await MirrorOneAsync(
                    runtime, home, outcome.Instance!, ancestor, pr, association, nearestFirst, statuses, ct);
                nearestFirst.Insert(0, mirrored);
            }

            type = outcome.OwnType ?? "task";
            title = outcome.OwnTitle is { Length: > 0 } ownTitle ? OutsidePullRequests.CapTitle(ownTitle) : Title(pr);

            var (resolved, note) = OutsidePullRequests.ResolveParent(type, nearestFirst);
            parentKey = resolved;
            if (note is not null) description += "\n\n" + note;
        }

        var created = await runtime.QuietBoard.CreateIssueAsync(
            new IssueCreateRequest(home.Project.Id, type, title, description, parentKey, null, null), ct);
        if (created is null) return;

        _filing[url] = (created.Key, Stage.Created, association);
        await FinishAsync(runtime, complain, url, pr, ct);
    }

    /// <summary>
    /// One ancestor of the chain a pull request's envelope carries: found as it already stands, or created,
    /// recorded and moved into review as a new issue. Resumable the same way the pull request's own issue
    /// is - see <see cref="_mirrors"/> - and anything that goes wrong here is a <see cref="HatchException"/>
    /// that aborts this pull request for the tick, to be picked up again, rather than a <c>complain</c> that
    /// would let a later ancestor or the pull request's own issue hang under one left unfinished.
    /// </summary>
    private async Task<(string Key, string Type)> MirrorOneAsync(
        Runtime runtime, OutsidePullRequests.Home home, string instance, PullRequestEnvelopeIssue ancestor,
        PullRequestAnswer pr, string association, IReadOnlyList<(string Key, string Type)> nearestFirstSoFar,
        IReadOnlyList<StatusDto> statuses, CancellationToken ct)
    {
        var cacheKey = $"{instance}|{ancestor.Key}";

        if (!_mirrors.TryGetValue(cacheKey, out var state))
        {
            var found = await runtime.QuietBoard.OriginRecordedAsync(home.Project.Id, instance, ancestor.Key, ct);
            if (found.Count > 0) return (found[0].Key, found[0].Type);

            var type = OutsidePullRequests.FoldType(ancestor.Type);
            var (parentKey, note) = OutsidePullRequests.ResolveParent(type, nearestFirstSoFar);
            var title = OutsidePullRequests.CapTitle(ancestor.Title);
            var description = OutsidePullRequests.MirrorDescription(instance, ancestor.Key, ancestor.Description, ancestor.Truncated, note);

            var created = await runtime.QuietBoard.CreateIssueAsync(
                new IssueCreateRequest(home.Project.Id, type, title, description, parentKey, null, null), ct);
            if (created is null) throw new HatchException($"the board would not create a mirrored issue for {ancestor.Key}");

            state = (created.Key, type, Stage.Created);
            _mirrors[cacheKey] = state;
        }

        if (state.Stage == Stage.Created)
        {
            await runtime.QuietBoard.ContributionAsync(
                state.Key,
                new ContributionRequest(
                    null, null, pr.AuthorLogin, association, instance, OutsidePullRequests.ProjectOf(ancestor.Key), ancestor.Key),
                ct);
            // A 409 here means another runner's tick already recorded this ancestor - walked on, not retried.

            state = (state.Key, state.Type, Stage.Recorded);
            _mirrors[cacheKey] = state;
        }

        if (Columns.AwaitingReview(statuses) is not { } review)
            throw new HatchException($"the board has no review column to put the mirrored {state.Key} in");

        await runtime.QuietBoard.MoveAsync(state.Key, review.Id, ct);
        runtime.Say.Line($"hatch: {state.Key} mirrored from {instance} {ancestor.Key} -> {review.Name}");

        _mirrors.Remove(cacheKey);
        return (state.Key, state.Type);
    }

    /// <summary>The writes after the issue exists, from whichever one has not been made.</summary>
    private async Task FinishAsync(
        Runtime runtime, Action<string> complain, string url, PullRequestAnswer pr, CancellationToken ct)
    {
        var (key, stage, association) = _filing[url];

        if (stage == Stage.Created)
        {
            var outcome = OutsidePullRequests.ReadEnvelope(pr);
            (IssueDto? Issue, string? Conflict) put;
            try
            {
                put = await runtime.QuietBoard.ContributionAsync(
                    key,
                    new ContributionRequest(
                        url, pr.HeadBranch, pr.AuthorLogin, association,
                        outcome.Instance, outcome.OriginProjectKey, outcome.OriginKey,
                        EnvelopeVersion: outcome.Version, EnvelopeNote: outcome.Note),
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
        return OutsidePullRequests.CapTitle(title);
    }
}
