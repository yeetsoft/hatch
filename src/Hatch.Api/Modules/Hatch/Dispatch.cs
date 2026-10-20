using System.Globalization;
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
    /// <param name="principalOverride">
    /// Whose tickets <paramref name="mine"/> means, already resolved - so
    /// <see cref="IActorDirectory.PrincipalAsync"/> is not asked again. Null,
    /// every caller before this one, keeps today's live-caller resolution
    /// exactly as it was. For a caller answering on a named runner's behalf,
    /// the row's own owner is not whoever the HTTP caller happens to be.
    /// </param>
    public async Task<Scan> ScanAsync(
        int offsetMinutes, string? ancestorKey, Guid? heldToken, RepositoryDeclaration repos, bool mine,
        CancellationToken ct, Actor? principalOverride = null)
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
            if (principalOverride is not null)
            {
                principal = principalOverride;
            }
            else
            {
                principal = await actors.PrincipalAsync(ct);
                if (await actors.MeAsync(ct) is { Kind: ActorKind.Key } me) callerKeyId = me.Id;
                if (principal is null)
                    return Scan.Refused("this key belongs to nobody, so it has no tickets of its own - an admin sets its owner on the API Keys page");
            }
        }

        var loop = new LoopScope(DayNumber(now, offsetMinutes), offsetMinutes, mine, principal, callerKeyId);

        // One instant for the whole pass. A scan in which the clock moved
        // between two rows could fold one card and not its neighbour for a
        // reason nobody could reconstruct afterwards.
        // Read once for the pass: the claim lineage, the dependency gate and the
        // family gate all answer one-PR questions from it.
        var tree = await PriorityTree.ForAsync(db, ct);

        var claimed = new ClaimGate(claims, now, heldToken, await claims.LineageAsync(db, now, ct, tree));

        var gate = await DependencyGate.ForAsync(db, statuses, ct, tree);
        var family = await FamilyGate.ForAsync(db, statuses, ct, tree);
        var wip = await Wip.LoadAsync(db, claims, statuses, now, ct);
        var open = await Questions.WaitingCountsAsync(db, ct);
        var playbooks = await db.Playbooks.AsNoTracking()
            .Include(p => p.FromStatus)
            .Include(p => p.ToStatus)
            .ToListAsync(ct);
        var boardWidePlaybooks = playbooks.Where(p => p.ProjectId == null).ToList();

        // A repo-sourced project's own resolution - the primary repository, its
        // trunk, and the .hatch/playbooks.yaml copy on it - never varies by issue
        // or transition, so it is resolved once per project per pass rather than
        // once per candidate, the first time a project is met below.
        var repoSourced = new Dictionary<int, RepoSourcedPlaybooks?>();
        var repoSourcedRows = new Dictionary<int, IReadOnlyList<EfHatchPlaybook>>();

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

        // What runners have found about the branches of the issues in review
        // and the issues waiting to merge, read once for the pass and
        // grouped, the way `open` is. Only those two columns are asked
        // about: they are the only moves a verdict gates.
        var review = Columns.AwaitingReview(statuses);
        var merges = Columns.Merges(statuses);
        var checkedIds = (review is not null && byColumn.TryGetValue(review.Id, out var inReview) ? inReview : [])
            .Concat(merges is not null && byColumn.TryGetValue(merges.Id, out var inMerges) ? inMerges : [])
            .Select(i => i.Id).ToList();
        var verdicts = checkedIds.Count > 0 ? await MergeChecksAsync(checkedIds, ct) : [];
        var builds = checkedIds.Count > 0 ? await BuildChecksAsync(checkedIds, ct) : [];
        var landed = review is not null && byColumn.TryGetValue(review.Id, out var inReviewRows)
            ? await LandedAsync(inReviewRows, now, ct)
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

        // The merges column's own order and head, named once for the whole
        // pass - see Train. MergeWork.Judge is asked again inside Blocked
        // below for its own sentence; that is not a second opinion about the
        // order, since both calls read the same verdicts and builds fetched
        // above.
        IReadOnlyDictionary<long, TrainSlot> trainSlots = new Dictionary<long, TrainSlot>();
        if (merges is not null && byColumn.TryGetValue(merges.Id, out var inTrain))
        {
            // A task waiting on its story's pull request has none of its own to
            // merge, so it has no place in the train - and left in, a row with no
            // checks at all would be elected head over a real pull request.
            var merging = inTrain.Where(i => !family.IsWaiting(i.Id)).ToList();
            var judged = merging.ToDictionary(i => i.Id, i => MergeWork.Judge(
                i.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(),
                verdicts.TryGetValue(i.Id, out var v) ? v : [],
                builds.TryGetValue(i.Id, out var b) ? b : [],
                i.PullRequestClosedAt, i.Project!.ChecksRequired, i.PullRequestMergeState, i.PullRequestHeadSha, now));
            trainSlots = Train(merging, judged, effective);
        }

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

        // Unconditional on the implementation column existing - nothing as cheap
        // as StalledAt pre-filters which candidates could be mid-round-trip.
        Dictionary<long, int>? sendBackStreaks = implementation is null
            ? null
            : await SendBackStreak.CountsAsync(db, candidates.Select(i => i.Id).ToList(), implementation.Name, ct);

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

                    if (!repoSourced.TryGetValue(issue.ProjectId, out var projectRepoSourced))
                    {
                        IReadOnlyList<EfHatchPlaybook> fileRows;
                        (projectRepoSourced, fileRows, _) = await ResolveRepoSourcedAsync(issue.Project!, ct);
                        repoSourced[issue.ProjectId] = projectRepoSourced;
                        repoSourcedRows[issue.ProjectId] = fileRows;
                    }

                    var candidatePlaybooks = projectRepoSourced is null
                        ? playbooks
                        : repoSourcedRows[issue.ProjectId].Concat(boardWidePlaybooks).ToList();

                    var playbook = Match(candidatePlaybooks, status.Id, to.Id, issue.Type, family.Children(issue.Id).Count > 0, issue.ProjectId);
                    var waiting = open.GetValueOrDefault(issue.Id, 0);
                    var merged = verdicts.TryGetValue(issue.Id, out var found) ? found : [];
                    var built = builds.TryGetValue(issue.Id, out var foundBuilds) ? foundBuilds : [];
                    var parent = family.ParentOf(issue.Id);
                    var sendBackStreak = sendBackStreaks?.GetValueOrDefault(issue.Id, 0) ?? 0;
                    var hopKind = HopKind(issue, status, to, family, statuses, wip, parent, playbook, sendBackStreak, claimed.Claims!.StallResumeLimit, gate.EffectivePreApproval(issue.Id).Level, merged, built, claimed.Now);
                    var hop = hopKind is not null;
                    var hopUnder = hopKind == HopKinds.Under ? parent?.Key : null;
                    var rowTo = hopKind == HopKinds.Approve ? Columns.Advance(statuses, status)
                        : hopKind == HopKinds.Close ? Columns.Closes(statuses, status)
                        : to;
                    MergeWork.Judgement? landing = landed.TryGetValue(issue.Id, out var landedJudgement) ? landedJudgement : null;
                    var letGo = issue.StalledAt is not null ? letGoCounts?.GetValueOrDefault(issue.Id, 0) ?? 0 : 0;
                    var blocking = BlockedWithKind(
                        issue, status, rowTo, playbook, waiting, letGo, sendBackStreak, loop, pace, gate, family, claimed,
                        implementation, assignees[issue.Id], repos, merged, built, hopKind, statuses, wip, epics,
                        trainSlots.GetValueOrDefault(issue.Id),
                        landing, projectRepoSourced);

                    var blocked = blocking.Sentence;
                    var sentence = blocked;
                    var kind = blocking.Kind;

                    // Nothing at a lower tier is picked up while a strictly
                    // higher one still has clear work - a hop is exempt from
                    // being parked itself, but still becomes the row that
                    // parks the tiers below it.
                    if (blocked is null && !hop && parkedByKey is not null)
                    {
                        sentence = $"{parkedByKey} ranks {parkedByLevel} and is clear - " +
                                   $"nothing at {PriorityLevels.Name(level)} is picked up while higher-ranking work is available";
                        kind = BlockedKinds.Turn;
                    }

                    // The first clear row of this tier. Independent of the park
                    // above: a row already parked was clear before the park
                    // replaced its sentence, and still counts as clear for the
                    // tier below it.
                    if (firstClearThisTier is null && blocked is null)
                        firstClearThisTier = IssueKey.Format(issue.Project!.Key, issue.Number);

                    rows.Add(new ScanRow(
                        issue, status, rowTo, sentence, kind,
                        KindOf(issue, status, rowTo, merged, built, claimed.Now, landing),
                        hop && sentence is null,
                        sentence is null ? hopKind : null,
                        sentence is null ? hopUnder : null,
                        sentence is null && hopKind == HopKinds.Approve
                            ? MergeWork.Judge(issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(), merged, built, issue.PullRequestClosedAt, issue.Project!.ChecksRequired, issue.PullRequestMergeState, issue.PullRequestHeadSha, claimed.Now).Sentence
                            : sentence is null && issue.StalledAt is not null
                            ? ResumedClearNote(issue.StalledAt.Value, claimed.Now, claimed.Claims!.StallResumeSeconds)
                            : sentence is null && effective[issue.Id].Level == PriorityLevels.Economy ? pace?.Economy.ClearNote
                            : sentence is null && effective[issue.Id].Level == PriorityLevels.Low ? pace?.Low.ClearNote
                            : null,
                        effective[issue.Id].Level,
                        effective[issue.Id].FromKey,
                        Model: issue.ModelOverride ?? playbook?.Model,
                        DeniedTools: playbook is null ? null : EfHatchPlaybook.ReadDeniedTools(playbook.DeniedTools) ?? [],
                        WaitsOn: blocking.WaitsOn,
                        AnyOf: blocking.AnyOf));
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
    /// see <see cref="QueueEntryDto.ClearNote"/>: its own mark resumed.
    /// </summary>
    private static string ResumedClearNote(DateTimeOffset stalledAt, DateTimeOffset now, int resumeSeconds) =>
        $"it stalled {Minutes((int)(now - stalledAt).TotalSeconds)} ago and resumes itself after {Minutes(resumeSeconds)}";

    private static string Minutes(int seconds)
    {
        var minutes = seconds / 60;
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    // ---- The train ----

    /// <summary>
    /// One project's merges column, in order: where a row sits, whether it is
    /// the one the train is acting on next, and if not, the key of the one
    /// that is.
    /// </summary>
    public sealed record TrainSlot(int Position, bool IsHead, string? HeadKey);

    /// <summary>
    /// The order of a project's merges-column rows, and which one each
    /// project's train is acting on next - named once per pass rather than
    /// asked per row, so two rows in the same column never disagree about
    /// whose turn it is. One dictionary however many projects
    /// <paramref name="merging"/> spans, so a caller holding two projects'
    /// worth of rows calls this once, not once per project.
    /// </summary>
    /// <remarks>
    /// Ordered within each project the same way every tier in <see cref="ScanAsync"/>
    /// already is: <paramref name="effective"/>'s own level, descending, then
    /// <c>Rank</c>, then <c>Id</c>. The head is the first row in that order
    /// whose <see cref="MergeWork.Judgement.Kind"/> is not
    /// <see cref="WorkKinds.Hold"/> or <see cref="WorkKinds.Pushback"/>; every
    /// other slot in its project carries that head's key, and a project with
    /// no eligible row gets every slot <c>IsHead: false</c> and every
    /// <c>HeadKey</c> null - the honest answer for a merges column that is
    /// all holds.
    ///
    /// <paramref name="judged"/> is the same judgement <see cref="Blocked"/>
    /// asks <see cref="MergeWork.Judge"/> for again below, on the same
    /// verdicts and builds - not a second opinion about the order, which is
    /// precisely the thing the comment on <see cref="ScanAsync"/>'s own six
    /// passes warns against. The duplication is deliberate: threading the
    /// judgement through instead would make this method's answer depend on
    /// which row <see cref="Blocked"/> happened to be judging at the time.
    /// </remarks>
    public static IReadOnlyDictionary<long, TrainSlot> Train(
        IReadOnlyList<EfHatchIssue> merging,
        IReadOnlyDictionary<long, MergeWork.Judgement> judged,
        IReadOnlyDictionary<long, (int Level, string? FromKey)> effective)
    {
        var slots = new Dictionary<long, TrainSlot>();

        foreach (var project in merging.GroupBy(i => i.ProjectId))
        {
            var ordered = project
                .OrderByDescending(i => effective[i.Id].Level)
                .ThenBy(i => i.Rank)
                .ThenBy(i => i.Id)
                .ToList();

            var head = ordered.FirstOrDefault(i => judged[i.Id].Kind is not (WorkKinds.Hold or WorkKinds.Pushback));
            var headKey = head is null ? null : IssueKey.Format(head.Project!.Key, head.Number);

            for (var position = 0; position < ordered.Count; position++)
            {
                var isHead = head is not null && ordered[position].Id == head.Id;
                slots[ordered[position].Id] = new TrainSlot(position + 1, isHead, isHead ? null : headKey);
            }
        }

        return slots;
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
    private static (string Sentence, IReadOnlyList<long> Holders)? WipFold(
        WipSection? wip, EfHatchIssue issue, EfHatchStatus from, EfHatchStatus to,
        IReadOnlyDictionary<long, EpicLimit> epics)
    {
        if (wip is null) return null;
        if (wip.Inside(from.Id)) return null;
        if (!wip.Inside(to.Id)) return null;
        if (wip.SliceFor(issue.Type) is not { } slice) return null;

        // The holders are the issues any one of which leaving would clear the
        // fold; where both limits are over, both sets.
        string? sentence = null;
        var holders = new HashSet<long>();

        if (slice.Limit is { } limit)
        {
            var room = slice.Load - (slice.Counted(issue) ? 1 : 0);
            if (room >= limit)
            {
                sentence = $"{Wip.Sentence(room, limit, slice.Types)} - nothing more is pulled in until something leaves";
                holders.UnionWith(slice.Holders(issue));
            }
        }

        if (slice.Counts("story") && issue.ParentId is { } parentId && epics.TryGetValue(parentId, out var epic))
        {
            var epicRoom = wip.LoadUnder(epic.Id) - (slice.Counted(issue) ? 1 : 0);
            if (epicRoom >= epic.Limit)
            {
                sentence ??= $"{Wip.EpicSentence(epic.Key, epicRoom, epic.Limit, slice.Types)} - nothing more of it is pulled in until one leaves";
                holders.UnionWith(wip.CountedUnder(epic.Id, issue.Id));
            }
        }

        return sentence is null ? null : (sentence, holders.Order().ToList());
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

    /// <summary>The landings recorded for these issues, grouped by issue - one query, ordered by repository as the checks are.</summary>
    public async Task<Dictionary<long, IReadOnlyList<EfHatchLanding>>> LandingsAsync(
        List<long> issueIds, CancellationToken ct) =>
        (await db.Landings.AsNoTracking()
            .Where(l => issueIds.Contains(l.IssueId))
            .OrderBy(l => l.Canonical)
            .ToListAsync(ct))
        .GroupBy(l => l.IssueId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchLanding>)g.ToList());

    /// <summary>The trunk builds the board holds for these repositories - one query.</summary>
    public async Task<IReadOnlyList<EfHatchTrunkBuild>> TrunkBuildsAsync(
        IEnumerable<string> canonicals, CancellationToken ct)
    {
        var wanted = canonicals.Distinct().ToList();
        return await db.TrunkBuilds.AsNoTracking()
            .Where(t => wanted.Contains(t.Canonical))
            .ToListAsync(ct);
    }

    /// <summary>
    /// What <see cref="LandingWork"/> makes of each of these review rows, for
    /// the ones it applies to - a trunk-landing issue with no pull request - and
    /// nothing for the rest. Landings and trunk builds are read once however
    /// many rows there are, and not at all when none applies.
    /// </summary>
    public async Task<Dictionary<long, MergeWork.Judgement>> LandedAsync(
        IEnumerable<EfHatchIssue> reviewIssues, DateTimeOffset now, CancellationToken ct)
    {
        var landing = reviewIssues.Where(LandingWork.Applies).ToList();
        if (landing.Count == 0) return [];

        var landings = await LandingsAsync(landing.Select(i => i.Id).ToList(), ct);
        var trunkBuilds = await TrunkBuildsAsync(
            landings.Values.SelectMany(l => l).Select(l => l.Canonical), ct);

        return landing.ToDictionary(i => i.Id, i => LandingWork.Judge(
            i.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(),
            landings.TryGetValue(i.Id, out var l) ? l : [],
            trunkBuilds, i.Project.ChecksRequired, now));
    }

    /// <summary>
    /// What a move is for. Derived rather than stored: a review dispatch is
    /// exactly the one that starts and ends in the same column, and nothing
    /// compares column names to say so. Which of the two review kinds it is - a
    /// conflict to resolve or a failing build to fix - is what runners have
    /// reported about the branch, and <see cref="ReviewWork"/> says. A column
    /// ticked <see cref="EfHatchStatus.MergesPullRequest"/> is asked the same
    /// way, of <see cref="MergeWork"/> instead - it is never itself a review
    /// dispatch, since nothing claims or works an issue sitting there. A
    /// trunk-landing issue in review has no branch to conflict or fail, so
    /// <paramref name="landing"/>, when <see cref="LandingWork"/> judged it,
    /// names the kind instead.
    /// </summary>
    public static string KindOf(
        EfHatchIssue issue, EfHatchStatus from, EfHatchStatus? to,
        IReadOnlyList<EfHatchMergeCheck> merges, IReadOnlyList<EfHatchBuildCheck> builds, DateTimeOffset now,
        MergeWork.Judgement? landing = null) =>
        from.MergesPullRequest
            ? MergeWork.Judge(
                issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(),
                merges, builds, issue.PullRequestClosedAt, issue.Project!.ChecksRequired, issue.PullRequestMergeState, issue.PullRequestHeadSha, now).Kind
            : to is not null && to.Id == from.Id
                ? landing?.Kind ?? ReviewWork.Judge(issue, merges, builds).Kind
                : WorkKinds.Advance;

    /// <summary>
    /// Why an agent should not be spawned for this issue, or null when it
    /// should. Every fold in one place and in one order, so that the scan
    /// prints the same sentence the walk acted on.
    /// </summary>
    /// <remarks>
    /// <para>The order is what it costs to change the answer, most fundamental
    /// first: a terminal column, no column after this one, a column that
    /// merges a pull request, a terminal next column, a live claim, a ready
    /// date, an assignee, an unanswered question,
    /// a repository the caller has no checkout of, an unmet dependency, an open
    /// child, a full WIP section or an epic at its own limit, the verdict on an
    /// issue's branch, and last a missing playbook. A column with nowhere an
    /// agent may go is a fact about the board and no argument alters it; a
    /// ready date needs time; a question needs a person; a repository needs a
    /// clone; a dependency needs other work to land; an open child needs its
    /// own session or its own close; a full section or a full epic each need
    /// other work to leave; a clean branch with a build that passes, is
    /// running or has not been read needs nothing at all; a column that
    /// merges a pull request needs whatever <see cref="MergeWork"/> itself
    /// names, which is never nothing - it is why that fold sits ahead of
    /// every later one, none of which apply to a column nothing is ever
    /// claimed or worked in; and a missing playbook needs the operator,
    /// which is last because it is only worth saying about an issue that is
    /// otherwise a candidate.</para>
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
    /// <para>A column ticked <see cref="EfHatchStatus.MergesPullRequest"/> is
    /// folded the same way and for the same reason, but ahead of the
    /// terminal and protected checks rather than in their place: such a
    /// column is always followed by one of those two, so without this fold
    /// first its generic sentence would fire and <see cref="MergeWork"/>'s
    /// own would never be seen. Nothing claims or works an issue sitting
    /// there either, so every fold below - the claim, the stalled window,
    /// mine, the assignee, the open question - is short-circuited past the
    /// same way the terminal check already short-circuits past them.</para>
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
    /// <param name="sendBackStreak">
    /// How many times in a row a send-back hop has bounced this issue - see
    /// <see cref="SendBackStreak.Count"/>. A send-back is immediate, so unlike
    /// <paramref name="letGo"/> there is no window to wait out: an issue this
    /// would otherwise send back is held instead once this reaches
    /// <see cref="IssueClaims.StallResumeLimit"/>, the same config key the
    /// stalled fold above already reads.
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
    /// <param name="hopKind">
    /// Which of <see cref="HopKinds"/> carried this hop, or null where this
    /// issue is not one - see <see cref="EfHatchIssue.PreApproval"/>. Checked
    /// last, after every other fold, because a hop answers only "does this
    /// column still need a session" and every other reason to hold the issue
    /// back still applies to it exactly as it applies to any other issue. One
    /// kind, <see cref="HopKinds.Approve"/>, bypasses the protected-column fold
    /// below as well - every other kind is folded by it exactly as a non-hop
    /// issue is.
    /// </param>
    /// <param name="statuses">
    /// The board, for the same sibling check <see cref="FamilyGate.SharedBranchSibling"/>
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
    /// <param name="train">
    /// This row's place in its project's merges column, or null - see
    /// <see cref="Train"/>. Trailing and defaulted so the two single-issue
    /// callers (a hop, or a named dispatch) need no change: neither ever
    /// loads a merges-column row's siblings, since a merges-column row is
    /// never a hop or a session target. Only <see cref="ScanAsync"/>'s own
    /// call passes it.
    /// </param>
    /// <param name="landing">
    /// What <see cref="LandingWork"/> makes of this row, or null where it does
    /// not apply - see <see cref="LandedAsync"/>. Handed in, as the verdicts
    /// are, because this has no db. When set, and the move is the review
    /// column's own, its sentence is the fold.
    /// </param>
    /// <param name="repoSourced">
    /// Null for a project whose playbooks are written on the board - today's
    /// path, byte for byte. Set for a repo-sourced one, see
    /// <see cref="ResolveRepoSourcedAsync"/>: a <see cref="RepoSourcedPlaybooks.Fold"/>
    /// folds every move of the project regardless of what <paramref name="playbook"/>
    /// matched, and a <see cref="RepoSourcedPlaybooks.Trunk"/> with no match
    /// names the file in the fallback sentence instead of only the Playbooks
    /// page.
    /// </param>
    public static string? Blocked(
        EfHatchIssue issue,
        EfHatchStatus from,
        EfHatchStatus? to,
        EfHatchPlaybook? playbook,
        int waiting,
        int letGo,
        int sendBackStreak,
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
        string? hopKind,
        List<EfHatchStatus> statuses,
        WipSection? wip,
        IReadOnlyDictionary<long, EpicLimit> epics,
        TrainSlot? train = null,
        MergeWork.Judgement? landing = null,
        RepoSourcedPlaybooks? repoSourced = null) =>
        BlockedWithKind(
            issue, from, to, playbook, waiting, letGo, sendBackStreak, loop, pace, gate, family, claimed,
            implementation, assignee, repos, verdicts, builds, hopKind, statuses, wip, epics, train, landing,
            repoSourced).Sentence;

    /// <summary>
    /// <see cref="Blocked"/>'s own answer, plus which of <see cref="BlockedKinds"/>
    /// it is - null exactly where the sentence is. Every return point tags a
    /// kind from HA-937's table: <see cref="BlockedKinds.Person"/> and
    /// <see cref="BlockedKinds.Family"/> where a person or a relative holds the
    /// row, <see cref="BlockedKinds.Other"/> only where the table says other.
    /// </summary>
    public static Blocking BlockedWithKind(
        EfHatchIssue issue,
        EfHatchStatus from,
        EfHatchStatus? to,
        EfHatchPlaybook? playbook,
        int waiting,
        int letGo,
        int sendBackStreak,
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
        string? hopKind,
        List<EfHatchStatus> statuses,
        WipSection? wip,
        IReadOnlyDictionary<long, EpicLimit> epics,
        TrainSlot? train = null,
        MergeWork.Judgement? landing = null,
        RepoSourcedPlaybooks? repoSourced = null)
    {
        var hop = hopKind is not null;

        if (from.IsTerminal)
            return new Blocking($"\"{from.Name}\" is where work ends - there is nothing after it", BlockedKinds.Other);

        // Said before the column-after test, which would otherwise refuse this
        // with "there is nowhere for this to go" - true, and no use to somebody
        // reading a queue trying to work out why a ticket they filed is not
        // moving. Nothing comes off the shelf on a pass's say-so: a deferred
        // ticket is waiting on a person deciding it is work again.
        if (from.IsDeferred)
            return new Blocking($"\"{from.Name}\" is deferred - a person puts it back on the board, not a pass", BlockedKinds.Person);

        if (to is null)
            return new Blocking($"there is no column after \"{from.Name}\", so there is nowhere for this to go", BlockedKinds.Other);

        // A task whose work is pushed to its story's branch has nothing left for
        // an agent to do and no pull request of its own to merge: it waits for the
        // story's. Said ahead of the merges column's judgement and the review
        // column's conflict and build checks, both of which read a branch named
        // for this issue's own key and would find none.
        if (gate.WaitingOn(issue.Id, statuses) is { } storyKey)
            return new Blocking(
                $"waiting on {storyKey}'s pull request - its work is pushed to the story's branch, and the story opens the one pull request",
                BlockedKinds.Family,
                gate.SharedStoryOf(issue.Id) is { } storyId ? [storyId] : null);

        if (from.MergesPullRequest)
        {
            var judgement = MergeWork.Judge(
                issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(),
                verdicts, builds, issue.PullRequestClosedAt, issue.Project!.ChecksRequired, issue.PullRequestMergeState, issue.PullRequestHeadSha, claimed.Now,
                Columns.AwaitingReview(statuses)?.Name);

            // A held or pushed-back row already explains itself, and so does
            // the head's own row - its kind word (merge/update/pending) says
            // it is the one being acted on. Only a row that would otherwise
            // read as actionable but is not the head gains the clause.
            if (train is { IsHead: false } slot
                && judgement.Kind is WorkKinds.Merge or WorkKinds.Update or WorkKinds.Pending)
                return new Blocking($"{judgement.Sentence} - waiting its turn behind {slot.HeadKey}, " +
                       $"position {slot.Position} in the train", BlockedKinds.Turn);

            return new Blocking(judgement.Sentence, BlockedKinds.Other);
        }

        // A trunk-landing review row has no branch, so it is never conflict or
        // build work for a session, and like a merge-column row it is never an
        // agent's: the sentence is the judge's, whatever else (a claim, an
        // assignee) would also fold it. Only the review self-move reaches this -
        // a hop out of the column carries another column as its target.
        if (landing is { } landed && to.Id == from.Id)
            return new Blocking(landed.Sentence, BlockedKinds.Other);

        if (to.IsTerminal && hopKind != HopKinds.Close)
            return new Blocking($"the next column is \"{to.Name}\", and only the operator moves work there", BlockedKinds.Person);

        if (to.IsProtected && hopKind != HopKinds.Approve)
            return new Blocking($"the next column is \"{to.Name}\", and only its approvers move work there", BlockedKinds.Person);

        var held = claimed.HeldWithKind(issue);
        if (held.Sentence is { } holder)
            return new Blocking(holder, held.Kind);

        if (issue.Held)
            return new Blocking("held - a person told the loop to leave this alone until they say otherwise", BlockedKinds.Person);

        if (issue.StalledAt is { } stalledAt)
        {
            var resumeSeconds = claimed.Claims!.StallResumeSeconds;
            var why = issue.StalledWhy is { Length: > 0 } w ? $" ({w})" : "";

            if (resumeSeconds <= 0)
                return new Blocking($"stalled{why} - this board never resumes a marked ticket unattended; a person moves it", BlockedKinds.Person);

            if (claimed.Now - stalledAt < TimeSpan.FromSeconds(resumeSeconds))
                return new Blocking($"stalled {Minutes((int)(claimed.Now - stalledAt).TotalSeconds)} ago{why} and resumes itself after {Minutes(resumeSeconds)}", BlockedKinds.Other);

            if (letGo >= claimed.Claims!.StallResumeLimit)
                return new Blocking($"stalled {letGo} times in a row, at this board's limit of {claimed.Claims!.StallResumeLimit} - held for a person now, not resumed again", BlockedKinds.Person);
        }

        var (pausedLevel, pausedFrom) = gate.Effective(issue.Id);
        if (pausedLevel == PriorityLevels.Paused)
            return new Blocking(pausedFrom is null
                ? "paused - a person set it aside, and nothing picks it up until they set it back"
                : $"paused from {pausedFrom} - a person set it aside, and nothing picks it up until they set it back", BlockedKinds.Person);

        if (loop is not null)
        {
            if (IsWaiting(issue, loop.Today, loop.OffsetMinutes))
                return new Blocking($"not workable until {IssueMoment.Format(issue.ReadyAt, issue.ReadyAtHasTime)}", BlockedKinds.Other);

            // Loop policy, not a fact about the issue - a hop is exempt, the
            // same way it is exempt from needing a playbook, and a named
            // dispatch (work/{key}) never sees loop at all. Two gates, same
            // shape, each reading its own level's judgement off the one pace
            // reading. Both windows reset on their own, so both are the
            // board's to clear.
            if (!hop && pausedLevel == PriorityLevels.Economy && pace?.Economy.Fold is { } economyFold)
                return new Blocking(economyFold, BlockedKinds.Other);

            if (!hop && pausedLevel == PriorityLevels.Low && pace?.Low.Fold is { } lowFold)
                return new Blocking(lowFold, BlockedKinds.Other);

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
                    return new Blocking(assignee is null
                        ? "assigned to nobody - a --mine pass takes only your own"
                        : $"assigned to {assignee.Name}, not to you", BlockedKinds.Other);
            }
            else if (assignee?.Kind == ActorKind.Person)
            {
                // A person's name on a ticket takes it off the night shift, and
                // only a person's: an issue assigned to a key is exactly the
                // thing an agent should pick up, and one whose assignee no
                // longer resolves is not assigned at all - the liveness rule
                // reaching the dispatcher without a line of its own.
                return new Blocking($"assigned to {assignee.Name} - an unattended pass leaves a person's work alone", BlockedKinds.Person);
            }
        }

        if (waiting > 0)
            return new Blocking($"{waiting} unanswered question{(waiting == 1 ? "" : "s")} - it is waiting on a person, not on an agent", BlockedKinds.Person);

        // A repository matters wherever a session is spawned, because every
        // session runs in the primary repository's checkout: every move but a
        // hop, which runs no session and needs no checkout - unless it lands in
        // the implementation column, where the fold has always applied.
        // Dependencies gate the move into implementation, and a parent pull out
        // of a ParentPulls column - a pull request that already exists is not
        // held back by what its ticket once waited on.
        var conflicts = to.Id == from.Id;

        if (!hop || to.Id == implementation?.Id)
        {
            if (RepositoryFold(issue, repos) is { } repoBlock) return new Blocking(repoBlock, BlockedKinds.Other);
        }

        if (to.Id == implementation?.Id
            || (from.ParentPulls && issue.ParentId is { } pullParentId && family.ParentStarted(pullParentId, statuses)))
        {
            if (gate.Unmet(issue.Id) is { Count: > 0 } waitingOn)
                return Blocking.Waiting(WaitingOn(waitingOn), BlockedKinds.Dependency, waitingOn.Select(e => e.BlockerId).ToList());
        }

        if (to.Id == implementation?.Id
            && family.SharedBranchSiblingId(issue, statuses) is { } siblingId)
            return new Blocking(FamilyGate.SharedBranchHold(family.KeyOf(siblingId)), BlockedKinds.Family, [siblingId]);

        // The mirror of the dependency fold above: that one gates the move in,
        // this one gates the move out. An issue standing in the implementation
        // column with a child that is not yet closed is not itself the work -
        // its children are - so it is folded rather than carried into review.
        // An epic has its own version of this rule, just below, with its own
        // sentence and its own scope - this one is guarded off epics so the
        // two do not both speak for the same issue.
        if (issue.Type != "epic" && from.Id == implementation?.Id
            && family.OpenWorkChildren(issue.Id) is { Count: > 0 } openChildren)
            return new Blocking("its children are the work, and some are still open", BlockedKinds.Family, openChildren);

        // An epic is verified by its own stories, not merely cleared off one
        // column: it is held wherever it stands at or past the implementation
        // column, not only while leaving it, and a deferred child does not
        // count against it the way OpenChildren does for everything else
        // above - the operator shelving a story is not a gap in the epic
        // (HA-114, Taken here). Never on the move to itself: an epic in
        // review is folded by a conflict or a failing build, not by this.
        if (issue.Type == "epic" && !conflicts && wip is not null
            && Columns.AtOrPastImplementation(statuses, from.Id)
            && family.EpicFold(issue.Id, statuses) is { } epicBlock)
            return Blocking.Waiting(epicBlock, BlockedKinds.Family, family.EpicOpenChildren(issue.Id, statuses));

        // A full section, or an epic at its own limit, needs other work to
        // leave, so it is said after a dependency, which needs other work to
        // land, and before a missing playbook, which needs the operator - see
        // Wip.LoadAsync.
        if (WipFold(wip, issue, from, to, epics) is { } full)
            return Blocking.Waiting(full.Sentence, BlockedKinds.Wip, full.Holders, anyOf: true);

        // Last before the playbook, and after the repository: a question needs a
        // person, a repository needs a clone, and a clean branch with a build
        // that is passing, running or unread needs nothing at all - so it is the
        // least useful thing to say about an issue that is folded for a reason
        // somebody can act on.
        if (conflicts && ReviewWork.Judge(issue, verdicts, builds) is { Fold: { } reviewBlock })
            return new Blocking(reviewBlock, BlockedKinds.Other);

        // A story, bug or task standing left of the implementation column
        // stops where it stands until its own parent reaches it - whatever
        // that column is ticked, not only the ones ticked ParentPulls. Asked
        // above the hop, so the hold beats one: an express issue in a column
        // ticked ExpressSkips, and a story under an epic that has not itself
        // reached the implementation column, are both folded here rather than
        // carried. "epic" is deliberately not in this list - an epic reads its
        // own parent two folds above, with its own sentence. A review
        // self-move can't reach this: the review column is always at or past
        // implementation, so the guard below excludes it already.
        if (issue.Type is "story" or "bug" or "task"
            && issue.ParentId is { } holdParentId
            && !Columns.AtOrPastImplementation(statuses, from.Id)
            && !family.ParentStarted(holdParentId, statuses))
            return new Blocking("its parent has not reached the implementation column, so nothing pulls it forward yet", BlockedKinds.Family, [holdParentId]);

        // The hop answers condition 8 and nothing else: an express issue in a
        // column marked ExpressSkips needs no playbook, because the loop
        // carries it on itself rather than spawning a session for it. Every
        // fold above still applies exactly as it applies to any other issue.
        if (hop) return new Blocking(null, null);

        // Reached only when hop is false and an epic is otherwise entering the
        // implementation column - one of HopKind's own epic branch conditions
        // has failed: its own parent, or its children. Asked in that order -
        // is anything above it running, then has it anything under it - so a
        // top-level epic, or one whose parent epic is not running, never
        // reads as merely childless, and never silently "no playbook covers
        // this" (HA-113, HA-202).
        if (issue.Type == "epic" && wip is not null && !Columns.AtOrPastImplementation(statuses, from.Id)
            && to is not null && Columns.AtOrPastImplementation(statuses, to.Id))
        {
            var epicParent = family.ParentOf(issue.Id);

            if (epicParent is null)
                return new Blocking("a top-level epic is moved in by a person - its own column is the signal for everything under it", BlockedKinds.Person);

            if (epicParent is not { Type: "epic" } || !Columns.AtOrPastImplementation(statuses, epicParent.StatusId))
                return new Blocking("its parent epic is not running, so nothing pulls it in", BlockedKinds.Family, [issue.ParentId!.Value]);

            return new Blocking(FamilyGate.NothingUnder, BlockedKinds.Family);
        }

        // A column that pulls its children is never "no playbook covers
        // this" - this names the one of FamilyGate.Pulls's two conditions
        // still unmet here. The other - a parent that has not reached the
        // implementation column - is the fold above, for every story, bug or
        // task before it can ever reach this one, so this can now only ever
        // speak for a sibling on the story's shared branch, still in flight.
        // A childless issue in such a column (e.g. plain Backlog) has nothing
        // to pull, so it falls through unchanged.
        if (from.ParentPulls && issue.ParentId is { } parentId && family.ParentStarted(parentId, statuses)
            && family.SharedBranchSiblingId(issue, statuses) is { } sharedId)
            return new Blocking(FamilyGate.SharedBranchHold(family.KeyOf(sharedId)), BlockedKinds.Family, [sharedId]);

        if (SendBackEligible(issue, from, family, statuses, playbook) && sendBackStreak >= claimed.Claims!.StallResumeLimit)
            return new Blocking($"sent back {sendBackStreak} times in a row, at this board's limit of " +
                   $"{claimed.Claims!.StallResumeLimit} - held for a person now, not sent back again", BlockedKinds.Person);

        if (playbook?.BriefLimit is { } limit && family.Children(issue.Id).Count == 0 && issue.Description.Length > limit)
            return new Blocking($"its brief is {issue.Description.Length.ToString("N0", CultureInfo.InvariantCulture)} characters, " +
                   $"and this move is spawned on at most {limit.ToString("N0", CultureInfo.InvariantCulture)} - cut it into tasks first", BlockedKinds.Other);

        if (repoSourced is { Fold: { } fold }) return new Blocking(fold, BlockedKinds.Other);
        return playbook is null
            ? new Blocking(repoSourced is { Trunk: { } trunk }
                ? $"no playbook covers \"{from.Name}\" to \"{to.Name}\" for {An(issue.Type)} - add one to .hatch/playbooks.yaml on {trunk}, or a board-wide one on the Playbooks page"
                : $"no playbook covers \"{from.Name}\" to \"{to.Name}\" for {An(issue.Type)} - add one on the Playbooks page", BlockedKinds.Other)
            : new Blocking(null, null);
    }

    /// <summary>
    /// Whether this issue crosses the column it stands in with no session, and
    /// which of <see cref="HopKinds"/> carried it. Asked by the scan, a named
    /// dispatch and the write itself, so the three cannot disagree about what a
    /// hop is.
    /// </summary>
    /// <remarks>
    /// Asked in order: an express issue in a ticked column; an epic still
    /// short of the implementation column, carried on every move right while
    /// it stays short of it, with something filed under it and its own direct
    /// parent a running epic; a story or bug in a ticked column whose parent
    /// is a running epic; a child a ParentPulls column pulls (HA-149); a
    /// childless issue whose brief is over its
    /// playbook's BriefLimit, with <see cref="Columns.SendBack"/> naming a
    /// column strictly left of the one it stands in (HA-394); and last, a
    /// pre-approved issue standing in the review column whose branch
    /// <see cref="MergeWork.Judge"/> would merge or bring current, with a
    /// mergeable column to carry it to (HA-560) - asked last so nothing that is
    /// already one of the first five changes meaning. A top-level epic - one
    /// with no parent, or a parent that is not a running epic - is never
    /// carried by the second arm: only a person moves it in (HA-202). The order
    /// matters only where a board ticks both ExpressSkips and ParentPulls on
    /// the same column and an epic stands exactly in the implementation
    /// column - see HA-113's own decision on the overlap. Never on a move that
    /// ends where it starts: a review self-move is never a hop for the first
    /// three, by construction.
    /// </remarks>
    public static string? HopKind(
        EfHatchIssue issue, EfHatchStatus from, EfHatchStatus? to, FamilyGate family,
        List<EfHatchStatus> statuses, WipSection? wip, FamilyGate.Parent? parent,
        EfHatchPlaybook? playbook, int sendBackStreak, int sendBackStreakLimit, int effectivePreApproval,
        IReadOnlyList<EfHatchMergeCheck> verdicts, IReadOnlyList<EfHatchBuildCheck> builds, DateTimeOffset now)
    {
        var crosses = to is not null && to.Id != from.Id;

        if (crosses && effectivePreApproval >= PreApprovalLevels.Express && from.ExpressSkips) return HopKinds.Express;

        if (crosses && wip is not null)
        {
            if (issue.Type == "epic" && !Columns.AtOrPastImplementation(statuses, from.Id) && wip.Inside(to!.Id)
                && family.Children(issue.Id).Count > 0
                && parent is { Type: "epic" } parentEpic && Columns.AtOrPastImplementation(statuses, parentEpic.StatusId))
                return HopKinds.Epic;

            if (issue.Type is "story" or "bug" && from.ExpressSkips
                && parent is { Type: "epic" } p && Columns.AtOrPastImplementation(statuses, p.StatusId))
                return HopKinds.Under;
        }

        if (from.ParentPulls && family.Pulls(issue, statuses)) return HopKinds.Parent;

        if (SendBackEligible(issue, from, family, statuses, playbook) && sendBackStreak < sendBackStreakLimit)
            return HopKinds.SendBack;

        if (Columns.AwaitingReview(statuses)?.Id == from.Id
            && Columns.Advance(statuses, from) is { MergesPullRequest: true }
            && effectivePreApproval == PreApprovalLevels.PreApproved
            && issue.PullRequestUrl is { Length: > 0 }
            && MergeWork.Judge(
                   issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(),
                   verdicts, builds, issue.PullRequestClosedAt, issue.Project!.ChecksRequired, issue.PullRequestMergeState, issue.PullRequestHeadSha, now).Kind is WorkKinds.Merge or WorkKinds.Update)
            return HopKinds.Approve;

        if (Columns.AwaitingReview(statuses)?.Id == from.Id
            && Columns.Closes(statuses, from) is not null
            && effectivePreApproval == PreApprovalLevels.PreApproved
            && issue.PullRequestUrl is not { Length: > 0 }
            && family.Children(issue.Id).Count > 0
            && (issue.Type == "epic"
                ? family.EpicFold(issue.Id, statuses) is null
                : family.OpenChildren(issue.Id).Count == 0))
            return HopKinds.Close;

        return null;
    }

    /// <summary>
    /// Whether this issue, standing in <paramref name="from"/>, is the one
    /// case <see cref="HopKind"/> and <see cref="Blocked"/> must agree on
    /// exactly: a childless issue whose brief is over its playbook's
    /// <see cref="EfHatchPlaybook.BriefLimit"/>, with <see cref="Columns.SendBack"/>
    /// naming a column strictly left of the one it stands in (HA-394). Neither
    /// caller asks this on its own, so the two cannot drift onto different
    /// issues as this ticket's send-back limit is checked against it.
    /// </summary>
    private static bool SendBackEligible(
        EfHatchIssue issue, EfHatchStatus from, FamilyGate family,
        List<EfHatchStatus> statuses, EfHatchPlaybook? playbook)
    {
        if (playbook?.BriefLimit is not { } limit) return false;
        if (family.Children(issue.Id).Count != 0) return false;
        if (issue.Description.Length <= limit) return false;
        if (Columns.SendBack(statuses) is not { } sendBack) return false;

        var board = Columns.Board(statuses);
        return board.FindIndex(s => s.Id == sendBack.Id) < board.FindIndex(s => s.Id == from.Id);
    }

    /// <summary>
    /// A type with its article, because "a epic" in a sentence a person reads
    /// at a terminal is a typo they have to look past. The four types are
    /// enough for the crude rule to be the right one.
    /// </summary>
    private static string An(string type) =>
        "aeiou".Contains(char.ToLowerInvariant(type[0])) ? $"an {type}" : $"a {type}";

    /// <summary>
    /// A repo-sourced project's own resolution, independent of any issue or
    /// transition: its primary repository, that repository's trunk, and the
    /// <c>.hatch/playbooks.yaml</c> copy read off it. The first element is
    /// null for a project without the setting on - today's path - and
    /// otherwise doubles as the fold <see cref="Blocked"/> needs, whether or
    /// not a good copy was found. The second element is the copy's own rows,
    /// resolved into the same transient, never-persisted shape a candidate
    /// list hands to <see cref="Match"/> - empty wherever the first element
    /// folds every move, and empty too for a good copy that simply names
    /// nothing of its own (<c>playbooks: []</c>).
    /// </summary>
    private async Task<(RepoSourcedPlaybooks? RepoSourced, IReadOnlyList<EfHatchPlaybook> Rows, PlaybookFileReference? File)> ResolveRepoSourcedAsync(
        EfHatchProject project, CancellationToken ct)
    {
        if (!project.RepoSourced) return (null, [], null);

        var primary = project.Repositories.OrderBy(r => r.SortOrder).FirstOrDefault();
        if (primary is null)
            return (new RepoSourcedPlaybooks(
                $"{project.Key} is repo-sourced but is bound to no repository - bind one on the Projects page", null), [], null);

        string trunk;
        if (primary.BaseBranch is { Length: > 0 } baseBranch)
        {
            trunk = baseBranch;
        }
        else
        {
            var trunks = await db.PlaybookFiles.AsNoTracking()
                .Where(f => f.Canonical == primary.Canonical)
                .Select(f => f.Trunk)
                .Distinct()
                .ToListAsync(ct);

            if (trunks.Count != 1)
                return (new RepoSourcedPlaybooks(
                    $"{project.Key} names no base branch and more than one .hatch/playbooks.yaml trunk exists " +
                    $"for {primary.Canonical} - set the base branch on the Projects page", null), [], null);

            trunk = trunks[0];
        }

        var file = await db.PlaybookFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Canonical == primary.Canonical && f.Trunk == trunk, ct);

        if (file is null)
            return (new RepoSourcedPlaybooks(
                $"no runner has read .hatch/playbooks.yaml on {trunk} of {primary.Remote} yet", null), [], null);

        if (file.State == EfHatchPlaybookFile.AbsentState)
            return (new RepoSourcedPlaybooks($".hatch/playbooks.yaml is missing on {trunk} at {file.Sha}", null), [], null);

        if (file.State == EfHatchPlaybookFile.InvalidState)
            return (new RepoSourcedPlaybooks($"{file.Problem} - fix it on the trunk", null), [], null);

        var rows = EfHatchPlaybookFile.ReadRows(file.Rows)
            .Select((row, i) => new EfHatchPlaybook
            {
                Id = i - 1_000_000_000,
                ProjectId = project.Id,
                Project = null,
                FromStatusId = row.FromStatusId,
                ToStatusId = row.ToStatusId,
                Types = string.Join(",", row.Types),
                Shape = row.Shape,
                Prompt = row.Prompt,
                Model = row.Model,
                Effort = row.Effort,
                Budget = row.Budget,
                BriefLimit = row.BriefLimit,
                DeniedTools = EfHatchPlaybook.WriteDeniedTools(row.DeniedTools),
                IsCheck = row.IsCheck,
                CreatedAt = file.CheckedAt,
                UpdatedAt = file.CheckedAt,
            })
            .ToList();

        return (new RepoSourcedPlaybooks(null, trunk), rows, new PlaybookFileReference(PlaybookFile.Path, file.Canonical, trunk, file.Sha, file.Hash!));
    }

    /// <summary>
    /// The playbook that speaks for this move. A row naming the issue's own
    /// project beats a row naming every project, whatever either names for
    /// type or shape; only once that is settled does a row naming the issue's
    /// type beat a row naming only its shape, which beats a bare row naming
    /// neither, and ties go to the older row - so adding a specific rule never
    /// requires editing the general one.
    /// </summary>
    /// <remarks>
    /// One dispatch, not a loop over a pass's worth of candidates, so
    /// <see cref="ResolveRepoSourcedAsync"/> is asked fresh rather than
    /// through a cache - and for the same reason, a repo-sourced project's
    /// own table rows are excluded from the query itself rather than
    /// filtered afterward, so one can never be loaded as a candidate here
    /// even if one somehow exists on the table.
    /// </remarks>
    public async Task<PlaybookMatch?> MatchAsync(int from, int to, string type, bool isParent, int projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Repositories)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        var (repoSourced, fileRows, file) = project is null
            ? ((RepoSourcedPlaybooks?)null, (IReadOnlyList<EfHatchPlaybook>)[], (PlaybookFileReference?)null)
            : await ResolveRepoSourcedAsync(project, ct);

        var query = db.Playbooks.AsNoTracking()
            .Include(p => p.FromStatus)
            .Include(p => p.ToStatus)
            .Include(p => p.Project)
            .Where(p => p.FromStatusId == from && p.ToStatusId == to);

        if (repoSourced is not null) query = query.Where(p => p.ProjectId == null);

        var tableRows = await query.ToListAsync(ct);

        var playbook = Match(fileRows.Concat(tableRows).ToList(), from, to, type, isParent, projectId);
        return new PlaybookMatch(playbook, SourceOf(playbook), file);
    }

    /// <summary>
    /// The layer a matched row came from, decided from the row itself rather
    /// than from which query produced it: a transient id inside
    /// <see cref="ResolveRepoSourcedAsync"/>'s range is a file row, a real id
    /// naming a project is today's own-project-row case, and anything else is
    /// the board. Null only where there is no row to speak of at all.
    /// </summary>
    private static string? SourceOf(EfHatchPlaybook? playbook) => playbook switch
    {
        null => null,
        { Id: < 0 } => "file",
        { ProjectId: not null } => "project",
        _ => "board",
    };

    /// <summary>
    /// The same rule against rows already in hand, which is how a scan matches
    /// a whole board's worth of transitions without a query a row. The matrix
    /// is small enough to read whole and the tie-break is arithmetic.
    /// </summary>
    public static EfHatchPlaybook? Match(List<EfHatchPlaybook> playbooks, int from, int to, string type, bool isParent, int projectId) =>
        playbooks
            .Where(p => p.FromStatusId == from && p.ToStatusId == to && p.Covers(type, isParent)
                        && (p.ProjectId == null || p.ProjectId == projectId))
            .OrderByDescending(p => p.ProjectId is not null)
            .ThenByDescending(p => p.Specificity)
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

    // ---- Which checkout the caller holds ----

    /// <summary>
    /// Whether a caller's declared repositories give it a checkout of a
    /// project's: the project's own repositories matched against the
    /// caller's, or - for a project that binds none - whatever a standing
    /// checkout already means. One rule, shared by <see cref="WorkController.GetReview"/>'s
    /// own eligibility and <see cref="Runners.DriversAsync"/>'s election, so
    /// the two can never disagree about which projects a runner can act on.
    /// </summary>
    public static bool HoldsCheckout(ICollection<EfHatchProjectRepository> bound, RepositoryDeclaration repos) =>
        bound.Count == 0 ? repos.Standing : bound.Any(r => repos.Match(r.Canonical) is not null);
}

