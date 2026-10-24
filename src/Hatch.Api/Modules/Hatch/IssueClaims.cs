using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The claim, and the one place its rule lives: how long a lease survives, when
/// it is over, what it looks like on the wire, and the sentence that names its
/// holder.
///
/// A class rather than a static so it can hold the TTL, and a singleton because
/// it is a pure function with a class around it - it reads
/// <see cref="HatchOptions"/> and touches neither the clock nor, except through
/// a context handed to it, the database.
/// </summary>
/// <remarks>
/// <para>Every judgement here takes <c>now</c> rather than reading it, so one
/// scan judges every row on a board against a single instant. A pass in which
/// the clock moved between two rows would be a pass that could fold one card
/// and not its neighbour for no reason anybody could reconstruct.</para>
///
/// <para>The writes are the other half of it, and they are why this class
/// exists rather than four copies of a predicate in a controller. Each one is a
/// single conditional <c>UPDATE</c> carrying the guarantee in its
/// <c>WHERE</c>: a take matches only a row nothing live holds, and a refresh or
/// a release matches only a row still carrying the caller's own token. That is
/// what closes the window between a request's read and its write - a runner
/// whose lease expired mid-increment cannot clear the lease that replaced
/// it.</para>
///
/// <para><see cref="Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"/>'s
/// <c>ExecuteUpdateAsync</c> runs outside the change tracker, so an event
/// written beside one of these is a second statement. They are deliberately not
/// wrapped in a transaction: a crash between the two loses a trail row and no
/// correctness.</para>
/// </remarks>
public sealed class IssueClaims(IOptions<HatchOptions> options)
{
    /// <summary>
    /// The TTL a claim is judged against, in seconds. Guarded here and nowhere
    /// else: a configured zero or a negative would make every claim dead on
    /// arrival, and a misconfigured TTL should cost a fallback rather than a
    /// board nobody may claim.
    /// </summary>
    public int TtlSeconds { get; } =
        options.Value.ClaimTtlSeconds > 0 ? options.Value.ClaimTtlSeconds : new HatchOptions().ClaimTtlSeconds;

    /// <summary>
    /// The window a silent claim is judged against, in
    /// seconds - see <see cref="HatchOptions.StallLapseMinutes"/>. Guarded the
    /// way <see cref="TtlSeconds"/> is, with one difference: zero is not a
    /// misconfiguration here but the operator turning lapsing off, so it is
    /// kept rather than replaced with the fallback. Only a negative value -
    /// which nothing before this could ever have meant - falls back.
    /// </summary>
    public int StallLapseSeconds { get; } = options.Value.StallLapseMinutes switch
    {
        0 => 0,
        > 0 => options.Value.StallLapseMinutes * 60,
        _ => new HatchOptions().StallLapseMinutes * 60,
    };

    /// <summary>
    /// How long a marked issue is folded before the loop picks it back up by
    /// itself, in seconds - see <see cref="HatchOptions.StallResumeMinutes"/>.
    /// Guarded the way <see cref="StallLapseSeconds"/> is: zero is kept as
    /// "never resume unattended", and only a negative value falls back.
    /// </summary>
    public int StallResumeSeconds { get; } = options.Value.StallResumeMinutes switch
    {
        0 => 0,
        > 0 => options.Value.StallResumeMinutes * 60,
        _ => new HatchOptions().StallResumeMinutes * 60,
    };

    /// <summary>
    /// How many consecutive stalls an issue may resume through before it
    /// holds for a person instead - see
    /// <see cref="HatchOptions.StallResumeLimit"/>.
    /// </summary>
    public int StallResumeLimit { get; } = options.Value.StallResumeLimit > 0
        ? options.Value.StallResumeLimit
        : new HatchOptions().StallResumeLimit;

    /// <summary>
    /// The longest a holder may ask to be left alone, in seconds - see
    /// <see cref="HatchOptions.StallGraceMaxMinutes"/>. Guarded the way
    /// <see cref="StallLapseSeconds"/> is, except that zero keeps no
    /// meaning of its own beyond "nobody may ask": a negative value falls back
    /// to the default.
    /// </summary>
    public int StallGraceMaxSeconds { get; } = options.Value.StallGraceMaxMinutes switch
    {
        0 => 0,
        > 0 => options.Value.StallGraceMaxMinutes * 60,
        _ => new HatchOptions().StallGraceMaxMinutes * 60,
    };

