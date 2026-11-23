using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The health of the board in one read, for a program that wants the whole
/// answer rather than the pieces: wires <see cref="BoardHealthReport.Fold"/> to
/// the database and says nothing of its own.
/// </summary>
/// <remarks>
/// The last bar's waiting-on-you is the attention panel's own count - this reads
/// <see cref="AttentionReviews"/> and <see cref="Questions.Waiting"/> exactly as
/// <see cref="AttentionController"/> does - and the bands are the cumulative
/// flow's, so the three reads agree. The answer names the issues in flight now, and only those: the
/// route is open to a caller who may see them, `db.Issues` carries the caller's project filter, and
/// `project` narrows the list as it narrows every count. Titles are read for the listed ids alone.
///
/// The status history is never loaded whole. The fold reads moves back to the
/// start of the window before this one, and everything else only from the start
/// of this one; for each issue the newest event before that point is read too,
/// since it says where a stretch began or whether a claim was open.
///
/// "When anything last waited" is the one answer that reaches past that: the
/// window's own data gives the later part, and <see cref="WaitHistory"/> keeps
/// thirty days per project for up to five minutes, read only on the caller's
/// own context. Neither loads the whole history.
/// </remarks>
[ApiController]
[Route("api/board/health")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class BoardHealthController(
    HatchContext db, IActorDirectory actors, Runners runners, Dispatch dispatch, TimeProvider time, ColumnNormals normals,
    WaitHistory waits, ICallerIdentity caller, IOptions<AuthOptions> auth, IAuthService keys, RunnerStateLog stateLog, RunnerSessions sessions) : ControllerBase
{
    private const string DefaultWindow = "8h";

    private static readonly string[] ClaimKinds =
    [
        EfHatchIssueEvent.ClaimTaken, EfHatchIssueEvent.ClaimReleased, EfHatchIssueEvent.ClaimCleared, EfHatchIssueEvent.ClaimLapsed,
    ];

    /// <summary>
    /// The board's health over a window ending now.
    /// </summary>
    /// <param name="window">One of <c>2h</c>, <c>4h</c>, <c>8h</c>, <c>12h</c>, <c>24h</c>; <c>8h</c> when absent. A bar is a twenty-fourth of it.</param>
    /// <param name="project">Narrows every count to these projects' issues, repeatable; every project when absent or empty.</param>
    [HttpGet]
    public async Task<ActionResult<BoardHealthDto>> GetBoardHealth(
        [FromQuery] string? window, [FromQuery] List<string>? project, CancellationToken ct)
    {
        var hours = BoardHealthReport.WindowHours.FirstOrDefault(h => $"{h}h" == (window ?? DefaultWindow).Trim().ToLowerInvariant());
        if (hours == 0)
            return BadRequest($"a window is one of {string.Join(", ", BoardHealthReport.WindowHours.Select(h => $"{h}h"))} - not {window}");

        var keys = WorkController.ProjectKeys(project);
        if (keys.Count > 0)
        {
            var known = await db.Projects.Select(p => p.Key).ToListAsync(ct);
            var unknown = keys.Where(k => !known.Contains(k)).ToList();
            if (unknown.Count > 0) return BadRequest($"there is no project {string.Join(", ", unknown)}");
        }

        var scope = keys.Count > 0 ? keys.ToHashSet() : null;

        var now = time.GetUtcNow();
        var axis = BoardHealthReport.AxisOf(hours, now);
        var previous = axis.From - (axis.To - axis.From);

        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        // The one full read: a row per issue, every project the caller can see,
        // since a parent outside the scope still carries a level down.
        var issues = await BoardHealthTrail.IssuesAsync(db, ct);

        var moves = await BoardHealthTrail.MovesAsync(db, previous, scope, ct);

        // Every claim of every issue, whatever the project scope: the lanes read
        // them all, and the Moved fold takes the ones it needs. Of what came
        // before the window, only an open claim (a take) is kept.
        var claims = (await BoardHealthTrail.ReadAsync(db, ClaimKinds, axis.From, null, null, ct, olderOnlyIf: EfHatchIssueEvent.ClaimTaken))
            .Select(e => new BoardHealthReport.ClaimEvent(e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload))
            .ToList();

        var levels = await BoardHealthTrail.LevelsAsync(db, axis.From, ct);
        var records = await BoardHealthTrail.RecordsAsync(db, axis.From, scope, ct);

        var questions = await QuestionTrail.ReadAsync(db, axis.From, axis.To, ct);

        var table = await normals.ForAsync(db, statuses, now, ct);
        var normalOf = table.Where(n => n.Seconds is not null).ToDictionary(n => n.StatusId, n => n.Seconds!.Value);

        var tree = await PriorityTree.ForAsync(db, actors, ct, ignoreFilters: true);

        // Leaf-ness is taken for every scoped row, not only those in flight now:
        // the oldest in flight reads cards that have since moved on.
        var leaves = new HashSet<long>();
        if (Columns.Implementation(statuses) is not null)
        {
            var gate = await FamilyGate.ForAsync(db, actors, statuses, ct, tree);
            foreach (var i in issues)
            {
                if (scope is not null && !scope.Contains(i.Row.ProjectKey)) continue;
                if (gate.IsLeaf(i.Row.Id, i.Row.Type, statuses)) leaves.Add(i.Row.Id);
            }
        }

        var attention = await WaitingOnYouAsync(statuses, tree, scope, moves, issues, now, ct);

        // When anything last waited: this window's own answer, and the kept thirty days behind it.
        var windowWaited = BoardHealthReport.LastWaited(
            issues, moves, records, levels, questions, statuses, axis.From, now, scope);
        var keptWaited = await waits.ForAsync(db, statuses, scope, now, ct);
        var scopedIssues = scope is null ? issues : issues.Where(i => scope.Contains(i.Row.ProjectKey)).ToList();
        var (waitedAt, waitedIsFloor) = BoardHealthReport.Settle(
            windowWaited, keptWaited, attention.PullRequests + attention.Questions,
            scopedIssues.Count == 0 ? null : scopedIssues.Min(i => i.Row.CreatedAt), now);

        var result = BoardHealthReport.Fold(
            issues, moves, claims, levels, records, questions, statuses,
            hours, now, scope, leaves, normalOf, attention);

        // Titles for the issues listed and no others; `db.Issues` already keeps to what the caller may see.
        var listedIds = result.InFlightNow.Select(r => r.Row.Id).ToList();
        var titles = listedIds.Count == 0
            ? new Dictionary<long, string>()
            : (await db.Issues.AsNoTracking()
                    .Where(i => listedIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.Title })
                    .ToListAsync(ct))
                .ToDictionary(i => i.Id, i => i.Title);
        var columnNames = statuses.ToDictionary(c => c.Id, c => c.Name);

        var lanes = await LanesAsync(
            statuses, scope is null ? moves : await UnscopedMovesAsync(axis, ct), claims, axis, now, ct);

        return new BoardHealthDto(
            result.From, result.To, result.BarMinutes,
            result.Bars.Select(b => new BoardHealthBarDto(
                b.Start, b.End, b.Board, b.Runner, b.Person, b.Moved, b.Waiting, b.InFlight, b.Done,
                b.WaitingPullRequests, b.WaitingQuestions, b.OldestInFlightSeconds, b.OldestWaitSeconds,
                new BoardHealthAgesDto(b.Ages.Healthy, b.Ages.Aging, b.Ages.PastNormal, b.Ages.NoNormal))).ToList(),
            result.Total, result.PreviousTotal, result.PastNormal, result.HasWorkColumn,
            new BoardHealthRunnersDto(
                lanes.Up, lanes.Paused, lanes.Lanes.Count,
                lanes.Lanes.Select(l => new BoardHealthLaneDto(
                    l.Name, l.Up, l.Paused, l.LastSeenAt,
                    l.Cells.Select(c => new BoardHealthCellDto(c.State, c.Why)).ToList())).ToList()),
            result.InFlightNow
                .Where(r => titles.ContainsKey(r.Row.Id))
                .Select(r => new BoardHealthIssueDto(
                    IssueKey.Format(r.Row.ProjectKey, r.Row.Number), titles[r.Row.Id], r.StatusId,
                    columnNames.GetValueOrDefault(r.StatusId, ""), r.Opens, r.Seconds, r.NormalSeconds, r.Band))
                .ToList(),
            waitedAt, waitedIsFloor);
    }

    /// <summary>
    /// The runners' lanes: the runners <c>GET /api/runners</c> would show this
    /// caller, whatever the project scope, with their state record, claims,
    /// stalls and failed sessions folded by <see cref="RunnerLanes"/>. Writes
    /// nothing - unlike the Runners page it does not sweep sessions gone, since
    /// <see cref="RunnerSessions.IsLive"/> is time-based already.
    /// </summary>
    private async Task<RunnerLanes.Result> LanesAsync(
        List<EfHatchStatus> statuses, List<BoardHealthReport.Move> moves, List<BoardHealthReport.ClaimEvent> claims,
        BoardHealthReport.Axis axis, DateTimeOffset now, CancellationToken ct)
    {
        var (all, personId) = await RunnerReach.OfAsync(auth.Value.Enabled, caller, actors, ct);
        var visible = (await Runner.JoinAsync(db, keys, ct))
            .Where(r => all || r.OwnerPersonId == personId)
            .ToList();
        if (visible.Count == 0) return new RunnerLanes.Result(0, 0, []);

        // Each name's rows from the window's start, and the newest before it.
        var names = visible.Select(r => r.Name).ToList();
        var rows = (await db.RunnerStates.AsNoTracking()
                .Where(s => names.Contains(s.Runner))
                .Where(s => s.At >= axis.From
                    || s.Id == db.RunnerStates
                        .Where(x => x.Runner == s.Runner && x.At < axis.From)
                        .OrderByDescending(x => x.At).ThenByDescending(x => x.Id)
                        .Select(x => (long?)x.Id)
                        .FirstOrDefault())
                .OrderBy(s => s.At).ThenBy(s => s.Id)
                .ToListAsync(ct))
            .ToLookup(s => s.Runner, StringComparer.Ordinal);

        var inputs = new List<RunnerLanes.RunnerInput>(visible.Count);
        foreach (var r in visible)
        {
            var recorded = rows[r.Name].Select(s => new RunnerStateRule.Change(s.At, s.State, s.Kind)).ToList();
            var tail = recorded.Count > 0
                ? stateLog.ClockTail(RunnerStateLog.SourcesOf(r), recorded[^1].At, now)
                : [];
            inputs.Add(new RunnerLanes.RunnerInput(
                r.Name, r.LastSeenAt, r.IsLive(runners, sessions, now), RunnerStateRule.Timeline(recorded, tail)));
        }

        var stalls = (await ReadSinceAsync(EfHatchIssueEvent.Stalled, axis.From, ct))
            .Select(e => new RunnerLanes.Stall(e.IssueId, e.At, e.Actor))
            .ToList();

        var failed = (await db.WorkLog.AsNoTracking()
                .Where(w => w.IsError && w.CreatedAt >= axis.From)
                .Select(w => new { w.IssueId, w.CreatedAt })
                .ToListAsync(ct))
            .Select(w => new RunnerLanes.FailedSession(w.IssueId, w.CreatedAt))
            .ToList();

        var forward = BoardHealthReport.ForwardMoves(moves, claims, statuses, axis);
        return RunnerLanes.Fold(inputs, claims, forward, stalls, failed, axis, now);
    }

    /// <summary>Every status change from the start of the window, in every project: the lanes ignore the project scope.</summary>
    private async Task<List<BoardHealthReport.Move>> UnscopedMovesAsync(BoardHealthReport.Axis axis, CancellationToken ct) =>
        await BoardHealthTrail.MovesAsync(db, axis.From, null, ct);

    /// <summary>The events of one kind at or after <paramref name="since"/>, and nothing before it.</summary>
    private async Task<List<BoardHealthTrail.Ev>> ReadSinceAsync(string kind, DateTimeOffset since, CancellationToken ct) =>
        await db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == kind && e.At >= since)
            .Select(e => new BoardHealthTrail.Ev(e.IssueId, e.Id, e.Kind, e.At, e.Actor, e.Payload))
            .ToListAsync(ct);

    /// <summary>
    /// What waits on a person at this instant: the attention panel's own pull
    /// requests and questions, kept to the scope. Not <see cref="Questions.ProjectAsync"/>,
    /// which walks an issue's ambitions for a wording this has no use for.
    /// </summary>
    private async Task<BoardHealthReport.Attention> WaitingOnYouAsync(
        List<EfHatchStatus> statuses, PriorityTree tree, HashSet<string>? scope,
        List<BoardHealthReport.Move> moves, List<BoardHealthReport.Issue> issues, DateTimeOffset now, CancellationToken ct)
    {
        var prs = 0;
        long? oldest = null;

        void Older(DateTimeOffset since)
        {
            var seconds = (long)Math.Round((now - since).TotalSeconds);
            if (oldest is not { } current || seconds > current) oldest = seconds;
        }

        if (Columns.AwaitingReview(statuses) is { } review)
        {
            var read = await AttentionReviews.ReadAsync(db, dispatch, runners, now, tree, statuses, review, ct);
            var rows = issues.ToDictionary(i => i.Row.Id, i => i.Row);
            var newestMove = moves
                .GroupBy(m => m.IssueId)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.At).ThenBy(m => m.EventId).Last().At);

            foreach (var r in read.Reviews.Where(r => scope is null || scope.Contains(r.ProjectKey)))
            {
                prs++;
                if (newestMove.TryGetValue(r.IssueId, out var at)) Older(at);
                else if (rows.TryGetValue(r.IssueId, out var row)) Older(row.CreatedAt);
            }
        }

        var waiting = (await Questions.Waiting(db)
                .Select(c => new { c.IssueId, c.CreatedAt, ProjectKey = c.Issue!.Project!.Key })
                .ToListAsync(ct))
            .Where(c => scope is null || scope.Contains(c.ProjectKey))
            .ToList();

        foreach (var q in waiting) Older(q.CreatedAt);

        return new BoardHealthReport.Attention(prs, waiting.Count, oldest);
    }
}