/// <summary>
/// A repo-sourced project's own resolution - see
/// <see cref="Dispatch.ResolveRepoSourcedAsync"/>. Non-null means the
/// project is repo-sourced, which doubles as the flag <see cref="Dispatch.Blocked"/>
/// needs: a non-null <see cref="Fold"/> folds every move of the project
/// regardless of what any playbook matched, and a non-null <see cref="Trunk"/>
/// (independent of <see cref="Fold"/>) means the <c>.hatch/playbooks.yaml</c>
/// copy resolved to a real trunk, which is all the fallback sentence needs
/// to name the file. Null means the project does not have the setting on at
/// all - today's path, byte for byte.
/// </summary>
public sealed record RepoSourcedPlaybooks(string? Fold, string? Trunk);

/// <summary>
/// The repo copy a dispatch was judged against - see
/// <see cref="Dispatch.ResolveRepoSourcedAsync"/>. Carried whenever the
/// project is repo-sourced and a copy was found, whether or not any of its
/// rows is the one that matched.
/// </summary>
public sealed record PlaybookFileReference(string Path, string Canonical, string Trunk, string Sha, string Hash);

/// <summary>
/// <see cref="Dispatch.MatchAsync"/>'s own return: the row that matched, if
/// any, which layer it came from, and the repo copy the dispatch was judged
/// against. <see cref="Source"/> is null only where <see cref="Playbook"/> is.
/// </summary>
public sealed record PlaybookMatch(EfHatchPlaybook? Playbook, string? Source, PlaybookFileReference? File);

