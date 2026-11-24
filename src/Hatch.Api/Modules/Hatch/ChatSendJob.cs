using Hatch.Api.Common;
using Hatch.Api.Jobs;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The numbers the sender runs on. Constants and not settings: nothing here is
/// true of one install and not another.
/// </summary>
public static class ChatDelivery
{
    /// <summary>How often the sender looks for waiting messages.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>The most calls to the chat service in one tick, across every workspace.</summary>
    public const int MaxSendsPerTick = 50;

    /// <summary>The most rooms looked at in one tick.</summary>
    public const int MaxCandidates = 200;

    /// <summary>A message still waiting after this long is dropped; news that old is not news.</summary>
    public static readonly TimeSpan DropAfter = TimeSpan.FromDays(1);

    /// <summary>How long a workspace waits after a throttle that did not say how long.</summary>
    public static readonly TimeSpan DefaultThrottle = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LongestBackoff = TimeSpan.FromMinutes(10);

    /// <summary>The wait after the <paramref name="failures"/>th failure in a row: 10 seconds, doubled each time, to 10 minutes.</summary>
    public static TimeSpan Backoff(int failures)
    {
        var doubled = FirstBackoff * Math.Pow(2, Math.Clamp(failures - 1, 0, 20));
        return doubled > LongestBackoff ? LongestBackoff : doubled;
    }
}

