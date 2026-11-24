using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose's finding for questions a person has left unanswered too long: one
/// <see cref="EfHatchGooseFinding"/>, about the board and no project, while at
/// least one question in <see cref="Questions.Waiting"/> is
/// <see cref="GooseSettings.QuestionBound"/> old or older - HA-2498.
/// </summary>
/// <remarks>
/// It judges by age alone: it suggests no answer and answers nothing. A question
/// on an issue filed from a finding is left out, as every check's is.
///
/// The bound is applied here, by choosing whether there is a candidate, and not
/// by the base class's own wait, so <see cref="ReadAsync"/> returns a zero
/// bound. The candidate's token is constant, so another question crossing the
/// bound refreshes the one row rather than opening a second. The condition
/// begins at the first sweep that sees a question past the bound - at most one
/// sweep interval after it, the same clock <see cref="TrainReadyCheck"/> has.
/// </remarks>
public sealed class QuestionsWaitingCheck(HatchContext db, IOptions<HatchOptions> options, TimeProvider time, FindingMessages? messages = null)
    : FindingCheck(db, time, messages)
{
    private const string BoardSubject = "board";
    private const string PastBoundToken = "past-bound";

    public override string Name => "questions-waiting";

    /// <summary>The questions past the bound, oldest first - read once in <see cref="ReadAsync"/>, reused by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyList<Asked> pastBound = [];

    private sealed record Asked(long IssueId, string Key, string Title, string Author, string Body, DateTimeOffset At);

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var hours = GooseSettings.QuestionBound(options.Value);
        pastBound = [];

        var rows = await Questions.Waiting(Db)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.IssueId,
                ProjectKey = c.Issue!.Project!.Key,
                c.Issue.Number,
                c.Issue.Title,
                c.Author,
                c.Body,
                c.CreatedAt,
            })
            .ToListAsync(ct);

        // Zero turns the check off: no candidate, so the base reconciler closes
        // what the check holds.
        var past = hours == TimeSpan.Zero ? [] : rows.Where(r => now - r.CreatedAt >= hours).ToList();

        var filed = new HashSet<long>();
        foreach (var issueId in past.Select(r => r.IssueId).Distinct())
        {
            if (await GooseFindings.FiledFromAFindingAsync(Db, issueId, ct)) filed.Add(issueId);
        }

        pastBound = past
            .Where(r => !filed.Contains(r.IssueId))
            .Select(r => new Asked(r.IssueId, IssueKey.Format(r.ProjectKey, r.Number), r.Title, r.Author, r.Body, r.CreatedAt))
            .ToList();

        var looked = $"{rows.Count} waiting questions, {pastBound.Count} past {hours.TotalHours:0.##} hours";

        return (
            looked, TimeSpan.Zero,
            pastBound.Count == 0 ? [] : [new FindingCandidate(BoardSubject, PastBoundToken, null)]);
    }

    protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        var hours = GooseSettings.QuestionBound(options.Value).TotalHours;
        var count = pastBound.Count;

        var sentence = count == 1
            ? $"1 question has waited more than {hours:0.##} hours"
            : $"{count} questions have waited more than {hours:0.##} hours";

        IReadOnlyList<GooseFindingEvidence> evidence = pastBound
            .Select(q => new GooseFindingEvidence(
                q.Key, $"{q.Title} - asked by {q.Author} at {q.At:O} - {FirstLine(q.Body)}"))
            .ToList();

        return Task.FromResult((sentence, evidence));
    }

    private static string FirstLine(string body) =>
        body.Split('\n', 2)[0].Trim();
}
