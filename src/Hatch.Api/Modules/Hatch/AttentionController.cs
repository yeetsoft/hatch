using System.Text;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What the loop is waiting on a person for: a pull request nobody has
/// reviewed, and a question nobody has answered - and, listed beside them but
/// not counted, the branches in review that have stopped merging.
///
/// The nav strip draws it on every page, so it has to be one request rather
/// than two. Two polls can be a poll interval apart, and a badge counting one
/// instant beside a panel drawn from another shows up as a lit control whose
/// list is empty - which is the one failure a widget like this cannot recover
/// from, because after it happens twice nobody reads it again.
/// </summary>
/// <remarks>
/// Neither half restates a rule that already lives somewhere. Which column is
/// the review column is <see cref="Columns.AwaitingReview"/>'s answer, measured
/// off the board's shape and not off a name, and what makes a question wait on
/// somebody is <see cref="Questions.Waiting"/>'s - the same call the board
/// badges a card with. A browser that derived either for itself would be the
/// same rule written twice, in two languages, and the two would drift.
/// </remarks>
[ApiController]
[Route("api/attention")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class AttentionController(HatchContext db, IActorDirectory actors, Runners runners, TimeProvider time, Dispatch dispatch, BackupService backup, IAuthService keys, GooseWatch gooseWatch, IOptions<HatchOptions> options, ICallerIdentity caller, RunnerSessions sessions, IOptions<AuthOptions> auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AttentionDto>> GetAttention(CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        // Every open question still waiting on somebody - not one on an issue
        // that has already shipped, which nobody is going to answer.
        var questions = await Questions.ProjectAsync(db, Questions.Waiting(db), ct);

        var exhausted = await ExhaustedRunnersAsync(ct);

        // What is wrong with the runners this reader is sent - whatever the board's shape.
        var runnerFailures = await RunnerFailuresAsync(ct);

        // Only the ancestor walk MergeWork.Judge needs below, for a
        // pre-approved review row - not the full DependencyGate, which this
        // endpoint has no dependency-gating need for.
        var tree = await PriorityTree.ForAsync(db, actors, ct, ignoreFilters: true);

        // A repository's trunk is nobody's issue, so this does not wait on
        // there being a review column at all - a person's row here even on a
        // board too short to have one. The same failing rule the branch builds
        // use: a check that has failed counts while the rest are still running.
        var failingTrunks = await db.TrunkBuilds.AsNoTracking()
            .Where(TrunkBuildFailing.Where)
            .Include(t => t.BugIssue).ThenInclude(i => i!.Project)
            .OrderBy(t => t.Canonical).ThenBy(t => t.Trunk)
            .ToListAsync(ct);

        // The project that binds each trunk, so the File a bug? dialog can ask
        // whether this person may pre-approve there - as /trunk-builds sends it.
        var trunkWinners = await ProjectRepositoryBinding.WinnersAsync(
            db, failingTrunks.Select(t => t.Canonical).Distinct().ToList(), ct);
        var trunkBuilds = failingTrunks
            .Select(t => TrunkBuildProjection.Project(t, trunkWinners.TryGetValue(t.Canonical, out var w) ? w.Key : null))
            .ToList();

        // The same read as the rest, never a second poll of its own - and
        // whatever the board's shape, as the trunk builds are.
        var backupRow = backup.Attention();

        // The queue's own verdict, unnarrowed, so the scan runs the fixpoint over
        // its own rows and this list and the queue's marked rows cannot disagree.
        // Whatever the board's shape: a ring needs no review column to be read.
        var deadlocked = (await dispatch.ScanAsync(ScanCaller.Attention, 0, null, null, RepositoryDeclaration.Undeclared, false, ct, markDeadlocks: true))
            .Rows
            .Where(r => r.Deadlocked)
            .Select(r => new DeadlockedDto(IssueKey.Format(r.Issue.Project!.Key, r.Issue.Number), r.Issue.Title, r.Issue.Type, r.Blocked!))
            .ToList();

        // The moves every live runner's own declaration folds on a model or a
        // development dependency, and nothing else - whatever the board's shape.
        // The controller holds Runners, so the cutoff comes from here.
        var waitingOnARunner = await dispatch.WaitingOnARunnerAsync(ScanCaller.Attention, 0, runners.Cutoff(time.GetUtcNow()), ct);

        // The issues held only because a level or pull from another project is
        // not honoured, read for this reader: an ancestor they cannot see is
        // never named. Whatever the board's shape, as the list above is.
        var reader = tree.WithVisible(await PriorityTree.VisibleIdsAsync(db, ct));
        var heldAtProjectLine = (await dispatch.HeldAtProjectLineAsync(ScanCaller.Attention, ct))
            .Select(h =>
            {
                var hide = reader.Disclose(h.Row.Issue.Id).Key(DisclosedValues.PreApprovalPassedOver, h.Line.FromKey) is null;
                var projectKey = h.Row.Issue.Project!.Key;
                return new ProjectLineHeldDto(
                    IssueKey.Format(projectKey, h.Row.Issue.Number), h.Row.Issue.Title, h.Row.Issue.Type, projectKey,
                    Dispatch.AcrossSentence(h.Line, projectKey, hide));
            })
            .ToList();

        // A stalled train is a project's, not a column's - whatever the board's shape.
        var trainStalled = await TrainStalledAsync(ct);

        // What the goose found, and whether it is still looking - both whatever the board's shape.
        var findings = await FindingsAsync(ct);
        var goose = await GooseAsync(ct);

        // The projects whose clear implementation steps one live runner, or none, can take.
        var runnerCapacity = await dispatch.RunnerCapacityAsync(ScanCaller.Attention, 0, runners.Cutoff(time.GetUtcNow()), ct);

        // Every issue a person held or the board marked stalled, in board order,
        // whatever the board's shape - the fold is the queue's own, so this list
        // and the queue's rows cannot disagree about who it waits on.
        var stalledCandidates = await db.Issues.AsNoTracking()
            .Where(i => !i.Status!.IsTerminal && !i.Status!.IsDeferred && (i.StalledAt != null || i.Held))
            .OrderBy(i => i.Status!.SortOrder).ThenBy(i => i.Rank).ThenBy(i => i.Id)
            .Select(i => new { i.Id, ProjectKey = i.Project!.Key, i.Number, i.Type, i.Title, i.StalledAt, i.StalledWhy, i.Held })
            .ToListAsync(ct);
        var marked = stalledCandidates.Where(i => i.StalledAt is not null).Select(i => i.Id).ToList();
        var letGoCounts = marked.Count > 0 ? await LetGo.CountsAsync(db, marked, ct) : [];
        var stalledNow = time.GetUtcNow();
        var stalled = stalledCandidates.Select(c =>
        {
            var letGo = c.StalledAt is not null ? letGoCounts.GetValueOrDefault(c.Id, 0) : 0;
            var blocking = Dispatch.StalledFold(c.Held, c.StalledAt, c.StalledWhy, letGo, dispatch.StallResumeSeconds, dispatch.StallResumeLimit, stalledNow);
            var kind = blocking is { Kind: BlockedKinds.Person } ? StalledKinds.Person : StalledKinds.Loop;
            return new StalledIssueDto(IssueKey.Format(c.ProjectKey, c.Number), c.Title, c.Type, c.StalledAt, c.StalledWhy, c.Held, letGo, blocking?.Sentence, kind);
        }).ToList();

        // A board too short to have a review column has nothing in review, and
        // that is an answer rather than an error: the section draws its empty
        // state and the control stays quiet about a half that cannot exist here.
        if (Columns.AwaitingReview(statuses) is not { } review)
            return new AttentionDto([], 0, questions, [], TrunkBuilds: trunkBuilds, ExhaustedRunners: exhausted, Deadlocked: deadlocked, Backup: backupRow, WaitingOnARunner: waitingOnARunner, TrainStalled: trainStalled, RunnerCapacity: runnerCapacity, Stalled: stalled, StallResumeSeconds: dispatch.StallResumeSeconds, HeldAtProjectLine: heldAtProjectLine, Findings: findings, Goose: goose, RunnerFailures: runnerFailures);

        // The column's own order, which is the board's: (Rank, Id), the same
        // ordering BoardController slices its columns with, so a row here sits
        // where the eye already found it on the board.
        var read = await AttentionReviews.ReadAsync(db, dispatch, runners, time.GetUtcNow(), tree, statuses, review, ct);
        var inReview = read.InReview;
        var bound = read.Bound;
        var landingsByIssue = read.LandingsByIssue;
        var landedBuilds = read.LandedBuilds;
        var conflicted = read.Conflicted;
        var conflicts = read.Conflicts;
        var failed = read.Failed;
        var failingBuilds = read.FailingBuilds;
        var contributionsHeld = read.ContributionsHeld;
        var heldIds = read.HeldIds;
        var reviews = read.Reviews.Select(r => r.Dto).ToList();

        var closesTo = Columns.Closes(statuses, review);
        var family = closesTo is not null ? await FamilyGate.ForAsync(db, actors, statuses, ct) : null;

        // A trunk-landing issue stops being "without a pull request" once its
        // landing is recorded: it was never going to have one, and where it
        // stands from there is LandingWork's to say - see landingHeld below.
        var withoutPr = inReview.Count(i =>
            string.IsNullOrWhiteSpace(i.PullRequestUrl)
            && !NoCode.Declared(i.NoCode)
            && !WaitingPullRequest.Has(i.WaitingPrTitle)
            && !(LandingWork.Applies(i.Landing, i.PullRequestUrl) && landingsByIssue.ContainsKey(i.Id))
            && !(family is not null
                && tree.EffectivePreApproval(i.Id).Level == PreApprovalLevels.PreApproved
                && family.Children(i.Id).Count > 0
                && (IssueTypeKinds.RunsChildren(i.Type)
                    ? family.EpicFold(i.Id, statuses) is null
                    : family.OpenChildren(i.Id).Count == 0)));

        var reviewsHeldBack = inReview.Count(i => !string.IsNullOrWhiteSpace(i.PullRequestUrl)
            && !heldIds.Contains(i.Id)
            && (conflicted.ContainsKey(i.Id) || failed.ContainsKey(i.Id)));

        // Only the ones the judge holds: a landing still waiting on its trunk
        // build, or one that is good to go, is the loop's and not a person's.
        var landingHeld = new List<LandingHeldDto>();
        foreach (var i in inReview.Where(r => LandingWork.Applies(r.Landing, r.PullRequestUrl)))
        {
            var judged = LandingWork.Judge(
                bound.TryGetValue(i.ProjectId, out var landedRepos) ? landedRepos : [],
                landingsByIssue.TryGetValue(i.Id, out var landed) ? landed : [],
                landedBuilds, i.ChecksRequired, time.GetUtcNow());

            if (judged.Kind == WorkKinds.Hold)
                landingHeld.Add(new LandingHeldDto(IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, judged.Sentence));
        }

        // A declared issue the close hop would carry is the board's. One that is
        // neither pre-approved nor in a project whose NoCodeCloses is on stays in
        // review for a person, with the reason it gave: it has no pull request to
        // be a row in Reviews and is not "without" one.
        var noCodeHeld = inReview
            .Where(i => string.IsNullOrWhiteSpace(i.PullRequestUrl)
                && NoCode.Declared(i.NoCode)
                && tree.EffectivePreApproval(i.Id).Level != PreApprovalLevels.PreApproved
                && !i.NoCodeCloses)
            .Select(i => new NoCodeHeldDto(IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.NoCode!))
            .ToList();

        var approvedHeld = new List<ApprovedHeldDto>();
        var approvedWaiting = new List<ApprovedWaitingDto>();

        if (Columns.Merges(statuses) is { } merges)
        {
            var inMerges = await db.Issues.AsNoTracking()
                .Where(i => i.StatusId == merges.Id)
                .OrderBy(i => i.Rank).ThenBy(i => i.Id)
                .Include(i => i.Project).ThenInclude(p => p!.Repositories)
                .Include(i => i.Contribution)
                .ToListAsync(ct);

            var mergeIds = inMerges.Select(i => i.Id).ToList();

            var mergeChecksForMerges = await db.MergeChecks.AsNoTracking()
                .Where(m => mergeIds.Contains(m.IssueId))
                .OrderBy(m => m.Canonical)
                .ToListAsync(ct);
            var mergesByIssueForMerges = mergeChecksForMerges.GroupBy(m => m.IssueId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchMergeCheck>)g.ToList());

            var buildChecksForMerges = await db.BuildChecks.AsNoTracking()
                .Where(b => mergeIds.Contains(b.IssueId))
                .OrderBy(b => b.Canonical)
                .ToListAsync(ct);
            var buildsByIssueForMerges = buildChecksForMerges.GroupBy(b => b.IssueId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchBuildCheck>)g.ToList());

            foreach (var issue in inMerges)
            {
                var merged = mergesByIssueForMerges.TryGetValue(issue.Id, out var mv) ? mv : [];
                var built = buildsByIssueForMerges.TryGetValue(issue.Id, out var bv) ? bv : [];

                var j = MergeWork.Judge(
                    issue.Project!.Repositories.OrderBy(r => r.SortOrder).ToList(),
                    merged, built, issue.PullRequestClosedAt, issue.Project!.ChecksRequired, issue.PullRequestMergeState, issue.PullRequestHeadSha, time.GetUtcNow(), review.Name,
                    pullRequestHeadSince: issue.PullRequestHeadSince, recordedBranch: issue.RecordedBranch);

                var key = IssueKey.Format(issue.Project!.Key, issue.Number);
                if (j.Kind is WorkKinds.Hold or WorkKinds.Pushback)
                    approvedHeld.Add(new ApprovedHeldDto(key, issue.Title, issue.Type, issue.PullRequestUrl, j.Sentence));
                else
                    approvedWaiting.Add(new ApprovedWaitingDto(key, issue.Title, issue.Type, issue.PullRequestUrl, j.Kind, j.Sentence));
            }
        }

        return new AttentionDto(
            reviews, withoutPr, questions, conflicts, failingBuilds, reviewsHeldBack, trunkBuilds, exhausted,
            approvedHeld, approvedWaiting, landingHeld, noCodeHeld, deadlocked, backupRow, waitingOnARunner, trainStalled, runnerCapacity, stalled, dispatch.StallResumeSeconds,
            ContributionsHeld: contributionsHeld, HeldAtProjectLine: heldAtProjectLine, Findings: findings, Goose: goose, RunnerFailures: runnerFailures);
    }

    /// <summary>
    /// Every open finding, longest-standing first. The entity's query filter has
    /// already left out another project's findings, and every finding that names
    /// no project, for a caller whose scope is narrowed - so there is no second
    /// filter here to drift from it.
    /// </summary>
    private async Task<IReadOnlyList<AttentionFindingDto>> FindingsAsync(CancellationToken ct)
    {
        var rows = await db.GooseFindings.AsNoTracking()
            .Include(f => f.Project).Include(f => f.BugIssue).ThenInclude(i => i!.Project)
            .Where(f => f.ClosedAt == null)
            .OrderBy(f => f.BeganAt).ThenBy(f => f.Id)
            .ToListAsync(ct);

        return rows.Select(f =>
        {
            var issueKey = GooseFindings.IssueKey(f, f.Project?.Key);
            return new AttentionFindingDto(
                f.Id, f.Check, f.Project?.Key, f.Sentence, f.BeganAt, f.OpenedAt,
                f.BugIssue is { } bug ? IssueKey.Format(bug.Project!.Key, bug.Number) : null,
                issueKey);
        }).ToList();
    }

    /// <summary>
    /// When the goose last swept and whether it has stopped, by this read's own
    /// clock: stopped is the newest run's last sweep being older than three
    /// intervals, or - with no run stored - the server having been up that long.
    /// Read through <c>GooseRuns</c>, which has no query filter, so a narrowed
    /// caller still learns the goose has stopped. The sweep writes nothing for it.
    /// </summary>
    private async Task<AttentionGooseDto> GooseAsync(CancellationToken ct)
    {
        var lastRan = await db.GooseRuns.AsNoTracking()
            .OrderByDescending(r => r.LastRanAt)
            .Select(r => (DateTimeOffset?)r.LastRanAt)
            .FirstOrDefaultAsync(ct);
        var interval = GooseSettings.IntervalSeconds(options.Value);
        var stopped = time.GetUtcNow() - (lastRan ?? gooseWatch.StartedAt) > TimeSpan.FromSeconds(3L * interval);
        return new AttentionGooseDto(lastRan, interval, stopped);
    }

    /// <summary>
    /// Every runner out of Claude usage that is still being heard from, soonest
    /// reset first. Nothing sweeps this: a reset that has passed, or a runner
    /// that has gone quiet, simply is not in the list the next time somebody
    /// asks - the same lazy expiry the rest of <see cref="Runners"/> uses.
    /// </summary>
    private async Task<IReadOnlyList<ExhaustedRunnerDto>> ExhaustedRunnersAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();

        var candidates = await db.LegacyRunners.AsNoTracking()
            .Where(r => r.ExhaustedUntil != null)
            .Select(r => new { r.Name, r.Where, r.LastSeenAt, r.ExhaustedUntil })
            .ToListAsync(ct);

        return candidates
            .Where(r => r.ExhaustedUntil!.Value > now && runners.IsHere(r.LastSeenAt, now))
            .OrderBy(r => r.ExhaustedUntil)
            .Select(r => new ExhaustedRunnerDto(r.Name, r.Where, r.ExhaustedUntil!.Value))
            .ToList();
    }

    /// <summary>
    /// Every current failure of the runners this reader is sent, longest-standing
    /// first. What is current is <see cref="RunnerFailures.Current"/>'s - an
    /// ended row and a runner gone quiet are simply not in it - so this writes
    /// no second rule, and nothing sweeps.
    /// </summary>
    /// <remarks>
    /// Narrower than the Runners page, where any signed-in person sees every
    /// runner: a failure is a thing the person who can fix it is told, so an
    /// owned runner is sent to its owner, and one that belongs to nobody to an
    /// admin. The wall off sends all, since nobody there is anybody in particular.
    /// A key is sent what its owner would be.
    /// </remarks>
    private async Task<IReadOnlyList<AttentionRunnerFailureDto>> RunnerFailuresAsync(CancellationToken ct)
    {
        var open = await db.RunnerFailures.AsNoTracking()
            .Where(f => f.EndedAt == null)
            .ToListAsync(ct);
        if (open.Count == 0) return [];

        var now = time.GetUtcNow();
        var wallOff = !auth.Value.Enabled;
        var principal = wallOff ? null : (await actors.PrincipalAsync(ct))?.Id;
        var admin = !wallOff && await IsAdminAsync(ct);

        var failures = new List<AttentionRunnerFailureDto>();
        foreach (var runner in await Runner.JoinAsync(db, keys, ct))
        {
            var sent = wallOff
                || (runner.OwnerPersonId is { } owner ? principal == owner : admin || principal is null);
            if (!sent) continue;

            failures.AddRange(RunnerFailures.Current(open, runner, sessions, now)
                .Select(f => new AttentionRunnerFailureDto(runner.Name, f.Where, f.Kind, f.Why, f.BeganAt)));
        }

        return failures
            .OrderBy(f => f.BeganAt).ThenBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Kind, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether the reader is an admin: a person's own role, or the calling key's owner's.</summary>
    private async Task<bool> IsAdminAsync(CancellationToken ct) =>
        await caller.PersonAsync(ct) is { Role: >= PersonRole.Admin }
        || await caller.ApiKeyAsync(ct) is { Owner.Role: >= PersonRole.Admin };

    /// <summary>
    /// Every project whose merge train has passed over every runner that could
    /// drive it, with the head issue the newest pass-over was written on. The
    /// head is not on the project: the newest <c>driver_stalled</c> event naming
    /// a runner on the project's roster carries it, and a project with no such
    /// event - its issue was deleted - is left out rather than given a head.
    /// A project with a live eligible runner not yet tried is still being
    /// worked, so it is not listed.
    /// </summary>
    private async Task<IReadOnlyList<TrainStalledDto>> TrainStalledAsync(CancellationToken ct)
    {
        var stalled = await db.Projects.AsNoTracking()
            .Where(p => p.TrainStalledDrivers != "[]")
            .OrderBy(p => p.Key)
            .Select(p => new { p.Id, p.Key, p.Name, p.TrainStalledDrivers })
            .ToListAsync(ct);

        if (stalled.Count == 0) return [];

        // Covered only when no live eligible runner is missing from the roster:
        // a project nobody is eligible for has no entry and is covered by
        // whatever it has already passed over.
        var eligible = await runners.EligibleNamesAsync(db, keys, time.GetUtcNow(), ct);
        var covered = stalled
            .Select(p => (Project: p, Roster: RunnerTagColumn.Read(p.TrainStalledDrivers)))
            .Where(p => p.Roster.Count > 0
                && eligible.GetValueOrDefault(p.Project.Id, []).All(name => p.Roster.Contains(name, StringComparer.Ordinal)))
            .ToList();

        if (covered.Count == 0) return [];

        var ids = covered.Select(p => p.Project.Id).ToList();
        var events = await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.DriverStalled && ids.Contains(e.Issue!.ProjectId))
            .Select(e => new { e.Id, e.At, e.Payload, e.Issue!.ProjectId, e.Issue.Number, e.IssueId })
            .ToListAsync(ct);

        // In memory: ordering a DateTimeOffset in SQL is not portable across providers.
        var newestFirst = events.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).ToList();

        var rows = new List<(string ProjectKey, string ProjectName, string PassedOver, string? Elected, long IssueId, string HeadKey, DateTimeOffset At)>();
        foreach (var (project, roster) in covered)
        {
            foreach (var e in newestFirst.Where(e => e.ProjectId == project.Id))
            {
                var (passedOver, elected) = StalledPayload(e.Payload);
                if (passedOver is null || !roster.Contains(passedOver, StringComparer.Ordinal)) continue;

                rows.Add((project.Key, project.Name, passedOver, elected, e.IssueId, IssueKey.Format(project.Key, e.Number), e.At));
                break;
            }
        }

        if (rows.Count == 0) return [];

        // Why it stalled is whatever the head's newest comment says: a driver
        // that skips a head comments the reason on it.
        var headIds = rows.Select(r => r.IssueId).ToList();
        var stamps = await db.Comments.AsNoTracking()
            .Where(c => headIds.Contains(c.IssueId))
            .Select(c => new { c.IssueId, c.Id, c.CreatedAt })
            .ToListAsync(ct);

        // Only the newest comment's body is read back, since a comment can run to 100 KB.
        var newestIds = stamps
            .GroupBy(c => c.IssueId)
            .Select(g => g.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).First().Id)
            .ToList();
        var reasons = await db.Comments.AsNoTracking()
            .Where(c => newestIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.IssueId, c => c.Body, ct);

        return rows
            .Select(r => new TrainStalledDto(r.ProjectKey, r.ProjectName, r.PassedOver, r.Elected, r.HeadKey, r.At, reasons.GetValueOrDefault(r.IssueId)))
            .ToList();
    }

    /// <summary>The runner a <c>driver_stalled</c> event passed over and the one it elected, each null where the payload did not say.</summary>
    private static (string? PassedOver, string? Elected) StalledPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return (Read(doc.RootElement, "passedOver"), Read(doc.RootElement, "elected"));
        }
        catch (JsonException)
        {
            return (null, null);
        }

        static string? Read(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
