using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Says in chat when the goose has stopped or the backups need a person, and
/// edits the same message to say when it ended. These belong to no project, so
/// they go to the rooms that take problems belonging to none.
/// </summary>
/// <remarks>
/// <para>Nothing sweeps for these - the attention read judges them afresh on
/// each call - so the chat sender calls <see cref="TickAsync"/> once per tick,
/// inside its lock. The judging is the attention read's own: <see cref="GooseWatch.JudgeAsync"/>
/// and <see cref="BackupService.Judge"/>. What this adds is memory, one
/// <see cref="EfHatchChatProblem"/> row per stoppage, so a standing problem is
/// announced once and a tick that sees nothing new queues nothing.</para>
///
/// <para>It adds rows to the context and saves once; the row and its message land
/// together or not at all. A unique index on the open rows keeps two instances
/// from announcing the same stoppage - the loser's save is refused, and it sees
/// the winner's row on the next tick.</para>
///
/// <para>A backup problem closes only when a backup newer than its start exists.
/// The last attempt is kept per process, so one instance can judge the backups
/// healthy while another still judges them failed; without that rule the
/// message would open and close as they took turns. Backups becoming not
/// configured while a problem stands ends the row and edits nothing: there is
/// nothing to say about a backup nobody asks for.</para>
/// </remarks>
public sealed class ChatProblemWatch(
    HatchContext db, ChatOutbox outbox, ChatLinks links, BackupService backup, GooseWatch gooseWatch,
    IOptions<HatchOptions> options, TimeProvider time)
{
    /// <summary>Judges both problems, queues what changed and saves. Never calls the chat service.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();

        var open = await db.ChatProblems.Where(p => p.EndedAt == null).ToListAsync(ct);

        await GooseAsync(open.FirstOrDefault(p => p.Kind == EfHatchChatProblem.GooseKind), now, ct);
        await BackupsAsync(open.FirstOrDefault(p => p.Kind == EfHatchChatProblem.BackupsKind), now, ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task GooseAsync(EfHatchChatProblem? row, DateTimeOffset now, CancellationToken ct)
    {
        var goose = await GooseWatch.JudgeAsync(db, gooseWatch, options.Value, now, ct);

        if (goose.Stopped && row is null)
        {
            // The last sweep is the last moment the goose was known to be working;
            // with none stored, the server has only just been looking.
            var began = goose.LastSweepAt ?? now;
            row = Begin(EfHatchChatProblem.GooseKind, GooseSentence, began);

            await outbox.QueueAsync(ChatMessageKind.Problem, row.Subject, null, Standing(row, []), ct);
        }
        else if (!goose.Stopped && row is not null)
        {
            row.EndedAt = now;
            await outbox.QueueAsync(ChatMessageKind.Problem, row.Subject, null, Ended(row, now), ct);
        }
    }

    private async Task BackupsAsync(EfHatchChatProblem? row, DateTimeOffset now, CancellationToken ct)
    {
        var (attention, newestCopyAt) = backup.Judge();

        if (row is null)
        {
            if (attention is null) return;

            row = Begin(EfHatchChatProblem.BackupsKind, Fit(attention.Sentence), now);
            await outbox.QueueAsync(
                ChatMessageKind.Problem, row.Subject, null, Standing(row, LastGood(attention)), ct);
            return;
        }

        if (attention is not null)
        {
            var sentence = Fit(attention.Sentence);
            if (sentence == row.Sentence) return;

            row.Sentence = sentence;
            await outbox.QueueAsync(
                ChatMessageKind.Problem, row.Subject, null, Standing(row, LastGood(attention)), ct);
            return;
        }

        if (string.IsNullOrEmpty(options.Value.BackupPath))
        {
            row.EndedAt = now;
            return;
        }

        // Healthy to this process, but only a copy made since the problem began
        // proves the problem is over; another instance may still see it failing.
        if (newestCopyAt is { } newest && newest > row.BeganAt)
        {
            row.EndedAt = now;
            await outbox.QueueAsync(ChatMessageKind.Problem, row.Subject, null, Ended(row, now), ct);
        }
    }

    private const string GooseSentence = "The goose has stopped";

    private EfHatchChatProblem Begin(string kind, string sentence, DateTimeOffset began)
    {
        var row = new EfHatchChatProblem
        {
            Kind = kind,
            Subject = ChatSubjectKey.Occurrence(kind, Guid.NewGuid()),
            BeganAt = began,
            Sentence = sentence,
        };

        db.ChatProblems.Add(row);
        return row;
    }

    /// <summary>The message while the problem stands: what is wrong, since when, and where to look.</summary>
    private ChatMessageContent Standing(EfHatchChatProblem row, IReadOnlyList<string> extra)
    {
        List<string> lines = [$"Since {ChatText.When(row.BeganAt)}", .. extra];
        return new ChatMessageContent(Title(row), null, lines, Links(row));
    }

    /// <summary>The same message once it ended, with when it began and when it ended.</summary>
    private ChatMessageContent Ended(EfHatchChatProblem row, DateTimeOffset endedAt) =>
        new($"Ended: {Title(row)}", null,
            [$"Began {ChatText.When(row.BeganAt)}", $"Ended {ChatText.When(endedAt)}"], Links(row));

    private static string Title(EfHatchChatProblem row) =>
        row.Kind == EfHatchChatProblem.GooseKind ? row.Sentence : $"The backups need a person: {row.Sentence}";

    /// <summary>The goose page, for the goose; a link with no address to give is left out.</summary>
    private List<ChatLink> Links(EfHatchChatProblem row) =>
        row.Kind == EfHatchChatProblem.GooseKind && links.Goose() is { } url ? [new ChatLink("Goose", url)] : [];

    private static List<string> LastGood(BackupAttentionDto attention) =>
        attention.LastSuccessfulAt is { } at ? [$"Last good backup {ChatText.When(at)}"] : [];

    /// <summary>One line no longer than the column: an error from a failed dump can run long and over several lines.</summary>
    private static string Fit(string sentence)
    {
        var line = string.Join(' ', sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0) line = "the last backup failed";

        return line.Length <= EfHatchChatProblem.MaxSentenceLength
            ? line
            : line[..(EfHatchChatProblem.MaxSentenceLength - 1)] + "…";
    }
}
