using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The one thing in Hatch that knows whether an issue is actionable - every
/// fold, in one order, with the sentence for each. <c>WorkController</c>'s two
/// work endpoints are both built on the same <see cref="ScanAsync"/>, and
/// anything else that needs to ask "what would a pass do" asks it here rather
/// than re-deriving a second opinion: two walks that could disagree about the
/// order of the board is precisely the bug this class exists to prevent.
/// </summary>
public sealed class Dispatch(HatchContext db, IActorDirectory actors, IssueClaims claims, TimeProvider time)
{
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
    public async Task<Scan> ScanAsync(
        int offsetMinutes, string? ancestorKey, Guid? heldToken, RepositoryDeclaration repos, bool mine,
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

        // Resolved once for the pass, and only when asked: a plain scan has no
        // reason to touch either. A caller whose key belongs to nobody is
        // refused here, before anything is scanned, the same way an unknown
        // ancestorKey is refused above.
        Actor? principal = null;
        Guid? callerKeyId = null;
        if (mine)
        {
            principal = await actors.PrincipalAsync(ct);
            if (await actors.MeAsync(ct) is { Kind: ActorKind.Key } me) callerKeyId = me.Id;
            if (principal is null)
                return Scan.Refused("this key belongs to nobody, so it has no tickets of its own - an admin sets its owner on the API Keys page");
        }

        var loop = new LoopScope(DayNumber(now, offsetMinutes), offsetMinutes, mine, principal, callerKeyId);

        // One instant for the whole pass. A scan in which the clock moved
        // between two rows could fold one card and not its neighbour for a
        // reason nobody could reconstruct afterwards.
        var claimed = new ClaimGate(claims, now, heldToken, await claims.LineageAsync(db, now, ct));

        var gate = await DependencyGate.ForAsync(db, statuses, ct);
        var family = await FamilyGate.ForAsync(db, statuses, ct);
        var wip = await Wip.LoadAsync(db, claims, statuses, now, ct);
        var open = await Questions.DispatchCountsAsync(db, claims.StallLapseSeconds, now, ct);
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

        // Every epic a candidate's own parent might be, read once for the
        // whole pass - HA-112's own limit, beside the section-wide one.
        var epics = await Wip.EpicsAsync(db, candidates.Select(i => i.ParentId), ct);

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

        // The whole walk, six times: every emergency candidate right to
        // left, then every expedited candidate right to left, then every
        // normal one right to left, then every low one right to left, then
        // every economy one right to left, then every paused one right to
        // left. So an emergency bug in the leftmost column is listed above an
        // expedited story in the rightmost one, which is listed above a
        // normal one in the rightmost one, which is listed above a low one
        // wherever it sits, which is listed above an economy one wherever it
        // sits, which is listed above a paused one wherever it sits, while
        // inside each tier the order is the board's own - rightmost column
        // first, and (Rank, Id) within a column.
        //
        // Six passes over the same columns rather than a sort of the
        // finished rows, because the published scan is the explanation of
        // what `next` picked: a comparator applied afterwards would be a
        // second opinion about the order, and passes that could disagree is
        // precisely the bug this endpoint exists to expose.
        //
        // Priority reorders and gates nothing. Every row is judged by the same
        // Blocked below whichever pass reaches it, so an emergency or
        // expedited issue that is blocked is folded with exactly the sentence
        // it is folded with today - it is simply folded sooner.
        var rows = new List<ScanRow>();
        var effective = candidates.ToDictionary(i => i.Id, i => gate.Effective(i.Id));

        // Resolved only when some candidate actually reads at economy or low
        // level - a pass with nothing at either tier has no reason to read an
        // account's usage at all. One read, judged twice - see PaceReadingsAsync.
        PaceReadings? pace = null;
        if (effective.Values.Any(e => e.Level is PriorityLevels.Economy or PriorityLevels.Low))
            pace = await PaceReadingsAsync(ct);

        // Resolved only when some candidate is actually marked - a pass with
        // nothing stalled has no reason to read any issue's trail at all.
        Dictionary<long, int>? letGoCounts = null;
        var marked = candidates.Where(i => i.StalledAt is not null).Select(i => i.Id).ToList();
        if (marked.Count > 0) letGoCounts = await LetGo.CountsAsync(db, marked, ct);

        // Set only after a tier's own loop below has finished, so two clear rows in
        // the same tier never park each other - only a strictly higher tier's clear
        // row, remembered here on a previous iteration, can. Never reassigned once
        // set: the first clear row in the whole (top-down) walk is also the only one
        // the park sentence ever needs to name. A row that is folded parks nothing,
        // whatever folds it: most folds stand for days, and a tier parked behind one
        // is a tier that never runs (HA-312).
        string? parkedByKey = null;
        string? parkedByLevel = null;

        foreach (var level in new[] { PriorityLevels.Emergency, PriorityLevels.Expedited, PriorityLevels.Normal, PriorityLevels.Low, PriorityLevels.Economy, PriorityLevels.Paused })
        {
            string? firstClearThisTier = null;

            foreach (var status in Enumerable.Reverse(statuses))
            {
                if (Columns.Target(statuses, status) is not { } to) continue;
                if (!byColumn.TryGetValue(status.Id, out var column)) continue;

                foreach (var issue in column)
                {
                    if (effective[issue.Id].Level != level) continue;

                    var playbook = Match(playbooks, status.Id, to.Id, issue.Type, family.Children(issue.Id).Count > 0);
                    var summary = open.GetValueOrDefault(issue.Id, new OpenSummary(0, false));
                    var merged = verdicts.TryGetValue(issue.Id, out var found) ? found : [];
                    var built = builds.TryGetValue(issue.Id, out var foundBuilds) ? foundBuilds : [];
                    var parent = family.ParentOf(issue.Id);
                    var hopKind = HopKind(issue, status, to, family, statuses, wip, parent);
                    var hop = hopKind is not null;
                    var hopUnder = hopKind == HopKinds.Under ? parent?.Key : null;
                    var letGo = issue.StalledAt is not null ? letGoCounts?.GetValueOrDefault(issue.Id, 0) ?? 0 : 0;
                    var blocked = Blocked(
                        issue, status, to, playbook, summary.Waiting, letGo, loop, pace, gate, family, claimed,
                        implementation, assignees[issue.Id], repos, merged, built, hop, statuses, wip, epics);

                    var sentence = blocked;

                    // Nothing at a lower tier is picked up while a strictly
                    // higher one still has clear work - a hop is exempt from
                    // being parked itself, but still becomes the row that
                    // parks the tiers below it.
                    if (blocked is null && !hop && parkedByKey is not null)
                        sentence = $"{parkedByKey} ranks {parkedByLevel} and is clear - " +
                                   $"nothing at {PriorityLevels.Name(level)} is picked up while higher-ranking work is available";

                    // The first clear row of this tier. Independent of the park
                    // above: a row already parked was clear before the park
                    // replaced its sentence, and still counts as clear for the
                    // tier below it.
                    if (firstClearThisTier is null && blocked is null)
                        firstClearThisTier = IssueKey.Format(issue.Project!.Key, issue.Number);

                    rows.Add(new ScanRow(
                        issue, status, to, sentence,
                        KindOf(issue, status, to, merged, built),
                        hop && sentence is null,
                        sentence is null ? hopKind : null,
                        sentence is null ? hopUnder : null,
                        sentence is null && summary.LapsedStall
                            ? ClearNote(claims.StallLapseSeconds)
                            : sentence is null && issue.StalledAt is not null
                            ? ResumedClearNote(issue.StalledAt.Value, claimed.Now, claimed.Claims!.StallResumeSeconds)
                            : sentence is null && effective[issue.Id].Level == PriorityLevels.Economy ? pace?.Economy.ClearNote
                            : sentence is null && effective[issue.Id].Level == PriorityLevels.Low ? pace?.Low.ClearNote
                            : null,
                        effective[issue.Id].Level,
                        effective[issue.Id].FromKey));
                }
            }

            if (parkedByKey is null && firstClearThisTier is not null)
            {
                parkedByKey = firstClearThisTier;
                parkedByLevel = PriorityLevels.Name(level);
            }
        }

        return new Scan(statuses, rows, loop, gate, family, claimed, repos, wip, epics, null, pace);
    }

