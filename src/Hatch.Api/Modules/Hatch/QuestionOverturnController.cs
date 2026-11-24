using System.Globalization;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A person who disagrees with a decision the loop took without them says so
/// on the look-back question, and the rework is filed for them.
/// </summary>
/// <remarks>
/// <para>A sibling of <see cref="IssueThreadController"/> and key-accepting as
/// it is: whoever may answer a question may overturn it, and the answer is the
/// same comment a press of an option writes. Keeping the recommended option
/// needs nothing here - the page answers with its label through the comments
/// route.</para>
///
/// <para>The answer, its event and the task are one <c>SaveChanges</c>, so a
/// question is never answered against the loop with no rework filed.</para>
/// </remarks>
[ApiController]
[Route("api/issues/{key}")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class QuestionOverturnController(
    HatchContext db, RankService ranks, ICallerIdentity caller, TimeProvider time,
    ChatOutbox? chatOutbox = null, ChatLinks? links = null) : ControllerBase
{
    /// <summary>Same bound as <see cref="IssuesController"/>'s, for the same reason.</summary>
    private const int MintAttempts = 5;

    /// <summary>
    /// Answer a look-back question against the loop and file the task that
    /// brings the work in line - see <see cref="QuestionOverturnRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para>Refused with a sentence: an empty or over-long answer, a comment
    /// that is not a question on the issue, a question a person has answered
    /// (whatever else answered it too), a question a key answered at
    /// <c>hold</c>, a question not asked at <c>merge</c> with no answers at
    /// all, and one whose issue has not reached a terminal column with no
    /// answers at all. A question is a look-back - <see cref="Questions.IsLookBack"/>
    /// - either because every answer it has is a key's, at <c>build</c> or
    /// <c>merge</c>, or because, as before, it was asked at <c>merge</c> and
    /// has none at all once its issue reaches a terminal column. Anything
    /// else can still be answered the ordinary way.</para>
    ///
    /// <para>The task is filed under the issue, or under its nearest ancestor
    /// that can take a task where the issue cannot (a task cannot hang under a
    /// task), and under nothing where none can. It lands where a filing lands
    /// today: the leftmost column for a person, the agents' column for a key.
    /// No WIP gate, as there - nothing chose the column.</para>
    /// </remarks>
    [HttpPost("questions/{id:long}/overturn")]
    public async Task<ActionResult<QuestionOverturnDto>> Overturn(
        string key, long id, QuestionOverturnRequest request, CancellationToken ct)
    {
        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.Include(i => i.Project).Include(i => i.Status).WithKey(projectKey, number)
            .FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var answer = request.Answer?.Trim();
        if (string.IsNullOrEmpty(answer)) return BadRequest("an overturning answer needs something in it");
        if (answer.Length > EfHatchComment.MaxBodyLength)
            return BadRequest($"an answer is at most {EfHatchComment.MaxBodyLength} characters");

        var question = await db.Comments.AsNoTracking().FirstOrDefaultAsync(
            c => c.Id == id && c.IssueId == issue.Id && c.Kind == EfHatchComment.Question, ct);
        if (question is null) return BadRequest($"comment {id} is not a question on {key}");

        var answers = await db.Comments.AsNoTracking()
            .Where(a => a.AnswersId == id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.AuthorKind, a.Body })
            .ToListAsync(ct);

        if (!Questions.IsLookBack(question.Level, issue.Status!.IsTerminal, answers.Select(a => a.AuthorKind).ToList()))
        {
            if (answers.Any(a => a.AuthorKind != ActorKind.Key))
                return BadRequest($"question {id} is already answered, so there is nothing to overturn");

            if (answers.Count > 0)
                return BadRequest($"question {id} was answered at {QuestionLevels.HoldName}, so it was already decided - there is nothing to look back on");

            if (QuestionLevels.Normalise(question.Level) != QuestionLevels.MergeName)
                return BadRequest($"question {id} was not asked at {QuestionLevels.MergeName}, so the work waited for it - answer it, there is nothing to overturn");

            return BadRequest($"{key} has not closed, so question {id} is not yet a look back - answer it the ordinary way");
        }

        var status = await FilingColumnAsync(ct);
        if (status is null) return Conflict("this board has no columns to put an issue in");

        var parentId = await ParentForAsync(issue, ct);
        var actor = await caller.ActorNameAsync(ct);
        var authorKind = await caller.IsProgramAsync(ct) ? ActorKind.Key : ActorKind.Person;
        var now = time.GetUtcNow();

        var recommended = Questions.ReadOptions(question.Options)?.FirstOrDefault(o => o.Recommended)?.Label;
        var programAnswer = answers.Count > 0 ? Questions.FirstLine(answers[0].Body) : null;
        var title = $"Bring {key} in line with the answer";
        var description = Describe(key, id, question.Body, recommended, programAnswer, answer, actor, now);

        if (chatOutbox is not null && links is not null)
        {
            var lookBack = Questions.IsLookBack(
                question.Level, issue.Status!.IsTerminal, [..answers.Select(a => a.AuthorKind), authorKind]);
            var content = QuestionChatContent.Build(
                links, key, issue.Title, question.Body, Questions.ReadOptions(question.Options),
                QuestionLevels.Normalise(question.Level), actor, answer, lookBack);
            await chatOutbox.QueueAsync(ChatMessageKind.Question, $"comment:{id}", issue.ProjectId, content, ct);
        }

        for (var attempt = 1; ; attempt++)
        {
            var project = await db.Projects.FirstAsync(p => p.Id == issue.ProjectId, ct);
            var taskNumber = project.NextIssueNumber;

            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = actor,
                AuthorKind = authorKind,
                Body = answer,
                Kind = EfHatchComment.Answer,
                AnswersId = id,
                CreatedAt = now,
            };
            var answered = new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = actor,
                Kind = EfHatchIssueEvent.Answered,
                Payload = JsonSerializer.Serialize(new { questionId = id }),
                At = now,
            };
            var task = new EfHatchIssue
            {
                ProjectId = project.Id,
                Number = taskNumber,
                Type = "task",
                Title = title,
                Description = description,
                StatusId = status.Id,
                ParentId = parentId,
                FiledFromId = issue.Id,
                Rank = await ranks.BottomAsync(status.Id, ct),
                CreatedBy = actor,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var created = new EfHatchIssueEvent
            {
                Actor = actor,
                Kind = EfHatchIssueEvent.Created,
                Payload = JsonSerializer.Serialize(new { type = task.Type, title }),
                At = now,
            };
            task.Events.Add(created);

            // The mint and the rework in one SaveChanges - NextIssueNumber is a
            // concurrency token, so a second request that read the same number
            // fails here rather than writing a second AER-12.
            project.NextIssueNumber++;
            db.Comments.Add(comment);
            db.IssueEvents.Add(answered);
            db.Issues.Add(task);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < MintAttempts)
            {
                Unstage(project, comment, answered, task, created);
                await db.Entry(project).ReloadAsync(ct);
                continue;
            }
            catch (DbUpdateException)
            {
                Unstage(project, comment, answered, task, created);
                return Conflict("could not mint an issue number - try again");
            }

            var read = (await Questions.ProjectAsync(db, db.Comments.AsNoTracking().Where(c => c.Id == id), ct))[0];
            return Ok(new QuestionOverturnDto(read, IssueKey.Format(project.Key, taskNumber)));
        }
    }

    /// <summary>
    /// The column a filing lands in: the agents' for a program, the leftmost
    /// for a person - <see cref="IssuesController.CreateIssueAsync"/>'s own
    /// choice, so the two cannot come to disagree.
    /// </summary>
    private async Task<EfHatchStatus?> FilingColumnAsync(CancellationToken ct)
    {
        Task<EfHatchStatus?> Leftmost() =>
            db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).FirstOrDefaultAsync(ct);

        return await caller.IsProgramAsync(ct)
            ? await db.Statuses.FirstOrDefaultAsync(s => s.AgentFiles && !s.IsDeferred && !s.IsTerminal, ct)
              ?? await Leftmost()
            : await Leftmost();
    }

    /// <summary>
    /// The issue itself where it can take a task, else the nearest ancestor
    /// that can, else nothing.
    /// </summary>
    private async Task<long?> ParentForAsync(EfHatchIssue issue, CancellationToken ct)
    {
        var legal = EfHatchIssue.LegalParentTypes["task"];
        var seen = new HashSet<long> { issue.Id };
        var at = issue;

        while (!legal.Contains(at.Type))
        {
            if (at.ParentId is not { } parentId || !seen.Add(parentId)) return null;

            var parent = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == parentId, ct);
            if (parent is null) return null;
            at = parent;
        }

        return at.Id;
    }

    private static string Describe(
        string key, long questionId, string body, string? recommended, string? programAnswer, string answer, string actor, DateTimeOffset now)
    {
        var decided = programAnswer is not null
            ? $"The loop decided: {programAnswer}"
            : recommended is null
                ? "No option was recommended, so the work went ahead without a recommendation."
                : $"The loop decided: {recommended}";

        var quoted = string.Join('\n', body.Replace("\r\n", "\n").Split('\n').Select(l => $"> {l}"));

        return
            $"A decision the loop took on {key} without a person has been overturned.\n\n" +
            $"{decided}\n\n" +
            $"{actor} decided instead: {answer}\n\n" +
            $"Overturned by {actor} on {now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.\n\n" +
            "Bring the work in line with the answer.\n\n" +
            $"It was asked on {key} as question {questionId}, so a session can read it there. The question:\n\n" +
            quoted;
    }

    /// <summary>
    /// Only what this attempt staged: the project goes back to what it was read
    /// as by the reload the caller does next, and everything else is dropped.
    /// </summary>
    private void Unstage(EfHatchProject project, params object[] staged)
    {
        foreach (var entity in staged) db.Entry(entity).State = EntityState.Detached;
        db.Entry(project).State = EntityState.Unchanged;
    }
}
