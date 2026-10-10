using System.Text.Json;
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
/// Every part of that answer is decided on the server rather than in the
/// shell, for the reason the rank is decided on the server (docs/hatch.md,
/// "Rank computation"): it keeps every client dumb. Which issue is next,
/// which column it is headed for, whether it may go there at all, and which
/// playbook speaks for the move are all questions about rows this process
/// owns, and a script that re-derived them would drift the first time a
/// column was renamed. The walk itself is <see cref="Dispatch"/>; these
/// routes call it and shape what it decides into a response.
/// </summary>
[ApiController]
[Route("api/hatch/work")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class WorkController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, TimeProvider time,
    IOptions<AppsOptions> apps, RankService ranks, ICallerIdentity caller) : ControllerBase
{
    private readonly Dispatch _dispatch = new(db, actors, claims, time);

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
    /// terminal column, a missing playbook, an unfinished dependency, a full
    /// WIP section, an epic at its own limit, or a branch that
    /// merges cleanly. The load a full section or a full epic is judged
    /// against is always the board's own, never the scope's - see
    /// <see cref="Wip.LoadAsync"/> and <see cref="Wip.EpicsAsync"/>. The
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
    /// <param name="mine">
    /// Narrows the pass to the caller's own tickets - assigned to the person it
    /// works for, or to its own key - and folds every other one with a sentence
    /// naming whose it is. "Its own" is read off the calling key's owner, never
    /// off anything the caller says about itself (docs/hatch.md, "the one edge
    /// that is deliberately cut"). A caller whose key belongs to nobody is
    /// refused before anything is scanned. It narrows the candidates and
    /// decides nothing else about them - the same contract <paramref
    /// name="ancestorKey"/> carries.
    /// </param>
    [HttpGet("next")]
    public async Task<ActionResult<WorkDto>> GetNextWork(
        [FromQuery] int offsetMinutes = 0,
        [FromQuery] string? ancestorKey = null,
        [FromQuery] Guid? heldToken = null,
        [FromQuery] List<string>? remote = null,
        [FromQuery] bool? standing = null,
        [FromQuery] bool? clones = null,
        [FromQuery] bool mine = false,
        CancellationToken ct = default)
    {
        var repos = RepositoryDeclaration.From(remote, standing, clones);
        var scan = await _dispatch.ScanAsync(offsetMinutes, ancestorKey, heldToken, repos, mine, ct);
        if (scan.Failure is not null) return BadRequest(scan.Failure);

        // The first clear row of the queue, and nothing else. Not a second
        // walk that happens to agree with the scan's - the whole point of
        // publishing the scan is that it explains what this line picked, and
        // two loops that could disagree about the order of the board is
        // precisely the bug it exists to expose.
        var clear = scan.Rows.FirstOrDefault(r => r.Blocked is null);
        if (clear is null) return NoContent();

        return await ResolveAsync(clear.Issue, scan.Statuses, scan.Loop, scan.Pace, scan.Gate, scan.Family, scan.Claims, scan.Repos, scan.Wip, scan.Epics, ct);
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
        [FromQuery] bool mine = false,
        CancellationToken ct = default)
    {
        // No heldToken here, and deliberately: the queue is a report on what a
        // pass would do, not a pass, and a caller reading it holds nothing.
        var repos = RepositoryDeclaration.From(remote, standing, clones);
        var scan = await _dispatch.ScanAsync(offsetMinutes, ancestorKey, null, repos, mine, ct);
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
            r.Kind,
            r.Hop,
            r.HopKind,
            r.HopUnder,
            r.ClearNote)).ToList();
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

        var statuses = await _dispatch.OrderedStatusesAsync(ct);
        if (Columns.AwaitingReview(statuses) is not { } review) return new List<ReviewCheckDto>();

        var inReview = await db.Issues
            .Where(i => i.StatusId == review.Id)
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Include(i => i.Project).ThenInclude(p => p!.Repositories)
            .ToListAsync(ct);

        var held = inReview.Where(i => HoldsCheckout(i, repos)).ToList();
        var verdicts = await _dispatch.MergeChecksAsync(held.Select(i => i.Id).ToList(), ct);
        var builds = await _dispatch.BuildChecksAsync(held.Select(i => i.Id).ToList(), ct);

        return held.Select(i => new ReviewCheckDto(
            IssueKey.Format(i.Project!.Key, i.Number),
            i.Project.Repositories
                .OrderBy(r => r.SortOrder)
                .Select((r, at) => new WorkRepositoryDto(r.Remote, r.Canonical, r.BaseBranch, at == 0, repos.Match(r.Canonical)))
                .ToList(),
            (verdicts.TryGetValue(i.Id, out var found) ? found : []).Select(IssueMergeChecks.Project).ToList(),
            (builds.TryGetValue(i.Id, out var built) ? built : []).Select(IssueBuildChecks.Project).ToList(),
            i.PullRequestUrl))
            .ToList();
    }

    /// <summary>
    /// The dispatcher's own repository rule without the clone allowance: the
    /// caller has a checkout the project's repositories match, or the project
    /// binds nothing and the caller has a standing one.
    /// </summary>
    private static bool HoldsCheckout(EfHatchIssue issue, RepositoryDeclaration repos)
    {
        var bound = issue.Project!.Repositories;
        return bound.Count == 0 ? repos.Standing : bound.Any(r => repos.Match(r.Canonical) is not null);
    }

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

        var statuses = await _dispatch.OrderedStatusesAsync(ct);
        var now = time.GetUtcNow();

        // Its own gate rather than the scan's, because nothing scanned here -
        // built from the same rows and the same rule, so a named dispatch and a
        // pass cannot disagree about what an issue is waiting on.
        return await ResolveAsync(
            issue,
            statuses,
            null,
            null,
            await DependencyGate.ForAsync(db, statuses, ct),
            await FamilyGate.ForAsync(db, statuses, ct),
            new ClaimGate(claims, now, heldToken, await claims.LineageAsync(db, now, ct)),
            RepositoryDeclaration.From(remote, standing, clones),
            await Wip.LoadAsync(db, claims, statuses, now, ct),
            await Wip.EpicsAsync(db, [issue.ParentId], ct),
            ct);
    }

    /// <summary>
    /// Carries an express issue, or a child its parent pulls, across the
    /// column it stands in, with no session - the loop's own write, on the
    /// loop's own say-so. Judged fresh as the move is made, with the same
    /// <see cref="Dispatch.Blocked"/> a named dispatch is judged by, so a
    /// stale runner cannot carry an issue the server would no longer carry.
    /// </summary>
    /// <remarks>
    /// Takes no claim: a claim protects a session that runs for minutes, and a
    /// hop is one write. Writes <c>status_changed</c> naming the caller as the
    /// actor and carrying <c>express: true</c> or <c>pulled: true</c>,
    /// whichever of <see cref="HopKinds"/> carried it - the same event a move
    /// writes, so a card that passed a gate with nobody present says so on its
    /// trail.
    /// </remarks>
    [HttpPost("{key}/hop")]
    public async Task<ActionResult<IssueDto>> HopWork(
        string key,
        [FromQuery] List<string>? remote = null,
        [FromQuery] bool? standing = null,
        [FromQuery] bool? clones = null,
        CancellationToken ct = default)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).ThenInclude(p => p!.Repositories)
            .WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var statuses = await _dispatch.OrderedStatusesAsync(ct);
        var from = statuses.First(s => s.Id == issue.StatusId);
        var to = Columns.Target(statuses, from);
        var repos = RepositoryDeclaration.From(remote, standing, clones);
        var now = time.GetUtcNow();

        var family = await FamilyGate.ForAsync(db, statuses, ct);
        var playbook = to is null
            ? null
            : await _dispatch.MatchAsync(from.Id, to.Id, issue.Type, family.Children(issue.Id).Count > 0, ct);
        var questions = await Questions.ForIssueAsync(db, issue.Id, ct);
        var waiting = await UnlapsedWaitingAsync(questions, issue.Id, now, ct);

        var inReview = from.Id == Columns.AwaitingReview(statuses)?.Id;
        var merged = inReview ? (await _dispatch.MergeChecksAsync([issue.Id], ct)).GetValueOrDefault(issue.Id, []) : [];
        var built = inReview ? (await _dispatch.BuildChecksAsync([issue.Id], ct)).GetValueOrDefault(issue.Id, []) : [];

        var wip = await Wip.LoadAsync(db, claims, statuses, now, ct);
        var parent = family.ParentOf(issue.Id);
        var hopKind = to is null ? null : Dispatch.HopKind(issue, from, to, family, statuses, wip, parent);
        var hop = hopKind is not null;
        var letGo = await LetGo.ForIssueAsync(db, issue.Id, ct);
        var blocked = Dispatch.Blocked(
            issue, from, to, playbook, waiting, letGo,
            null, // a hop takes no ready-date fold of its own, and no economy or low fold either - see Dispatch.Blocked's loop parameter
            null,
            await DependencyGate.ForAsync(db, statuses, ct),
            family,
            new ClaimGate(claims, now, null, await claims.LineageAsync(db, now, ct)),
            Columns.Implementation(statuses),
            await IssueProjection.ToAssigneeAsync(actors, issue.AssigneePersonId, issue.AssigneeApiKeyId, ct),
            repos, merged, built, hop, statuses,
            wip,
            await Wip.EpicsAsync(db, [issue.ParentId], ct));

        if (blocked is not null) return Conflict(blocked);
        if (!hop) return Conflict($"{key} is not a hop - a session moves this issue, and a hop does not");

        var target = to!;
        var actor = await caller.ActorNameAsync(ct);

        var payload = hopKind switch
        {
            HopKinds.Parent => JsonSerializer.Serialize(new { from = from.Name, to = target.Name, pulled = true }),
            HopKinds.Epic => JsonSerializer.Serialize(new { from = from.Name, to = target.Name, epic = true }),
            HopKinds.Under => JsonSerializer.Serialize(new { from = from.Name, to = target.Name, under = parent?.Key }),
            _ => JsonSerializer.Serialize(new { from = from.Name, to = target.Name, express = true }),
        };

        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.StatusChanged,
            Payload = payload,
            At = now,
        });
        issue.Rank = await ranks.BottomAsync(target.Id, ct);
        issue.StatusId = target.Id;
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, now, ct);
    }

    /// <summary>
    /// Advances an issue waiting in review whose pull request has merged - the
    /// one path that may move work into a terminal column without the operator
    /// pressing it there themselves, because a merge on the forge <em>is</em>
    /// that decision. Uses <see cref="Columns.Advance"/> rather than
    /// <see cref="Columns.Target"/> and carries its own refusals rather than
    /// <see cref="Dispatch.Blocked"/>'s, since <c>Target</c> and <c>Blocked</c>
    /// both exist to keep an unattended pass from shipping anything itself.
    /// </summary>
    /// <remarks>
    /// Takes no claim: like a hop, this is one write and not a session. A
    /// claimed issue still refuses - a session is running on it, and a row must
    /// not move out from under one.
    /// </remarks>
    [HttpPost("{key}/merged")]
    public async Task<ActionResult<IssueDto>> MergedWork(
        string key, PullRequestMergedRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project)
            .WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var statuses = await _dispatch.OrderedStatusesAsync(ct);
        var from = statuses.First(s => s.Id == issue.StatusId);

        if (Columns.AwaitingReview(statuses)?.Id != from.Id)
            return Conflict($"\"{from.Name}\" is not the review column - a merge only advances a ticket waiting there");

        var recorded = issue.PullRequestUrl?.Trim();
        if (string.IsNullOrEmpty(recorded))
            return Conflict("no pull request is recorded on this issue, so there is nothing for a merge to confirm");

        var url = request.Url?.Trim() ?? "";
        if (!string.Equals(url, recorded, StringComparison.Ordinal))
            return Conflict($"\"{url}\" is not the pull request recorded on this issue");

        var to = Columns.Advance(statuses, from);
        if (to is null)
            return Conflict($"there is no column after \"{from.Name}\", so there is nowhere for this to go");

        var now = time.GetUtcNow();
        var claimed = new ClaimGate(claims, now, null, await claims.LineageAsync(db, now, ct));
        if (claimed.Held(issue) is { } holder) return Conflict(holder);

        var actor = await caller.ActorNameAsync(ct);
        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.StatusChanged,
            Payload = JsonSerializer.Serialize(new { from = from.Name, to = to.Name, merged = true }),
            At = now,
        });
        issue.Rank = await ranks.BottomAsync(to.Id, ct);
        issue.StatusId = to.Id;
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return await IssueProjection.ToDtoAsync(db, actors, issue, claims, now, ct);
    }

    // ---- Resolution ----

    /// <summary>
    /// One issue, rendered as a dispatch: the playbook, the children and the
    /// questions the spawned session is handed, on top of the same refusal the
    /// scan computed.
    /// </summary>
    /// <param name="loop">
    /// The pass this issue came out of, or null for an issue somebody named.
    /// Passed straight through to <see cref="Dispatch.Blocked"/> so that a row
    /// the scan called clear cannot come back blocked here.
    /// </param>
    /// <param name="pace">
    /// The scan's own pace judgements - economy's and low's, off the one
    /// reading - or null where nothing in it needed either. The same guarantee
    /// as <paramref name="loop"/>: a row the scan called clear on pace cannot
    /// come back blocked here.
    /// </param>
    /// <param name="gate">
    /// The unmet dependencies, for the same reason and with the same guarantee:
    /// the scan's own, so a row it called clear cannot come back blocked here.
    /// </param>
    /// <param name="family">
    /// Every issue's children and which are open, with the same guarantee: the
    /// scan's own, so a row it called clear cannot come back blocked here.
    /// </param>
    /// <param name="claimed">
    /// Who holds what, and what this caller holds. Not the loop's policy: a
    /// claim is a fact about the issue, so an issue somebody named by hand is
    /// refused too.
    /// </param>
    /// <param name="wip">
    /// How full the WIP section is, or null where the board has never turned it
    /// on. A fact about the board, not the loop's policy: an issue somebody
    /// named by hand is refused by a full section too.
    /// </param>
    /// <param name="epics">
    /// This issue's own parent's epic limit, if it has one - see
    /// <see cref="Wip.EpicsAsync"/>. The same guarantee as <paramref name="wip"/>:
    /// a fact about the board, so an issue somebody named by hand is held by
    /// its epic's limit too.
    /// </param>
    private async Task<WorkDto> ResolveAsync(
        EfHatchIssue issue, List<EfHatchStatus> statuses, LoopScope? loop, PaceReadings? pace, DependencyGate gate,
        FamilyGate family, ClaimGate claimed, RepositoryDeclaration repos, WipSection? wip,
        IReadOnlyDictionary<long, EpicLimit> epics, CancellationToken ct)
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
                i.AssigneePersonId, i.AssigneeApiKeyId, i.Priority, i.Express,
                i.StalledAt, i.StalledWhy, i.Held,
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
                Expedited: c.Priority >= PriorityLevels.Expedited,
                Express: c.Express,
                Priority: PriorityLevels.Name(c.Priority),
                StalledAt: c.StalledAt,
                StalledWhy: c.StalledWhy,
                Held: c.Held));

        var playbook = to is null
            ? null
            : await _dispatch.MatchAsync(from.Id, to.Id, issue.Type, children.Count > 0, ct);

        // Both halves in one read: the open ones decide whether an agent is
        // dispatched at all, and the answered ones are what it is dispatched
        // knowing.
        var questions = await Questions.ForIssueAsync(db, issue.Id, ct);
        var waiting = await UnlapsedWaitingAsync(questions, issue.Id, claimed.Now, ct);

        var issueDto = await IssueProjection.ToDtoAsync(db, actors, issue, claims, claimed.Now, ct);

        // What runners reported about the branch, for an issue in review: the
        // fold and the kind are decided from the same two lists, in one place.
        var inReview = from.Id == Columns.AwaitingReview(statuses)?.Id;
        IReadOnlyList<EfHatchMergeCheck> merged = inReview
            ? (await _dispatch.MergeChecksAsync([issue.Id], ct)).GetValueOrDefault(issue.Id, [])
            : [];
        IReadOnlyList<EfHatchBuildCheck> built = inReview
            ? (await _dispatch.BuildChecksAsync([issue.Id], ct)).GetValueOrDefault(issue.Id, [])
            : [];

        var repositories = issue.Project!.Repositories
            .OrderBy(r => r.SortOrder)
            .Select((r, i) => new WorkRepositoryDto(r.Remote, r.Canonical, r.BaseBranch, i == 0, repos.Match(r.Canonical)))
            .ToList();

        var hopParent = family.ParentOf(issue.Id);
        var hopKind = to is null ? null : Dispatch.HopKind(issue, from, to, family, statuses, wip, hopParent);
        var hop = hopKind is not null;
        var letGo = await LetGo.ForIssueAsync(db, issue.Id, ct);
        var blocked = Dispatch.Blocked(
            issue, from, to, playbook, waiting, letGo, loop, pace, gate, family, claimed, Columns.Implementation(statuses),
            await IssueProjection.ToAssigneeAsync(actors, issue.AssigneePersonId, issue.AssigneeApiKeyId, ct),
            repos, merged, built, hop, statuses, wip, epics);
        var hopped = hop && blocked is null;

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
            //
            // Null on a hop, even where a playbook covers the move, so no
            // client can spawn a session for it by accident - see WorkDto.Hop.
            hopped || playbook is null ? null : PlaybooksController.ToDto(playbook) with
            {
                Model = issue.ModelOverride ?? playbook.Model,
                Effort = issue.EffortOverride ?? playbook.Effort,
            },
            childCards,
            repositories,
            questions,
            blocked,
            IssueUrl(issueDto.Key),
            Dispatch.KindOf(issue, from, to, merged, built),
            await IssueMessagesController.UnreadAsync(db, issue.Id, ct),
            hopped,
            hopped ? hopKind : null,
            hopped && hopKind == HopKinds.Under ? hopParent?.Key : null,
            letGo,
            inReview);
    }

    // ---- Waiting, past a lapsed stall question ----

    /// <summary>
    /// A named issue's own open-question count, the dispatcher's way: every
    /// open question but a lapsed stall one - see
    /// <see cref="Questions.DispatchCountsAsync"/>, this method's counterpart
    /// for a whole scan. Two reads rather than one because a single named
    /// dispatch has no scan-wide newest-event map to share.
    /// </summary>
    private async Task<int> UnlapsedWaitingAsync(
        IReadOnlyList<QuestionDto> questions, long issueId, DateTimeOffset now, CancellationToken ct)
    {
        var open = questions.Where(q => q.Answers.Count == 0).ToList();
        if (open.Count == 0) return 0;

        var newestEventAt = await Questions.NewestEventAtAsync(db, issueId, ct);

        return open.Count(q => !(StallAnswers.IsStall(q.Options)
            && Questions.IsLapsed(q.AskedAt, newestEventAt, claims.StallLapseSeconds, now)));
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

    // ---- Loading ----

    private static StatusDto ToStatusDto(EfHatchStatus s) =>
        new(s.Id, s.Name, s.SortOrder, s.IsTerminal, s.IsDeferred, s.IsWip, s.Color, s.ExpressSkips, s.ParentPulls);
}