    /// <summary>
    /// The calling key's own account, read the same way <see cref="UtilizationController.Get"/>
    /// does - the freshest whole reading across its runners - and judged twice:
    /// economy's stricter arithmetic and low's laxer one. Resolved lazily by
    /// <see cref="ScanAsync"/>, only when some candidate actually reads at
    /// economy or low level, and read once regardless of how many candidates
    /// are at either.
    /// </summary>
    private async Task<PaceReadings> PaceReadingsAsync(CancellationToken ct)
    {
        var principal = await actors.PrincipalAsync(ct);
        if (principal is null)
        {
            var noAccount = EconomyPace.Behind("economy - this key belongs to nobody, so there is no account to read usage for");
            var noAccountLow = EconomyPace.Behind("low - this key belongs to nobody, so there is no account to read usage for");
            return new PaceReadings(noAccount, noAccountLow);
        }

        var runners = await db.Runners.AsNoTracking()
            .Where(r => r.ForPersonId == principal.Id && r.Usage != null)
            .ToListAsync(ct);

        var reading = Utilization.Of(runners, time.GetUtcNow());
        if (reading is null)
        {
            var noReading = EconomyPace.Behind("economy - no usage reading for this account yet; a runner's first session gives one");
            var noReadingLow = EconomyPace.Behind("low - no usage reading for this account yet; a runner's first session gives one");
            return new PaceReadings(noReading, noReadingLow);
        }

        return new PaceReadings(Utilization.Pace(reading, time.GetUtcNow()), Utilization.SessionPace(reading, time.GetUtcNow()));
    }

    /// <summary>
    /// Why a row that carries no <see cref="ScanRow.Blocked"/> is clear at all -
    /// see <see cref="QueueEntryDto.ClearNote"/>. The only caller today is a
    /// lapsed stall question, so this is the one sentence rather than a switch.
    /// </summary>
    private static string ClearNote(int lapseSeconds) =>
        $"its stall question lapsed after {Minutes(lapseSeconds)} untouched";

    /// <summary>
    /// The other reason a row is clear with nothing in <see cref="ScanRow.Blocked"/>:
    /// its own mark resumed, rather than a stall question lapsing - see
    /// <see cref="ClearNote"/>.
    /// </summary>
    private static string ResumedClearNote(DateTimeOffset stalledAt, DateTimeOffset now, int resumeSeconds) =>
        $"it stalled {Minutes((int)(now - stalledAt).TotalSeconds)} ago and resumes itself after {Minutes(resumeSeconds)}";