    /// <summary>
    /// How many claims in a row may lapse on one ticket before the take that
    /// would be the next one is refused and a person is asked - see
    /// <see cref="HatchOptions.ClaimLapseLimit"/>. Zero is kept as "never
    /// stop"; only a negative value falls back to the default.
    /// </summary>
    public int ClaimLapseLimit { get; } = options.Value.ClaimLapseLimit switch
    {
        0 => 0,
        > 0 => options.Value.ClaimLapseLimit,
        _ => new HatchOptions().ClaimLapseLimit,
    };

    /// <summary>
    /// The quiet window a project's claims are judged against, in seconds: the
    /// project's own <see cref="EfHatchProject.StallLapseMinutes"/> where it
    /// sets one (zero turning lapsing off for it), else the install's. A
    /// negative value on a row is a misconfiguration and falls back, the way
    /// the install-wide one does.
    /// </summary>
    public int LapseSecondsFor(int? projectMinutes) => projectMinutes switch
    {
        0 => 0,
        > 0 => projectMinutes.Value * 60,
        _ => StallLapseSeconds,
    };

    /// <summary>The most grace a project's claim may ask for, in seconds - the project's own, else the install's.</summary>
    public int GraceMaxSecondsFor(int? projectMinutes) => projectMinutes switch
    {
        0 => 0,
        > 0 => projectMinutes.Value * 60,
        _ => StallGraceMaxSeconds,
    };

    /// <summary>The lapse limit for a project's tickets - the project's own, else the install's. Zero is off.</summary>
    public int LapseLimitFor(int? projectLimit) => projectLimit switch
    {
        0 => 0,
        > 0 => projectLimit.Value,
        _ => ClaimLapseLimit,
    };

    /// <summary>
    /// When a claim that was last heard from at <paramref name="lastWord"/> goes
    /// quiet, or null where lapsing is off. The one place the arithmetic lives:
    /// every write that moves it calls this, and every reader compares against
    /// the column it lands in.
    /// </summary>
    public static DateTimeOffset? QuietAtFor(DateTimeOffset lastWord, int lapseSeconds) =>
        lapseSeconds > 0 ? lastWord.AddSeconds(lapseSeconds) : null;

    /// <summary>The heartbeat at or after which a claim is still alive.</summary>
    public DateTimeOffset Cutoff(DateTimeOffset now) => now.AddSeconds(-TtlSeconds);

    /// <summary>
    /// Whether something holds this issue right now. Exactly at the cutoff is
    /// alive; one tick past it is not. A claim that has gone quiet is not
    /// live either, however recently it was heartbeat - see
    /// <see cref="IsQuiet"/>.
    /// </summary>
    public bool IsLive(ClaimSnapshot? claim, DateTimeOffset now) =>
        claim is { Token: not null, HeartbeatAt: { } beat } && beat >= Cutoff(now) && !IsQuiet(claim, now);

    /// <summary>
    /// Whether a live-looking claim has gone quiet: its
    /// <see cref="ClaimSnapshot.QuietAt"/> - the last time anybody said anything
    /// (or the take itself where nobody ever has) plus the project's quiet
    /// window, pushed out by any grace the holder asked for - is in the past. A
    /// bare heartbeat does not move it - it says a process is still alive, not
    /// that anybody is still looking at this ticket - and a runner that never
    /// sends a line at all (<c>--quiet</c>) is measured from when it was claimed.
    /// Null means lapsing is off for this claim, or that it was taken before the
    /// column existed, and is never quiet.
    /// </summary>
    public bool IsQuiet(ClaimSnapshot claim, DateTimeOffset now) =>
        claim.QuietAt is { } at && at < now;

    /// <summary>
    /// The claim as a client draws it, or null where there is none <em>or it
    /// has expired</em>. A dead claim is not handed out to be drawn: nobody
    /// downstream should have to redo this arithmetic, and a card showing a
    /// holder that stopped existing four hours ago is worse than a card showing
    /// nothing.
    /// </summary>
    public IssueClaimDto? Project(ClaimSnapshot? claim, DateTimeOffset now) =>
        IsLive(claim, now)
            ? new IssueClaimDto(
                claim!.ClaimedBy ?? "", claim.Runner ?? "",
                claim.ClaimedAt ?? claim.HeartbeatAt!.Value, claim.HeartbeatAt!.Value,
                claim.Chatter, claim.ChatterAt,
                TtlSeconds)
            : null;

