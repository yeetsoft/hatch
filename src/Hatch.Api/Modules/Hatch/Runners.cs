using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The runner, and the one place its two horizons live: how long a row may go
/// unheard from before it reads <em>gone</em>, how much longer before it stops
/// being returned at all, and what a row plus whatever claim it holds looks
/// like on the wire.
///
/// A class rather than a static for the reason <see cref="IssueClaims"/> is
/// one: it holds a configured number and nothing else, and every judgement it
/// makes takes the instant it is judging against rather than reading a clock.
/// </summary>
/// <remarks>
/// Nothing here sweeps and nothing here writes. A runner that stopped answering
/// at midnight is idle at 00:01, gone at 00:02 and not in the answer at all by
/// 02:01 - each of those by arithmetic against <c>LastSeenAt</c> at the moment
/// somebody asks, which is the same lazy expiry the claim uses. There is no
/// timeout to tune and no background job to notice has stopped.
/// </remarks>
public sealed class Runners(IOptions<HatchOptions> options, IOptions<AuthOptions> auth)
{
    /// <summary>
    /// How long after its last heartbeat a row is still returned by the read:
    /// two hours, so a runner that died overnight is a ghost row somebody can
    /// look at the next morning rather than one gone before they sit down.
    /// </summary>
    /// <remarks>
    /// A constant and not a setting. The read is what the queue view and the
    /// Runners page both draw, and <see cref="DropBefore"/> takes the longer of
    /// this and <see cref="GoneAfterSeconds"/>, so a gone horizon above two
    /// hours cannot drop a row before it was ever read as gone.
    /// </remarks>
    public const int RetentionSeconds = 2 * 60 * 60;

    /// <summary>
    /// The silence after which a runner reads as gone, in seconds. Guarded here
    /// and nowhere else: a configured zero or a negative would make every
    /// runner gone on arrival, and a misconfigured horizon should cost a
    /// fallback rather than a page that says every loop is dead.
    /// </summary>
    public int GoneAfterSeconds { get; } =
        options.Value.RunnerGoneAfterSeconds > 0
            ? options.Value.RunnerGoneAfterSeconds
            : new HatchOptions().RunnerGoneAfterSeconds;

    /// <summary>The heartbeat at or after which a runner is still being heard from.</summary>
    public DateTimeOffset Cutoff(DateTimeOffset now) => now.AddSeconds(-GoneAfterSeconds);

    /// <summary>The heartbeat before which a row is not handed out at all.</summary>
    public DateTimeOffset DropBefore(DateTimeOffset now) => now.AddSeconds(-Math.Max(GoneAfterSeconds, RetentionSeconds));

    /// <summary>
    /// Whether this runner is still answering. Exactly at the cutoff is here;
    /// one tick past it is gone - the same edge the claim draws, so the two
    /// never disagree about which side of a boundary an instant is on.
    /// </summary>
    public bool IsHere(DateTimeOffset lastSeenAt, DateTimeOffset now) => lastSeenAt >= Cutoff(now);

    /// <summary>
    /// Whether this row has aged out of the read. Not a deletion: the row stays
    /// where it is, and a runner that comes back under the same name picks its
    /// own bounds up again rather than being seeded afresh.
    /// </summary>
    public bool IsDropped(DateTimeOffset lastSeenAt, DateTimeOffset now) => lastSeenAt < DropBefore(now);