    private static string Minutes(int seconds)
    {
        var minutes = seconds / 60;
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
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
    /// have. Every session spawns in the primary repository's checkout - the
    /// first by sort order - so it is the primary that has to match, and a
    /// runner holding only a later one is refused with its own sentence. Null
    /// when the caller declared nothing at all (see
    /// <see cref="RepositoryDeclaration"/>), when the project binds nothing
    /// and the caller has a standing checkout, when the project's primary
    /// repository matches a remote the caller declared, or when the caller
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

        if (repos.Match(bound[0].Canonical) is not null) return null;
        if (repos.Clones) return null;

        if (bound.Skip(1).Any(r => repos.Match(r.Canonical) is not null))
            return $"its primary repository is {bound[0].Remote}, and this runner has no checkout of it";

        var remotes = bound.Select(r => r.Remote).ToList();
        var list = remotes.Count == 1
            ? remotes[0]
            : $"{string.Join(", ", remotes.Take(remotes.Count - 1))} and {remotes[^1]}";

        return $"bound to {list}, and this runner has no checkout of it";
    }

    /// <summary>
    /// Why a move into the WIP section is not this issue's to make: the move
    /// leaves outside it and lands inside it, a slice counts the type, and
    /// either that slice's load or - for a story or a bug under an epic - the
    /// load under that epic, neither counting this issue, is already at or
    /// over its limit. Null where the board has never turned WIP on, where the
    /// move does not cross into the section, where no slice counts the type,
    /// or where neither limit is over (the section-wide slice may simply have
    /// no row).
    /// </summary>
    /// <param name="epics">
    /// Every epic a candidate in this pass or this named dispatch might stand
    /// under, with its own limit - see <see cref="Wip.EpicsAsync"/>. The
    /// section's sentence wins where both limits are over, the more general
    /// fact, said first.
    /// </param>
    private static string? WipFold(
        WipSection? wip, EfHatchIssue issue, EfHatchStatus from, EfHatchStatus to,
        IReadOnlyDictionary<long, EpicLimit> epics)
    {
        if (wip is null) return null;
        if (wip.Inside(from.Id)) return null;
        if (!wip.Inside(to.Id)) return null;
        if (wip.SliceFor(issue.Type) is not { } slice) return null;

        if (slice.Limit is { } limit)
        {
            var room = slice.Load - (slice.Counted(issue) ? 1 : 0);
            if (room >= limit)
                return $"{Wip.Sentence(room, limit, slice.Types)} - nothing more is pulled in until something leaves";
        }

        if (slice.Counts("story") && issue.ParentId is { } parentId && epics.TryGetValue(parentId, out var epic))
        {
            var epicRoom = wip.LoadUnder(epic.Id) - (slice.Counted(issue) ? 1 : 0);
            if (epicRoom >= epic.Limit)
                return $"{Wip.EpicSentence(epic.Key, epicRoom, epic.Limit, slice.Types)} - nothing more of it is pulled in until one leaves";
        }

        return null;
    }

