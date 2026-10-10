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
[Route("api/hatch/issues/{key}/claim")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class IssueClaimController(
    HatchContext db, IssueClaims claims, ICallerIdentity caller, TimeProvider time, Preemption preemption) : ControllerBase
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

        var now = time.GetUtcNow();

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
        if ((await claims.LineageAsync(db, now, ct)).Holder(issue.Id, null) is { } relative)
            return Conflict(relative);

        var token = Guid.NewGuid();
        var actor = await caller.ActorNameAsync(ct);

        if (!await claims.TryTakeAsync(db, issue.Id, token, actor, runner, now, ct))
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
        if (await claims.ConfirmLineAsync(db, issue.Id, token, now, ct) is { } lost)
            return Conflict(lost);

        // The previous token survives only when its lease lapsed rather than
        // being let go cleanly - a release clears it - so its presence here is
        // itself the fact worth telling: this take is a takeover, and the trail
        // says who stopped answering, and when, before it says who holds it now.
        if (previous.Token is not null)
            LogLapsed(issue, actor, Holder(previous.ClaimedBy, previous.Runner), previous.HeartbeatAt, now);

        Log(issue, actor, EfHatchIssueEvent.ClaimTaken, null, Holder(actor, runner), now);
        await db.SaveChangesAsync(ct);

        return new ClaimTakenDto(token, actor, now, claims.TtlSeconds, claims.StallLapseSeconds);
    }

    /// <summary>The name an answer nobody pressed is written under.</summary>
    private const string HatchActor = "Hatch";

    /// <summary>
    /// Still here. Refreshes the lease and, optionally, replaces the line it is
    /// carrying - and answers <see cref="ClaimPreemptedDto"/> in place of the
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
    /// does not tell it, or anybody else, twice.
    /// </remarks>
    [HttpPost("heartbeat")]
    public async Task<ActionResult<ClaimPreemptedDto>> Heartbeat(
        string key, ClaimHeartbeatRequest request, CancellationToken ct)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        var now = time.GetUtcNow();
        var claim = ClaimSnapshot.Of(issue);

        if (claim.Token != request.Token || !claims.IsLive(claim, now))
            return Conflict(Why(claim, request.Token, now));

        // Normalised, then truncated rather than refused: the field is
        // cosmetic, and killing a live lease because a terminal printed
        // something wide would be the wrong trade.
        var chatter = Normalise(request.Chatter);

        // The predicate can still refuse - the lease may have expired or been
        // retaken between that read and this write - and it is what actually
        // fences the write, so its answer is the one that decides.
        if (!await claims.TryRefreshAsync(db, issue.Id, request.Token, chatter, now, ct))
            return Conflict(Why(await SnapshotAsync(issue.Id, ct), request.Token, now));

        // Only a lease that actually renewed touches the row - see "One column
        // for liveness" on the ticket, and the remark on Runners.Project.
        await TouchRunnerAsync(claim.Runner, now, ct);

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
            Body = $"{claim.ClaimedBy} on {claim.Runner} was preempted here, putting down {victimKey} - {issue.Title}.",
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
    [HttpDelete]
    public async Task<IActionResult> ReleaseClaim(
        string key, [FromQuery] Guid? token, [FromQuery] string? outcome, CancellationToken ct = default)
    {
        if (await LoadAsync(key, ct) is not { } issue) return NotFound();

        if (token is null && await NotAPerson(ct) is { } refusal) return refusal;

        string? given = null;
        if (token is not null && !string.IsNullOrEmpty(outcome))
        {
            if (!ClaimOutcomes.IsValid(outcome))
                return BadRequest(
                    $"\"{outcome}\" is not an outcome - it is \"{ClaimOutcomes.Dropped}\", \"{ClaimOutcomes.Worked}\", " +
                    $"\"{ClaimOutcomes.Preempted}\", or nothing at all");
            given = outcome;
        }

        var now = time.GetUtcNow();
        var claim = ClaimSnapshot.Of(issue);

        if (claim.Token is null) return NoContent();

        if (token is { } presented && presented != claim.Token)
            return Conflict(Why(claim, presented, now));

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
        if (token is not null) await TouchRunnerAsync(claim.Runner, now, ct);

        Log(
            issue,
            actor,
            token is null ? EfHatchIssueEvent.ClaimCleared : EfHatchIssueEvent.ClaimReleased,
            holder,
            null,
            now,
            given);

        await db.SaveChangesAsync(ct);
        return NoContent();
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
    /// Why a token was refused, in the four shapes a runner can be refused in:
    /// the row carries nothing, the row carries this one but has gone quiet,
    /// the row carries this one and the lease is simply over, or the row
    /// carries somebody else's.
    /// </summary>
    private string Why(ClaimSnapshot claim, Guid presented, DateTimeOffset now) => claim.Token switch
    {
        null => "this claim was cleared",
        var held when held == presented => claims.IsQuiet(claim, now)
            ? $"this claim has gone quiet for {claims.StallLapseSeconds / 60} minutes"
            : "this claim has expired",
        _ => claims.IsLive(claim, now) ? claims.Sentence(claim, now) : "this claim was taken over",
    };

    // ---- One column for liveness ----

    /// <summary>
    /// Bumps the holding runner's own row to now - an unconditional courtesy
    /// write, with no token to fence against, matching
    /// <see cref="IssueClaims.TryRefreshAsync"/>'s own <c>ExecuteUpdateAsync</c>
    /// style rather than loading a tracked row for a second
    /// <c>SaveChangesAsync</c>. A heartbeat racing the runner's own first
    /// <c>POST /api/hatch/runners/{name}</c> matches no row and does nothing -
    /// fine, there is nothing yet to bump. This is what keeps
    /// <see cref="EfHatchRunner.LastSeenAt"/> the one fact
    /// <see cref="Preemption"/>'s rule 5 reads, current the moment a claim
    /// heartbeat or a claim release touches it, rather than only on the
    /// runner's own next beat.
    /// </summary>
    private Task TouchRunnerAsync(string? runner, DateTimeOffset now, CancellationToken ct) =>
        runner is null
            ? Task.CompletedTask
            : db.Runners.Where(r => r.Name == runner).ExecuteUpdateAsync(s => s.SetProperty(r => r.LastSeenAt, now), ct);

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
    private static string Holder(string? who, string? runner) => $"{who} on {runner}";

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
                i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt))
            .FirstOrDefaultAsync(ct)
        ?? new ClaimSnapshot(null, null, null, null, null, null, null);

    /// <summary>
    /// The trail row, in <c>DependencyAdded</c>'s <c>{ from, to }</c> shape so
    /// the issue page's event line needs no new case to read it. A take is
    /// null-to-holder and both releases are holder-to-null; which of the two a
    /// release was is the kind's to say.
    /// </summary>
    private void Log(
        EfHatchIssue issue, string actor, string kind, string? from, string? to, DateTimeOffset at,
        string? outcome = null)
    {
        issue.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = kind,
            // outcome is left out of the object entirely rather than
            // serialized as null, so a release that named none writes the
            // same payload it always has.
            Payload = outcome is null
                ? JsonSerializer.Serialize(new { from, to })
                : JsonSerializer.Serialize(new { from, to, outcome }),
            At = at,
        });

        issue.UpdatedAt = at;
    }

    private async Task<EfHatchIssue?> LoadAsync(string key, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return null;

        return await db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstOrDefaultAsync(ct);
    }
}
