using System.Text.Json;
using System.Text.RegularExpressions;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Turns an answered question's answer into a bullet under the heading
/// <c>## Principles</c> of the ambition above the issue it was asked on.
/// Set by a person; a key cannot.
/// </summary>
/// <remarks>
/// <para>Its own controller, cut the way <see cref="QuestionLevelController"/>
/// is: no class-level attribute, so the write below inherits no key
/// acceptance from <see cref="QuestionsController"/>. A principle is what a
/// session is held to everywhere beneath an ambition, and an agent that
/// could write one could let itself past the judgment it names.</para>
///
/// <para>Reading is open: every question route carries the ambition above it
/// in <see cref="QuestionDto"/>.</para>
/// </remarks>
[ApiController]
[Route("api/issues")]
public partial class QuestionPromoteController(
    HatchContext db, ICallerIdentity caller, TimeProvider time) : ControllerBase
{
    /// <summary>
    /// Promote a question's answer, or the sentence given instead, into a
    /// principle - see <see cref="QuestionPromoteRequest"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="NotAPerson"/> first and unconditionally, so a key, or
    /// a keyless runner where the wall is off, is refused before anything is
    /// looked up.</para>
    ///
    /// <para>Refused with a sentence: a comment that is not a question on the
    /// issue, a question with no answer, an empty sentence, one over
    /// <see cref="QuestionPromoteRequest.MaxPrincipleLength"/> characters, an
    /// issue with no ambition above it, and a sentence already a bullet under
    /// the heading, compared trimmed and without case.</para>
    ///
    /// <para>The sentence is collapsed to one line - every run of whitespace,
    /// newlines included, to one space - before the length and duplicate
    /// checks, so a multi-line answer becomes one bullet rather than being
    /// truncated at its first line break.</para>
    /// </remarks>
    [HttpPost("{key}/questions/{id:long}/promote")]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<QuestionPromoteDto>> Promote(
        string key, long id, QuestionPromoteRequest request, CancellationToken ct)
    {
        if (await NotAPerson(ct) is { } refusal) return refusal;

        if (!IssueKey.TryParse(key, out var projectKey, out var number)) return NotFound();

        var issue = await db.Issues.WithKey(projectKey, number).FirstOrDefaultAsync(ct);
        if (issue is null) return NotFound();

        var question = await db.Comments.AsNoTracking().FirstOrDefaultAsync(
            c => c.Id == id && c.IssueId == issue.Id && c.Kind == EfHatchComment.Question, ct);
        if (question is null) return BadRequest($"comment {id} is not a question on {key}");

        var answer = await db.Comments.AsNoTracking()
            .Where(a => a.AnswersId == id)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);
        if (answer is null) return BadRequest($"question {id} has no answer to promote");

        var raw = request.Principle is { Length: > 0 } principle ? principle.Trim() : answer.Body.Trim();
        var sentence = Collapse(raw);

        if (sentence.Length == 0) return BadRequest("a principle needs something in it");
        if (sentence.Length > QuestionPromoteRequest.MaxPrincipleLength)
            return BadRequest($"a principle is at most {QuestionPromoteRequest.MaxPrincipleLength} characters");

        var ambitionId = await IssueServesController.AmbitionAboveAsync(db, issue.Id, ct);
        if (ambitionId is null) return BadRequest($"{key} hangs beneath no ambition");

        var ambition = await db.Issues.FirstAsync(i => i.Id == ambitionId.Value, ct);

        if (Principles.Contains(ambition.Description, sentence))
            return BadRequest($"\"{sentence}\" is already a principle of {await IssueProjection.KeyOfAsync(db, ambition.Id, ct)}");

        var from = ambition.Description;
        var to = Principles.Append(from, sentence);
        var actor = await caller.ActorNameAsync(ct);
        var now = time.GetUtcNow();

        ambition.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.Redescribed,
            Payload = JsonSerializer.Serialize(new { from, to }),
            At = now,
        });
        ambition.Events.Add(new EfHatchIssueEvent
        {
            Actor = actor,
            Kind = EfHatchIssueEvent.PrinciplePromoted,
            Payload = JsonSerializer.Serialize(new { questionId = id, issueKey = key, principle = sentence }),
            At = now,
        });

        ambition.Description = to;
        ambition.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var ambitionKey = await IssueProjection.KeyOfAsync(db, ambition.Id, ct);
        return new QuestionPromoteDto(ambitionKey!, sentence);
    }

    /// <summary>Every run of whitespace, newlines included, to one space.</summary>
    private static string Collapse(string sentence) => Whitespace().Replace(sentence, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// A person, not a key - see <see cref="QuestionLevelController"/>.
    /// </summary>
    private async Task<ObjectResult?> NotAPerson(CancellationToken ct) =>
        await caller.IsProgramAsync(ct)
            ? new ObjectResult("promoting an answer into a principle is a person's call, not an agent's")
            {
                StatusCode = StatusCodes.Status403Forbidden,
            }
            : null;
}