    /// <summary>
    /// The verdicts on these issues, grouped by issue - one query for however
    /// many, and ordered by repository so the sentence a fold prints is stable.
    /// </summary>
    public async Task<Dictionary<long, IReadOnlyList<EfHatchMergeCheck>>> MergeChecksAsync(
        List<long> issueIds, CancellationToken ct) =>
        (await db.MergeChecks.AsNoTracking()
            .Where(m => issueIds.Contains(m.IssueId))
            .OrderBy(m => m.Canonical)
            .ToListAsync(ct))
        .GroupBy(m => m.IssueId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchMergeCheck>)g.ToList());

    /// <summary>The build verdicts on these issues, grouped by issue - one query, ordered by repository as the merge checks are.</summary>
    public async Task<Dictionary<long, IReadOnlyList<EfHatchBuildCheck>>> BuildChecksAsync(
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
    public static string KindOf(
        EfHatchIssue issue, EfHatchStatus from, EfHatchStatus? to,
        IReadOnlyList<EfHatchMergeCheck> merges, IReadOnlyList<EfHatchBuildCheck> builds) =>
        to is not null && to.Id == from.Id
            ? ReviewWork.Judge(issue, merges, builds).Kind
            : WorkKinds.Advance;

    /// <summary>
    /// Why an agent should not be spawned for this issue, or null when it
    /// should. Every fold in one place and in one order, so that the scan
    /// prints the same sentence the walk acted on.
    /// </summary>
    /// <remarks>
    /// <para>The order is what it costs to change the answer, most fundamental
    /// first: a terminal column, no column after this one, a terminal next
    /// column, a live claim, a ready date, an assignee, an unanswered question,
    /// a repository the caller has no checkout of, an unmet dependency, an open
    /// child, a full WIP section or an epic at its own limit, the verdict on an
    /// issue's branch, and last a missing playbook. A column with nowhere an
    /// agent may go is a fact about the board and no argument alters it; a
    /// ready date needs time; a question needs a person; a repository needs a
    /// clone; a dependency needs other work to land; an open child needs its
    /// own session or its own close; a full section or a full epic each need
    /// other work to leave; a clean branch with a build that passes, is
    /// running or has not been read needs nothing at all; and a missing
    /// playbook needs the operator, which is last because it is only worth
    /// saying about an issue that is otherwise a candidate.</para>
    ///
    /// <para>An epic's own "open child" rule sits beside the generic one and
    /// reads differently in two ways: it ignores a deferred child entirely
    /// rather than counting it open, because shelving a story is the
    /// operator's call and not a gap the epic should be held for; and it
    /// reaches the epic wherever it stands in the WIP section, not only the
    /// column where code is written, because an epic is never itself the
    /// thing being implemented.</para>
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
    /// <param name="letGo">
    /// How many increments in a row let this issue go without moving it - see
    /// <see cref="LetGo.Count"/>. Read only when the issue is marked - a
    /// stalled issue past its own resume window is held rather than resumed
    /// once this reaches <see cref="IssueClaims.StallResumeLimit"/>.
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
    /// <param name="family">
    /// Every issue's children and which of them are open - see
    /// <see cref="FamilyGate"/>. Not the loop's policy: a story's tasks are the
    /// work, so a named dispatch is folded by an open one too, and somebody who
    /// disagrees closes or defers the child.
    /// </param>
    /// <param name="claimed">
    /// Who is holding this issue, and what the caller holds itself. Not the
    /// loop's policy either, and for the same reason: a second agent sent at a
    /// ticket somebody is mid-increment on is the failure the claim exists to
    /// prevent, whoever asked for it.
    /// </param>
    /// <param name="implementation">
    /// The column a dependency gates the move into, and the only one it gates.
    /// The repository fold below is wider: it applies to every move a session
    /// is spawned for, and to a hop only when it lands here.
    /// </param>
    /// <param name="repos">
    /// What the caller told the dispatcher about its own checkouts. A fact
    /// about the work rather than the pass's policy, so a named dispatch is
    /// refused by it too - see <see cref="RepositoryFold"/>.
    /// </param>
    /// <param name="hop">
    /// Whether this issue is express and stands in a column marked
    /// <see cref="EfHatchStatus.ExpressSkips"/> - see
    /// <see cref="EfHatchIssue.Express"/>. Checked last, after every other fold,
    /// because a hop answers only "does this column still need a session" and
    /// every other reason to hold the issue back still applies to it exactly as
    /// it applies to any other issue.
    /// </param>
    /// <param name="statuses">
    /// The board, for the same board-order comparison <see cref="FamilyGate.SiblingInFlight"/>
    /// runs to decide <paramref name="hop"/> - needed again here so the
    /// ParentPulls fold below can name which of its two reasons applies,
    /// without re-deriving the rule <see cref="FamilyGate.Pulls"/> already
    /// encodes.
    /// </param>
    /// <param name="wip">
    /// How full the WIP section is, or null where the board has never turned it
    /// on - see <see cref="Wip.LoadAsync"/>. A fact about the board, not the
    /// loop's policy, so a named dispatch is folded by it too. Read once per
    /// pass and once per named dispatch, always over the whole board, whatever
    /// a scope narrows the candidates to.
    /// </param>
    /// <param name="epics">
    /// Every epic this issue's own parent might be, with its own limit - see
    /// <see cref="Wip.EpicsAsync"/>. A fact about the board, like
    /// <paramref name="wip"/>, and read the same way: once per pass over
    /// whichever parent ids the candidates hold, once per named dispatch over
    /// this issue's own.
    /// </param>
    public static string? Blocked(
        EfHatchIssue issue,
        EfHatchStatus from,
        EfHatchStatus? to,
        EfHatchPlaybook? playbook,
        int waiting,
        int letGo,
        LoopScope? loop,
        PaceReadings? pace,
        DependencyGate gate,
        FamilyGate family,
        ClaimGate claimed,
        EfHatchStatus? implementation,
        AssigneeDto? assignee,
        RepositoryDeclaration repos,
        IReadOnlyList<EfHatchMergeCheck> verdicts,
        IReadOnlyList<EfHatchBuildCheck> builds,
        bool hop,
        List<EfHatchStatus> statuses,
        WipSection? wip,
        IReadOnlyDictionary<long, EpicLimit> epics)
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

        if (issue.Held)
            return "held - a person told the loop to leave this alone until they say otherwise";

        if (issue.StalledAt is { } stalledAt)
        {
            var resumeSeconds = claimed.Claims!.StallResumeSeconds;
            var why = issue.StalledWhy is { Length: > 0 } w ? $" ({w})" : "";

            if (resumeSeconds <= 0)
                return $"stalled{why} - this board never resumes a marked ticket unattended; a person moves it";

            if (claimed.Now - stalledAt < TimeSpan.FromSeconds(resumeSeconds))
                return $"stalled {Minutes((int)(claimed.Now - stalledAt).TotalSeconds)} ago{why} and resumes itself after {Minutes(resumeSeconds)}";

            if (letGo >= claimed.Claims!.StallResumeLimit)
                return $"stalled {letGo} times in a row, at this board's limit of {claimed.Claims!.StallResumeLimit} - held for a person now, not resumed again";
        }

        var (pausedLevel, pausedFrom) = gate.Effective(issue.Id);
        if (pausedLevel == PriorityLevels.Paused)
            return pausedFrom is null
                ? "paused - a person set it aside, and nothing picks it up until they set it back"
                : $"paused from {pausedFrom} - a person set it aside, and nothing picks it up until they set it back";

        if (loop is not null)
        {
            if (IsWaiting(issue, loop.Today, loop.OffsetMinutes))
                return $"not workable until {IssueMoment.Format(issue.ReadyAt, issue.ReadyAtHasTime)}";

            // Loop policy, not a fact about the issue - a hop is exempt, the
            // same way it is exempt from needing a playbook, and a named
            // dispatch (work/{key}) never sees loop at all. Two gates, same
            // shape, each reading its own level's judgement off the one pace
            // reading. Both windows reset on their own, so both are the
            // board's to clear.
            if (!hop && pausedLevel == PriorityLevels.Economy && pace?.Economy.Fold is { } economyFold)
                return economyFold;

            if (!hop && pausedLevel == PriorityLevels.Low && pace?.Low.Fold is { } lowFold)
                return lowFold;

            if (loop.Mine)
            {
                // "Mine held" (docs/hatch.md): this is a new, separate fold
                // beside the person-assignee one below, not a rewrite of it -
                // plain go-to-work keeps skipping a person's own tickets
                // exactly as it always has.
                var isMine =
                    (assignee is { Kind: ActorKind.Person } person && loop.Principal is { Kind: ActorKind.Person } p && person.Id == p.Id) ||
                    (assignee is { Kind: ActorKind.Key } && assignee.Id == loop.CallerKeyId);
                if (!isMine)
                    return assignee is null
                        ? "assigned to nobody - a --mine pass takes only your own"
                        : $"assigned to {assignee.Name}, not to you";
            }
            else if (assignee?.Kind == ActorKind.Person)
            {
                // A person's name on a ticket takes it off the night shift, and
                // only a person's: an issue assigned to a key is exactly the
                // thing an agent should pick up, and one whose assignee no
                // longer resolves is not assigned at all - the liveness rule
                // reaching the dispatcher without a line of its own.
                return $"assigned to {assignee.Name} - an unattended pass leaves a person's work alone";
            }
        }

        if (waiting > 0)
            return $"{waiting} unanswered question{(waiting == 1 ? "" : "s")} - it is waiting on a person, not on an agent";

        // A repository matters wherever a session is spawned, because every
        // session runs in the primary repository's checkout: every move but a
        // hop, which runs no session and needs no checkout - unless it lands in
        // the implementation column, where the fold has always applied.
        // Dependencies gate only the move into implementation - a pull request
        // that already exists is not held back by what its ticket once waited on.
        var conflicts = to.Id == from.Id;

        if (!hop || to.Id == implementation?.Id)
        {
            if (RepositoryFold(issue, repos) is { } repoBlock) return repoBlock;
        }

        if (to.Id == implementation?.Id)
        {
            if (gate.Unmet(issue.Id) is { Count: > 0 } waitingOn) return WaitingOn(waitingOn);
        }

        // The mirror of the dependency fold above: that one gates the move in,
        // this one gates the move out. An issue standing in the implementation
        // column with a child that is not yet closed is not itself the work -
        // its children are - so it is folded rather than carried into review.
        // An epic has its own version of this rule, just below, with its own
        // sentence and its own scope - this one is guarded off epics so the
        // two do not both speak for the same issue.
        if (issue.Type != "epic" && from.Id == implementation?.Id && family.OpenChildren(issue.Id).Count > 0)
            return "its children are the work, and some are still open";

        // An epic is verified by its own stories, not merely cleared off one
        // column: it is held wherever it stands in the WIP section, not only
        // the implementation column, and a deferred child does not count
        // against it the way OpenChildren does for everything else above -
        // the operator shelving a story is not a gap in the epic (HA-114,
        // Taken here). Never on the move to itself: an epic in review is
        // folded by a conflict or a failing build, not by this.
        if (issue.Type == "epic" && !conflicts && from.IsWip && family.EpicFold(issue.Id, statuses) is { } epicBlock)
            return epicBlock;

        // A full section, or an epic at its own limit, needs other work to
        // leave, so it is said after a dependency, which needs other work to
        // land, and before a missing playbook, which needs the operator - see
        // Wip.LoadAsync.
        if (WipFold(wip, issue, from, to, epics) is { } full) return full;

        // Last before the playbook, and after the repository: a question needs a
        // person, a repository needs a clone, and a clean branch with a build
        // that is passing, running or unread needs nothing at all - so it is the
        // least useful thing to say about an issue that is folded for a reason
        // somebody can act on.
        if (conflicts && ReviewWork.Judge(issue, verdicts, builds) is { Fold: { } reviewBlock })
            return reviewBlock;

        // The hop answers condition 8 and nothing else: an express issue in a
        // column marked ExpressSkips needs no playbook, because the loop
        // carries it on itself rather than spawning a session for it. Every
        // fold above still applies exactly as it applies to any other issue.
        if (hop) return null;

        // Reached only when hop is false and an epic is otherwise entering the
        // WIP section - one of HopKind's own epic branch conditions has failed:
        // its own parent, or its children. Asked in that order - is anything
        // above it running, then has it anything under it - so a top-level
        // epic, or one whose parent epic is not running, never reads as merely
        // childless, and never silently "no playbook covers this" (HA-113,
        // HA-202).
        if (issue.Type == "epic" && wip is not null && !wip.Inside(from.Id) && to is not null && wip.Inside(to.Id))
        {
            var epicParent = family.ParentOf(issue.Id);

            if (epicParent is null)
                return "a top-level epic is moved in by a person - its own column is the signal for everything under it";

            if (epicParent is not { Type: "epic" } || !wip.Inside(epicParent.StatusId))
                return "its parent epic is not running, so nothing pulls it in";

            return FamilyGate.NothingUnder;
        }

        // A column that pulls its children is never "no playbook covers
        // this" - it is one of these two, naming which of FamilyGate.Pulls's
        // two conditions is unmet. A childless issue in such a column (e.g.
        // plain Backlog) has nothing to pull, so it falls through unchanged.
        if (from.ParentPulls && issue.ParentId is { } parentId)
        {
            return family.ParentStarted(parentId, statuses)
                ? "a sibling is already in flight, so only one child is pulled through at a time"
                : "its parent has not reached the implementation column, so nothing pulls it forward yet";
        }

        return playbook is null
            ? $"no playbook covers \"{from.Name}\" to \"{to.Name}\" for {An(issue.Type)} - add one on the Playbooks page"
            : null;
    }

    /// <summary>
    /// Whether this issue crosses the column it stands in with no session, and
    /// which of <see cref="HopKinds"/> carried it. Asked by the scan, a named
    /// dispatch and the write itself, so the three cannot disagree about what a
    /// hop is.
    /// </summary>
    /// <remarks>
    /// Asked in order: an express issue in a ticked column; an epic standing
    /// outside the WIP section whose next column is inside it, with something
    /// filed under it and its own direct parent a running epic; a story or bug
    /// in a ticked column whose parent is a running epic; and last, unrelated
    /// to the WIP section, a child a ParentPulls column pulls (HA-149). A
    /// top-level epic - one with no parent, or a parent that is not a running
    /// epic - is never carried by the second arm: only a person moves it in
    /// (HA-202). The order matters only where a board ticks both ExpressSkips
    /// and ParentPulls on the same column and an epic stands exactly in the
    /// implementation column - see HA-113's own decision on the overlap. Never
    /// on a move that ends where it starts: a review self-move is never a hop
    /// for the first three, by construction.
    /// </remarks>
    public static string? HopKind(
        EfHatchIssue issue, EfHatchStatus from, EfHatchStatus? to, FamilyGate family,
        List<EfHatchStatus> statuses, WipSection? wip, FamilyGate.Parent? parent)
    {
        var crosses = to is not null && to.Id != from.Id;

        if (crosses && issue.Express && from.ExpressSkips) return HopKinds.Express;

        if (crosses && wip is not null)
        {
            if (issue.Type == "epic" && !wip.Inside(from.Id) && wip.Inside(to!.Id)
                && family.Children(issue.Id).Count > 0
                && parent is { Type: "epic" } parentEpic && wip.Inside(parentEpic.StatusId))
                return HopKinds.Epic;

            if (issue.Type is "story" or "bug" && from.ExpressSkips
                && parent is { Type: "epic" } p && wip.Inside(p.StatusId))
                return HopKinds.Under;
        }

        if (from.ParentPulls && family.Pulls(issue, statuses)) return HopKinds.Parent;

        return null;
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
    /// beats a row naming only its shape, which beats a bare row naming
    /// neither, and ties go to the older row - so adding a specific rule never
    /// requires editing the general one.
    /// </summary>
    public async Task<EfHatchPlaybook?> MatchAsync(int from, int to, string type, bool isParent, CancellationToken ct) =>
        Match(
            await db.Playbooks.AsNoTracking()
                .Include(p => p.FromStatus)
                .Include(p => p.ToStatus)
                .Where(p => p.FromStatusId == from && p.ToStatusId == to)
                .ToListAsync(ct),
            from, to, type, isParent);

    /// <summary>
    /// The same rule against rows already in hand, which is how a scan matches
    /// a whole board's worth of transitions without a query a row. The matrix
    /// is small enough to read whole and the tie-break is arithmetic.
    /// </summary>
    public static EfHatchPlaybook? Match(List<EfHatchPlaybook> playbooks, int from, int to, string type, bool isParent) =>
        playbooks
            .Where(p => p.FromStatusId == from && p.ToStatusId == to && p.Covers(type, isParent))
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

    public Task<List<EfHatchStatus>> OrderedStatusesAsync(CancellationToken ct) =>
        db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
}