    /// <summary>
    /// The one sentence a live claim prints, wherever it is printed: in the
    /// <c>409</c> refusing a second claim, in the row <c>work/queue</c> folds,
    /// and in the dispatch <c>work/{key}</c> refuses. One method called three
    /// times, so a holder-shaped fold cannot come to read three ways.
    /// </summary>
    /// <param name="subject">
    /// What is being worked, from the reader's side: <c>this</c> for the issue
    /// itself, or a relative's key and where it sits, as in
    /// <c>HA-12, above this,</c> with its own comma, since a clause set off by
    /// commas needs one on each side - so the three shapes a fold can take are one
    /// format string and cannot drift apart.
    /// </param>
    public string Sentence(ClaimSnapshot claim, DateTimeOffset now, string subject = "this") =>
        $"{claim.ClaimedBy} is working {subject} from {claim.Runner}, last heard from {Ago(claim.HeartbeatAt, now)}";

    /// <summary>
    /// How long ago, at the resolution somebody reading a refusal at a terminal
    /// cares about. Seconds under a minute because the interesting case is a
    /// lease taken moments ago by the runner in the next window; minutes after
    /// that, because past a minute nobody is counting.
    /// </summary>
    private static string Ago(DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } beat) return "never";

        var since = now - beat;
        if (since < TimeSpan.FromSeconds(10)) return "just now";
        if (since < TimeSpan.FromMinutes(1)) return $"{(int)since.TotalSeconds} seconds ago";

        var minutes = (int)since.TotalMinutes;
        return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
    }

    // ---- The line of the tree ----

    /// <summary>
    /// The board's tree and its live claims, read once and judged against one
    /// instant - the input to <see cref="ClaimLineage.Holder"/>.
    /// </summary>
    /// <remarks>
    /// Two <c>AsNoTracking</c> reads, the second of which is the negation of
    /// the predicate <see cref="TryTakeAsync"/> writes under. The tree comes
    /// out in <c>(Rank, Id)</c> order so that the children of each node are
    /// already in the order the board draws them, which is the order
    /// <see cref="ClaimLineage.Holder"/> names a holder in.
    /// </remarks>
    public async Task<ClaimLineage> LineageAsync(HatchContext db, DateTimeOffset now, CancellationToken ct, PriorityTree? priorities = null)
    {
        var cutoff = Cutoff(now);

        var tree = await db.Issues.AsNoTracking()
            .OrderBy(i => i.Rank).ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.ParentId, ProjectKey = i.Project!.Key, i.Number })
            .ToListAsync(ct);

        var live = await db.Issues.AsNoTracking()
            .Where(i => i.ClaimToken != null && i.ClaimHeartbeatAt >= cutoff
                && (i.ClaimQuietAt == null || i.ClaimQuietAt >= now))
            .Select(i => new
            {
                i.Id,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt, i.ClaimQuietAt),
            })
            .ToListAsync(ct);

        var parents = new Dictionary<long, long?>(tree.Count);
        var keys = new Dictionary<long, string>(tree.Count);
        var children = new Dictionary<long, List<long>>();

        foreach (var row in tree)
        {
            parents[row.Id] = row.ParentId;
            keys[row.Id] = IssueKey.Format(row.ProjectKey, row.Number);
        }

        foreach (var row in tree)
        {
            // A parent id pointing at nothing is a root, as it is in Rollup.
            if (row.ParentId is not { } parent || !parents.ContainsKey(parent)) continue;

            if (!children.TryGetValue(parent, out var siblings)) children[parent] = siblings = [];
            siblings.Add(row.Id);
        }

        // Which tasks share a story's branch, and which story: two of them
        // working at once push to the same branch. Read only when something
        // holds a claim at all - a lineage with nobody in it never asks.
        var shared = new Dictionary<long, long>();
        if (live.Count > 0)
        {
            priorities ??= await PriorityTree.ForAsync(db, ct);
            foreach (var id in keys.Keys)
                if (priorities.SharedStoryOf(id) is { } story) shared[id] = story;
        }

        return new ClaimLineage(this, now, parents, keys, children, live.ToDictionary(r => r.Id, r => r.Claim), shared);
    }

    /// <summary>
    /// Runs after a take has landed, and answers the sentence naming a relative
    /// who holds a live claim - having let go of the new lease first - or null
    /// where the line is clear and the take stands.
    /// </summary>
    /// <remarks>
    /// Take first, then look. A conditional <c>UPDATE</c> fences one row and a
    /// line of the tree is many, so two takes on a parent and its child from
    /// one pre-claim state each write their own row and no <c>WHERE</c> can see
    /// the other. Looking after the write does: whichever take looks second
    /// sees the first. The worst case is that both look before either lets go
    /// and both release, and the next pass tries again; two runners never both
    /// keep a claim.
    /// </remarks>
    public async Task<string?> ConfirmLineAsync(
        HatchContext db, long issueId, Guid token, DateTimeOffset now, CancellationToken ct)
    {
        var lineage = await LineageAsync(db, now, ct);
        if (lineage.Holder(issueId, token) is not { } sentence) return null;

        await TryReleaseAsync(db, issueId, token, ct);
        return sentence;
    }

    // ---- The writes ----

    /// <summary>
    /// Takes the lease, and answers whether it was taken. One conditional
    /// <c>UPDATE</c>: the <c>WHERE</c> is the exact negation of
    /// <see cref="IsLive"/>, so the guarantee lives on the write and two
    /// requests that both decided against the same pre-claim state cannot both
    /// come away holding it.
    /// </summary>
    /// <remarks>
    /// The chatter is cleared with the take. A new lease does not inherit the
    /// last one's last words, nor its grace: the quiet deadline starts again at
    /// the take, <paramref name="lapseSeconds"/> out (the install's where null).
    /// </remarks>
    public async Task<bool> TryTakeAsync(
        HatchContext db, long issueId, Guid token, string actor, string runner, int? playbookId,
        DateTimeOffset now, CancellationToken ct, int? lapseSeconds = null)
    {
        var cutoff = Cutoff(now);
        var quietAt = QuietAtFor(now, lapseSeconds ?? StallLapseSeconds);

        return await db.Issues
            .Where(i => i.Id == issueId
                && (i.ClaimToken == null || i.ClaimHeartbeatAt == null || i.ClaimHeartbeatAt < cutoff
                    || (i.ClaimQuietAt != null && i.ClaimQuietAt < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimToken, token)
                .SetProperty(i => i.ClaimedBy, actor)
                .SetProperty(i => i.ClaimRunner, runner)
                .SetProperty(i => i.ClaimedAt, now)
                .SetProperty(i => i.ClaimHeartbeatAt, now)
                .SetProperty(i => i.ClaimChatter, (string?)null)
                .SetProperty(i => i.ClaimChatterAt, (DateTimeOffset?)null)
                .SetProperty(i => i.ClaimQuietAt, quietAt)
                .SetProperty(i => i.ClaimPlaybookId, playbookId), ct) == 1;
    }

    /// <summary>
    /// Refreshes a live lease, and answers whether there was one to refresh.
    /// Both halves of the predicate matter: the token fences a runner whose
    /// lease was retaken, and the cutoff ends a lease whose own token is still
    /// on the row - once it is over it is over, and a late heartbeat does not
    /// resurrect it.
    /// </summary>
    /// <param name="chatter">
    /// The line to carry: null leaves whatever is there alone, and an empty
    /// string clears it. Already normalised by the caller - see
    /// <see cref="IssueClaimController"/>.
    /// </param>
    /// <param name="playbookId">
    /// Null leaves <c>ClaimPlaybookId</c> alone; a value overwrites it. There
    /// is no third state to clear it back to null from a heartbeat.
    /// </param>
    public async Task<bool> TryRefreshAsync(
        HatchContext db, long issueId, Guid token, string? chatter, int? playbookId, DateTimeOffset now,
        CancellationToken ct, int? lapseSeconds = null)
    {
        var cutoff = Cutoff(now);

        // A word moves the deadline; a bare heartbeat, or a line cleared, does not.
        var quietAt = QuietAtFor(now, lapseSeconds ?? StallLapseSeconds);
        var live = db.Issues.Where(i => i.Id == issueId && i.ClaimToken == token && i.ClaimHeartbeatAt >= cutoff
            && (i.ClaimQuietAt == null || i.ClaimQuietAt >= now));

        var written = (chatter, playbookId) switch
        {
            (null, null) => await live.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimHeartbeatAt, now), ct),
            (null, { } id) => await live.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimHeartbeatAt, now)
                .SetProperty(i => i.ClaimPlaybookId, id), ct),
            ("", null) => await live.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimHeartbeatAt, now)
                .SetProperty(i => i.ClaimChatter, (string?)null)
                .SetProperty(i => i.ClaimChatterAt, (DateTimeOffset?)null), ct),
            ("", { } id) => await live.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimHeartbeatAt, now)
                .SetProperty(i => i.ClaimChatter, (string?)null)
                .SetProperty(i => i.ClaimChatterAt, (DateTimeOffset?)null)
                .SetProperty(i => i.ClaimPlaybookId, id), ct),
            (_, null) => await live.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimHeartbeatAt, now)
                .SetProperty(i => i.ClaimChatter, chatter)
                .SetProperty(i => i.ClaimChatterAt, (DateTimeOffset?)now)
                .SetProperty(i => i.ClaimQuietAt, quietAt), ct),
            (_, { } id) => await live.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimHeartbeatAt, now)
                .SetProperty(i => i.ClaimChatter, chatter)
                .SetProperty(i => i.ClaimChatterAt, (DateTimeOffset?)now)
                .SetProperty(i => i.ClaimQuietAt, quietAt)
                .SetProperty(i => i.ClaimPlaybookId, id), ct),
        };

        return written == 1;
    }

    /// <summary>
    /// Pushes a live lease's quiet deadline out to <paramref name="until"/>,
    /// and answers whether there was a live lease with a deadline to push. Never
    /// shortens one: the later of the two instants stands, so a second ask for
    /// less is not a way to take the first back, and a word said after the
    /// grace ends still moves the deadline from there.
    /// </summary>
    /// <remarks>
    /// No token in the predicate, deliberately: the caller is the agent a
    /// dispatcher spawned, which knows the ticket and not the lease. The
    /// predicate is <see cref="IsLive"/> all the same, so it cannot extend a
    /// lease that is already over, and the cap the caller applies bounds what
    /// one ask can hold.
    /// </remarks>
    public async Task<bool> TryGraceAsync(
        HatchContext db, long issueId, DateTimeOffset until, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = Cutoff(now);

        return await db.Issues
            .Where(i => i.Id == issueId && i.ClaimToken != null && i.ClaimHeartbeatAt >= cutoff
                && i.ClaimQuietAt != null && i.ClaimQuietAt >= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ClaimQuietAt, i => i.ClaimQuietAt > until ? i.ClaimQuietAt : until), ct) == 1;
    }

    /// <summary>
    /// Clears the lease this token holds, and answers whether it held one.
    /// Unlike <see cref="TryRefreshAsync"/> there is no cutoff in the
    /// predicate: a matching token may tidy up a lease that expired underneath
    /// it, because the row is still the holder's and nobody else has taken it.
    /// </summary>
    public async Task<bool> TryReleaseAsync(
        HatchContext db, long issueId, Guid token, CancellationToken ct) =>
        await Clear(db.Issues.Where(i => i.Id == issueId && i.ClaimToken == token), ct) == 1;

    /// <summary>
    /// Clears whatever is there, whoever holds it - the operator's clobber, and
    /// the only write here with no token in its predicate. Answers whether
    /// there was anything to clear, so releasing an unclaimed issue can write
    /// no event.
    /// </summary>
    public async Task<bool> ClearAsync(
        HatchContext db, long issueId, CancellationToken ct) =>
        await Clear(db.Issues.Where(i => i.Id == issueId && i.ClaimToken != null), ct) == 1;

    /// <summary>The nine columns back to unclaimed, written once so the two release lanes cannot clear different things.</summary>
    private static Task<int> Clear(IQueryable<EfHatchIssue> rows, CancellationToken ct) =>
        rows.ExecuteUpdateAsync(s => s
            .SetProperty(i => i.ClaimToken, (Guid?)null)
            .SetProperty(i => i.ClaimedBy, (string?)null)
            .SetProperty(i => i.ClaimRunner, (string?)null)
            .SetProperty(i => i.ClaimedAt, (DateTimeOffset?)null)
            .SetProperty(i => i.ClaimHeartbeatAt, (DateTimeOffset?)null)
            .SetProperty(i => i.ClaimChatter, (string?)null)
            .SetProperty(i => i.ClaimChatterAt, (DateTimeOffset?)null)
            .SetProperty(i => i.ClaimQuietAt, (DateTimeOffset?)null)
            .SetProperty(i => i.ClaimPlaybookId, (int?)null), ct);
}