/// <summary>
/// Sends what <see cref="ChatOutbox"/> queued. Each tick takes the lock, has
/// <see cref="ChatProblemWatch"/> queue the goose and the backups if they need
/// saying, drops what has waited a day, and sends at most one message per
/// destination - a room, or a person's direct message conversation: the oldest
/// waiting there, as a post when it has not heard of it and as an edit when it
/// has. A change before the first send is one post
/// with the latest words, and a change after is an edit of the same message.
///
/// <para>A failure delays a message and never loses it. A throttle holds the
/// whole workspace for as long as the service asked. A token the service will
/// not take holds the workspace and backs off. A channel the service refuses
/// backs off that message and lets the other rooms go on. A direct message the
/// service refuses is written on its link, and nowhere else.</para>
///
/// <para>Never throws, because Quartz re-fires a job that does. A job's context
/// has no request, so it reads every project.</para>
/// </summary>
[DisallowConcurrentExecution]
public class ChatSendJob(
    HatchContext db, IChatProvider provider, SecretProtector protector, IChatSendLock gate,
    TimeProvider time, ILogger<ChatSendJob> logger, ChatProblemWatch? problems = null) : IAppJob
{
    public string Name => "ChatSend";

    public string Group => "Hatch.Api";

    public TimeSpan Interval => ChatDelivery.Interval;

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await TickAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // The type and message of an exception from here never carry the
            // token: it is only ever in a header, which no message quotes.
            logger.LogWarning(e, "The chat sender's tick failed - the next one tries again");
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var held = await gate.TryAcquireAsync(db, ct);
        if (held is null)
        {
            logger.LogDebug("Another chat sender is running - skipping this tick");
            return;
        }

        await WatchProblemsAsync(ct);
        await DropStaleAsync(ct);

        var now = time.GetUtcNow();
        var rooms = await CandidatesAsync(now, ct);
        var heldWorkspaces = new HashSet<int>();
        var sends = 0;

        foreach (var candidate in rooms)
        {
            if (sends >= ChatDelivery.MaxSendsPerTick) break;
            ct.ThrowIfCancellationRequested();

            // A workspace that was throttled or refused a moment ago is not asked again.
            if (heldWorkspaces.Contains(candidate.WorkspaceId)) continue;

            var id = await Due(now)
                .Where(m => m.RoomId == candidate.RoomId && m.LinkId == candidate.LinkId && m.PendingSince == candidate.Since)
                .OrderBy(m => m.Id)
                .Select(m => (long?)m.Id)
                .FirstOrDefaultAsync(ct);
            if (id is null) continue;

            if (await DeliverAsync(id.Value, heldWorkspaces, ct)) sends++;
        }
    }

    /// <summary>
    /// Queues what the goose and the backups need said, before the drop and send
    /// steps look at what is waiting. A failure here is the problem watch's alone:
    /// it is logged, and the tracker is cleared so a refused save is not retried by
    /// the steps after it, which would fail the same way and stop the sends.
    /// </summary>
    private async Task WatchProblemsAsync(CancellationToken ct)
    {
        if (problems is null) return;

        try
        {
            await problems.TickAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The chat sender could not check the goose and the backups - sending goes on");
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Drops what has waited a day. Loaded and set, since the in-memory provider refuses a bulk update.</summary>
    private async Task DropStaleAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var cutoff = now - ChatDelivery.DropAfter;

        var stale = await db.ChatMessages
            .Where(m => m.DroppedAt == null && m.SentRevision < m.Revision && m.PendingSince <= cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0) return;

        foreach (var row in stale) row.DroppedAt = now;
        await SaveAsync(ct);

        logger.LogWarning("Dropped {Count} chat messages that waited more than a day", stale.Count);
    }

    /// <summary>The waiting messages that may be tried now: not backing off, in a workspace that is not held.</summary>
    private IQueryable<EfHatchChatMessage> Due(DateTimeOffset now)
    {
        var providerName = provider.Name;

        return db.ChatMessages
            .Where(m => m.DroppedAt == null && m.SentRevision < m.Revision)
            .Where(m => m.NextAttemptAt == null || m.NextAttemptAt <= now)
            .Where(m => db.ChatWorkspaces.Any(w => w.Id == m.WorkspaceId
                && w.Provider == providerName
                && (w.HoldUntil == null || w.HoldUntil <= now)));
    }

    /// <summary>
    /// The destinations with something worth sending, each with the age of its
    /// oldest such message, oldest first. One entry per room or link, so one that
    /// cannot be sent to never crowds out the rest. The message itself is looked up
    /// as the destination's turn comes, since most ticks stop at the send budget.
    /// </summary>
    private async Task<List<Candidate>> CandidatesAsync(DateTimeOffset now, CancellationToken ct)
    {
        // A group's smallest value, rather than its first row: it reads the same on
        // every provider, which a group's first row does not.
        var rooms = await Due(now)
            .GroupBy(m => new { m.RoomId, m.LinkId, m.WorkspaceId })
            .Select(g => new Candidate(g.Key.RoomId, g.Key.LinkId, g.Key.WorkspaceId, g.Min(m => m.PendingSince)))
            .ToListAsync(ct);

        return rooms
            .OrderBy(r => r.Since)
            .ThenBy(r => r.RoomId)
            .ThenBy(r => r.LinkId)
            .Take(ChatDelivery.MaxCandidates)
            .ToList();
    }

    /// <summary>One destination: exactly one of the room and the link is set.</summary>
    private sealed record Candidate(int? RoomId, int? LinkId, int WorkspaceId, DateTimeOffset Since);

    /// <summary>
    /// Tries one message. True when a call was made to the chat service, which
    /// is what the tick's budget counts.
    /// </summary>
    private async Task<bool> DeliverAsync(long id, HashSet<int> heldWorkspaces, CancellationToken ct)
    {
        // Read fresh each time, so a save that has to be abandoned leaves
        // nothing stale behind for the next message.
        var row = await db.ChatMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (row is null || row.DroppedAt is not null || row.SentRevision >= row.Revision) return false;
        if (heldWorkspaces.Contains(row.WorkspaceId)) return false;

        var workspace = await db.ChatWorkspaces.FirstOrDefaultAsync(w => w.Id == row.WorkspaceId, ct);
        var room = row.RoomId is { } roomId ? await db.ChatRooms.FirstOrDefaultAsync(r => r.Id == roomId, ct) : null;
        var link = row.LinkId is { } linkId ? await db.ChatLinks.FirstOrDefaultAsync(l => l.Id == linkId, ct) : null;
        if (workspace is null || (room is null && link is null)) return false;

        // A room's channel, or the conversation with the person's member.
        var destination = room?.ExternalId ?? link!.ConversationId;

        var now = time.GetUtcNow();

        var content = ChatMessageContent.Read(row.Content);
        if (content is null)
        {
            row.DroppedAt = now;
            row.LastRefusal = Short("the message could not be read");
            await SaveAsync(ct);
            logger.LogWarning("Chat message {MessageId} holds content that cannot be read; dropped", row.Id);
            return false;
        }

        // Buttons need the socket an app-level token opens, or a press has nowhere to go. The stored
        // row is left as it is, so a token set later gives the next edit its buttons, and setting one
        // sends nothing again.
        if (string.IsNullOrEmpty(workspace.AppToken)) content = content with { Actions = null };

        // The revision as read: a change that lands while the send is out leaves
        // a revision above this one, and goes next tick.
        var revision = row.Revision;

        ChatSendResult result;
        var called = true;
        var token = protector.Unprotect(workspace.Token, SecretStores.ChatWorkspace(workspace.Provider, workspace.ExternalId));
        if (token is null)
        {
            called = false;
            result = ChatSendResult.Failed(ChatSendOutcome.WorkspaceRefused, ChatController.TokenUnreadable);
        }
        else
        {
            result = row.ExternalId is null
                ? await provider.PostAsync(token.Reveal(), destination, content, ct)
                : await provider.UpdateAsync(token.Reveal(), destination, row.ExternalId, content, ct);
        }

        now = time.GetUtcNow();
        var sentence = Short(result.Sentence ?? "the message was not sent");

        switch (result.Outcome)
        {
            case ChatSendOutcome.Sent:
                if (row.ExternalId is null)
                {
                    var messageId = result.MessageId;
                    row.ExternalId = !string.IsNullOrEmpty(messageId) && messageId.Length <= EfHatchChatMessage.MaxExternalIdLength
                        ? messageId
                        : null;
                    if (row.ExternalId is null)
                        logger.LogWarning("Chat message {MessageId} was sent but the service named no id; an edit will post again", row.Id);
                }

                row.SentRevision = revision;
                row.Attempts = 0;
                row.NextAttemptAt = null;
                row.LastRefusal = null;

                if (room is not null)
                {
                    room.SendFailingSince = null;
                    room.LastRefusal = null;
                }

                if (link is not null)
                {
                    link.LastRefusal = null;
                    link.LastRefusalAt = null;
                }

                workspace.FailStreak = 0;
                workspace.SendFailingSince = null;
                workspace.LastRefusal = null;
                workspace.LastRefusalAt = null;
                break;

            case ChatSendOutcome.Throttled:
                workspace.HoldUntil = now + (result.RetryAfter ?? ChatDelivery.DefaultThrottle);
                heldWorkspaces.Add(workspace.Id);
                break;

            case ChatSendOutcome.Unreachable:
            case ChatSendOutcome.WorkspaceRefused:
                BackOff(row, now, sentence);

                workspace.FailStreak++;
                workspace.SendFailingSince ??= now;
                workspace.HoldUntil = now + ChatDelivery.Backoff(workspace.FailStreak);
                workspace.LastRefusal = sentence;
                workspace.LastRefusalAt = now;
                heldWorkspaces.Add(workspace.Id);
                break;

            default:
                BackOff(row, now, sentence);

                if (link is not null)
                {
                    // A person's own conversation says nothing about the workspace or
                    // a room, and opens no finding: the refusal is the link's.
                    link.LastRefusal = sentence;
                    link.LastRefusalAt = now;
                    break;
                }

                room!.SendFailingSince ??= now;
                room.LastRefusal = sentence;

                workspace.LastRefusal = sentence;
                workspace.LastRefusalAt = now;
                break;
        }

        // False here is a row or workspace removed while the call was out. The
        // message may have been sent; there is nothing left to record it on.
        await SaveAsync(ct);

        return called;
    }

    private static void BackOff(EfHatchChatMessage row, DateTimeOffset now, string sentence)
    {
        row.Attempts++;
        row.NextAttemptAt = now + ChatDelivery.Backoff(row.Attempts);
        row.LastRefusal = sentence;
    }

    /// <summary>
    /// Saves, and says whether it did. A row or workspace removed mid-send (the
    /// project unbound, the workspace deleted) is not an error: the tracked
    /// changes are thrown away and the tick goes on.
    /// </summary>
    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("A chat message or its workspace was removed while it was being sent");
            db.ChangeTracker.Clear();
            return false;
        }
    }

    internal static string Short(string sentence) =>
        sentence.Length <= EfHatchChatWorkspace.MaxRefusalLength ? sentence : sentence[..(EfHatchChatWorkspace.MaxRefusalLength - 1)] + "…";
}

/// <summary>
/// Keeps two senders from running at once, on this instance or another. Two
/// senders would post the same waiting message twice. Quartz's own guard is
/// per scheduler instance, and every instance shares one id, so the lock lives
/// in the database.
/// </summary>
public interface IChatSendLock
{
    /// <summary>
    /// Takes the lock, or returns null when another sender holds it. Disposing
    /// what is returned releases it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct);
}

/// <summary>A Postgres session advisory lock - see <see cref="PostgresGooseLock"/>, which it is a copy of.</summary>
public sealed class PostgresChatSendLock : IChatSendLock
{
    /// <summary>The lock's key, arbitrary but fixed - "chat" in ASCII. Advisory locks are per database.</summary>
    private const long Key = 0x63686174;

    public Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The Hatch context has no connection string to take the chat send lock on");

        return PostgresAdvisoryLock.TryAcquireAsync(connectionString, Key, ct);
    }
}