/// <summary>One issue the pass looked at, and what it decided.</summary>
public sealed record ScanRow(
    EfHatchIssue Issue, EfHatchStatus From, EfHatchStatus? To, string? Blocked, string Kind, bool Hop,
    string? HopKind = null, string? HopUnder = null, string? ClearNote = null,
    int EffectivePriority = PriorityLevels.Normal, string? EffectiveFrom = null);

/// <summary>
/// A finished pass, or the argument it would not accept. A refusal carries
/// the sentence and nothing else; both endpoints turn it into the same 400.
/// </summary>
public sealed record Scan(
    List<EfHatchStatus> Statuses, List<ScanRow> Rows, LoopScope? Loop, DependencyGate Gate,
    FamilyGate Family, ClaimGate Claims, RepositoryDeclaration Repos, WipSection? Wip,
    IReadOnlyDictionary<long, EpicLimit> Epics, string? Failure, PaceReadings? Pace = null)
{
    public static Scan Refused(string why) =>
        new(
            [], [], null, DependencyGate.None, FamilyGate.None, ClaimGate.None, RepositoryDeclaration.Undeclared, null,
            new Dictionary<long, EpicLimit>(), why);
}

/// <summary>
/// The claims on the board, judged against one instant - and the one token
/// whose own claim does not count as somebody else's.
/// </summary>
/// <remarks>
/// Built once per pass and once per named dispatch. An issue's own claim
/// comes out of the rows the scan already materialised: <see cref="Dispatch.ScanAsync"/>
/// loads whole <see cref="EfHatchIssue"/> entities, so the seven claim
/// columns arrive for free. A relative's claim is the
/// <see cref="ClaimLineage"/>, two queries a pass rather than two a row -
/// the tree and the live claims, read against the same instant.
///
/// <para>The issue's own claim is named first. The relative's is named only
/// where the issue has none live, so a fold that printed one sentence
/// yesterday prints the same one today.</para>
/// </remarks>
public sealed record ClaimGate(IssueClaims? Claims, DateTimeOffset Now, Guid? HeldToken, ClaimLineage Lineage)
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
/// <see cref="Dispatch.Blocked"/>.
/// </summary>
/// <param name="Mine">Whether this pass takes only the caller's own tickets.</param>
/// <param name="Principal">
/// Whose tickets "own" means, resolved once for the pass. Always non-null
/// when <paramref name="Mine"/> is true - <see cref="Dispatch.ScanAsync"/> refuses
/// the scan before this is ever constructed with one and the other not.
/// </param>
/// <param name="CallerKeyId">The calling key's own id, when the caller is a key - a ticket assigned to it is the caller's own too.</param>
public sealed record LoopScope(long Today, int OffsetMinutes, bool Mine, Actor? Principal, Guid? CallerKeyId);