/// <summary>
/// An issue's seven claim columns, lifted off the row so that everything
/// judging a claim - a controller with an entity in hand, a scan with a
/// projection, a board read that never materialised an entity at all - asks the
/// same question of the same shape.
/// </summary>
public sealed record ClaimSnapshot(
    Guid? Token,
    string? ClaimedBy,
    string? Runner,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? HeartbeatAt,
    string? Chatter,
    DateTimeOffset? ChatterAt,
    DateTimeOffset? QuietAt = null)
{
    public static ClaimSnapshot Of(EfHatchIssue issue) => new(
        issue.ClaimToken,
        issue.ClaimedBy,
        issue.ClaimRunner,
        issue.ClaimedAt,
        issue.ClaimHeartbeatAt,
        issue.ClaimChatter,
        issue.ClaimChatterAt,
        issue.ClaimQuietAt);
}


/// <summary>
/// Who is working what, along each line of the tree, judged against one
/// instant. Answers the one question the dispatcher and the take both ask: does
/// somebody hold a live claim on an ancestor or a descendant of this issue?
/// </summary>
/// <remarks>
/// Ancestor or descendant, and nothing else. Siblings and cousins share no line
/// - a parent's session builds what its children describe, and no other pair
/// can duplicate each other. The one exception is two tasks under a story that
/// shares one branch and one pull request: they are siblings and they push to
/// the same branch, so they are one line for the purpose of who may be working.
/// </remarks>
public sealed class ClaimLineage
{
    /// <summary>A lineage with nobody in it, for a refused scan.</summary>
    public static readonly ClaimLineage Empty = new(null, default, [], [], [], [], []);

