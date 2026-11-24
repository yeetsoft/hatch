using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// How far work may go on an issue before one of its questions is answered -
/// see <see cref="EfHatchComment.Level"/>. Set by a person; a key cannot.
/// </summary>
/// <remarks>
/// <para>Its own controller, cut the way <see cref="IssueLocalModelController"/>
/// is: no class-level attribute, so the write below inherits no key acceptance
/// from <see cref="QuestionsController"/>. A level is what lets work go on
/// unanswered, and an agent that could raise it could let itself past its own
/// question.</para>
///
/// <para>No owner or approver role is needed. The person is whoever may answer
/// the question, and this only loosens what that same person could end by
/// answering.</para>
///
/// <para>Reading is open: every question route carries the level in
/// <see cref="QuestionDto"/>.</para>
/// </remarks>
[ApiController]
[Route("api/issues")]
public class QuestionLevelController(
    HatchContext db, ICallerIdentity caller, TimeProvider time,
    ChatOutbox? chatOutbox = null, ChatLinks? links = null) : ControllerBase
{
    /// <summary>
    /// Set a question's level, by name - see <see cref="QuestionLevels"/> and
    /// <see cref="QuestionLevelRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="NotAPerson"/> first and unconditionally, so a key, or a
    /// keyless runner where the wall is off, is refused before anything is
    /// looked up.</para>
    ///
    /// <para>Refused with a sentence: a name that is not one of the three
    /// levels, a comment that is not a question on the issue, a question
    /// already answered, and a level above <c>hold</c> on a question with no
    /// recommended option - a session would have nothing to build on.</para>
    ///
    /// <para>Setting the level the question already holds writes nothing. A
    /// comment has no <c>UpdatedAt</c> and adding one does not touch the
    /// issue's, so neither does this.</para>
    /// </remarks>
    [HttpPut("{key}/questions/{id:long}/level")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<QuestionDto>> PutQuestionLevel(
        string key, long id, QuestionLevelRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        // Exact names: Normalise would read nonsense as hold.
        var to = QuestionLevels.ByStrictness.FirstOrDefault(l => l == request.Level);
        if (to is null)
            return BadRequest($"\"{request.Level}\" is not a question level - hold, build or merge");

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var row = await db.Comments.FirstOrDefaultAsync(
            c => c.Id == id && c.IssueId == issue.Id && c.Kind == EfHatchComment.Question, ct);
        if (row is null) return BadRequest($"comment {id} is not a question on {key}");

        if (await db.Comments.AnyAsync(a => a.AnswersId == id, ct))
            return BadRequest($"question {id} is already answered, so its level no longer matters");

        if (QuestionLevels.Rank(to) > 0 && !(Questions.ReadOptions(row.Options)?.Any(o => o.Recommended) ?? false))
            return BadRequest(
                $"question {id} has no recommended option, so a session would have nothing to build on - it can only be {QuestionLevels.HoldName}");

        var from = QuestionLevels.Normalise(row.Level);
        if (from != to)
        {
            issue.Events.Add(new EfHatchIssueEvent
            {
                Actor = await caller.ActorNameAsync(ct),
                Kind = EfHatchIssueEvent.QuestionLevelChanged,
                Payload = JsonSerializer.Serialize(new { questionId = id, from, to }),
                At = time.GetUtcNow(),
            });

            row.Level = to;

            if (chatOutbox is not null && links is not null)
            {
                var content = QuestionChatContent.Build(links, id, key, issue.Title, row.Body, Questions.ReadOptions(row.Options), to);
                await chatOutbox.QueueAsync(ChatMessageKind.Question, $"comment:{id}", issue.ProjectId, content, ct);
            }

            await db.SaveChangesAsync(ct);
        }

        return (await Questions.ProjectAsync(db, db.Comments.AsNoTracking().Where(c => c.Id == id), ct))[0];
    }

    /// <summary>
    /// A person, not a key - see <see cref="IssueLocalModelController"/>.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("setting a question's level is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}