// ---- Which checkout the runner has ----

/// <summary>
/// What a caller told the dispatcher about its own checkouts, or nothing
/// at all. <see cref="Undeclared"/> is the sentinel that keeps "declared
/// nothing" distinct from "declared, and happens to have none of the
/// three set" - a request carrying none of <c>remote</c>, <c>standing</c>
/// or <c>clones</c> opts out of the fold entirely, which is what keeps an
/// older CLI and the issue page working unchanged.
/// </summary>
public sealed record RepositoryDeclaration(IReadOnlyDictionary<string, string>? ByCanonical, bool Standing, bool Clones)
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

// ---- Priority ----

/// <summary>
/// The parent/key/priority of every issue, read once, and the question
/// "what priority is live here" answered against it by walking to the
/// nearest ancestor - including the issue itself - that is not Normal.
/// </summary>
public sealed class PriorityTree
{
    /// <summary>An empty tree, for a caller that needs the shape but has nothing loaded - so <c>DependencyGate.None</c> needs no dictionary literal of its own.</summary>
    public static readonly PriorityTree Empty = new(new());

    private readonly Dictionary<long, (long? ParentId, string Key, int Priority)> _rows;

    internal PriorityTree(Dictionary<long, (long?, string, int)> rows) => _rows = rows;

    public static async Task<PriorityTree> ForAsync(HatchContext db, CancellationToken ct)
    {
        var rows = await db.Issues.AsNoTracking()
            .Select(i => new { i.Id, i.ParentId, i.Priority, ProjectKey = i.Project!.Key, i.Number })
            .ToListAsync(ct);

        return new PriorityTree(rows.ToDictionary(
            r => r.Id,
            r => ((long?)r.ParentId, IssueKey.Format(r.ProjectKey, r.Number), r.Priority)));
    }