/// <summary>One issue the pass looked at, and what it decided.</summary>
public sealed record ScanRow(
    EfHatchIssue Issue, EfHatchStatus From, EfHatchStatus? To, string? Blocked, string? BlockedKind, string Kind, bool Hop,
    string? HopKind = null, string? HopUnder = null, string? ClearNote = null,
    int EffectivePriority = PriorityLevels.Normal, string? EffectiveFrom = null,
    string? Model = null, IReadOnlyList<string>? DeniedTools = null,
    IReadOnlyList<long>? WaitsOn = null, bool AnyOf = false);

/// <summary>
/// <see cref="Dispatch.BlockedWithKind"/>'s answer: the sentence and its kind,
/// both null where the row is clear, and - for a fold that waits on other
/// issues - their ids. <see cref="WaitsOn"/> is null where the fold waits on no
/// issue (a person, a clock, a build); <see cref="AnyOf"/> is true where any
/// one of them leaving is enough (a WIP limit), false where all must (a
/// dependency).
/// </summary>
public sealed record Blocking(string? Sentence, string? Kind, IReadOnlyList<long>? WaitsOn = null, bool AnyOf = false)
{
    /// <summary>A fold on a list that could in principle be empty: empty means it waits on no issue, so null.</summary>
    public static Blocking Waiting(string sentence, string kind, IReadOnlyList<long> ids, bool anyOf = false) =>
        new(sentence, kind, ids.Count > 0 ? ids : null, anyOf);
}

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
    public string? Held(EfHatchIssue issue) => HeldWithKind(issue).Sentence;

    /// <summary>
    /// <see cref="Held"/>'s own answer, plus which of <see cref="BlockedKinds"/>
    /// it is: <see cref="BlockedKinds.Working"/> off the live-claim branch,
    /// <see cref="BlockedKinds.Family"/> off the relative's, both null
    /// wherever <see cref="Held"/> itself returns null.
    /// </summary>
    public (string? Sentence, string? Kind) HeldWithKind(EfHatchIssue issue)
    {
        if (Claims is null) return (null, null);

        var claim = ClaimSnapshot.Of(issue);
        if (Claims.IsLive(claim, Now))
            return claim.Token == HeldToken ? (null, null) : (Claims.Sentence(claim, Now), BlockedKinds.Working);

        var holder = Lineage.Holder(issue.Id, HeldToken);
        return holder is null ? (null, null) : (holder, BlockedKinds.Family);
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

    private readonly Dictionary<long, (long? ParentId, string Key, int Priority, int PreApproval)> _rows;

    // The one-PR walk needs two things the other two do not: each issue's type,
    // to find the nearest story, and each story's own override. Kept beside
    // _rows rather than widening its tuple, which every other walk destructures.
    private readonly Dictionary<long, (string Type, bool? OnePr, bool ProjectOnePr, int StatusId, bool HasPr, bool Terminal)> _onePr;

    internal PriorityTree(Dictionary<long, (long?, string, int, int)> rows, Dictionary<long, (string, bool?, bool, int, bool, bool)>? onePr = null)
    {
        _rows = rows;
        _onePr = onePr ?? new();
    }

    public static async Task<PriorityTree> ForAsync(HatchContext db, CancellationToken ct)
    {
        var rows = await db.Issues.AsNoTracking()
            .Select(i => new
            {
                i.Id, i.ParentId, i.Priority, i.PreApproval, ProjectKey = i.Project!.Key, i.Number,
                i.Type, i.OnePullRequest, ProjectOnePr = i.Project!.OnePullRequest,
                i.StatusId, HasPr = i.PullRequestUrl != null && i.PullRequestUrl != "", Terminal = i.Status!.IsTerminal,
            })
            .ToListAsync(ct);

        return new PriorityTree(
            rows.ToDictionary(
                r => r.Id,
                r => ((long?)r.ParentId, IssueKey.Format(r.ProjectKey, r.Number), r.Priority, r.PreApproval)),
            rows.ToDictionary(r => r.Id, r => (r.Type, r.OnePullRequest, r.ProjectOnePr, r.StatusId, r.HasPr, r.Terminal)));
    }

    internal bool TryGet(long id, out (long? ParentId, string Key, int Priority, int PreApproval) row) => _rows.TryGetValue(id, out row);

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

    /// <summary>
    /// The pre-approval live on this issue: its own, if set, or the nearest
    /// ancestor's that is not None. <c>FromKey</c> is null when the level
    /// found is the issue's own, or when the walk never leaves None.
    /// </summary>
    public (int Level, string? FromKey) EffectivePreApproval(long issueId)
    {
        var seen = new HashSet<long>();
        long? at = issueId;

        while (at is { } id && seen.Add(id))
        {
            if (TryGet(id, out var row) && row.PreApproval != PreApprovalLevels.None)
                return (row.PreApproval, id == issueId ? null : row.Key);

            at = TryGet(id, out var found) ? found.ParentId : null;
        }

        return (PreApprovalLevels.None, null);
    }

    /// <summary>
    /// Whether the stories' tasks share one branch and pull request, live on
    /// this issue: the nearest story at or above it, and that story's own
    /// override if it carries one, else its project's default. An issue with no
    /// story above it - an epic, or a task filed straight under one - is off,
    /// because there is no story whose pull request it could share.
    /// </summary>
    /// <remarks>
    /// <c>Own</c> is this issue's own override, which only a story carries.
    /// <c>FromKey</c> is the story the value was read from, and is null when
    /// that is the issue itself or when there is no story at all; with a null
    /// <c>FromKey</c> and a null <c>Own</c>, a story's value came from its
    /// project. Derived at read and never copied, the same as
    /// <see cref="EffectivePreApproval"/>.
    /// </remarks>
    public (bool OnePr, bool? Own, string? FromKey) EffectiveOnePr(long issueId)
    {
        _onePr.TryGetValue(issueId, out var self);
        var own = self.Type == "story" ? self.OnePr : null;

        if (StoryAbove(issueId) is not { } story) return (false, own, null);

        var row = _onePr[story];
        return (row.OnePr ?? row.ProjectOnePr, own, story == issueId ? null : _rows[story].Key);
    }

    /// <summary>
    /// The nearest story at or above an issue, or null. The one walk every
    /// one-PR question is asked through, so they cannot come to pick different
    /// stories.
    /// </summary>
    private long? StoryAbove(long issueId)
    {
        var seen = new HashSet<long>();
        long? at = issueId;

        while (at is { } id && seen.Add(id))
        {
            if (_onePr.TryGetValue(id, out var row) && row.Type == "story") return id;

            at = TryGet(id, out var found) ? found.ParentId : null;
        }

        return null;
    }

    /// <summary>
    /// The story whose pull request this task is waiting on, or null - a task
    /// under a story that shares one pull request, that has pushed to the
    /// story's branch and stands in the review column or past it, with no pull
    /// request of its own.
    /// </summary>
    /// <remarks>
    /// Such a task is finished as far as the story is concerned - the next
    /// sibling is pulled, an edge between siblings clears, and the story moves
    /// on to open the one pull request - and it is not finished as far as the
    /// board is concerned: it ends in a terminal column only when that pull
    /// request merges. A task with a pull request of its own is not waiting on
    /// the story's. See <see cref="SharedStoryOf"/> for who shares, and
    /// <c>docs/hatch.md</c>, "One pull request".
    /// </remarks>
    public string? WaitingOn(long issueId, List<EfHatchStatus> statuses) =>
        _onePr.TryGetValue(issueId, out var row)
        && SharedStoryOf(issueId) is { } story
        && Columns.AtOrPastReview(statuses, row.StatusId)
            ? _rows[story].Key
            : null;

    /// <summary>
    /// Every task waiting on a story's pull request, with that story's key -
    /// <see cref="WaitingOn"/> asked of the whole board, with the column test
    /// worked out once instead of once a task.
    /// </summary>
    public Dictionary<long, string> WaitingIds(List<EfHatchStatus> statuses)
    {
        var lane = Columns.AtOrPastReviewIds(statuses);
        var waiting = new Dictionary<long, string>();
        if (lane.Count == 0) return waiting;

        foreach (var (id, row) in _onePr)
            if (row.Type == "task" && !row.HasPr && lane.Contains(row.StatusId) && SharedStoryOf(id) is { } story)
                waiting[id] = _rows[story].Key;

        return waiting;
    }

    /// <summary>
    /// Every task waiting on this story's pull request - see <see cref="WaitingOn"/>.
    /// What a merged story takes to its own terminal column with it.
    /// </summary>
    public IReadOnlyList<long> WaitingUnder(long storyId, List<EfHatchStatus> statuses) =>
        !_rows.TryGetValue(storyId, out var story)
            ? []
            : WaitingIds(statuses).Where(r => r.Value == story.Key).Select(r => r.Key).Order().ToList();

    /// <summary>
    /// The story a task shares the pull request of, by its row and with no
    /// regard to where the task stands - the question the claim lineage, the
    /// dependency gate and the branch a dispatch is named for ask about tasks
    /// that are not yet, or no longer, waiting. Null for anything that is not a
    /// task under a story that shares, for a task that already has a pull
    /// request of its own, and for a task whose story is in a terminal column:
    /// that story's pull request has merged, so there is none left to share.
    /// </summary>
    public long? SharedStoryOf(long issueId)
    {
        if (!_onePr.TryGetValue(issueId, out var self) || self.Type != "task" || self.HasPr) return null;
        if (StoryAbove(issueId) is not { } story) return null;

        var row = _onePr[story];
        return !row.Terminal && (row.OnePr ?? row.ProjectOnePr) ? story : null;
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

    private readonly Dictionary<long, List<(long Id, string Key)>> _unmetByIssue;
    private readonly PriorityTree _tree;

    private DependencyGate(
        Dictionary<long, List<(long Id, string Key)>> unmetByIssue, PriorityTree tree)
    {
        _unmetByIssue = unmetByIssue;
        _tree = tree;
    }

    public static async Task<DependencyGate> ForAsync(
        HatchContext db, List<EfHatchStatus> statuses, CancellationToken ct, PriorityTree? tree = null)
    {
        var terminal = statuses.Where(s => s.IsTerminal).Select(s => s.Id).ToList();

        // The unmet edges, and nothing else. Ordered by the blocker's key
        // so the sentence a fold prints is stable between two passes over
        // an unchanged board.
        var unmet = await db.Dependencies.AsNoTracking()
            .Where(d => !terminal.Contains(d.DependsOn!.StatusId))
            .OrderBy(d => d.DependsOn!.Project!.Key).ThenBy(d => d.DependsOn!.Number)
            .Select(d => new { d.IssueId, d.DependsOnId, ProjectKey = d.DependsOn!.Project!.Key, d.DependsOn!.Number })
            .ToListAsync(ct);

        tree ??= await PriorityTree.ForAsync(db, ct);

        // The one exception to "satisfied means done": a task under a story that
        // shares one pull request, waiting on its pull request, satisfies an
        // edge from a sibling task under the same story. Building on a
        // sibling's unmerged commits is the point of a shared branch, and the
        // sibling it waits on can only reach a terminal column once the whole
        // story does - so holding the edge would hold the story for ever.
        // Edges to or from anything outside that story stay as strict as ever.
        unmet = unmet
            .Where(r => !(tree.WaitingOn(r.DependsOnId, statuses) is not null
                && tree.SharedStoryOf(r.IssueId) is { } story
                && tree.SharedStoryOf(r.DependsOnId) == story))
            .ToList();

        return new DependencyGate(
            unmet.GroupBy(r => r.IssueId).ToDictionary(
                g => g.Key,
                g => g.Select(r => (r.DependsOnId, IssueKey.Format(r.ProjectKey, r.Number))).ToList()),
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
                return blockers.Select(b => new UnmetEdge(b.Key, holder, b.Id)).ToList();
            }

            at = _tree.TryGet(id, out var found) ? found.ParentId : null;
        }

        return [];
    }

    /// <summary>The priority live on this issue - see <see cref="PriorityTree.Effective"/>.</summary>
    public (int Level, string? FromKey) Effective(long issueId) => _tree.Effective(issueId);

    /// <summary>The pre-approval live on this issue - see <see cref="PriorityTree.EffectivePreApproval"/>.</summary>
    public (int Level, string? FromKey) EffectivePreApproval(long issueId) => _tree.EffectivePreApproval(issueId);

    /// <summary>Whether one pull request is shared across the story this issue sits under - see <see cref="PriorityTree.EffectiveOnePr"/>.</summary>
    public (bool OnePr, bool? Own, string? FromKey) EffectiveOnePr(long issueId) => _tree.EffectiveOnePr(issueId);

    /// <summary>The story whose pull request this task is waiting on, or null - see <see cref="PriorityTree.WaitingOn"/>.</summary>
    public string? WaitingOn(long issueId, List<EfHatchStatus> statuses) => _tree.WaitingOn(issueId, statuses);

    /// <summary>The story this task shares the branch and pull request of, or null - see <see cref="PriorityTree.SharedStoryOf"/>.</summary>
    public long? SharedStoryOf(long issueId) => _tree.SharedStoryOf(issueId);
}

