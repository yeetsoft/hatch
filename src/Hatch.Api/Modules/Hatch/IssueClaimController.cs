using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The lease on an issue: taken before an increment, refreshed while it runs,
/// released after it - see <see cref="EfHatchIssue.ClaimToken"/> for what it is
/// and <see cref="IssueClaims"/> for the rule.
///
/// A key may take, refresh and release one, which is the whole point: the
/// caller is a dispatcher, and a mutex only an operator could operate would
/// mutex nothing. The one narrowing is the tokenless <c>DELETE</c> - see
/// <see cref="NotAPerson"/>.
/// </summary>
/// <remarks>
/// <para>There is no <c>GET</c>. The claim rides <see cref="IssueDto"/> and
/// <see cref="IssueCardDto"/>, which is where every client needs it anyway, and
/// a second route serving the same object would be a second thing to keep in
/// step - the same call <see cref="IssueDependenciesController"/> makes about
/// its edges.</para>
///
/// <para>Nothing here looks at the issue's column. A claim is a lease, not a
/// dispatch: <see cref="WorkController"/> is what refuses to send an agent at a
/// terminal column, and a claim that second-guessed it would be the same rule
/// in two places, disagreeing the first time somebody edited one of them.</para>
/// </remarks>
[ApiController]
[Route("api/issues/{key}/claim")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class IssueClaimController(
    HatchContext db, IActorDirectory actors, IssueClaims claims, ICallerIdentity caller, TimeProvider time, Preemption preemption,
    Runners runners, Dispatch dispatch, RunnerSessions runnerSessions, IAuthService keys, RunnerStateLog stateLog) : ControllerBase
{
    /// <summary>
    /// Takes the lease, or says who already has it.
    /// </summary>
    /// <remarks>
    /// A live claim refuses a second one <em>including the caller's own</em>.
    /// Re-claiming is not a thing a runner does - it heartbeats - and a take
    /// that quietly succeeded for the same runner would hide the case the
    /// feature exists to catch, which is two checkouts on one box under one key.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<ClaimTakenDto>> TakeClaim(string key, ClaimRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        var runner = request.Runner?.Trim() ?? "";
        if (runner.Length == 0) return BadRequest("a claim names the runner holding it");
        if (runner.Length > EfHatchIssue.MaxClaimRunnerLength)
            return BadRequest($"a runner is at most {EfHatchIssue.MaxClaimRunnerLength} characters");

        // The id to store (a file row has none), the quiet window and the run
        // cap, all read off the row the request names - see Dispatch.ClaimPlaybookAsync.
        (int? Id, int? Limit, int? MaxRun)? playbook = null;
        if (request.PlaybookId is { } playbookId)
        {
            if (await dispatch.ClaimPlaybookAsync(issue.ProjectId, playbookId, ct) is not { } row)
                return BadRequest($"there is no playbook {playbookId}");
            playbook = (row.Id > 0 ? row.Id : null, row.QuietLimitSeconds, row.MaxRunSeconds);
        }

        var now = time.GetUtcNow();

        // A session the board has paused takes no new claim until the pause
        // ends. One claim it already holds is not touched: its heartbeats and
        // its release do not come through here.
        if (await caller.ApiKeyAsync(ct) is { } paused)
        {
            var pausedSession = await db.RunnerSessions.AsNoTracking()
                .Where(s => s.ApiKeyId == paused.Id && s.Where == runner && s.EndedAt == null)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync(ct);
            if (pausedSession is not null && RunnerSessions.BoardPausedAt(pausedSession, now))
                return Conflict(PausedSentence(pausedSession, now));
        }

        // Read first, because the refusal has to name a holder and that
        // sentence can only be written from the row. The guarantee is not in
        // this read - it is in the predicate on the write below, which is what
        // makes two callers who both got here on the same pre-claim state
        // resolve to one claim. Kept past the refusal, too: a token still on
        // the row here is a claim whose lease has lapsed rather than one that
        // was never taken, and that is worth a line on the trail of its own.
        var previous = ClaimSnapshot.Of(issue);
        if (claims.IsLive(previous, now))
            return Conflict(claims.Sentence(previous, now));

        // One runner per line of the tree: a live claim on an ancestor or a
        // descendant refuses this one with the same kind of sentence.
        if ((await claims.LineageAsync(db, actors, now, ct)).Holder(issue.Id, null) is { } relative)
            return Conflict(relative);

        var token = Guid.NewGuid();
        var actor = await caller.ActorNameAsync(ct);

        if (!await claims.TryTakeAsync(db, issue.Id, token, actor, runner, playbook?.Id, playbook?.Limit, playbook?.MaxRun, now, ct))
        {
            // Somebody won the race between that read and this write. Say who,
            // from the row as it stands now - and where the re-read finds it
            // clear again, it was taken and released inside this request, which
            // is worth naming honestly rather than dressing up.
            var current = await SnapshotAsync(issue.Id, ct);
            return Conflict(claims.IsLive(current, now)
                ? claims.Sentence(current, now)
                : "somebody else claimed this first");
        }

        // Take first, then look - see IssueClaims.ConfirmLineAsync for why the
        // check above is not enough. A take that loses lets go and writes no
        // event, so the trail never says a claim was taken that was not kept.
        if (await claims.ConfirmLineAsync(db, actors, issue.Id, token, now, ct) is { } lost)
            return Conflict(lost);

        // The previous token survives only when its lease lapsed rather than
        // being let go cleanly - a release clears it - so its presence here is
        // itself the fact worth telling: this take is a takeover, and the trail
        // says who stopped answering, and when, before it says who holds it now.
        if (previous.Token is not null)
            LogLapsed(issue, actor, Holder(previous.ClaimedBy, previous.Runner), previous.HeartbeatAt, now);

        await WithdrawDeclarationAsync(issue, actor, now, ct);
        Log(issue, actor, EfHatchIssueEvent.ClaimTaken, null, Holder(actor, runner), now);
        await db.SaveChangesAsync(ct);

        var window = claims.WindowFor(new ClaimSnapshot(null, null, null, null, null, null, null, playbook?.Limit, playbook?.MaxRun));
        return new ClaimTakenDto(token, actor, now, claims.TtlSeconds, window, playbook?.MaxRun ?? 0, runners.GoneAfterSeconds);
    }

    /// <summary>
    /// Why a paused session was refused a claim: the board paused it, for how
    /// much longer, what it saw, and who can lift it.
    /// </summary>
    private static string PausedSentence(EfHatchRunnerSession session, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((session.AgentDownUntil!.Value - now).TotalMinutes));
        var line = string.IsNullOrWhiteSpace(session.AgentDownLine) ? "" : $" ({session.AgentDownLine})";
        return $"the board paused this runner until {session.AgentDownUntil:HH:mm} UTC, " +
            $"{(minutes == 1 ? "1 minute" : $"{minutes} minutes")} from now{line}; " +
            "a person can lift the pause on the Runners page";
    }

    /// <summary>The name an answer nobody pressed is written under.</summary>
    private const string HatchActor = "Hatch";

    /// <summary>
    /// Still here. Refreshes the lease and, optionally, replaces the line it is
    /// carrying and hands on the runner's usage reading - and answers <see cref="ClaimPreemptedDto"/> in place of the
    /// ordinary <c>204</c> where the board has chosen this runner's issue to
    /// make room for an emergency one - see <see cref="Preemption"/> for the
    /// five rules that decide it.
    /// </summary>
    /// <remarks>
    /// No event on an ordinary beat, deliberately. A heartbeat is not a
    /// decision, and the trail would be a row a minute for every running
    /// increment - the same argument the work log makes about meter readings.
    /// A preempted one is the exception: it is the board's own decision, and
    /// the event it writes is also the record that this runner has already
    /// been told, so a later heartbeat - this one's own, or another runner's -
    /// does not tell it, or anybody else, twice. Nor does a usage reading
    /// carried on the beat write one - see <see cref="WriteUsageAsync"/>.
    /// </remarks>
    [HttpPost("heartbeat")]
    public async Task<ActionResult<ClaimPreemptedDto>> Heartbeat(
        string key, ClaimHeartbeatRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        // The id to store (a file row has none), the quiet window and the run
        // cap, all read off the row the request names - see Dispatch.ClaimPlaybookAsync.
        (int? Id, int? Limit, int? MaxRun)? playbook = null;
        if (request.PlaybookId is { } playbookId)
        {
            if (await dispatch.ClaimPlaybookAsync(issue.ProjectId, playbookId, ct) is not { } row)
                return BadRequest($"there is no playbook {playbookId}");
            playbook = (row.Id > 0 ? row.Id : null, row.QuietLimitSeconds, row.MaxRunSeconds);
        }

        var now = time.GetUtcNow();
        var claim = ClaimSnapshot.Of(issue);

        if (claim.Token != request.Token || !claims.IsLive(claim, now))
            return Conflict(Refused(claim, request.Token, now));

        // Normalised, then truncated rather than refused: the field is
        // cosmetic, and killing a live lease because a terminal printed
        // something wide would be the wrong trade.
        var chatter = Normalise(request.Chatter);

        // The predicate can still refuse - the lease may have expired or been
        // retaken between that read and this write - and it is what actually
        // fences the write, so its answer is the one that decides.
        if (!await claims.TryRefreshAsync(db, issue.Id, request.Token, chatter, playbook, now, ct))
            return Conflict(Refused(await SnapshotAsync(issue.Id, ct), request.Token, now));

        // Only a lease that actually renewed touches the row - see "One column
        // for liveness" on the ticket, and the remark on Runners.Project.
        var beat = await TouchRunnerAsync(claim, now, ct);
        await WriteUsageAsync(claim, beat, request, now, ct);

        if (await preemption.ForAsync(issue, ct) is not { } emergency) return NoContent();

        var emergencyKey = IssueKey.Format(emergency.Project!.Key, emergency.Number);
        var victimKey = IssueKey.Format(issue.Project!.Key, issue.Number);

        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = HatchActor,
            Kind = EfHatchIssueEvent.ClaimPreempted,
            Payload = JsonSerializer.Serialize(new { emergencyKey, emergencyTitle = emergency.Title }),
            At = now,
        });
        issue.UpdatedAt = now;

        db.Comments.Add(new EfHatchComment
        {
            IssueId = emergency.Id,
            Author = HatchActor,
            Body = $"{claim.ClaimedBy} at {claim.Runner} was preempted here, putting down {victimKey} - {issue.Title}.",
            Kind = EfHatchComment.Note,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);

        return new ClaimPreemptedDto(emergencyKey, emergency.Title);
    }

    /// <summary>
    /// Lets go of it. Two lanes: a runner presenting the token it was given,
    /// and the operator clearing whatever is there.
    /// </summary>
    /// <remarks>
    /// Releasing an issue that holds no claim is a <c>204</c> that writes
    /// nothing, in both lanes. Releasing twice is not an error, and neither is
    /// releasing a claim that expired underneath you - a <c>DELETE</c> says
    /// what should not exist afterwards, and it does not.
    ///
    /// <para>Nothing is returned. The caller is on its way out, and a
    /// projection it will not read is a query for nobody.</para>
    /// </remarks>
    /// <param name="token">
    /// The one the claim was taken with. Absent is the operator's clobber -
    /// see <see cref="NotAPerson"/>.
    /// </param>
    /// <param name="outcome">
    /// How the increment ended - one of <see cref="ClaimOutcomes"/>, read only
    /// where <paramref name="token"/> is given: the operator's tokenless
    /// clobber names no increment to have an outcome. Absent is accepted as it
    /// always has been, which covers the pick's own releases, a restart, and an
    /// older CLI.
    /// </param>
    /// <param name="reason">
    /// Why the runner let go - <see cref="ClaimLostReasons.Quiet"/> or
    /// <see cref="ClaimLostReasons.RunTime"/> or <see cref="ClaimLostReasons.Error"/>, written beside
    /// the outcome on the event. Read only where <paramref name="token"/> is given, and written only
    /// where <paramref name="outcome"/> is too: a reason with no outcome is accepted
    /// and not written. Absent is accepted as it always has been.
    /// </param>
    /// <param name="sessionId">
    /// The work-log session this increment ran as. Where it names a row of this issue and an outcome
    /// is given, the row's outcome and reason are stamped in the same save as the event, and the
    /// event carries the session id too. Absent, or matching no row, writes the event alone: a
    /// release is never refused for it.
    /// </param>
    /// <param name="failure">
    /// The runner failure the increment ended on - one of <see cref="RunnerFailureKinds.All"/>,
    /// read only where <paramref name="token"/> and <paramref name="outcome"/> are given, as
    /// <paramref name="reason"/> is. A kind the board does not know is dropped, not refused: the
    /// release goes through and writes no failure.
    /// </param>
    /// <param name="failureWhy">What the tool said, cut to <see cref="EfHatchRunnerFailure.MaxWhyLength"/>. Rides with a valid <paramref name="failure"/> only.</param>
    [HttpDelete]
    public async Task<IActionResult> ReleaseClaim(
        string key, [FromQuery] Guid? token, [FromQuery] string? outcome, [FromQuery] string? reason = null,
        [FromQuery] string? sessionId = null, [FromQuery] string? failure = null, [FromQuery] string? failureWhy = null,
        CancellationToken ct = default)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        if (token is null && await NotAPerson(ct) is { } refusal) return refusal;

        string? given = null;
        if (token is not null && !string.IsNullOrEmpty(outcome))
        {
            if (!ClaimOutcomes.IsValid(outcome))
                return BadRequest(
                    $"\"{outcome}\" is not an outcome - it is \"{ClaimOutcomes.Dropped}\", \"{ClaimOutcomes.Worked}\", " +
                    $"\"{ClaimOutcomes.Preempted}\", \"{ClaimOutcomes.Recused}\", " +
                    $"\"{ClaimOutcomes.Unservable}\", \"{ClaimOutcomes.Abandoned}\", \"{ClaimOutcomes.Unstarted}\", or nothing at all");
            given = outcome;
        }

        string? why = null;
        if (token is not null && !string.IsNullOrEmpty(reason))
        {
            if (reason is not (ClaimLostReasons.Quiet or ClaimLostReasons.RunTime or ClaimLostReasons.Error))
                return BadRequest(
                    $"\"{reason}\" is not a reason - it is \"{ClaimLostReasons.Quiet}\", " +
                    $"\"{ClaimLostReasons.RunTime}\", \"{ClaimLostReasons.Error}\", or nothing at all");
            why = reason;
        }

        // Never a 400: a board that knows fewer kinds than the loop sends is still
        // owed its release.
        var failed = given is not null && failure is not null && RunnerFailureKinds.All.Contains(failure, StringComparer.Ordinal)
            ? failure
            : null;
        var failedWhy = failed is not null ? RunnersController.Fits(failureWhy, EfHatchRunnerFailure.MaxWhyLength) : null;

        var now = time.GetUtcNow();
        var claim = ClaimSnapshot.Of(issue);

        if (claim.Token is null) return NoContent();

        if (token is { } presented && presented != claim.Token)
            return Conflict(Refused(claim, presented, now));

        var actor = await caller.ActorNameAsync(ct);
        var holder = Holder(claim.ClaimedBy, claim.Runner);

        var cleared = token is { } mine
            ? await claims.TryReleaseAsync(db, issue.Id, mine, ct)
            : await claims.ClearAsync(db, issue.Id, ct);

        // Nothing cleared means it went between the read and the write, which
        // is the state a DELETE was asking for. No event, because nothing here
        // did anything.
        if (!cleared) return NoContent();

        // The runner's own release, and only that lane: the operator's
        // tokenless clobber must not read as this runner having just been
        // freshly heard from, or a runner that has gone unresponsive would
        // suppress a preemption that should fire the moment it is forced off.
        if (token is not null) await TouchRunnerAsync(claim, now, ct);

        // The verdict goes on the row the session posted, in the save below. No
        // row is not a failure: the release is the state a DELETE asked for.
        var session = given is not null ? sessionId?.Trim() : null;
        if (string.IsNullOrEmpty(session)) session = null;
        if (session is not null
            && await db.WorkLog.FirstOrDefaultAsync(w => w.IssueId == issue.Id && w.SessionId == session, ct) is { } row)
        {
            row.Outcome = given;
            row.Reason = why;
        }

        // A dropped release by a claim that spent nothing is marked, so the
        // let-go count neither counts it nor stops at it - see SpentNothing.
        // The outcome is the runner's own and stays as sent.
        var verdict = token is not null && given == ClaimOutcomes.Dropped
            ? await SpentNothing.ClaimVerdictAsync(db, issue.Id, claim.ClaimedAt, ct)
            : SpentNothing.Verdict.NoRows;
        var spentNothing = verdict == SpentNothing.Verdict.Nothing;

        await WithdrawDeclarationAsync(issue, actor, now, ct);
        Log(
            issue,
            actor,
            token is null ? EfHatchIssueEvent.ClaimCleared : EfHatchIssueEvent.ClaimReleased,
            holder,
            null,
            now,
            given,
            claim.Runner,
            why,
            session,
            spentNothing,
            failed,
            failedWhy);

        if (failed is not null && await caller.ApiKeyAsync(ct) is { } releasing)
            await RunnerFailures.LinkIssueAsync(db, releasing.Id, claim.Runner, failed, issue.Id, now, ct);

        var paused = token is not null && given == ClaimOutcomes.Dropped
            ? await CountForPauseAsync(issue, claim, verdict, now, ct)
            : null;

        await db.SaveChangesAsync(ct);
        if (paused is not null) await stateLog.RecordAsync(db, keys, paused, now, ct);
        return NoContent();
    }

    /// <summary>
    /// Keeps the releasing session's count of releases in a row that spent
    /// nothing, and pauses the session at <see cref="IssueClaims.RunnerPauseAfter"/>.
    /// Only a dropped release moves it: one that spent nothing adds one, one
    /// whose rows spent tokens sets it to zero, and a release with no row since
    /// the claim says nothing either way and leaves it alone. The session is
    /// found the way <see cref="TouchRunnerAsync"/> finds it, and the count is
    /// written in the same save as the release's event. Nothing happens
    /// where there is no key or no live session at the claim's checkout.
    /// </summary>
    private async Task<string?> CountForPauseAsync(
        EfHatchIssue issue, ClaimSnapshot claim, SpentNothing.Verdict verdict, DateTimeOffset now, CancellationToken ct)
    {
        if (verdict == SpentNothing.Verdict.NoRows || claim.Runner is null) return null;
        if (await caller.ApiKeyAsync(ct) is not { } key) return null;

        var session = await db.RunnerSessions
            .Where(s => s.ApiKeyId == key.Id && s.Where == claim.Runner && s.EndedAt == null)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (session is null) return null;

        if (verdict == SpentNothing.Verdict.Spent)
        {
            session.PauseStreak = 0;
            return null;
        }

        if (claims.RunnerPauseAfter <= 0) return null;

        session.PauseStreak++;
        if (session.PauseStreak < claims.RunnerPauseAfter) return null;

        // Before the pause is set, while the facts still say what the gap was.
        await stateLog.SettleAsync(db, keys, key.Name, now, ct);

        var line = $"{session.PauseStreak} sessions in a row ended in error without spending a token, " +
            $"the last on {IssueKey.Format(issue.Project!.Key, issue.Number)}";
        session.AgentDownUntil = now.AddMinutes(claims.RunnerPauseMinutes);
        session.AgentDownKind = RunnerAgentDownKinds.BoardPaused;
        session.AgentDownLine = line.Length > EfHatchRunner.MaxAgentDownLineLength
            ? line[..EfHatchRunner.MaxAgentDownLineLength]
            : line;
        session.PauseStreak = 0;

        // Opens the failure row beside the pause, in the same save; nothing
        // lifts it - the session's next beat after the pause ends it.
        await RunnerFailures.OpenBoardPauseAsync(db, key.Id, session.Where, session.AgentDownLine, now, ct);

        // The caller records it, after the save that carries the pause.
        return key.Name;
    }

    // ---- The one narrowing ----

    /// <summary>
    /// A person, not a key. The tokenless release takes a ticket off whoever is
    /// holding it, and an agent that could do that could take a ticket off
    /// another agent mid-increment - which is the exact failure the claim
    /// exists to prevent, reintroduced through its own back door. A runner lets
    /// go of its own lease by presenting its token, which every honest runner
    /// has.
    /// </summary>
    /// <remarks>
    /// Checked in the action rather than expressed in the attribute, for the
    /// reason <c>IssueWorkLogController.NotAKey</c> gives: <c>RoleGate</c> is
    /// dormant wherever the wall is off, which is all of local
    /// development, and a guarantee that evaporates under a switch is not a
    /// guarantee.
    ///
    /// <para>"A key" is asked as
    /// <see cref="ICallerIdentity.IsProgramAsync"/>, so a keyless runner in
    /// local mode is refused here too - the failure this narrowing exists to
    /// prevent is an agent taking a ticket off another agent, and an agent
    /// without a credential can do that just as well as one with.</para>
    /// </remarks>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("clearing another runner's claim is the operator's, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;

    // ---- Saying why ----

    /// <summary>
    /// Why a token was refused, in the five shapes a runner can be refused in:
    /// the row carries nothing, the row carries this one but has run past its
    /// cap, the row carries this one but has gone quiet, the row carries this
    /// one and the lease is simply over, or the row
    /// carries somebody else's - each with the sentence a person reads and the
    /// <see cref="ClaimLostReasons"/> a client asks.
    /// </summary>
    private ClaimRefusedDto Refused(ClaimSnapshot claim, Guid presented, DateTimeOffset now) => claim.Token switch
    {
        null => new ClaimRefusedDto("this claim was cleared", ClaimLostReasons.Cleared),
        // Over the cap is asked before quiet: a claim that is both is told the
        // one that no amount of chatter would have saved it from.
        var held when held == presented => claims.IsOverRun(claim, now)
            ? new ClaimRefusedDto(
                $"this claim ran past its {Limit(claim.MaxRunSeconds!.Value)} limit", ClaimLostReasons.RunTime)
            : claims.IsQuiet(claim, now)
                ? new ClaimRefusedDto($"this claim has gone quiet for {Minutes(claims.WindowFor(claim))}", ClaimLostReasons.Quiet)
                : new ClaimRefusedDto("this claim has expired", ClaimLostReasons.Expired),
        _ => new ClaimRefusedDto(
            claims.IsLive(claim, now) ? claims.Sentence(claim, now) : "this claim was taken over",
            ClaimLostReasons.Taken),
    };

    /// <summary><see cref="Minutes"/> as an adjective, for a limit: <c>60-minute</c>, <c>90-second</c>.</summary>
    private static string Limit(int seconds) => Minutes(seconds).Replace(' ', '-').TrimEnd('s');

    /// <summary>A window in the unit a person says it in: whole minutes where it is one, seconds otherwise.</summary>
    private static string Minutes(int seconds) => seconds % 60 == 0
        ? seconds == 60 ? "1 minute" : $"{seconds / 60} minutes"
        : $"{seconds} seconds";

    // ---- One column for liveness ----

    /// <summary>
    /// The one record a beat touched: the key whose live session it moved, if
    /// it moved one, and the runner row it moved, if it moved one.
    /// </summary>
    private readonly record struct Beat(Guid? SessionKeyId, string? RunnerName);

    /// <summary>
    /// Marks the holding loop as heard from, in exactly one place - the
    /// caller's own record and no other. An unconditional courtesy write, with
    /// no token to fence against, matching
    /// <see cref="IssueClaims.TryRefreshAsync"/>'s own <c>ExecuteUpdateAsync</c>
    /// style rather than loading a tracked row for a second
    /// <c>SaveChangesAsync</c>. In order:
    /// <list type="number">
    /// <item>Under a key, the live (not ended) <see cref="EfHatchRunnerSession"/>
    /// of that key at the claim's checkout - what a current CLI beats.</item>
    /// <item>The runner row named by <c>ClaimedBy</c>, its primary key, so at
    /// most one row: the wall off, where the claim's holder is the runner's
    /// own name, or a key named like its runner.</item>
    /// <item>Only where no session matched: for an older CLI beating
    /// <c>POST /api/runners/{name}</c> under a key named otherwise, the
    /// single row at the checkout that was live when the claim was taken - an
    /// older CLI beats just before it claims. A row under a superseded name was
    /// already gone by then, so it is never revived (HA-2074's ghost), and a
    /// checkout with a live session never reaches this step.</item>
    /// </list>
    /// Where no live session matched, the one the sweep ended is revived - see
    /// <see cref="RunnerSessions.ReviveAsync"/>.
    /// A heartbeat racing the runner's own first beat matches nothing and does
    /// nothing - there is nothing yet to bump. This is what keeps
    /// <see cref="EfHatchRunner.LastSeenAt"/> and
    /// <see cref="EfHatchRunnerSession.LastSeenAt"/> current the moment a claim
    /// heartbeat or a claim release touches them, rather than only on the
    /// loop's own next beat.
    /// </summary>
    private async Task<Beat> TouchRunnerAsync(ClaimSnapshot claim, DateTimeOffset now, CancellationToken ct)
    {
        if (claim.ClaimedBy is null || claim.Runner is null) return default;

        // Settle, never record, before the bump below overwrites LastSeenAt: a
        // beat after a long silence must not hide that the runner was gone. The
        // state the beat leaves behind is the loop's own next beat to record.
        var callerKey = await caller.ApiKeyAsync(ct);
        if (callerKey is not null) await stateLog.SettleAsync(db, keys, callerKey.Name, now, ct);
        if (!string.Equals(callerKey?.Name, claim.ClaimedBy, StringComparison.Ordinal))
            await stateLog.SettleAsync(db, keys, claim.ClaimedBy, now, ct);

        var sessions = 0;
        Guid? keyId = null;
        if (callerKey is { } key)
        {
            sessions = await db.RunnerSessions
                .Where(s => s.ApiKeyId == key.Id && s.Where == claim.Runner && s.EndedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastSeenAt, now), ct);

            // The sweep may have ended this session while the claim was still
            // being renewed - a read of the runner list after one late beat. A
            // renewed claim is the proof it is working, so it reads live again.
            if (sessions == 0)
                sessions = await runnerSessions.ReviveAsync(db, key.Id, claim.Runner, now, ct);

            if (sessions > 0) keyId = key.Id;
        }

        var byName = await db.LegacyRunners.Where(r => r.Name == claim.ClaimedBy)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastSeenAt, now), ct);

        if (byName > 0) return new Beat(keyId, claim.ClaimedBy);
        if (sessions > 0 || claim.ClaimedAt is not { } claimedAt) return new Beat(keyId, null);

        var cutoff = runners.Cutoff(claimedAt);
        var liveAtClaim = await db.LegacyRunners
            .Where(r => r.Where == claim.Runner && r.RetiredAt == null && r.LastSeenAt >= cutoff)
            .OrderByDescending(r => r.LastSeenAt).ThenBy(r => r.Name)
            .Select(r => r.Name)
            .FirstOrDefaultAsync(ct);

        if (liveAtClaim is null) return default;

        await stateLog.SettleAsync(db, keys, liveAtClaim, now, ct);
        await db.LegacyRunners.Where(r => r.Name == liveAtClaim)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastSeenAt, now), ct);

        return new Beat(null, liveAtClaim);
    }

    /// <summary>
    /// Hands the holding loop's usage reading to the record
    /// <see cref="TouchRunnerAsync"/> touched - its live session and/or the one
    /// runner row - so <c>GET /api/utilization</c> stays current while an
    /// increment runs; the loop's own heartbeat is sent only between
    /// increments. Never to a row by checkout: that reached every name the
    /// checkout had ever used. Nothing is written where the beat carries no
    /// windows or no read time: <see cref="Utilization.Of(IEnumerable{ValueTuple{string, DateTimeOffset?, DateTimeOffset?, string, string}}, DateTimeOffset)"/>
    /// skips a reading with no read time, so one without could only null out a
    /// good timestamp. A source is written, with its probe-off reason, only
    /// where the beat names one - see <see cref="ClaimHeartbeatRequest.UsageSource"/>;
    /// without one both stay as they were. The guard on <c>UsageReadAt</c> is in the <c>WHERE</c>
    /// rather than a read first, because this and the loop's own heartbeat
    /// race, and a reading older than the one held is not the account's
    /// current word. Equal writes.
    /// </summary>
    private async Task WriteUsageAsync(
        ClaimSnapshot claim, Beat beat, ClaimHeartbeatRequest request, DateTimeOffset now, CancellationToken ct)
    {
        if (claim.Runner is null
            || request.UsageReadAt is not { } readAt
            || Utilization.Serialise(request.Usage) is not { } usage)
            return;

        var runner = claim.Runner;

        // The source and probe-off reason join the update only when the beat
        // names a source; a null is never written otherwise.
        if (beat.SessionKeyId is { } keyId)
            await db.RunnerSessions
                .Where(s => s.ApiKeyId == keyId && s.Where == runner && s.EndedAt == null
                    && (s.UsageReadAt == null || s.UsageReadAt <= readAt))
                .ExecuteUpdateAsync(
                    s =>
                    {
                        s.SetProperty(r => r.Usage, usage)
                            .SetProperty(r => r.UsageReadAt, readAt)
                            .SetProperty(r => r.UsageReceivedAt, r => r.UsageReadAt == readAt ? r.UsageReceivedAt : now);
                        if (request.UsageSource is { } source)
                            s.SetProperty(r => r.UsageSource, source).SetProperty(r => r.UsageProbeOff, request.UsageProbeOff);
                    },
                    ct);

        if (beat.RunnerName is { } name)
            await db.LegacyRunners
                .Where(r => r.Name == name && (r.UsageReadAt == null || r.UsageReadAt <= readAt))
                .ExecuteUpdateAsync(
                    s =>
                    {
                        s.SetProperty(r => r.Usage, usage)
                            .SetProperty(r => r.UsageReadAt, readAt)
                            .SetProperty(r => r.UsageReceivedAt, r => r.UsageReadAt == readAt ? r.UsageReceivedAt : now);
                        if (request.UsageSource is { } source)
                            s.SetProperty(r => r.UsageSource, source).SetProperty(r => r.UsageProbeOff, request.UsageProbeOff);
                    },
                    ct);
    }

    // ---- Odds and ends ----

    /// <summary>
    /// One line at most, trimmed and capped. Null stays null - "no opinion" and
    /// "clear it" are different instructions and the empty string is the second
    /// one, so it survives to the write intact.
    /// </summary>
    private static string? Normalise(string? chatter)
    {
        if (chatter is null) return null;

        var line = chatter.ReplaceLineEndings("\n").Split('\n')[0].Trim();
        return line.Length > EfHatchIssue.MaxClaimChatterLength
            ? line[..EfHatchIssue.MaxClaimChatterLength]
            : line;
    }

    /// <summary>The holder as the trail names one: who, and from where.</summary>
    private static string Holder(string? who, string? runner) => $"{who} at {runner}";

    /// <summary>
    /// The trail row for a lapsed lease - its own shape, because
    /// <paramref name="heardAt"/> is not a claim to be held, only the last
    /// instant this one was.
    /// </summary>
    private void LogLapsed(EfHatchIssue issue, string actor, string from, DateTimeOffset? heardAt, DateTimeOffset at)
    {
        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.ClaimLapsed,
            Payload = JsonSerializer.Serialize(new { from, heardAt }),
            At = at,
        });

        issue.UpdatedAt = at;
    }

    /// <summary>
    /// The claim columns as they stand now, read past the change tracker -
    /// which the tracked entity cannot answer for, because the writes in
    /// <see cref="IssueClaims"/> run outside it.
    /// </summary>
    private async Task<ClaimSnapshot> SnapshotAsync(long issueId, CancellationToken ct) =>
        await db.Issues.AsNoTracking()
            .Where(i => i.Id == issueId)
            .Select(i => new ClaimSnapshot(
                i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt,
                i.ClaimQuietLimitSeconds, i.ClaimMaxRunSeconds))
            .FirstOrDefaultAsync(ct)
        ?? new ClaimSnapshot(null, null, null, null, null, null, null, null);

    /// <summary>
    /// The release payload. Every key past <c>from</c> and <c>to</c> is left
    /// out when there is nothing to say, so a release that named no outcome
    /// writes the same payload it always has.
    /// </summary>
    private static string ReleasePayload(
        string? from, string? to, string? outcome, string? runner, string? reason, string? sessionId, bool spentNothing,
        string? failure = null, string? failureWhy = null)
    {
        var payload = new Dictionary<string, object?> { ["from"] = from, ["to"] = to };
        if (outcome is null) return JsonSerializer.Serialize(payload);

        payload["outcome"] = outcome;
        payload["runner"] = runner;
        if (reason is not null) payload["reason"] = reason;
        if (sessionId is not null) payload["sessionId"] = sessionId;
        if (spentNothing) payload[SpentNothing.Marker] = true;
        if (failure is not null)
        {
            payload["failure"] = failure;
            if (failureWhy is not null) payload["failureWhy"] = failureWhy;
        }
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// The trail row, in <c>DependencyAdded</c>'s <c>{ from, to }</c> shape so
    /// the issue page's event line needs no new case to read it. A take is
    /// null-to-holder and both releases are holder-to-null; which of the two a
    /// release was is the kind's to say.
    /// </summary>
    private void Log(
        EfHatchIssue issue, string actor, string kind, string? from, string? to, DateTimeOffset at,
        string? outcome = null, string? runner = null, string? reason = null, string? sessionId = null,
        bool spentNothing = false, string? failure = null, string? failureWhy = null)
    {
        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = kind,
            // outcome is left out of the object entirely rather than
            // serialized as null, so a release that named none writes the
            // same payload it always has. The runner rides along with an
            // outcome only: "from" says "<who> on <runner>" and cannot be
            // split back apart, and LetGo.By needs the runner by itself. The
            // reason and the session id ride with an outcome too, and are left out the same way.
            Payload = ReleasePayload(from, to, outcome, runner, reason, sessionId, spentNothing, failure, failureWhy),
            At = at,
        });

        issue.UpdatedAt = at;
    }

    /// <summary>
    /// Withdraws a no-code declaration on an issue still left of review - see
    /// <see cref="NoCode.ClearStanding"/> - at either end of a claim. The
    /// columns are read only for a declared issue, so the ordinary take and
    /// release cost nothing more.
    /// </summary>
    private async Task WithdrawDeclarationAsync(EfHatchIssue issue, string actor, DateTimeOffset now, CancellationToken ct)
    {
        if (!NoCode.Declared(issue) || issue.Status is not { } standing) return;

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        if (NoCode.ClearStanding(issue, standing, statuses, actor, now) is not { } cleared) return;

        issue.Events.Add(cleared);
        issue.UpdatedAt = now;
    }

    private async Task<EfHatchIssue?> LoadAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.Include(i => i.Project).Include(i => i.Status).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
    }
}