    internal bool TryGet(long id, out (long? ParentId, string Key, int Priority) row) => _rows.TryGetValue(id, out row);

    /// <summary>
    /// The priority live on this issue: its own, if set, or the nearest
    /// ancestor's that is not Normal. <c>FromKey</c> is null when the level
    /// found is the issue's own, or when the walk never leaves Normal.
    /// </summary>
    public (int Level, string? FromKey) Effective(long issueId)
    {
        var seen = new HashSet<long>();
        long? at = issueId;

        while (at is { } id && seen.Add(id))
        {
            if (TryGet(id, out var row) && row.Priority != PriorityLevels.Normal)
                return (row.Priority, id == issueId ? null : row.Key);

            at = TryGet(id, out var found) ? found.ParentId : null;
        }

        return (PriorityLevels.Normal, null);
    }
}

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
public sealed class DependencyGate
{
    /// <summary>A gate that blocks nothing, for a refused scan - so <c>Scan.Gate</c> is never null and no call site needs a <c>!</c>.</summary>
    public static readonly DependencyGate None = new([], PriorityTree.Empty);

    private readonly Dictionary<long, List<string>> _unmetByIssue;
    private readonly PriorityTree _tree;

    private DependencyGate(
        Dictionary<long, List<string>> unmetByIssue, PriorityTree tree)
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

        var tree = await PriorityTree.ForAsync(db, ct);

        return new DependencyGate(
            unmet.GroupBy(r => r.IssueId).ToDictionary(
                g => g.Key,
                g => g.Select(r => IssueKey.Format(r.ProjectKey, r.Number)).ToList()),
            tree);
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
                var holder = id == issueId ? null : _tree.TryGet(id, out var row) ? row.Key : null;
                return blockers.Select(b => new UnmetEdge(b, holder)).ToList();
            }

            at = _tree.TryGet(id, out var found) ? found.ParentId : null;
        }

        return [];
    }

    /// <summary>The priority live on this issue - see <see cref="PriorityTree.Effective"/>.</summary>
    public (int Level, string? FromKey) Effective(long issueId) => _tree.Effective(issueId);
}

/// <summary>
/// One thing being waited on. <see cref="HolderKey"/> is null when the edge
/// is the issue's own, and the ancestor's key when it is not.
/// </summary>
public sealed record UnmetEdge(string BlockerKey, string? HolderKey);

// ---- Family ----

/// <summary>
/// Every issue's direct children, read once as <c>(Id, ParentId, StatusId)</c>
/// in board order, and which of them are open - the input to the fold that
/// keeps an issue with unfinished children out of review.
/// </summary>
public sealed class FamilyGate
{
    /// <summary>A gate that folds nothing, for a refused scan - so <c>Scan.Family</c> is never null.</summary>
    public static readonly FamilyGate None = new([], [], [], []);

    /// <summary>
    /// The sentence for an issue with nothing filed under it at all - shared
    /// by every fold that reaches a childless parent, written once here.
    /// </summary>
    public const string NothingUnder = "nothing is filed under it - an epic runs its stories, and it has none";

    /// <summary>One issue's own parent, as <see cref="ParentOf"/> answers it - see HA-113.</summary>
    public sealed record Parent(string Key, string Type, int StatusId);

    private readonly Dictionary<long, List<long>> _children;
    private readonly HashSet<long> _open;
    private readonly Dictionary<long, int> _statusById;
    private readonly Dictionary<long, (long? ParentId, string Type, string Key, int StatusId)> _byId;

