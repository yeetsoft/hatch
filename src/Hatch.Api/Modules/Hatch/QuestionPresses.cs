using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>Which way a press on a question's button went. Only <see cref="Answered"/> writes anything.</summary>
public enum PressCase
{
    /// <summary>Not a button this story draws, or not parseable. A later button is not an error, and gets no reply.</summary>
    Ignored,

    /// <summary>The question was answered, as the linked person.</summary>
    Answered,

    /// <summary>No confirmed link for the member in this workspace, or the link's person is gone.</summary>
    NoLink,

    /// <summary>The person cannot see the question's project.</summary>
    CannotSee,

    /// <summary>The question already has an answer, a person's or a key's.</summary>
    AlreadyAnswered,

    /// <summary>A look back with no answer: the issue has closed and an answer now would be an overturn.</summary>
    WorkWentAhead,

    /// <summary>The message is not one Hatch sent: no queued row for this question matches the room and message.</summary>
    NotOurs,

    /// <summary>The option is not on the question, or accept recommended was pressed on one that recommends nothing.</summary>
    NoSuchOption,
}

/// <summary>
/// What a press came to, with what a reply would need. Never the press's reply address.
/// </summary>
/// <param name="IssueKey">The issue's key, once the question was found and the message was ours.</param>
/// <param name="QuestionId">The question's id, when the action named one.</param>
/// <param name="AnsweredBy">For <see cref="PressCase.AlreadyAnswered"/>: who gave the latest answer.</param>
/// <param name="AnsweredByKind">For <see cref="PressCase.AlreadyAnswered"/>: an <see cref="ActorKind"/>.</param>
/// <param name="AnswerBody">For <see cref="PressCase.AlreadyAnswered"/>: what the latest answer said.</param>
public sealed record PressOutcome(
    PressCase Case,
    string? IssueKey = null,
    long? QuestionId = null,
    string? AnsweredBy = null,
    string? AnsweredByKind = null,
    string? AnswerBody = null);

/// <summary>
/// Answers a question from a press on its button in chat, as the person whose
/// confirmed link the press came from. Every refusal writes nothing and says
/// which case it was; <see cref="QuestionPressHandler"/> hands the outcome on.
/// </summary>
/// <remarks>
/// <para>The checks run in an order that matters. A press on a message Hatch did
/// not send learns nothing about the question, so that comes before anything
/// that says more about it. A person who cannot see the project learns nothing of
/// its answers, so that comes before the answer check.</para>
///
/// <para>The handler has no request behind it, so the context's query filters
/// hide no project. Every read of a question, its issue and its queued rows goes
/// past them on purpose, as <c>ChatOutbox.RoomsAsync</c> does: who may see the
/// project is decided here, by <see cref="PersonScope"/>, for the linked person,
/// and never by whose request the work happens to run in.</para>
///
/// <para>Two presses on one question at once: on Postgres the check and the write
/// run in one transaction behind a lock on the question, so the second waits,
/// reads the first's answer and is <see cref="PressCase.AlreadyAnswered"/>. The
/// same press delivered twice does the same. The in-memory provider has neither.
/// A Ballot answer takes no such lock, so it may still race a press; that is
/// outside this story.</para>
/// </remarks>
public sealed class QuestionPresses(
    HatchContext db, ChatLinkRoster roster, QuestionAnswers answers, IOptions<AuthOptions> auth)
{
    /// <summary>The first half of the advisory lock's key; the second is the question's id.</summary>
    private const int LockClass = 0x707265;

    public async Task<PressOutcome> AnswerAsync(int workspaceId, ChatPress press, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(press);

        if (ChatPressAction.Read(press.ActionId) is not { } target) return new PressOutcome(PressCase.Ignored);
        var questionId = target.QuestionId;

        // The workspace is the listener's, never the press's: a member id means
        // nothing outside the workspace it was read in.
        var link = await db.ChatLinks.AsNoTracking().Confirmed()
            .Where(l => l.WorkspaceId == workspaceId && l.MemberId == press.MemberId)
            .OrderBy(l => l.Id)
            .Select(l => new { l.PersonId })
            .FirstOrDefaultAsync(ct);
        if (link is null) return new PressOutcome(PressCase.NoLink, QuestionId: questionId);

        // Not IActorDirectory.ResolveAsync: with no request it cannot find the local person.
        var names = await roster.NamesAsync([link.PersonId], ct);
        if (!names.TryGetValue(link.PersonId, out var personName))
            return new PressOutcome(PressCase.NoLink, QuestionId: questionId);

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        if (db.Database.IsNpgsql())
        {
            // Released with the transaction, committed or not, so a press that fails leaves nothing held.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({LockClass}, {unchecked((int)questionId)})", ct);
        }

        // Read past the query filters, on purpose: see the remarks.
        var question = await db.Comments.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == questionId && c.Kind == EfHatchComment.Question)
            .Select(c => new
            {
                c.IssueId,
                c.Options,
                c.Level,
                ProjectId = c.Issue!.ProjectId,
                ProjectKey = c.Issue!.Project!.Key,
                c.Issue!.Number,
                IsTerminal = c.Issue!.Status!.IsTerminal,
            })
            .FirstOrDefaultAsync(ct);
        if (question is null) return new PressOutcome(PressCase.NotOurs, QuestionId: questionId);

        // A dropped row still counts: its message exists, and was sent.
        var subject = $"comment:{questionId}";
        var kind = ChatOutbox.KindName(ChatMessageKind.Question);
        var ours = await db.ChatMessages.IgnoreQueryFilters().AsNoTracking().AnyAsync(m =>
            m.Kind == kind && m.Subject == subject && m.WorkspaceId == workspaceId
            && m.RoomId != null && m.ExternalId == press.MessageId && m.Room!.ExternalId == press.ChannelId, ct);
        if (!ours) return new PressOutcome(PressCase.NotOurs, QuestionId: questionId);

        var issueKey = IssueKey.Format(question.ProjectKey, question.Number);
        PressOutcome Outcome(PressCase which) => new(which, issueKey, questionId);

        if (!await PersonScope.SeesAsync(db, auth, link.PersonId, question.ProjectId, ct)) return Outcome(PressCase.CannotSee);

        // Every answer, a key's included: not Questions.StandingAsync, which calls a key-answered question a look back.
        var given = await db.Comments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AnswersId == questionId)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Author, a.AuthorKind, a.Body })
            .ToListAsync(ct);
        if (given.LastOrDefault() is { } latest)
            return new PressOutcome(PressCase.AlreadyAnswered, issueKey, questionId, latest.Author, latest.AuthorKind, latest.Body);

        if (Questions.IsLookBack(question.Level, question.IsTerminal, [])) return Outcome(PressCase.WorkWentAhead);

        // The label comes from the stored list, never from the press.
        var options = Questions.ReadOptions(question.Options);
        string? label = null;
        if (options is not null)
        {
            if (target.Place is { } place)
            {
                if (place < options.Count) label = options[place].Label;
            }
            else if (options.Count(o => o.Recommended) == 1)
            {
                label = options.Single(o => o.Recommended).Label;
            }
        }

        if (label is null) return Outcome(PressCase.NoSuchOption);

        var written = await answers.WriteAsync(
            question.IssueId, questionId, label, new AnsweringActor(personName, ActorKind.Person), AnswerVia.Slack, ct);
        if (written is null) return Outcome(PressCase.NotOurs);

        if (transaction is not null) await transaction.CommitAsync(ct);

        return Outcome(PressCase.Answered);
    }
}