    /// <summary>
    /// The row as a client draws it, with whatever it holds folded in.
    /// </summary>
    /// <param name="claim">
    /// The live claim this runner holds, or null between increments. The
    /// liveness is the caller's to decide, because a scan judges every row
    /// against one instant - see <see cref="IssueClaims.IsLive"/>.
    /// </param>
    /// <remarks>
    /// The line is the claim's while there is one and the row's when there is
    /// not, and that is the whole of why the row has a line at all: an
    /// increment's chatter already rides its lease, so this column only ever
    /// carries what a runner said between tickets. <c>LastSeenAt</c> is read
    /// alone and nowhere folded against the claim's own heartbeat: the claim
    /// heartbeat and the claim release both touch the holding runner's row
    /// themselves (<see cref="IssueClaimController"/>), so this column is
    /// always current on its own - and reading it alone is load-bearing for
    /// <see cref="Preemption"/>'s rule 5, where a runner that has just
    /// released must read as free rather than gone in exactly the seconds
    /// that matters.
    /// </remarks>
    public RunnerDto Project(
        EfHatchRunner runner, (string Key, ClaimSnapshot Claim)? claim, IReadOnlyList<string>? drives = null,
        string? forName = null, bool canControl = true) => new(
        runner.Name,
        runner.Kind,
        runner.FirstSeenAt,
        runner.LastSeenAt,
        claim?.Key,
        claim is { } held ? held.Claim.Chatter : runner.Line,
        claim is { } at ? at.Claim.ChatterAt : runner.LineAt,
        runner.State,
        runner.Under,
        runner.MaxRuns,
        runner.MaxSpend,
        runner.UntilAt,
        GoneAfterSeconds,
        runner.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
        runner.Clones,
        runner.Mine,
        runner.Standing,
        runner.Where,
        runner.Agent,
        runner.ExhaustedUntil,
        drives,
        runner.Tags?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray() ?? [],
        forName,
        canControl);

    /// <summary>
    /// What the loop reads back off its own heartbeat: the row's operator half,
    /// plus who it works for - a fact about the caller and not about the row,
    /// so it is handed in rather than read off <paramref name="runner"/>.
    /// </summary>
    public static RunnerInstructionDto Instruct(EfHatchRunner runner, string? forName, IReadOnlyList<string>? drives = null) =>
        new(runner.State, runner.Under, runner.MaxRuns, runner.MaxSpend, runner.UntilAt, forName, drives,
            runner.Tags?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? []);

    /// <summary>
    /// Every live claim, by the runner holding it. The key is
    /// <see cref="EfHatchIssue.ClaimRunner"/> read backwards, which is what
    /// makes "what is this runner working" a question with one answer and no
    /// second copy to keep in step - shared between <see cref="RunnersController"/>'s
    /// own read and <see cref="Preemption"/>'s rule 5, rather than kept twice.
    /// </summary>
    public async Task<Dictionary<string, (string Key, ClaimSnapshot Claim)>> HeldAsync(
        HatchContext db, IssueClaims claims, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.Issues.AsNoTracking()
            .Where(i => i.ClaimToken != null && i.ClaimRunner != null)
            .Select(i => new
            {
                i.Project!.Key,
                i.Number,
                i.ClaimToken,
                i.ClaimedBy,
                i.ClaimRunner,
                i.ClaimedAt,
                i.ClaimHeartbeatAt,
                i.ClaimChatter,
                i.ClaimChatterAt,
            })
            .ToListAsync(ct);

        return rows
            .Select(i => (
                Runner: i.ClaimRunner!,
                Key: IssueKey.Format(i.Key, i.Number),
                Claim: new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner, i.ClaimedAt,
                    i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt)))
            .Where(i => claims.IsLive(i.Claim, now))
            // A runner holds one ticket at a time by construction, and the most
            // recent heartbeat is the one to draw if something ever leaves two
            // behind - a row about the wrong ticket is worse than a row about
            // none.
            .GroupBy(i => i.Runner, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.Claim.HeartbeatAt)
                    .Select(i => (i.Key, i.Claim))
                    .First(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// One project, one driver: the longest-running live loop that holds a
    /// checkout of it, by <see cref="Dispatch.HoldsCheckout"/> - the same rule
    /// <see cref="WorkController.GetReview"/> uses for its own eligibility, so
    /// the two can never disagree about which projects a runner can act on.
    /// </summary>
    /// <remarks>
    /// With the wall up, a project that has an owner is driven only by a
    /// runner working for one of its owners or approvers - the people a merge
    /// belongs to, and whose forge credentials can make it. A runner for
    /// anybody else, or on a key that belongs to nobody, is not a candidate
    /// however long it has run, and a project with no such runner live has no
    /// driver at all. With the wall off, or on a project with no owner, a role
    /// means nothing and every holder of a checkout is eligible. "Has an owner"
    /// and "is one" read the raw member rows, as <c>ProjectAccess</c> does: this
    /// runs on every heartbeat, and a project whose only owner was deleted stays
    /// owned here, which errs toward nobody driving rather than anybody.
    ///
    /// The tie-break - oldest <c>FirstSeenAt</c> first, then <c>Name</c>
    /// ordinally - is what makes the election stable: the longest-running
    /// eligible runner keeps a project for as long as it keeps heartbeating,
    /// and only moves when it drops out of <c>live</c>, the same lazy
    /// arithmetic <see cref="IsHere"/> already draws everywhere else. No
    /// sweep, no new table, no migration - every input already lives on
    /// <see cref="EfHatchRunner"/> and <see cref="EfHatchProjectRepository"/>.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DriversAsync(
        HatchContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = Cutoff(now);

        // Only a loop ever starts a MergeTrain; a `once` runner's own BeatAsync
        // throws its instruction away (GoToWork.cs), so electing one would
        // strand a project with a driver that can never answer.
        var live = await db.Runners.AsNoTracking()
            .Where(r => r.LastSeenAt >= cutoff && r.Kind == EfHatchRunner.LoopKind)
            .Select(r => new { r.Name, r.FirstSeenAt, r.Remotes, r.Standing, r.ForPersonId })
            .ToListAsync(ct);

        var candidates = live
            .Select(r => (r.Name, r.FirstSeenAt, r.ForPersonId, Repos: RepositoryDeclaration.From(
                r.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries), r.Standing, null)))
            .ToList();

