using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// "What should an agent do next, and how?" - answered in one request, and
/// "what would a whole pass do, and what would it skip?" in another.
///
/// Every part of that answer is decided here rather than in the shell, for the
/// reason the rank is decided on the server (docs/hatch.md, "Rank
/// computation"): it keeps every client dumb. Which issue is next, which column
/// it is headed for, whether it may go there at all, and which playbook speaks
/// for the move are all questions about rows this process owns, and a script
/// that re-derived them would drift the first time a column was renamed.
/// </summary>
[ApiController]
[Route("api/hatch/work")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class WorkController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, TimeProvider time,
    IOptions<AppsOptions> apps) : ControllerBase
{
    /// <summary>
    /// The issue an unattended run should pick up: the top of the rightmost
    /// column that still has work an agent may do.
    ///
    /// Rightmost first, and that is a scheduling policy worth saying out loud.
    /// A board worked left to right starts everything and finishes nothing; one
    /// worked right to left pushes whatever is furthest along over the line
    /// before it opens anything new. The second is what a person does when they
    /// mean to ship.
    /// </summary>
    /// <param name="offsetMinutes">
    /// The caller's offset from UTC, so a ready date is read against the
    /// caller's calendar day and not the server's - the same rule the board
    /// follows (schedule.ts). Absent is UTC, which is right for a machine and
    /// close enough for anybody who does not say.
    /// </param>
    /// <param name="ancestorKey">
    /// One corner of the board instead of all of it: the same question asked
    /// only of the issues below this key, at any depth, so an evening can be
    /// pointed at one epic. Absent is the whole board, which is what every
    /// caller before this asked for.
    ///
    /// <para>Nothing else changes - still right to left, still top of the
    /// column down, still folding past a ready date, an open question, a
    /// terminal column, a missing playbook, an unfinished dependency, or a branch that
    /// merges cleanly. The
    /// scope narrows the candidates and decides nothing about them.</para>
    ///
    /// <para>The issue itself is not a candidate. "Under AER-1" is a question
    /// about what hangs beneath it, which is how <c>ancestorKey</c> already
    /// reads on the search endpoint.</para>
    /// </param>
    /// <param name="heldToken">
    /// A claim token the caller already holds, so an issue it is itself working
    /// is not folded past on that account. Re-reading the dispatch for a ticket
    /// one already holds is not a conflict. Absent is a caller holding nothing,
    /// which is every caller before the claim existed.
    /// </param>
    /// <param name="remote">
    /// One of the runner's own checkouts, spelled exactly as it has it - raw,
    /// never canonicalised by the caller. Repeatable: a runner may hold more
    /// than one remote. Declaring at least one of <paramref name="remote"/>,
    /// <paramref name="standing"/> or <paramref name="clones"/> opts a caller
    /// into the repository fold; a caller that declares none of the three is
    /// undeclared, and nothing is folded on that account - see
    /// <see cref="RepositoryDeclaration"/>.
    /// </param>
    /// <param name="standing">
    /// The runner has a checkout it was started in, independent of anything
    /// named in <paramref name="remote"/> - what an unbound project is worked
    /// from.
    /// </param>
    /// <param name="clones">
    /// The runner will clone whatever it lacks, so a project bound to
    /// repositories none of which match <paramref name="remote"/> is not
    /// folded on that account either.
    /// </param>
    [HttpGet("next")]
    public async Task<ActionResult<WorkDto>> GetNextWork(
        [FromQuery] int offsetMinutes = 0,
        [FromQuery] string? ancestorKey = null,
        [FromQuery] Guid? heldToken = null,
        [FromQuery] List<string>? remote = null,
        [FromQuery] bool? standing = null,
        [FromQuery] bool? clones = null,
        CancellationToken ct = default)
    {
        var repos = RepositoryDeclaration.From(remote, standing, clones);
        var scan = await ScanAsync(offsetMinutes, ancestorKey, heldToken, repos, ct);
        if (scan.Failure is not null) return BadRequest(scan.Failure);

        // The first clear row of the queue, and nothing else. Not a second
        // walk that happens to agree with the scan's - the whole point of
        // publishing the scan is that it explains what this line picked, and
        // two loops that could disagree about the order of the board is
        // precisely the bug it exists to expose.
        var clear = scan.Rows.FirstOrDefault(r => r.Blocked is null);
        if (clear is null) return NoContent();

        return await ResolveAsync(clear.Issue, scan.Statuses, scan.Loop, scan.Gate, scan.Claims, scan.Repos, ct);
    }

    /// <summary>
    /// What a whole pass would do, rather than what its first step is: every
    /// issue the dispatcher considers, in the order it considers them, each
    /// carrying the sentence saying why it cannot be advanced - or nothing,
    /// where it can.
    ///
    /// <para><c>next</c> folds past everything blocked in silence, and for one
    /// increment that is right: "nothing to do" is the useful answer and a list
    /// of reasons is noise. For an unattended loop it is backwards. Nobody is
    /// watching, and the one thing worth having afterwards is what the pass
    /// skipped and why - above all a column and type nobody has written a
    /// playbook for, which reads as a finished board and is not one.</para>
    ///
    /// <para>The first entry with no reason is what <see cref="GetNextWork"/>
    /// returns for the same arguments, because it is the same walk. The
    /// arguments mean exactly what they mean there.</para>
    ///
    /// <para>The rows arrive in the dispatcher's order: every expedited
    /// candidate first, whatever column each sits in, and then everything else.
    /// Inside each half it is the rightmost column first and the board's own
    /// <c>(Rank, Id)</c> within a column - the same tuple
    /// <see cref="BoardController"/> serves. So a card's position in the queue
    /// is its position in its column on the board, and nothing between the two
    /// re-sorts.</para>
    /// </summary>
    /// <remarks>
    /// The columns with nowhere to go - a terminal one, and a rightmost one
    /// that is not terminal - are absent rather than listed as blocked. An
    /// issue the dispatcher never reaches is not something the pass skipped,
    /// and shipped work is not a backlog. The review column is listed, because
    /// it has somewhere to go - itself - and every issue in it is either a
    /// conflict to resolve, a failing build to fix, or a row saying why it is
    /// neither.
    /// </remarks>
    [HttpGet("queue")]
    public async Task<ActionResult<IReadOnlyList<QueueEntryDto>>> GetQueue(
        [FromQuery] int offsetMinutes = 0,
        [FromQuery] string? ancestorKey = null,
        [FromQuery] List<string>? remote = null,
        [FromQuery] bool? standing = null,
        [FromQuery] bool? clones = null,
        CancellationToken ct = default)
    {
        // No heldToken here, and deliberately: the queue is a report on what a
        // pass would do, not a pass, and a caller reading it holds nothing.
        var repos = RepositoryDeclaration.From(remote, standing, clones);
        var scan = await ScanAsync(offsetMinutes, ancestorKey, null, repos, ct);
        if (scan.Failure is not null) return BadRequest(scan.Failure);

        // One projection for the whole list. The per-issue one would be three
        // queries a row, which is what makes a scan of a board a thing nobody
        // runs twice.
        var issues = await IssueProjection.ToDtosAsync(
            db, actors, scan.Rows.Select(r => r.Issue).ToList(), claims, scan.Claims.Now, ct);

        return scan.Rows.Select(r => new QueueEntryDto(
            issues[r.Issue.Id],
            ToStatusDto(r.From),
            r.To is null ? null : ToStatusDto(r.To),
            r.Blocked,
            r.Kind)).ToList();
    }

    /// <summary>
    /// Every issue in the review column that the caller holds a checkout of, and
    /// what the board holds about each one's branch - what a runner's poll asks
    /// before it asks git anything.
    /// </summary>
    /// <remarks>
    /// <para>It is not the queue. A verdict is a fact about a branch and not
    /// work, so nothing narrows it: not <c>under</c>, not a claim, an open
    /// question, a ready date or an assignee. An issue another runner is fixing
    /// still has a branch, and a runner that only polled the issues it could
    /// work would leave the board's verdict on the rest to go stale.</para>
    ///
    /// <para>What it does share with the queue is the repository rule, without
    /// the clone allowance: a poll clones nothing, so a repository this runner
    /// has never cloned is not one it can check, and the queue's <em>no runner
    /// has checked</em> is the honest answer for it. A caller that declares
    /// nothing holds no checkout at all, and is answered with nothing.</para>
    /// </remarks>
    [HttpGet("review")]
    public async Task<ActionResult<IReadOnlyList<ReviewCheckDto>>> GetReview(
        [FromQuery] List<string>? remote = null,
        [FromQuery] bool? standing = null,
        CancellationToken ct = default)
    {
        var repos = RepositoryDeclaration.From(remote, standing, null);
        if (!repos.IsDeclared) return new List<ReviewCheckDto>();

        var statuses = await OrderedStatusesAsync(ct);
        if (Columns.AwaitingReview(statuses) is not { } review) return new List<ReviewCheckDto>();

        var inReview = await db.Issues
            .Where(i => i.StatusId == review.Id)
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Include(i => i.Project).ThenInclude(p => p!.Repositories)
            .ToListAsync(ct);

        var held = inReview.Where(i => HoldsCheckout(i, repos)).ToList();
        var verdicts = await MergeChecksAsync(held.Select(i => i.Id).ToList(), ct);
        var builds = await BuildChecksAsync(held.Select(i => i.Id).ToList(), ct);

        return held.Select(i => new ReviewCheckDto(
            IssueKey.Format(i.Project!.Key, i.Number),
            i.Project.Repositories
                .OrderBy(r => r.SortOrder)
                .Select((r, at) => new WorkRepositoryDto(r.Remote, r.Canonical, r.BaseBranch, at == 0, repos.Match(r.Canonical)))
                .ToList(),
            (verdicts.TryGetValue(i.Id, out var found) ? found : []).Select(IssueMergeChecks.Project).ToList(),
            (builds.TryGetValue(i.Id, out var built) ? built : []).Select(IssueBuildChecks.Project).ToList()))
            .ToList();
    }

    /// <summary>
    /// <see cref="RepositoryFold"/>'s rule without the clone allowance: the
    /// caller has a checkout the project's repositories match, or the project
    /// binds nothing and the caller has a standing one.
    /// </summary>
    private static bool HoldsCheckout(EfHatchIssue issue, RepositoryDeclaration repos)
    {
        var bound = issue.Project!.Repositories;
        return bound.Count == 0 ? repos.Standing : bound.Any(r => repos.Match(r.Canonical) is not null);
    }

    // ---- The walk ----

    /// <summary>
    /// The one pass over the board that both work endpoints are built on: the
    /// expedited candidates right to left and top of the column down, then
    /// everything else the same way, every issue judged once.
    /// </summary>
    /// <remarks>
    /// Everything a row is judged against is read once here rather than once
    /// per row - the statuses, the scope, which dependencies are unmet, how
    /// many questions each issue is waiting on, and the whole playbook matrix.
    /// A board's worth of rows against a handful of queries, because a scan that
    /// cost a query a row would be a scan nobody leaves running.
    /// </remarks>
    private async Task<Scan> ScanAsync(
        int offsetMinutes, string? ancestorKey, Guid? heldToken, RepositoryDeclaration repos,
        CancellationToken ct)
    {
        var statuses = await OrderedStatusesAsync(ct);

        // The scope, resolved once before the columns are walked. Null is the
        // whole board; a list is the subtree, and an empty one is a childless
        // issue, which is honestly "nothing under it" and falls out as an empty
        // queue and a 204.
        List<long>? scope = null;
        if (!string.IsNullOrWhiteSpace(ancestorKey))
        {
            if (!IssueKey.TryParse(ancestorKey, out var ancestorProject, out var ancestorNumber))
                return Scan.Refused($"there is no {ancestorKey}");

            var ancestorId = await db.Issues.WithKey(ancestorProject, ancestorNumber)
                .Select(i => (long?)i.Id).FirstOrDefaultAsync(ct);
            if (ancestorId is null) return Scan.Refused($"there is no {ancestorKey}");

            // The walk in Rollup, not a second one written here - a scope and a
            // meter that disagreed about what is under an epic would be a bug
            // nobody notices until the two are on the same screen.
            scope = await Rollup.DescendantIdsAsync(db, ancestorId.Value, ct);
        }

        var now = time.GetUtcNow();
        var loop = new LoopScope(DayNumber(now, offsetMinutes), offsetMinutes);

        // One instant for the whole pass. A scan in which the clock moved
        // between two rows could fold one card and not its neighbour for a
        // reason nobody could reconstruct afterwards.
        var claimed = new ClaimGate(claims, now, heldToken, await claims.LineageAsync(db, now, ct));

        var gate = await DependencyGate.ForAsync(db, statuses, ct);
        var open = await Questions.OpenCountsAsync(db, ct);
        var playbooks = await db.Playbooks.AsNoTracking()
            .Include(p => p.FromStatus)
            .Include(p => p.ToStatus)
            .ToListAsync(ct);

        // The columns a pass looks in at all: the ones with a column to go to -
        // the next one to their right, or, for the review column, itself. No
        // type filter - which types a move applies to is the
        // playbook's to say, and a row no playbook covers is folded with the
        // sentence naming that rather than dropped before it is judged.
        var walkable = statuses.Where(s => Columns.Target(statuses, s) is not null).Select(s => s.Id).ToList();

        var query = db.Issues.Where(i => walkable.Contains(i.StatusId));
        if (scope is not null) query = query.Where(i => scope.Contains(i.Id));

        var candidates = await query
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Include(i => i.Project).ThenInclude(p => p!.Repositories)
            .ToListAsync(ct);

        var byColumn = candidates.GroupBy(i => i.StatusId).ToDictionary(g => g.Key, g => g.ToList());

        // What runners have found about the branches of the issues in review,
        // read once for the pass and grouped, the way `open` is. Only that
        // column is asked about: it is the only move a verdict gates.
        var review = Columns.AwaitingReview(statuses);
        var verdicts = review is not null && byColumn.TryGetValue(review.Id, out var inReview)
            ? await MergeChecksAsync(inReview.Select(i => i.Id).ToList(), ct)
            : [];
        var builds = review is not null && byColumn.TryGetValue(review.Id, out var inReviewToo)
            ? await BuildChecksAsync(inReviewToo.Select(i => i.Id).ToList(), ct)
            : [];

        // Blocked is static and has no db, so the assignees are resolved here
        // and handed in, the way `open` and `gate` already are. One memoized
        // read for the whole pass, whatever the board holds.
        var assignees = new Dictionary<long, AssigneeDto?>();
        foreach (var issue in candidates)
            assignees[issue.Id] = await IssueProjection.ToAssigneeAsync(
                actors, issue.AssigneePersonId, issue.AssigneeApiKeyId, ct);

        var implementation = Columns.Implementation(statuses);

        // The whole walk, twice: every expedited candidate right to left, and
        // then everything else right to left. So an expedited bug in the
        // leftmost column is listed above a non-expedited story in the
        // rightmost one, while inside each half the order is the board's own -
        // rightmost column first, and (Rank, Id) within a column.
        //
        // Two passes over the same columns rather than a sort of the finished
        // rows, because the published scan is the explanation of what `next`
        // picked: a comparator applied afterwards would be a second opinion
        // about the order, and two loops that could disagree is precisely the
        // bug this endpoint exists to expose.
        //
        // Expedite reorders and gates nothing. Every row is judged by the same
        // Blocked below whichever pass reaches it, so an expedited issue that
        // is blocked is folded with exactly the sentence it is folded with
        // today - it is simply folded sooner.
        var rows = new List<ScanRow>();
        foreach (var expedited in new[] { true, false })
        {
            foreach (var status in Enumerable.Reverse(statuses))
            {
                if (Columns.Target(statuses, status) is not { } to) continue;
                if (!byColumn.TryGetValue(status.Id, out var column)) continue;

                foreach (var issue in column)
                {
                    if (issue.Expedited != expedited) continue;

                    var playbook = Match(playbooks, status.Id, to.Id, issue.Type);
                    open.TryGetValue(issue.Id, out var waiting);
                    var merged = verdicts.TryGetValue(issue.Id, out var found) ? found : [];
                    var built = builds.TryGetValue(issue.Id, out var foundBuilds) ? foundBuilds : [];
                    rows.Add(new ScanRow(
                        issue, status, to,
                        Blocked(
                            issue, status, to, playbook, waiting, loop, gate, claimed, implementation,
                            assignees[issue.Id], repos, merged, built),
                        KindOf(issue, status, to, merged, built)));
                }
            }
        }

        return new Scan(statuses, rows, loop, gate, claimed, repos, null);
    }

    /// <summary>One issue the pass looked at, and what it decided.</summary>
    private sealed record ScanRow(EfHatchIssue Issue, EfHatchStatus From, EfHatchStatus? To, string? Blocked, string Kind);

    /// <summary>
    /// A finished pass, or the argument it would not accept. A refusal carries
    /// the sentence and nothing else; both endpoints turn it into the same 400.
    /// </summary>
    private sealed record Scan(
        List<EfHatchStatus> Statuses, List<ScanRow> Rows, LoopScope? Loop, DependencyGate Gate,
        ClaimGate Claims, RepositoryDeclaration Repos, string? Failure)
    {
        public static Scan Refused(string why) =>
            new([], [], null, DependencyGate.None, ClaimGate.None, RepositoryDeclaration.Undeclared, why);
    }

    /// <summary>
    /// The claims on the board, judged against one instant - and the one token
    /// whose own claim does not count as somebody else's.
    /// </summary>
    /// <remarks>
    /// Built once per pass and once per named dispatch. An issue's own claim
    /// comes out of the rows the scan already materialised: <c>ScanAsync</c>
    /// loads whole <see cref="EfHatchIssue"/> entities, so the seven claim
    /// columns arrive for free. A relative's claim is the
    /// <see cref="ClaimLineage"/>, two queries a pass rather than two a row -
    /// the tree and the live claims, read against the same instant.
    ///
    /// <para>The issue's own claim is named first. The relative's is named only
    /// where the issue has none live, so a fold that printed one sentence
    /// yesterday prints the same one today.</para>
    /// </remarks>
    private sealed record ClaimGate(IssueClaims? Claims, DateTimeOffset Now, Guid? HeldToken, ClaimLineage Lineage)
    {
        /// <summary>A gate that folds nothing, for a refused scan - so <c>Scan.Claims</c> is never null.</summary>
        public static readonly ClaimGate None = new(null, default, null, ClaimLineage.Empty);

        /// <summary>
        /// The sentence naming who is working this right now, or null - which
        /// is a dead claim, no claim at all, or a live one this caller holds
        /// itself. Failing all of that, who is working a line of the tree
        /// through it: an ancestor, or a descendant, the caller does not hold.
        /// </summary>
        public string? Held(EfHatchIssue issue)
        {
            if (Claims is null) return null;

            var claim = ClaimSnapshot.Of(issue);
            if (Claims.IsLive(claim, Now))
                return claim.Token == HeldToken ? null : Claims.Sentence(claim, Now);

            return Lineage.Holder(issue.Id, HeldToken);
        }
    }

    /// <summary>
    /// What the loop is asking of the board this pass: which day it is in the
    /// caller's zone. Absent when somebody named a ticket by hand - see
    /// <see cref="Blocked"/>.
    /// </summary>
    private sealed record LoopScope(long Today, int OffsetMinutes);

    // ---- Which checkout the runner has ----

    /// <summary>
    /// What a caller told the dispatcher about its own checkouts, or nothing
    /// at all. <see cref="Undeclared"/> is the sentinel that keeps "declared
    /// nothing" distinct from "declared, and happens to have none of the
    /// three set" - a request carrying none of <c>remote</c>, <c>standing</c>
    /// or <c>clones</c> opts out of the fold entirely, which is what keeps an
    /// older CLI and the issue page working unchanged.
    /// </summary>
    private sealed record RepositoryDeclaration(IReadOnlyDictionary<string, string>? ByCanonical, bool Standing, bool Clones)
    {
        public static readonly RepositoryDeclaration Undeclared = new(null, false, false);

        public bool IsDeclared => ByCanonical is not null;

        public static RepositoryDeclaration From(IReadOnlyList<string>? remotes, bool? standing, bool? clones)
        {
            if ((remotes is null || remotes.Count == 0) && standing is null && clones is null)
                return Undeclared;

            var byCanonical = new Dictionary<string, string>();
            foreach (var raw in remotes ?? [])
            {
                var (canonical, _) = RemoteIdentity.Canonical(raw);
                if (canonical is not null) byCanonical.TryAdd(canonical, raw);
            }

            return new RepositoryDeclaration(byCanonical, standing ?? false, clones ?? false);
        }

        /// <summary>The declared remote, spelled as the caller sent it, that canonicalises to this - or null.</summary>
        public string? Match(string canonical) =>
            ByCanonical is not null && ByCanonical.TryGetValue(canonical, out var raw) ? raw : null;
    }

    // ---- The loop's own policy ----
    //
    // One rule that is not a fact about an issue but a decision about what an
    // unattended run may start: the ready date. It is asked in Blocked like
    // every other fold - a scan that could not name it would be a scan with
    // holes in it - but only when a LoopScope is present, which is to say only
    // when the pass is asking. A person who names a ticket is giving an
    // instruction; housekeeping does not overrule it.

    // ---- Dependencies ----

    /// <summary>
    /// Every unmet dependency on the board, read once, and the question "what
    /// stands in this issue's way" answered against it.
    ///
    /// <para>Unmet means the issue waited on is not in a terminal column.
    /// Merged, not merely up for review - anything softer and the second story
    /// starts on top of the first one's unmerged branch, which is the failure
    /// the whole feature exists to prevent.</para>
    /// </summary>
    /// <remarks>
    /// One gate serves the whole pass and another serves a single named issue,
    /// and they cannot come to disagree about what "unmet" means because there
    /// is one definition and both call it. Two board-wide reads for a single
    /// dispatch is the deliberate trade: the table is small, and a scan that
    /// could disagree with a named dispatch is precisely the bug the queue
    /// endpoint exists to expose.
    /// </remarks>
    private sealed class DependencyGate
    {
        /// <summary>A gate that blocks nothing, for a refused scan - so <c>Scan.Gate</c> is never null and no call site needs a <c>!</c>.</summary>
        public static readonly DependencyGate None = new([], []);

        private readonly Dictionary<long, List<string>> _unmetByIssue;
        private readonly Dictionary<long, (long? ParentId, string Key)> _tree;

        private DependencyGate(
            Dictionary<long, List<string>> unmetByIssue, Dictionary<long, (long?, string)> tree)
        {
            _unmetByIssue = unmetByIssue;
            _tree = tree;
        }

        public static async Task<DependencyGate> ForAsync(
            HatchContext db, List<EfHatchStatus> statuses, CancellationToken ct)
        {
            var terminal = statuses.Where(s => s.IsTerminal).Select(s => s.Id).ToList();

            // The unmet edges, and nothing else. Ordered by the blocker's key
            // so the sentence a fold prints is stable between two passes over
            // an unchanged board.
            var unmet = await db.Dependencies.AsNoTracking()
                .Where(d => !terminal.Contains(d.DependsOn!.StatusId))
                .OrderBy(d => d.DependsOn!.Project!.Key).ThenBy(d => d.DependsOn!.Number)
                .Select(d => new { d.IssueId, ProjectKey = d.DependsOn!.Project!.Key, d.DependsOn!.Number })
                .ToListAsync(ct);

            var tree = await db.Issues.AsNoTracking()
                .Select(i => new { i.Id, i.ParentId, ProjectKey = i.Project!.Key, i.Number })
                .ToListAsync(ct);

            return new DependencyGate(
                unmet.GroupBy(r => r.IssueId).ToDictionary(
                    g => g.Key,
                    g => g.Select(r => IssueKey.Format(r.ProjectKey, r.Number)).ToList()),
                tree.ToDictionary(
                    r => r.Id,
                    r => ((long?)r.ParentId, IssueKey.Format(r.ProjectKey, r.Number))));
        }

        /// <summary>
        /// The unmet edges standing in this issue's way: its own, or - where it
        /// has none - the nearest ancestor's that does. Empty when nothing is
        /// in the way.
        /// </summary>
        /// <remarks>
        /// The nearest holder, and only it. Naming every holder in a tree would
        /// be a paragraph where a fold gets a sentence, and it would still be a
        /// sentence about the nearest one afterwards: clearing that holder is
        /// what the reader has to do next, and the pass after it says what is
        /// behind it. One walk rather than the rule written twice, which is
        /// also how an ancestor's dependency reaches everything below it.
        /// </remarks>
        public IReadOnlyList<UnmetEdge> Unmet(long issueId)
        {
            var seen = new HashSet<long>();
            long? at = issueId;

            while (at is { } id && seen.Add(id))
            {
                if (_unmetByIssue.TryGetValue(id, out var blockers))
                {
                    var holder = id == issueId ? null : _tree.TryGetValue(id, out var row) ? row.Key : null;
                    return blockers.Select(b => new UnmetEdge(b, holder)).ToList();
                }

                at = _tree.TryGetValue(id, out var found) ? found.ParentId : null;
            }

            return [];
        }
    }

    /// <summary>
    /// One thing being waited on. <see cref="HolderKey"/> is null when the edge
    /// is the issue's own, and the ancestor's key when it is not.
    /// </summary>
    private sealed record UnmetEdge(string BlockerKey, string? HolderKey);

    /// <summary>
    /// Why an unattended run - or anybody - should not start writing this yet:
    /// the issues it waits on that are not done.
    /// </summary>
    private static string WaitingOn(IReadOnlyList<UnmetEdge> edges)
    {
        var keys = edges.Select(e => e.BlockerKey).ToList();
        var list = keys.Count == 1
            ? keys[0]
            : $"{string.Join(", ", keys.Take(keys.Count - 1))} and {keys[^1]}";

        var subject = edges[0].HolderKey is { } holder ? $"{holder} above this" : "this";

        return keys.Count == 1
            ? $"{list} is not done, and {subject} cannot be implemented until it is"
            : $"{list} are not done, and {subject} cannot be implemented until they are";
    }

    /// <summary>
    /// Why an unattended run - or anybody - should not start writing this yet:
    /// the project it is under is bound to a checkout this runner does not
    /// have. Null when the caller declared nothing at all (see
    /// <see cref="RepositoryDeclaration"/>), when the project binds nothing
    /// and the caller has a standing checkout, when one of the project's
    /// repositories matches a remote the caller declared, or when the caller
    /// says it will clone what it lacks.
    /// </summary>
    private static string? RepositoryFold(EfHatchIssue issue, RepositoryDeclaration repos)
    {
        if (!repos.IsDeclared) return null;

        var bound = issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList();

        if (bound.Count == 0)
            return repos.Standing
                ? null
                : $"{IssueKey.Format(issue.Project.Key, issue.Number)} is bound to no repository - bind one on the Projects page, or run the loop inside a checkout";

        if (bound.Any(r => repos.Match(r.Canonical) is not null)) return null;
        if (repos.Clones) return null;

        var remotes = bound.Select(r => r.Remote).ToList();
        var list = remotes.Count == 1
            ? remotes[0]
            : $"{string.Join(", ", remotes.Take(remotes.Count - 1))} and {remotes[^1]}";

        return $"bound to {list}, and this runner has no checkout of it";
    }

    /// <summary>
    /// The verdicts on these issues, grouped by issue - one query for however
    /// many, and ordered by repository so the sentence a fold prints is stable.
    /// </summary>
    private async Task<Dictionary<long, IReadOnlyList<EfHatchMergeCheck>>> MergeChecksAsync(
        List<long> issueIds, CancellationToken ct) =>
        (await db.MergeChecks.AsNoTracking()
            .Where(m => issueIds.Contains(m.IssueId))
            .OrderBy(m => m.Canonical)
            .ToListAsync(ct))
        .GroupBy(m => m.IssueId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchMergeCheck>)g.ToList());

    /// <summary>The build verdicts on these issues, grouped by issue - one query, ordered by repository as the merge checks are.</summary>
    private async Task<Dictionary<long, IReadOnlyList<EfHatchBuildCheck>>> BuildChecksAsync(
        List<long> issueIds, CancellationToken ct) =>
        (await db.BuildChecks.AsNoTracking()
            .Where(b => issueIds.Contains(b.IssueId))
            .OrderBy(b => b.Canonical)
            .ToListAsync(ct))
        .GroupBy(b => b.IssueId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchBuildCheck>)g.ToList());

    /// <summary>
    /// What a move is for. Derived rather than stored: a review dispatch is
    /// exactly the one that starts and ends in the same column, and nothing
    /// compares column names to say so. Which of the two review kinds it is - a
    /// conflict to resolve or a failing build to fix - is what runners have
    /// reported about the branch, and <see cref="ReviewWork"/> says.
    /// </summary>
    private static string KindOf(
        EfHatchIssue issue, EfHatchStatus from, EfHatchStatus? to,
        IReadOnlyList<EfHatchMergeCheck> merges, IReadOnlyList<EfHatchBuildCheck> builds) =>
        to is not null && to.Id == from.Id
            ? ReviewWork.Judge(issue, merges, builds).Kind
            : WorkKinds.Advance;

    /// <summary>
    /// The same answer for an issue somebody named. Blocked or not, it is
    /// returned rather than refused: a person who asked for <c>AER-12</c> is
    /// owed the sentence saying why it cannot move, not a 404.
    /// </summary>
    /// <param name="heldToken">
    /// A claim token the caller already holds - see <see cref="GetNextWork"/>.
    /// A claim is a fact about the issue, so it folds a named dispatch too, and
    /// this is what keeps a runner re-reading its own ticket from being refused
    /// by its own lease.
    /// </param>
    [HttpGet("{key}")]
    public async Task<ActionResult<WorkDto>> GetWork(
        string key, [FromQuery] Guid? heldToken = null,
        [FromQuery] List<string>? remote = null,
        [FromQuery] bool? standing = null,
        [FromQuery] bool? clones = null,
        CancellationToken ct = default)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).ThenInclude(p => p!.Repositories)
            .WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var statuses = await OrderedStatusesAsync(ct);
        var now = time.GetUtcNow();

        // Its own gate rather than the scan's, because nothing scanned here -
        // built from the same rows and the same rule, so a named dispatch and a
        // pass cannot disagree about what an issue is waiting on.
        return await ResolveAsync(
            issue,
            statuses,
            null,
            await DependencyGate.ForAsync(db, statuses, ct),
            new ClaimGate(claims, now, heldToken, await claims.LineageAsync(db, now, ct)),
            RepositoryDeclaration.From(remote, standing, clones),
            ct);
    }

    // ---- Resolution ----

    /// <summary>
    /// One issue, rendered as a dispatch: the playbook, the children and the
    /// questions the spawned session is handed, on top of the same refusal the
    /// scan computed.
    /// </summary>
    /// <param name="loop">
    /// The pass this issue came out of, or null for an issue somebody named.
    /// Passed straight through to <see cref="Blocked"/> so that a row the scan
    /// called clear cannot come back blocked here.
    /// </param>
    /// <param name="gate">
    /// The unmet dependencies, for the same reason and with the same guarantee:
    /// the scan's own, so a row it called clear cannot come back blocked here.
    /// </param>
    /// <param name="claimed">
    /// Who holds what, and what this caller holds. Not the loop's policy: a
    /// claim is a fact about the issue, so an issue somebody named by hand is
    /// refused too.
    /// </param>
    private async Task<WorkDto> ResolveAsync(
        EfHatchIssue issue, List<EfHatchStatus> statuses, LoopScope? loop, DependencyGate gate,
        ClaimGate claimed, RepositoryDeclaration repos, CancellationToken ct)
    {
        var from = statuses.First(s => s.Id == issue.StatusId);
        var to = Columns.Target(statuses, from);

        var children = await db.Issues.Where(i => i.ParentId == issue.Id)
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Select(i => new
            {
                ProjectKey = i.Project!.Key,
                i.Number, i.Type, i.Title, i.StatusId, i.Rank,
                i.ReadyAt, i.ReadyAtHasTime, i.DueAt, i.DueAtHasTime,
                i.AssigneePersonId, i.AssigneeApiKeyId, i.Expedited,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
            })
            .ToListAsync(ct);

        var childCards = new List<IssueCardDto>(children.Count);
        foreach (var c in children)
            childCards.Add(new IssueCardDto(
                IssueKey.Format(c.ProjectKey, c.Number),
                c.ProjectKey,
                c.Type,
                c.Title,
                c.StatusId,
                c.Rank,
                IssueKey.Format(issue.Project!.Key, issue.Number),
                IssueMoment.Format(c.ReadyAt, c.ReadyAtHasTime),
                IssueMoment.Format(c.DueAt, c.DueAtHasTime),
                Assignee: await IssueProjection.ToAssigneeAsync(actors, c.AssigneePersonId, c.AssigneeApiKeyId, ct),
                Claim: claims.Project(c.Claim, claimed.Now),
                Expedited: c.Expedited));

        var playbook = to is null ? null : await MatchAsync(from.Id, to.Id, issue.Type, ct);

        // Both halves in one read: the open ones decide whether an agent is
        // dispatched at all, and the answered ones are what it is dispatched
        // knowing.
        var questions = await Questions.ForIssueAsync(db, issue.Id, ct);
        var waiting = questions.Count(q => q.Answers.Count == 0);

        var issueDto = await IssueProjection.ToDtoAsync(db, actors, issue, claims, claimed.Now, ct);

        // What runners reported about the branch, for an issue in review: the
        // fold and the kind are decided from the same two lists, in one place.
        var inReview = from.Id == Columns.AwaitingReview(statuses)?.Id;
        IReadOnlyList<EfHatchMergeCheck> merged = inReview
            ? (await MergeChecksAsync([issue.Id], ct)).GetValueOrDefault(issue.Id, [])
            : [];
        IReadOnlyList<EfHatchBuildCheck> built = inReview
            ? (await BuildChecksAsync([issue.Id], ct)).GetValueOrDefault(issue.Id, [])
            : [];

        var repositories = issue.Project!.Repositories
            .OrderBy(r => r.SortOrder)
            .Select((r, i) => new WorkRepositoryDto(r.Remote, r.Canonical, r.BaseBranch, i == 0, repos.Match(r.Canonical)))
            .ToList();

        return new WorkDto(
            issueDto,
            ToStatusDto(from),
            to is null ? null : ToStatusDto(to),
            // The values the increment will actually run on, in place rather
            // than beside them. An issue's own model and effort beat whichever
            // playbook speaks for its next move, and a client that had to
            // remember to check a second pair of fields is a client that will
            // one day spawn sonnet on a ticket set to opus - silently. Nothing
            // is hidden by folding them in: issue.modelOverride rides the same
            // payload, and is how a printed line says where the value came
            // from. Everything else stays the matched row's own, so the
            // dispatch names the playbook that spoke *and* the values that won.
            playbook is null ? null : PlaybooksController.ToDto(playbook) with
            {
                Model = issue.ModelOverride ?? playbook.Model,
                Effort = issue.EffortOverride ?? playbook.Effort,
            },
            childCards,
            repositories,
            questions,
            Blocked(
                issue, from, to, playbook, waiting, loop, gate, claimed, Columns.Implementation(statuses),
                await IssueProjection.ToAssigneeAsync(actors, issue.AssigneePersonId, issue.AssigneeApiKeyId, ct),
                repos, merged, built),
            IssueUrl(issueDto.Key),
            KindOf(issue, from, to, merged, built),
            await IssueMessagesController.UnreadAsync(db, issue.Id, ct));
    }

    /// <summary>
    /// The issue's page as a browser would open it, or null when the install
    /// has no usable public origin. The request's own origin is deliberately
    /// not a fallback: behind a proxy only the forwarded scheme is honoured, so
    /// the runner's own address for Hatch is the better default and the CLI
    /// fills it in.
    /// </summary>
    private string? IssueUrl(string key) =>
        AppsOptions.NormalizeBaseUrl(apps.Value.PublicBaseUrl) is { } origin
            ? $"{origin}/apps/hatch/issues/{key}"
            : null;

    /// <summary>
    /// Why an agent should not be spawned for this issue, or null when it
    /// should. Every fold in one place and in one order, so that the scan
    /// prints the same sentence the walk acted on.
    /// </summary>
    /// <remarks>
    /// <para>The order is what it costs to change the answer, most fundamental
    /// first: a terminal column, no column after this one, a terminal next
    /// column, a live claim, a ready date, an assignee, an unanswered question,
    /// a repository the caller has no checkout of, an unmet dependency, the
    /// verdict on an issue's branch, and last a missing playbook. A column with
    /// nowhere an agent may go is a fact about the board and no argument alters
    /// it; a ready date needs time; a question needs a person; a repository
    /// needs a clone; a dependency needs other work to land; a clean branch with
    /// a build that passes, is running or has not been read needs nothing at all;
    /// and a missing playbook needs the operator, which
    /// is last because it is only worth saying about an issue that is otherwise
    /// a candidate.</para>
    ///
    /// <para>The verdict is asked only of the review column, which is
    /// dispatched to itself (<see cref="Columns.Target"/>) and only when its
    /// branch conflicts with the trunk or the build on its tip has failed -
    /// <see cref="ReviewWork"/> has the order and the reasons, and says which of
    /// the two it is. There is no "the next column is
    /// terminal" for it: an issue in review is not advanced, it is fixed, and
    /// only the operator moves it on. The repository fold applies to that move
    /// as it does to the implementation move - a session on a branch needs a
    /// checkout of the repository the branch is in - and dependencies do not: a
    /// pull request that exists is not held back by what its ticket once waited
    /// on.</para>
    ///
    /// <para>The claim sits above all of those and below the column checks, for
    /// a different reason than the rest of the order. It is the only fold that
    /// says <em>this is being worked right now</em>; everything after it is
    /// about whether the issue could be worked at all. Printing "no playbook
    /// covers this" about a ticket another runner is three minutes into is a
    /// true sentence about the wrong thing. The claim may be a relative's: a
    /// live claim on an ancestor or a descendant folds here too, in the same
    /// place and for the same reason, because a parent's session builds what
    /// its children describe.</para>
    ///
    /// <para>The issue's type is not in that list, and deliberately: which
    /// types a move applies to is the playbook row's to state, and a constant
    /// here saying it a second time is what shadowed the matrix. A type an
    /// unattended run does not pick up is a transition no playbook covers for
    /// that type, and it is folded with the sentence that names the fix.</para>
    ///
    /// <para>Two of them are load-bearing rules rather than missing
    /// configuration: only the operator decides that something shipped, so a
    /// transition into a terminal column is not an agent's to make; and an
    /// issue holding an unanswered question is waiting on a person, so
    /// dispatching another agent at it would only produce a second session
    /// asking the same thing or guessing at the answer.</para>
    /// </remarks>
    /// <param name="waiting">
    /// Unanswered questions on the issue. Checked before the playbook, because
    /// "nobody has answered you" is a more useful sentence than "no playbook
    /// covers this" when both are true.
    /// </param>
    /// <param name="loop">
    /// The pass's own policy, or null when somebody named this ticket by hand.
    /// The one fold it adds is a decision about what an unattended run may
    /// *start*, as opposed to what may move, and a person who names a ticket is
    /// giving an instruction that housekeeping does not overrule.
    /// </param>
    /// <param name="gate">
    /// What is unfinished that this issue waits on. Not the loop's policy - a
    /// dependency is a fact about the work, so an issue somebody named by hand
    /// is refused too, and somebody who disagrees removes the edge.
    /// </param>
    /// <param name="claimed">
    /// Who is holding this issue, and what the caller holds itself. Not the
    /// loop's policy either, and for the same reason: a second agent sent at a
    /// ticket somebody is mid-increment on is the failure the claim exists to
    /// prevent, whoever asked for it.
    /// </param>
    /// <param name="implementation">
    /// The column a dependency gates the move into, and the only one it gates -
    /// shared with the repository fold below, for the same reason: a wrong
    /// checkout matters only once code is about to be written, and everything
    /// left of that column needs no checkout at all.
    /// </param>
    /// <param name="repos">
    /// What the caller told the dispatcher about its own checkouts. A fact
    /// about the work rather than the pass's policy, so a named dispatch is
    /// refused by it too - see <see cref="RepositoryFold"/>.
    /// </param>
    private static string? Blocked(
        EfHatchIssue issue,
        EfHatchStatus from,
        EfHatchStatus? to,
        EfHatchPlaybook? playbook,
        int waiting,
        LoopScope? loop,
        DependencyGate gate,
        ClaimGate claimed,
        EfHatchStatus? implementation,
        AssigneeDto? assignee,
        RepositoryDeclaration repos,
        IReadOnlyList<EfHatchMergeCheck> verdicts,
        IReadOnlyList<EfHatchBuildCheck> builds)
    {
        if (from.IsTerminal)
            return $"\"{from.Name}\" is where work ends - there is nothing after it";

        // Said before the column-after test, which would otherwise refuse this
        // with "there is nowhere for this to go" - true, and no use to somebody
        // reading a queue trying to work out why a ticket they filed is not
        // moving. Nothing comes off the shelf on a pass's say-so: a deferred
        // ticket is waiting on a person deciding it is work again.
        if (from.IsDeferred)
            return $"\"{from.Name}\" is deferred - a person puts it back on the board, not a pass";

        if (to is null)
            return $"there is no column after \"{from.Name}\", so there is nowhere for this to go";

        if (to.IsTerminal)
            return $"the next column is \"{to.Name}\", and only the operator moves work there";

        if (claimed.Held(issue) is { } holder)
            return holder;

        if (loop is not null)
        {
            if (IsWaiting(issue, loop.Today, loop.OffsetMinutes))
                return $"not workable until {IssueMoment.Format(issue.ReadyAt, issue.ReadyAtHasTime)}";

            // A person's name on a ticket takes it off the night shift, and
            // only a person's: an issue assigned to a key is exactly the thing
            // an agent should pick up, and one whose assignee no longer
            // resolves is not assigned at all - the liveness rule reaching the
            // dispatcher without a line of its own.
            if (assignee?.Kind == ActorKind.Person)
                return $"assigned to {assignee.Name} - an unattended pass leaves a person's work alone";
        }

        if (waiting > 0)
            return $"{waiting} unanswered question{(waiting == 1 ? "" : "s")} - it is waiting on a person, not on an agent";

        // A repository matters wherever code is about to be written or a branch
        // is about to be entered: the move into the implementation column, and a
        // conflict or a failing build, each of which is a session on the branch. Dependencies gate only the
        // first - a pull request that already exists is not held back by what
        // its ticket once waited on.
        var conflicts = to.Id == from.Id;

        if (to.Id == implementation?.Id || conflicts)
        {
            if (RepositoryFold(issue, repos) is { } repoBlock) return repoBlock;
        }

        if (to.Id == implementation?.Id)
        {
            if (gate.Unmet(issue.Id) is { Count: > 0 } waitingOn) return WaitingOn(waitingOn);
        }

        // Last before the playbook, and after the repository: a question needs a
        // person, a repository needs a clone, and a clean branch with a build
        // that is passing, running or unread needs nothing at all - so it is the
        // least useful thing to say about an issue that is folded for a reason
        // somebody can act on.
        if (conflicts && ReviewWork.Judge(issue, verdicts, builds).Fold is { } reviewBlock) return reviewBlock;

        return playbook is null
            ? $"no playbook covers \"{from.Name}\" to \"{to.Name}\" for {An(issue.Type)} - add one on the Playbooks page"
            : null;
    }

    /// <summary>
    /// A type with its article, because "a epic" in a sentence a person reads
    /// at a terminal is a typo they have to look past. The four types are
    /// enough for the crude rule to be the right one.
    /// </summary>
    private static string An(string type) =>
        "aeiou".Contains(char.ToLowerInvariant(type[0])) ? $"an {type}" : $"a {type}";

    /// <summary>
    /// The playbook that speaks for this move. A row naming the issue's type
    /// beats a row naming every type, and ties go to the older row - so adding
    /// a specific rule never requires editing the general one.
    /// </summary>
    private async Task<EfHatchPlaybook?> MatchAsync(int from, int to, string type, CancellationToken ct) =>
        Match(
            await db.Playbooks.AsNoTracking()
                .Include(p => p.FromStatus)
                .Include(p => p.ToStatus)
                .Where(p => p.FromStatusId == from && p.ToStatusId == to)
                .ToListAsync(ct),
            from, to, type);

    /// <summary>
    /// The same rule against rows already in hand, which is how a scan matches
    /// a whole board's worth of transitions without a query a row. The matrix
    /// is small enough to read whole and the tie-break is arithmetic.
    /// </summary>
    private static EfHatchPlaybook? Match(List<EfHatchPlaybook> playbooks, int from, int to, string type) =>
        playbooks
            .Where(p => p.FromStatusId == from && p.ToStatusId == to && p.Covers(type))
            .OrderByDescending(p => p.Specificity)
            .ThenBy(p => p.Id)
            .FirstOrDefault();

    // ---- Dates ----

    /// <summary>
    /// Whether the issue is still waiting for its ready date, by the board's
    /// rule: workable from the start of the day it names, whatever hour was
    /// set, in the caller's zone.
    /// </summary>
    private static bool IsWaiting(EfHatchIssue issue, long today, int offsetMinutes)
    {
        if (issue.ReadyAt is not { } ready) return false;

        // A bare date is read in UTC, where its components are the ones that
        // were typed; an instant is read in the caller's zone, where the hour
        // somebody meant is the hour they meant. IssueMoment.cs holds the other
        // half of this contract.
        var day = issue.ReadyAtHasTime ? DayNumber(ready, offsetMinutes) : DayNumber(ready, 0);
        return day > today;
    }

    private static long DayNumber(DateTimeOffset at, int offsetMinutes) =>
        (long)Math.Floor((at.ToUnixTimeSeconds() + (offsetMinutes * 60L)) / 86_400.0);

    // ---- Loading ----

    private Task<List<EfHatchStatus>> OrderedStatusesAsync(CancellationToken ct) =>
        db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);

    private static StatusDto ToStatusDto(EfHatchStatus s) =>
        new(s.Id, s.Name, s.SortOrder, s.IsTerminal, s.IsDeferred, s.Color);
}
