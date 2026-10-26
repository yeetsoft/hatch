using System.Net;

namespace Hatch.Cli;

/// <summary>What a pass over the board came to.</summary>
public enum Pick
{
    /// <summary>A ticket is held, and the dispatch is in hand. The caller must let go of it.</summary>
    Claimed,

    /// <summary>Nothing on the board is an agent's to move.</summary>
    Idle,

    /// <summary>The board could not be read - a reason to wait, not a reason to stop.</summary>
    Unreadable,

    /// <summary>
    /// The board answered, and refused - not a reason to wait, a reason to
    /// stop. Today this is a <c>--mine</c> pass whose key belongs to nobody;
    /// nothing about retrying makes it answer differently.
    /// </summary>
    Refused,

    /// <summary>Every clear candidate is somebody else's right now.</summary>
    Busy,

    /// <summary>
    /// An issue was carried across the column it stood in by a hop - see
    /// <see cref="HoppedIssue"/> for which kind and why. No claim was taken
    /// and no session runs, so there is nothing for the caller to let go of.
    /// </summary>
    Hopped,

    /// <summary>Every clear candidate is one this agent cannot take - nothing was claimed.</summary>
    Skipped,
}

/// <summary>
/// What escaped while a specific ticket was being picked, after its claim was
/// already let go - so whoever catches it can still say which ticket failed,
/// without the claim it once held to ask.
/// </summary>
public sealed class PickFailedException(string key, Exception inner) : Exception(inner.Message, inner)
{
    public string Key { get; } = key;
}

/// <summary>
/// One issue a walk carried across itself, with no session - what
/// <see cref="Pick.Hopped"/> carries beside <see cref="Picked.Outcome"/>, the
/// way <see cref="Pick.Claimed"/> carries <see cref="Picked.Work"/>.
/// </summary>
public sealed record HoppedIssue(string Key, string From, string To, string Reason);

/// <param name="Busy">One line per candidate that was taken, naming the key and who has it.</param>
/// <param name="Skipped">One line per clear candidate passed over before any claim, naming the key and why this agent cannot take it.</param>
/// <param name="Chosen">
/// Where the claimed dispatch spawns, and what it resets - set only when
/// <see cref="Outcome"/> is <see cref="Pick.Claimed"/>.
/// </param>
/// <param name="Checkouts">
/// The checkouts this runner serves, possibly grown by a clone made on the way
/// to <see cref="Chosen"/> - set on every return path, so a clone earlier in
/// the walk survives even when a later candidate is the one actually claimed.
/// </param>
/// <param name="CloneFailures">
/// One failed increment per clone this walk could not make, accumulated across
/// every candidate it walked past - the same shape a real failed spawn
/// reports, so the tally's three-in-a-row rule sees it too.
/// </param>
/// <param name="Refusal">The sentence the board refused with - set only when <see cref="Outcome"/> is <see cref="Pick.Refused"/>.</param>
/// <param name="Declined">
/// The sentence an ordered ticket was turned away with - set only when
/// <see cref="Outcome"/> is <see cref="Pick.Idle"/> after <see cref="Picker.NamedAsync"/>,
/// with no claim held.
/// </param>
public sealed record Picked(
    Pick Outcome,
    WorkDto? Work,
    Claim? Claim,
    IReadOnlyList<QueueEntryDto> Queue,
    IReadOnlyList<string> Busy,
    Checkouts.Choice? Chosen = null,
    IReadOnlyList<CheckoutEntry>? Checkouts = null,
    IReadOnlyList<IncrementReport>? CloneFailures = null,
    string? Refusal = null,
    HoppedIssue? Hopped = null,
    IReadOnlyList<string>? Skipped = null,
    string? Declined = null);

