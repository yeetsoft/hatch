using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a failing trunk says in a chat room: one message when the trunk begins
/// failing, edited while the failure changes, and edited once more to say it
/// passed. A failure after that is a new message. It queues rows through
/// <see cref="ChatOutbox"/> in the verdict's own unit of work and sends nothing;
/// <see cref="ChatSendJob"/> does that.
/// </summary>
/// <remarks>
/// <para>A failure is named by its repository, trunk and the moment it began
/// (<see cref="EfHatchTrunkBuild.FailingSince"/>), not by the row's id, which
/// does not exist before its first save.</para>
///
/// <para>The rooms are those that carry problems for any project binding the
/// repository - the rule <see cref="HatchContext"/> uses to show a trunk row at
/// all, not <see cref="ProjectRepositoryBinding.Winner"/>, which picks one
/// project to file a bug under. A repository no project binds is said nowhere.</para>
///
/// <para>A trunk that was already failing when this shipped has no
/// <c>FailingSince</c>, so its next write announces it as a new failure: the
/// rooms never heard of it.</para>
/// </remarks>
public sealed class TrunkBuildMessages(ChatOutbox outbox, HatchContext db, TimeProvider time)
{
    /// <summary>
    /// Folds one stored verdict into the rooms. Call it after the row is written
    /// and before the save, so the verdict and the messages land together.
    /// </summary>
    public async Task SaidAsync(EfHatchTrunkBuild row, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (TrunkBuildFailing.Is(row.Verdict, row.Failing))
        {
            row.FailingSince ??= now;
            await QueueAsync(row, FailingContent(row, row.FailingSince.Value), row.FailingSince.Value, ct);
            return;
        }

        // Pending with no failed check says nothing: the old failure stands until
        // the trunk passes or fails again, so a re-run does not end it early.
        if (row.Verdict != BuildVerdicts.Passed || row.FailingSince is not { } since) return;

        await QueueAsync(row, PassedContent(row, since, now), since, ct);
        row.FailingSince = null;
    }

    private async Task QueueAsync(EfHatchTrunkBuild row, ChatMessageContent content, DateTimeOffset since, CancellationToken ct)
    {
        var projects = await db.ProjectRepositories.IgnoreQueryFilters()
            .Where(r => r.Canonical == row.Canonical)
            .Select(r => r.ProjectId)
            .Distinct()
            .ToListAsync(ct);
        if (projects.Count == 0) return;

        await outbox.QueueForProjectsAsync(ChatMessageKind.Problem, SubjectOf(row, since), projects, content, ct);
    }

    /// <summary>Milliseconds, because the database keeps microseconds: the name must come out the same before the save and after it.</summary>
    private static string SubjectOf(EfHatchTrunkBuild row, DateTimeOffset since) =>
        ChatSubjectKey.Occurrence("trunk", row.Canonical, row.Trunk, since.ToUnixTimeMilliseconds());

    private static ChatMessageContent FailingContent(EfHatchTrunkBuild row, DateTimeOffset since) => new(
        $"Trunk build failing: {row.Canonical} {row.Trunk}", null,
        [$"Commit {Short(row.Sha)}", $"Since {ChatText.When(since)}"],
        EfHatchBuildCheck.ReadFailing(row.Failing).Select(f => new ChatLink(f.Name, f.Url)).ToList());

    private static ChatMessageContent PassedContent(EfHatchTrunkBuild row, DateTimeOffset since, DateTimeOffset now) => new(
        $"Trunk build passing again: {row.Canonical} {row.Trunk}", null,
        [$"Failing since {ChatText.When(since)}", $"Passed {ChatText.When(now)}"],
        []);

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}