    private FamilyGate(
        Dictionary<long, List<long>> children, HashSet<long> open, Dictionary<long, int> statusById,
        Dictionary<long, (long? ParentId, string Type, string Key, int StatusId)> byId)
    {
        _children = children;
        _open = open;
        _statusById = statusById;
        _byId = byId;
    }

    public static async Task<FamilyGate> ForAsync(
        HatchContext db, List<EfHatchStatus> statuses, CancellationToken ct)
    {
        var terminal = statuses.Where(s => s.IsTerminal).Select(s => s.Id).ToHashSet();

        var rows = await db.Issues.AsNoTracking()
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.ParentId, i.StatusId, i.Type, ProjectKey = i.Project!.Key, i.Number })
            .ToListAsync(ct);

        var children = new Dictionary<long, List<long>>();
        var open = new HashSet<long>();
        var statusById = new Dictionary<long, int>();
        var byId = new Dictionary<long, (long?, string, string, int)>();
        foreach (var row in rows)
        {
            if (!terminal.Contains(row.StatusId)) open.Add(row.Id);
            statusById[row.Id] = row.StatusId;
            byId[row.Id] = (row.ParentId, row.Type, IssueKey.Format(row.ProjectKey, row.Number), row.StatusId);

            if (row.ParentId is not { } parent) continue;
            if (!children.TryGetValue(parent, out var siblings)) children[parent] = siblings = [];
            siblings.Add(row.Id);
        }

        return new FamilyGate(children, open, statusById, byId);
    }

    /// <summary>The direct children of this issue, in board order.</summary>
    public IReadOnlyList<long> Children(long issueId) =>
        _children.TryGetValue(issueId, out var kids) ? kids : [];

    /// <summary>
    /// This issue's own parent - its key, type and column - or null where it
    /// has none, or where its parent id does not resolve against the issues
    /// loaded for this pass. Read off the same rows <see cref="Children"/> and
    /// <see cref="OpenChildren"/> already hold, not a second query.
    /// </summary>
    public Parent? ParentOf(long issueId)
    {
        if (!_byId.TryGetValue(issueId, out var row) || row.ParentId is not { } parentId) return null;
        return _byId.TryGetValue(parentId, out var p) ? new Parent(p.Key, p.Type, p.StatusId) : null;
    }

    /// <summary>
    /// The direct children whose column is not terminal. A deferred child
    /// counts as open - <see cref="Deferrals"/> and <c>EfHatchIssueDependency</c>
    /// already hold that a shelved blocker does not satisfy a gate, and this is
    /// the same rule, not a new one.
    /// </summary>
    public IReadOnlyList<long> OpenChildren(long issueId) =>
        Children(issueId).Where(_open.Contains).ToList();

    /// <summary>
    /// Why an epic should not move on: how many of its direct children are
    /// not yet done, counting a deferred one out entirely rather than as open
    /// - the rollup's own rule (<c>Rollup.cs</c>: a shelved child is neither
    /// finished nor outstanding) applied to direct children only, which is
    /// the opposite of what <see cref="OpenChildren"/> does for every other
    /// fold. Null where every counted child is terminal, including where
    /// every child is deferred and none are counted at all.
    /// </summary>
    public string? EpicFold(long issueId, List<EfHatchStatus> statuses)
    {
        var children = Children(issueId);
        if (children.Count == 0) return NothingUnder;

        var counted = 0;
        var open = 0;
        foreach (var childId in children)
        {
            if (!_statusById.TryGetValue(childId, out var statusId)) continue;
            if (statuses.FirstOrDefault(s => s.Id == statusId) is not { } status) continue;
            if (status.IsDeferred) continue;

            counted++;
            if (!status.IsTerminal) open++;
        }

        if (open == 0) return null;

        return counted == 1
            ? "its only child is not done - an epic is verified once its stories are"
            : $"{open} of its {counted} children {(open == 1 ? "is" : "are")} not done - an epic is verified once its stories are";
    }

    /// <summary>Whether the parent's own column is Columns.Implementation.</summary>
    public bool ParentStarted(long parentId, List<EfHatchStatus> statuses) =>
        _statusById.TryGetValue(parentId, out var statusId)
        && Columns.Implementation(statuses) is { } implementation
        && statusId == implementation.Id;

    /// <summary>
    /// Whether a sibling of this issue is in flight - see HA-149: standing
    /// strictly right, in board order, of the column this issue would be pulled
    /// into, and not terminal.
    /// </summary>
    public bool SiblingInFlight(EfHatchIssue issue, List<EfHatchStatus> statuses)
    {
        if (issue.ParentId is not { } parentId) return false;
        if (statuses.FirstOrDefault(s => s.Id == issue.StatusId) is not { } from) return false;
        if (Columns.Target(statuses, from) is not { } target) return false;

        var board = Columns.Board(statuses);
        var targetIndex = board.FindIndex(s => s.Id == target.Id);

        foreach (var siblingId in Children(parentId))
        {
            if (siblingId == issue.Id) continue;
            if (!_statusById.TryGetValue(siblingId, out var siblingStatusId)) continue;

            var siblingIndex = board.FindIndex(s => s.Id == siblingStatusId);
            if (siblingIndex <= targetIndex) continue; // not pulled past yet, or deferred (-1)
            if (board[siblingIndex].IsTerminal) continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether this issue's column may carry it one column right with no
    /// session - see HA-149.
    /// </summary>
    public bool Pulls(EfHatchIssue issue, List<EfHatchStatus> statuses) =>
        issue.ParentId is { } parentId
        && ParentStarted(parentId, statuses)
        && !SiblingInFlight(issue, statuses);
}