/// <summary>
/// One thing being waited on. <see cref="HolderKey"/> is null when the edge
/// is the issue's own, and the ancestor's key when it is not.
/// <see cref="BlockerId"/> is the blocker's issue id, for the fold that names
/// who it waits on.
/// </summary>
public sealed record UnmetEdge(string BlockerKey, string? HolderKey, long BlockerId);

// ---- Family ----

/// <summary>
/// Every issue's direct children, read once as <c>(Id, ParentId, StatusId)</c>
/// in board order, and which of them are open - the input to the fold that
/// keeps an issue with unfinished children out of review.
/// </summary>
public sealed class FamilyGate
{
    /// <summary>A gate that folds nothing, for a refused scan - so <c>Scan.Family</c> is never null.</summary>
    public static readonly FamilyGate None = new([], [], [], [], [], []);

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
    private readonly HashSet<long> _waiting;
    private readonly Dictionary<long, long> _sharedStory;

    private FamilyGate(
        Dictionary<long, List<long>> children, HashSet<long> open, Dictionary<long, int> statusById,
        Dictionary<long, (long? ParentId, string Type, string Key, int StatusId)> byId, HashSet<long> waiting,
        Dictionary<long, long> sharedStory)
    {
        _children = children;
        _open = open;
        _statusById = statusById;
        _byId = byId;
        _waiting = waiting;
        _sharedStory = sharedStory;
    }