    private readonly IssueClaims? _claims;
    private readonly DateTimeOffset _now;
    private readonly Dictionary<long, long?> _parents;
    private readonly Dictionary<long, string> _keys;
    private readonly Dictionary<long, List<long>> _children;
    private readonly Dictionary<long, ClaimSnapshot> _live;
    private readonly Dictionary<long, long> _sharedStory;

    internal ClaimLineage(
        IssueClaims? claims, DateTimeOffset now,
        Dictionary<long, long?> parents, Dictionary<long, string> keys,
        Dictionary<long, List<long>> children, Dictionary<long, ClaimSnapshot> live,
        Dictionary<long, long> sharedStory)
    {
        _sharedStory = sharedStory;
        _claims = claims;
        _now = now;
        _parents = parents;
        _keys = keys;
        _children = children;
        _live = live;
    }

    /// <summary>
    /// The sentence naming who is working a relative of this issue, or null.
    /// </summary>
    /// <param name="heldToken">
    /// The caller's own token: a claim carrying it is the caller's and folds
    /// nothing, up or down - a session holding a story must still be able to
    /// read its own tasks.
    /// </param>
    /// <remarks>
    /// One holder. Up first, nearest ancestor first, with a <c>seen</c> guard
    /// for the same reason <c>DependencyGate.Unmet</c> has one; then down,
    /// breadth-first in board order, so the nearest descendant is the one named.
    /// The issue's own claim is not this method's business.
    /// </remarks>
    public string? Holder(long issueId, Guid? heldToken)
    {
        if (_claims is null || _live.Count == 0) return null;

        var seen = new HashSet<long> { issueId };
        long? at = _parents.GetValueOrDefault(issueId);

        while (at is { } id && seen.Add(id))
        {
            if (Named(id, heldToken, "above this") is { } above) return above;

            at = _parents.GetValueOrDefault(id);
        }

        seen = [issueId];
        var generation = new List<long> { issueId };

        while (generation.Count > 0)
        {
            var next = new List<long>();

            foreach (var id in generation)
            foreach (var child in _children.TryGetValue(id, out var kids) ? kids : [])
            {
                if (!seen.Add(child)) continue;

                if (Named(child, heldToken, "below this") is { } below) return below;

                next.Add(child);
            }

            generation = next;
        }

        // Then sideways, for a task whose story shares one branch: a sibling
        // under the same story that somebody is working would be pushing to the
        // very branch this one would. Claimed ones only, in the order they were filed, so the
        // sentence names the same sibling on every pass.
        if (_sharedStory.TryGetValue(issueId, out var story))
        {
            foreach (var id in _live.Keys.Where(id => id != issueId && _sharedStory.GetValueOrDefault(id, -1) == story)
                         .Order())
            {
                if (Named(id, heldToken, "sharing this story's branch") is { } beside) return beside;
            }
        }

        return null;
    }

    private string? Named(long id, Guid? heldToken, string where)
    {
        if (!_live.TryGetValue(id, out var claim) || claim.Token == heldToken) return null;

        return _claims!.Sentence(claim, _now, $"{_keys.GetValueOrDefault(id, "an issue")}, {where},");
    }
}