        // Who a merge belongs to, per project, for the projects that have an
        // owner. Left empty with the wall down, where a role means nothing.
        Dictionary<int, HashSet<Guid>> people = [];
        if (auth.Value.Enabled)
        {
            var members = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.Role == ProjectMemberRole.Owner || m.Role == ProjectMemberRole.Approver)
                .Select(m => new { m.ProjectId, m.PersonId, m.Role })
                .ToListAsync(ct);

            people = members
                .GroupBy(m => m.ProjectId)
                .Where(g => g.Any(m => m.Role == ProjectMemberRole.Owner))
                .ToDictionary(g => g.Key, g => g.Select(m => m.PersonId).ToHashSet());
        }

        var projects = await db.Projects.AsNoTracking().Include(p => p.Repositories).ToListAsync(ct);
        var byRunner = live.ToDictionary(r => r.Name, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var project in projects)
        {
            var driver = candidates
                .Where(c => Dispatch.HoldsCheckout(project.Repositories, c.Repos))
                .Where(c => !people.TryGetValue(project.Id, out var qualifying)
                    || (c.ForPersonId is { } person && qualifying.Contains(person)))
                .OrderBy(c => c.FirstSeenAt).ThenBy(c => c.Name, StringComparer.Ordinal)
                .Select(c => c.Name).FirstOrDefault();

            if (driver is not null) byRunner[driver].Add(project.Key);
        }

        return byRunner.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// One line at most, trimmed and capped - <see cref="IssueClaimController"/>'s
    /// rule, called on the other kind of heartbeat. Null stays null, because
    /// "no opinion" and "clear it" are different instructions and the empty
    /// string is the second one.
    /// </summary>
    public static string? Normalise(string? line)
    {
        if (line is null) return null;

        var first = line.ReplaceLineEndings("\n").Split('\n')[0].Trim();
        return first.Length > EfHatchRunner.MaxLineLength ? first[..EfHatchRunner.MaxLineLength] : first;
    }
}