    public static async Task<FamilyGate> ForAsync(
        HatchContext db, List<EfHatchStatus> statuses, CancellationToken ct, PriorityTree? tree = null)
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

        // Every task waiting on its story's pull request, found once for the
        // pass - see PriorityTree.WaitingOn. A board that shares no pull request
        // anywhere has an empty set and every rule below is exactly as it was.
        tree ??= await PriorityTree.ForAsync(db, ct);
        var waiting = tree.WaitingIds(statuses).Keys.ToHashSet();

        // Which tasks share a story's branch, and which story - read off the
        // same tree, for the fold that keeps two of them off the branch at once.
        var sharedStory = new Dictionary<long, long>();
        foreach (var id in byId.Keys)
            if (tree.SharedStoryOf(id) is { } story) sharedStory[id] = story;

        return new FamilyGate(children, open, statusById, byId, waiting, sharedStory);
    }

    /// <summary>
    /// Whether this issue is a task waiting on its story's pull request - see
    /// <see cref="PriorityTree.WaitingOn"/>.
    /// </summary>
    public bool IsWaiting(long issueId) => _waiting.Contains(issueId);

    /// <summary>
    /// The direct children whose work is not yet done: <see cref="OpenChildren"/>
    /// less the ones waiting on the story's pull request, which are finished as
    /// far as the story is concerned. Asked by the fold that holds a story out of
    /// review while its children are the work; <see cref="OpenChildren"/> itself
    /// still counts a waiting task, because it has not merged - which is what
    /// keeps a story from being closed over a pull request nobody has opened.
    /// </summary>
    public IReadOnlyList<long> OpenWorkChildren(long issueId) =>
        OpenChildren(issueId).Where(id => !_waiting.Contains(id)).ToList();

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

        var (counted, openIds) = EpicCounts(children, statuses);
        var open = openIds.Count;

        if (open == 0) return null;

        return counted == 1
            ? "its only child is not done - an epic is verified once its stories are"
            : $"{open} of its {counted} children {(open == 1 ? "is" : "are")} not done - an epic is verified once its stories are";
    }

    /// <summary>
    /// The direct children <see cref="EpicFold"/> counts as not done: not
    /// terminal, and not deferred. Empty where the epic has no children, and
    /// where it has no open one.
    /// </summary>
    public IReadOnlyList<long> EpicOpenChildren(long issueId, List<EfHatchStatus> statuses) =>
        EpicCounts(Children(issueId), statuses).Open;

    /// <summary>How many of these children an epic counts (every one but a deferred one), and which of them are not yet terminal.</summary>
    private (int Counted, IReadOnlyList<long> Open) EpicCounts(IReadOnlyList<long> children, List<EfHatchStatus> statuses)
    {
        var counted = 0;
        var open = new List<long>();
        foreach (var childId in children)
        {
            if (!_statusById.TryGetValue(childId, out var statusId)) continue;
            if (statuses.FirstOrDefault(s => s.Id == statusId) is not { } status) continue;
            if (status.IsDeferred) continue;

            counted++;
            if (!status.IsTerminal) open.Add(childId);
        }

        return (counted, open);
    }

    /// <summary>Whether the parent's own column has reached Columns.Implementation.</summary>
    public bool ParentStarted(long parentId, List<EfHatchStatus> statuses) =>
        _statusById.TryGetValue(parentId, out var statusId)
        && Columns.AtOrPastImplementation(statuses, statusId);

    /// <summary>The key of an issue the pass loaded - an id <see cref="SharedBranchSiblingId"/> returned.</summary>
    public string KeyOf(long issueId) => _byId[issueId].Key;

    /// <summary>
    /// The key of the first sibling in flight on the story's shared branch, or
    /// null where this issue shares none or no sibling is. In flight is judged
    /// by the sibling's own row: at or past the implementation column, not
    /// terminal, and not waiting on the story's pull request. Whether the
    /// sibling has a pull request of its own does not matter - this issue is
    /// the one that would go on the story's branch. This is also the only
    /// thing that holds a parent pull (see <see cref="Pulls"/>): under a story
    /// with sharing off, a bug or an epic there is no shared branch, and
    /// dependencies order the siblings instead.
    /// </summary>
    public string? SharedBranchSibling(EfHatchIssue issue, List<EfHatchStatus> statuses) =>
        SharedBranchSiblingId(issue, statuses) is { } id ? _byId[id].Key : null;

    /// <summary>The id of the sibling <see cref="SharedBranchSibling"/> names, or null where it names none.</summary>
    public long? SharedBranchSiblingId(EfHatchIssue issue, List<EfHatchStatus> statuses)
    {
        if (!_sharedStory.TryGetValue(issue.Id, out var storyId)) return null;

        var started = Columns.AtOrPastImplementationIds(statuses);
        var terminal = statuses.Where(s => s.IsTerminal).Select(s => s.Id).ToHashSet();

        foreach (var siblingId in Children(storyId))
        {
            if (siblingId == issue.Id) continue;
            if (!_statusById.TryGetValue(siblingId, out var siblingStatusId)) continue;
            if (!started.Contains(siblingStatusId) || terminal.Contains(siblingStatusId)) continue;
            if (_waiting.Contains(siblingId)) continue;

            return siblingId;
        }

        return null;
    }

    /// <summary>The sentence a sibling in flight on the shared branch holds an issue back with.</summary>
    public static string SharedBranchHold(string sibling) =>
        $"{sibling}, sharing this story's branch, is already in flight - one sibling at a time";

    /// <summary>
    /// Whether this issue's column may carry it one column right with no
    /// session - see HA-149: its parent has started, and no sibling is in
    /// flight on the story's shared branch.
    /// </summary>
    public bool Pulls(EfHatchIssue issue, List<EfHatchStatus> statuses) =>
        issue.ParentId is { } parentId
        && ParentStarted(parentId, statuses)
        && SharedBranchSibling(issue, statuses) is null;
}
