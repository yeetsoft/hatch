using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a runner failure says in a chat room and to the runner's owner: one
/// message when the failure opens, edited to say when it ended. The one place a
/// runner failure queues anything, called by both writers of a row, so every
/// message about a failure agrees on when it began. It adds rows through
/// <see cref="ChatOutbox"/> to the caller's unit of work and saves nothing.
/// </summary>
/// <remarks>
/// <para>A failure belongs to no project, so its room message goes to the rooms
/// that take problems belonging to none, in every workspace.</para>
///
/// <para>A failure is named by its key, checkout, kind and the millisecond it
/// began, not by the row's id, which does not exist before the save that also
/// queues the message. The room's message says only the runner, the kind and when
/// it began, and a beat changes none of them, so a refresh queues nothing there.
/// The checkout and what the tool said are never put in it - a whole channel
/// reads it, and the runner's page shows both to whoever may read that
/// runner.</para>
///
/// <para>A person is also told directly, through a confirmed link with the
/// runner failures switch on: the key's owner while the wall stands, every
/// confirmed link while it is down. That message is the person's alone, so it
/// carries what the room's must not - the checkout, the tool's own words and what
/// to do - and is edited when the tool's words change and when the failure ends.
/// Who hears is chosen once, when the failure opens; the switch is read then and
/// never again, so turning it off later still lets the end be said. A link whose
/// person is gone is removed, with its messages, whenever it is met.</para>
///
/// <para>Nothing here is called when a runner goes quiet, so its messages keep
/// saying the failure stands until the runner's next beat ends it.</para>
/// </remarks>
public sealed class RunnerFailureMessages(
    ChatOutbox outbox, ChatLinks links, HatchContext db, IActorDirectory actors, IOptions<AuthOptions> auth)
{
    /// <summary>
    /// Queues the messages for a row that has just opened. Call it after the row is added and before the save.
    /// </summary>
    /// <param name="ownerPersonId">The person the runner's key belongs to, or null. With the wall up, no owner means no direct message.</param>
    public async Task OpenedAsync(EfHatchRunnerFailure row, string runnerName, Guid? ownerPersonId, CancellationToken ct)
    {
        var subject = SubjectOf(row);
        await outbox.QueueAsync(ChatMessageKind.Problem, subject, null, Standing(row, runnerName), ct);

        // No eligible link, no subject: a link made later is told nothing of a
        // failure that began before it, which an unknown subject already says.
        var linkIds = await EligibleLinksAsync(ownerPersonId, ct);
        if (linkIds.Count > 0)
            await outbox.QueueToLinksAsync(ChatMessageKind.Direct, subject, linkIds, StandingDirect(row, runnerName), ct);
    }

    /// <summary>
    /// Edits the direct messages to carry what the tool says now. Call it only when
    /// the words changed. A failure that opened before direct messages existed was
    /// never told, so nothing is queued for it: an edit would otherwise post as a
    /// first message nobody saw begin.
    /// </summary>
    public async Task RefreshedAsync(EfHatchRunnerFailure row, string runnerName, CancellationToken ct)
    {
        var subject = SubjectOf(row);
        if (!await outbox.KnowsAsync(ChatMessageKind.Direct, subject, ct)) return;

        await RemoveGoneAsync(subject, ct);
        await outbox.QueueToLinksAsync(ChatMessageKind.Direct, subject, [], StandingDirect(row, runnerName), ct);
    }

    /// <summary>
    /// Edits the messages to say the failure ended. A failure that opened before
    /// rooms heard of runner failures was never announced, so nothing is queued
    /// for it: an edit would otherwise post as a first message nobody saw begin.
    /// The same holds, apart, for the direct messages.
    /// </summary>
    public async Task EndedAsync(EfHatchRunnerFailure row, string runnerName, DateTimeOffset endedAt, CancellationToken ct)
    {
        var subject = SubjectOf(row);

        if (await outbox.KnowsAsync(ChatMessageKind.Problem, subject, ct))
            await outbox.QueueAsync(ChatMessageKind.Problem, subject, null, Ended(row, runnerName, endedAt), ct);

        if (await outbox.KnowsAsync(ChatMessageKind.Direct, subject, ct))
        {
            await RemoveGoneAsync(subject, ct);
            await outbox.QueueToLinksAsync(ChatMessageKind.Direct, subject, [], EndedDirect(row, runnerName, endedAt), ct);
        }
    }

    /// <summary>Milliseconds, because the database keeps microseconds: the name must come out the same before the save and after it.</summary>
    private static string SubjectOf(EfHatchRunnerFailure row) =>
        ChatSubjectKey.Occurrence("runner-failure", row.ApiKeyId, row.Where, row.Kind, row.BeganAt.ToUnixTimeMilliseconds());

    /// <summary>
    /// The confirmed links with the switch on that should hear of a failure now
    /// opening: the owner's while the wall stands, everyone's while it is down. A
    /// link whose person is gone is removed here, in the caller's save.
    /// </summary>
    private async Task<List<int>> EligibleLinksAsync(Guid? ownerPersonId, CancellationToken ct)
    {
        var wallUp = auth.Value.Enabled;
        if (wallUp && ownerPersonId is null) return [];

        var query = db.ChatLinks.Confirmed();
        if (wallUp) query = query.Where(l => l.PersonId == ownerPersonId);

        var candidates = await query.OrderBy(l => l.Id).ToListAsync(ct);
        if (candidates.Count == 0) return [];

        var live = await LivePeopleAsync(ct);
        var kept = new List<int>();
        var gone = new List<EfHatchChatLink>();
        foreach (var link in candidates)
        {
            if (!live.Contains(link.PersonId)) gone.Add(link);
            else if (link.RunnerFailures) kept.Add(link.Id);
        }

        await RemoveAsync(gone, ct);
        return kept;
    }

    /// <summary>Removes the links this subject's messages were queued for whose person is gone, with their messages.</summary>
    private async Task RemoveGoneAsync(string subject, CancellationToken ct)
    {
        var direct = nameof(ChatMessageKind.Direct).ToLowerInvariant();
        var linkIds = await db.ChatMessages
            .Where(m => m.Kind == direct && m.Subject == subject && m.LinkId != null)
            .Select(m => m.LinkId!.Value)
            .ToListAsync(ct);
        if (linkIds.Count == 0) return;

        var linked = await db.ChatLinks.Where(l => linkIds.Contains(l.Id)).ToListAsync(ct);
        var live = await LivePeopleAsync(ct);

        await RemoveAsync(linked.Where(l => !live.Contains(l.PersonId)).ToList(), ct);
    }

    private async Task<HashSet<Guid>> LivePeopleAsync(CancellationToken ct) =>
        (await actors.LiveAsync(ct)).Where(a => a.Kind == ActorKind.Person).Select(a => a.Id).ToHashSet();

    /// <summary>
    /// Deletes each link and its messages by name, so none of them is edited on its
    /// way out and no save finds a row the cascade took first.
    /// </summary>
    private async Task RemoveAsync(IReadOnlyList<EfHatchChatLink> gone, CancellationToken ct)
    {
        if (gone.Count == 0) return;

        var ids = gone.Select(l => l.Id).ToArray();
        db.ChatMessages.RemoveRange(await db.ChatMessages
            .Where(m => m.LinkId != null && ids.Contains(m.LinkId.Value))
            .ToListAsync(ct));
        db.ChatLinks.RemoveRange(gone);
    }

    private ChatMessageContent Standing(EfHatchRunnerFailure row, string runnerName) =>
        new(Title(row, runnerName), null, [$"Since {ChatText.When(row.BeganAt)}"], Links(runnerName));

    private ChatMessageContent Ended(EfHatchRunnerFailure row, string runnerName, DateTimeOffset endedAt) =>
        new($"Ended: {Title(row, runnerName)}", null,
            [$"Began {ChatText.When(row.BeganAt)}", $"Ended {ChatText.When(endedAt)}"], Links(runnerName));

    /// <summary>The person's own: it says where, what the tool said and what to do.</summary>
    private ChatMessageContent StandingDirect(EfHatchRunnerFailure row, string runnerName)
    {
        var lines = Detail(row);
        lines.Add($"Since {ChatText.When(row.BeganAt)}");
        return new(Title(row, runnerName), RunnerFailureWords.Advice(row.Kind), lines, Links(runnerName));
    }

    private ChatMessageContent EndedDirect(EfHatchRunnerFailure row, string runnerName, DateTimeOffset endedAt)
    {
        var lines = Detail(row);
        lines.Add($"Began {ChatText.When(row.BeganAt)}");
        lines.Add($"Ended {ChatText.When(endedAt)}");
        return new($"Ended: {Title(row, runnerName)}", null, lines, Links(runnerName));
    }

    /// <summary>The checkout, when there is one, and what the tool said, when it said anything.</summary>
    private static List<string> Detail(EfHatchRunnerFailure row)
    {
        var lines = new List<string>(4);
        if (!string.IsNullOrEmpty(row.Where)) lines.Add($"Checkout: {row.Where}");
        if (!string.IsNullOrWhiteSpace(row.Why)) lines.Add($"The tool said: {row.Why}");
        return lines;
    }

    private static string Title(EfHatchRunnerFailure row, string runnerName) =>
        $"{runnerName}: {RunnerFailureWords.Sentence(row.Kind)}";

    /// <summary>The runner's page; with no address to give, no link.</summary>
    private List<ChatLink> Links(string runnerName) =>
        links.Runner(runnerName) is { } url ? [new ChatLink("Runner", url)] : [];
}