/// <summary>
/// Which ticket this runner is going to spend an increment on, and the lease on
/// it - one walk, used wherever a ticket is picked rather than named.
/// </summary>
/// <remarks>
/// <para>Two reads, and which two matters. The <b>queue</b> applies the loop's
/// policy - the ready-date fold, the person-assignee fold - and picks the
/// candidate; the <b>named</b> read is only the payload for the candidate
/// already chosen. It has to be the named one, because <c>work/next</c> would
/// answer with the first clear row <em>as it stands now</em>, which, once
/// another runner's claim has folded the row we were about to take, is a
/// different ticket from the one we hold.</para>
///
/// <para>The claim sits between them, and that is what closes the window the old
/// pair had: a row read as clear and then taken by somebody else in the
/// meantime comes back from the second read carrying their sentence, and this
/// walks on rather than spawning into it.</para>
/// </remarks>
public sealed class Picker(
    Board board, string runner, Terminal say,
    IReadOnlyList<CheckoutEntry> checkouts, string standingRoot, string? standingBaseBranch,
    string? workspace = null, Func<string, string, IClone>? clone = null,
    string agent = "claude", ModelFamilies? families = null, IAgentAdapter? adapter = null)
{
    /// <summary>
    /// How many clear rows to try before calling the board busy. A bounded walk,
    /// because a board where the first five are all being worked is a board
    /// where waiting an interval is the honest thing to do - and an unbounded
    /// one would walk a thousand-card column taking and releasing leases.
    /// </summary>
    public const int Attempts = 5;

    private readonly bool _clones = workspace is not null;
    private readonly ModelFamilies _families = families ?? ModelFamilies.Default;

    /// <summary>The word a hop row prints, naming which of the seven reasons carried it.</summary>
    public static string HopWord(string? hopKind, string? hopUnder) => hopKind switch
    {
        HopKinds.Express => "express",
        HopKinds.Parent => "parent pulled",
        HopKinds.Epic => "epic",
        HopKinds.Under => $"under {hopUnder}",
        HopKinds.SendBack => "sent back",
        HopKinds.Approve => "pre-approved",
        HopKinds.Close => "pre-approved",
        _ => hopKind ?? "express",
    };

    /// <summary>The label a hop row prints - <see cref="HopWord"/>, plus the reason nothing was spawned.</summary>
    public static string HopReason(string? hopKind, string? hopUnder) => $"{HopWord(hopKind, hopUnder)}, no session";

    /// <summary>
    /// Why a row pinned to <paramref name="model"/> is not this runner's, or
    /// null when it is - no model, an alias, and a pinned id of
    /// <paramref name="agent"/>'s own family are all anybody's to take.
    /// </summary>
    public static string? WrongAgent(string? model, string agent, ModelFamilies families) =>
        families.AgentOf(model) is { } owner && owner != agent
            ? $"pinned to {model}, {Article(owner)} {owner} model - this runner's agent is {agent}"
            : null;

    /// <summary>"an" before a vowel-initial word, "a" otherwise.</summary>
    private static string Article(string word) => "aeiou".Contains(word[0]) ? "an" : "a";

    public async Task<Picked> PickAsync(
        string? under, int offsetMinutes, CancellationToken ct, TimeSpan? heartbeat = null, bool mine = false)
    {
        IReadOnlyList<QueueEntryDto> queue;
        try
        {
            queue = await board.QueueAsync(checkouts, under, offsetMinutes, ct, _clones, mine, runner);
        }
        catch (HatchException e) when (e.Status == HttpStatusCode.BadRequest)
        {
            // A 400 is the dispatcher saying "this will never succeed as
            // asked" - today, a --mine pass whose key belongs to nobody. Ends
            // the night rather than the ordinary "the board didn't answer, try
            // again" weather every other refusal here is.
            return new Picked(Pick.Refused, null, null, [], [], Refusal: e.Message);
        }
        catch (HatchException e)
        {
            say.Complain(e.Message);
            return new Picked(Pick.Unreadable, null, null, [], []);
        }

        // The rows nothing folded, in the board's order. The queue has already
        // folded past everything under a live claim held by somebody else, so
        // this is a shortlist and not the whole column.
        var clear = queue.Where(q => q.Blocked is null).ToList();
        if (clear.Count == 0) return new Picked(Pick.Idle, null, null, queue, [], Checkouts: checkouts);

        // A row pinned to another agent's model, whose denied-tools this
        // agent cannot honour, or whose model its server has not pulled, is passed over before it is claimed - a claim
        // taken only to be let go would fold the row for every other runner
        // meanwhile. A hop row spawns nothing, so it stays.
        var skipped = new List<string>();
        var remaining = new List<QueueEntryDto>();
        foreach (var entry in clear)
        {
            if (!entry.Hop && WrongAgent(entry.Model, agent, _families) is { } why)
                skipped.Add($"  {entry.Issue.Key}  {why}");
            else if (!entry.Hop && adapter is not null && !adapter.CanDeny(entry.DeniedTools ?? [], out var denied))
                skipped.Add($"  {entry.Issue.Key}  {denied}");
            else if (!entry.Hop && adapter is not null && !adapter.CanServe(entry.Model, out var unserved))
                skipped.Add($"  {entry.Issue.Key}  {unserved}");
            else
                remaining.Add(entry);
        }

        if (remaining.Count == 0)
            return new Picked(Pick.Skipped, null, null, queue, [], Checkouts: checkouts, Skipped: skipped);

        var busy = new List<string>();
        var cloneFailures = new List<IncrementReport>();

        foreach (var entry in remaining.Take(Attempts))
        {
            var key = entry.Issue.Key;

            // A clear row with Hop is carried across itself - no claim, no
            // session. The server re-judges it as the write is made, so a 409
            // here is the same "the row changed under us" a lost claim race
            // is, and the walk goes on exactly as it does there.
            if (entry.Hop)
            {
                string? walkOn;
                try
                {
                    (_, walkOn) = await board.HopAsync(checkouts, key, ct, _clones);
                }
                catch (HatchException e)
                {
                    say.Complain(e.Message);
                    return new Picked(Pick.Unreadable, null, null, queue, busy, Checkouts: checkouts);
                }

                if (walkOn is not null)
                {
                    busy.Add($"  {key}  {walkOn}");
                    continue;
                }

                return new Picked(Pick.Hopped, null, null, queue, busy, Checkouts: checkouts,
                    Hopped: new HoppedIssue(key, entry.FromStatus.Name, entry.ToStatus?.Name ?? "?", HopReason(entry.HopKind, entry.HopUnder)));
            }

            var (claim, refused) = await Claim.TakeAsync(board.Client, key, runner, ct, heartbeat);

            if (refused is { Held: true })
            {
                // Somebody got there between the scan and the take. Not an
                // error, and not the end of the pass: two loops that started in
                // the same second must not both idle because each saw the other
                // take the row it was about to.
                busy.Add($"  {key}  {refused.Sentence}");
                continue;
            }

            if (refused is not null || claim is null)
            {
                // A refusal that is not "somebody has it" is a board that did
                // not answer, and reporting that as a busy board would be a
                // sentence saying the opposite of what happened.
                say.Complain($"hatch: {key} - {refused?.Sentence ?? "the claim was refused"}");
                return new Picked(Pick.Unreadable, null, null, queue, busy, Checkouts: checkouts);
            }

            // Everything from here to the return is this ticket's claim to give
            // back on any way out - including a way out nobody wrote a catch
            // for. The two expected refusals below still handle themselves,
            // for the sentence each one prints; this is the net under both of
            // them and under whatever neither expected.
            try
            {
                WorkDto? work;
                try
                {
                    work = await board.WorkAsync(checkouts, key, claim.Token, ct, _clones);
                }
                catch (HatchException e)
                {
                    await claim.ReleaseAsync();
                    say.Complain(e.Message);
                    return new Picked(Pick.Unreadable, null, null, queue, busy, Checkouts: checkouts);
                }

                // The ticket changed under us between the two reads - somebody
                // answered a question, something landed, a dependency closed -
                // and the lease goes back and the walk goes on.
                if (work is null || work.Blocked is { Length: > 0 })
                {
                    await claim.ReleaseAsync();
                    busy.Add($"  {key}  {work?.Blocked ?? "it left the dispatcher's path between two reads"}");
                    continue;
                }

                // Best-effort and non-fatal by design: losing this write loses
                // the move-refusal protection for this one dispatch, not the
                // claim itself, so the walk claims the ticket either way
                // rather than treating a failed attach as a reason to release
                // and move on.
                if (work.Playbook is { Id: var playbookId } && !await claim.AttachPlaybookAsync(playbookId, ct))
                    say.Complain($"hatch: {key} - could not attach its playbook to the claim");

                var resolved = Clones.Resolve(work, checkouts, standingRoot, standingBaseBranch, workspace, clone);
                checkouts = resolved.Checkouts;

                if (resolved.Failed is { } failure)
                {
                    await claim.ReleaseAsync();

                    // Best-effort: a board that will not take this comment has
                    // already had its claim let go of, and the walk goes on to
                    // the next candidate either way.
                    try
                    {
                        await board.CommentAsync(
                            key, $"hatch could not clone {failure.Remote} into {failure.Path}:\n\n    {failure.Error}", ct);
                    }
                    catch (Exception e) when (e is HatchException or OperationCanceledException)
                    {
                        say.Complain($"hatch: {key} - could not tell the board that {failure.Remote} could not be cloned - {e.Message}");
                    }

                    cloneFailures.Add(new IncrementReport
                    {
                        Key = key,
                        From = work.FromStatus.Name,
                        To = work.ToStatus?.Name ?? "?",
                        Ended = work.FromStatus.Name,
                        ExitCode = 1,
                        Flag = $"could not clone {failure.Remote}: {failure.Error}",
                    });
                    continue;
                }

                // Its primary repository matched no checkout this runner holds
                // or could clone - the same "changed under us" the queue's own
                // fold would have caught a moment later anyway.
                if (resolved.Chosen is null)
                {
                    await claim.ReleaseAsync();
                    busy.Add($"  {key}  it left the dispatcher's path between two reads");
                    continue;
                }

                return new Picked(Pick.Claimed, work, claim, queue, busy, resolved.Chosen, checkouts,
                    cloneFailures.Count > 0 ? cloneFailures : null, Skipped: skipped.Count > 0 ? skipped : null);
            }
            catch (Exception e)
            {
                await claim.ReleaseAsync();
                throw e is OperationCanceledException ? e : new PickFailedException(key, e);
            }
        }

        return new Picked(Pick.Busy, null, null, queue, busy, Checkouts: checkouts,
            CloneFailures: cloneFailures.Count > 0 ? cloneFailures : null, Skipped: skipped.Count > 0 ? skipped : null);
    }

    /// <summary>
    /// The one ticket a person ordered this runner to work, claimed the way
    /// <see cref="PickAsync"/> claims one it chose - or turned away with a
    /// sentence, which is <see cref="Pick.Idle"/> carrying
    /// <see cref="Picked.Declined"/> and no claim.
    /// </summary>
    /// <remarks>
    /// Neither <c>--under</c> nor <c>--mine</c> is consulted: an order is a
    /// person naming a ticket, which is what lets it step past both. It does
    /// not step past the dispatcher's own folds - another runner's claim, a
    /// person's assignee, a missing tag all come back as the named read's
    /// <c>Blocked</c> sentence, and that sentence is what the row says.
    /// </remarks>
    public async Task<Picked> NamedAsync(string key, CancellationToken ct, TimeSpan? heartbeat = null)
    {
        WorkDto? work;
        try
        {
            work = await board.WorkAsync(checkouts, key, null, ct, _clones);
        }
        catch (HatchException e)
        {
            say.Complain(e.Message);
            return new Picked(Pick.Unreadable, null, null, [], [], Checkouts: checkouts);
        }

        if (work is null) return Declined($"{key} - there is nothing to do here");
        if (work.Blocked is { Length: > 0 } blocked) return Declined($"{key} - {blocked}");

        // A hop spawns nothing, so it takes no claim - and the server
        // re-judges it as the write is made, which is where a 409 comes back
        // as a sentence to walk on with.
        if (work.Hop)
        {
            string? walkOn;
            try
            {
                (_, walkOn) = await board.HopAsync(checkouts, key, ct, _clones);
            }
            catch (HatchException e)
            {
                say.Complain(e.Message);
                return new Picked(Pick.Unreadable, null, null, [], [], Checkouts: checkouts);
            }

            if (walkOn is not null) return Declined($"{key} - {walkOn}");

            return new Picked(Pick.Hopped, null, null, [], [], Checkouts: checkouts,
                Hopped: new HoppedIssue(key, work.FromStatus.Name, work.ToStatus?.Name ?? "?", HopReason(work.HopKind, work.HopUnder)));
        }

        // Before any claim, for the reason the queue's walk does it: a claim
        // taken only to be let go folds the row for every other runner.
        var model = work.Playbook?.Model;
        string? why = WrongAgent(model, agent, _families);
        if (why is null && adapter is not null && !adapter.CanDeny(work.Playbook?.DeniedTools ?? [], out var denied)) why = denied;
        if (why is null && adapter is not null && !adapter.CanServe(model, out var unserved)) why = unserved;
        if (why is not null) return Declined($"{key} - {why}");

        var (claim, refused) = await Claim.TakeAsync(board.Client, key, runner, ct, heartbeat, playbookId: work.Playbook?.Id);
        if (claim is null) return Declined($"{key} - {refused?.Sentence ?? "the claim was refused"}");

        try
        {
            var resolved = Clones.Resolve(work, checkouts, standingRoot, standingBaseBranch, workspace, clone);
            checkouts = resolved.Checkouts;

            if (resolved.Failed is { } failure)
            {
                await claim.ReleaseAsync();

                try
                {
                    await board.CommentAsync(
                        key, $"hatch could not clone {failure.Remote} into {failure.Path}:\n\n    {failure.Error}", ct);
                }
                catch (Exception e) when (e is HatchException or OperationCanceledException)
                {
                    say.Complain($"hatch: {key} - could not tell the board that {failure.Remote} could not be cloned - {e.Message}");
                }

                var report = new IncrementReport
                {
                    Key = key,
                    From = work.FromStatus.Name,
                    To = work.ToStatus?.Name ?? "?",
                    Ended = work.FromStatus.Name,
                    ExitCode = 1,
                    Flag = $"could not clone {failure.Remote}: {failure.Error}",
                };

                return Declined($"{key} - could not clone {failure.Remote}: {failure.Error}") with { CloneFailures = [report] };
            }

            if (resolved.Chosen is null)
            {
                await claim.ReleaseAsync();
                return Declined($"{key} - it left the dispatcher's path between two reads");
            }

            return new Picked(Pick.Claimed, work, claim, [], [], resolved.Chosen, checkouts);
        }
        catch (Exception e)
        {
            await claim.ReleaseAsync();
            throw e is OperationCanceledException ? e : new PickFailedException(key, e);
        }

        Picked Declined(string sentence) =>
            new(Pick.Idle, null, null, [], [], Checkouts: checkouts, Declined: sentence);
    }

    /// <summary>
    /// The busy board, said properly - and said differently from the empty one,
    /// because they are different situations and only one of them means the
    /// night is over.
    /// </summary>
    public static IEnumerable<string> BusyReport(string? under, IReadOnlyList<string> busy)
    {
        yield return under is { Length: > 0 }
            ? $"hatch: every issue under {under} an agent could take is being worked by another runner"
            : "hatch: every issue an agent could take is being worked by another runner";

        foreach (var line in busy) yield return $"hatch: {line}";
    }

    /// <summary>The rows passed over as one this agent cannot take, said properly - for a pass that found nothing else clear.</summary>
    public static IEnumerable<string> SkippedReport(IReadOnlyList<string> skipped)
    {
        yield return "hatch: skipped - one this agent cannot take";

        foreach (var line in skipped) yield return $"hatch: {line}";
    }
}
