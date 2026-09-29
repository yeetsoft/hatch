using Hatch.Api.Common;
using Hatch.Api.Ef;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What the loop is waiting on a person for: a pull request nobody has
/// reviewed, and a question nobody has answered - and, listed beside them but
/// not counted, the branches in review that have stopped merging.
///
/// The nav strip draws it on every page, so it has to be one request rather
/// than two. Two polls can be a poll interval apart, and a badge counting one
/// instant beside a panel drawn from another shows up as a lit control whose
/// list is empty - which is the one failure a widget like this cannot recover
/// from, because after it happens twice nobody reads it again.
/// </summary>
/// <remarks>
/// Neither half restates a rule that already lives somewhere. Which column is
/// the review column is <see cref="Columns.AwaitingReview"/>'s answer, measured
/// off the board's shape and not off a name, and what makes a question open is
/// <see cref="Questions.Open"/>'s - the same call the board badges a card with
/// and the dispatcher refuses a ticket on. A browser that derived either for
/// itself would be the same rule written twice, in two languages, and the two
/// would drift.
/// </remarks>
[ApiController]
[Route("api/hatch/attention")]
[RequireRole(PersonRole.User, AcceptScope = ApiKeyScopes.Hatch)]
public class AttentionController(HatchContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AttentionDto>> GetAttention(CancellationToken ct)
    {
        var statuses = await db.Statuses.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        // Every open question in the house, whatever shape the board is - a
        // question is waiting on somebody wherever its issue happens to stand.
        var questions = await Questions.ProjectAsync(db, Questions.Open(db), ct);

        // A board too short to have a review column has nothing in review, and
        // that is an answer rather than an error: the section draws its empty
        // state and the control stays quiet about a half that cannot exist here.
        if (Columns.AwaitingReview(statuses) is not { } review)
            return new AttentionDto([], 0, questions, []);

        // The column's own order, which is the board's: (Rank, Id), the same
        // ordering BoardController slices its columns with, so a row here sits
        // where the eye already found it on the board.
        var inReview = await db.Issues.AsNoTracking()
            .Where(i => i.StatusId == review.Id)
            .OrderBy(i => i.Rank)
            .ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.PullRequestUrl,
            })
            .ToListAsync(ct);

        // The verdicts of the column's issues, one query for all of them. An
        // issue conflicts if any repository's verdict says so, and it is listed
        // with only those - a clean repository beside a conflicted one is not
        // part of the sentence. Listed whether or not it carries a pull
        // request: the branch conflicts either way.
        var reviewIds = inReview.Select(i => i.Id).ToList();
        var conflicted = (await db.MergeChecks.AsNoTracking()
                .Where(m => reviewIds.Contains(m.IssueId) && m.Verdict == MergeVerdicts.Conflicted)
                .OrderBy(m => m.Canonical)
                .ToListAsync(ct))
            .GroupBy(m => m.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MergeCheckDto>)g.Select(IssueMergeChecks.Project).ToList());

        var conflicts = inReview
            .Where(i => conflicted.ContainsKey(i.Id))
            .Select(i => new ConflictDto(
                IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.PullRequestUrl, conflicted[i.Id]))
            .ToList();

        // The same for the build: listed with only its failed verdicts, plus a
        // pending one that already carries a failing check - a check that has
        // failed counts while the rest are still running. Not counted towards
        // the badge, as the conflicts are not.
        var failed = (await db.BuildChecks.AsNoTracking()
                .Where(b => reviewIds.Contains(b.IssueId)
                    && (b.Verdict == BuildVerdicts.Failed || (b.Verdict == BuildVerdicts.Pending && b.Failing != null)))
                .OrderBy(b => b.Canonical)
                .ToListAsync(ct))
            .GroupBy(b => b.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BuildCheckDto>)g.Select(IssueBuildChecks.Project).ToList());

        var failingBuilds = inReview
            .Where(i => failed.ContainsKey(i.Id))
            .Select(i => new FailingBuildDto(
                IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.PullRequestUrl, failed[i.Id]))
            .ToList();

        // Split rather than filtered twice: the ones without a link are not
        // dropped, they are counted, and the empty state says how many. An
        // issue that has sat in review for a month with nowhere to review it is
        // worth saying out loud without being worth lighting the strip up for.
        // One held back by a conflict or a failed build is not a row here
        // either - it is the loop's to clear, not a person's - so it is
        // counted towards ReviewsHeldBack instead, never both.
        var reviews = inReview
            .Where(i => !string.IsNullOrWhiteSpace(i.PullRequestUrl)
                && !conflicted.ContainsKey(i.Id)
                && !failed.ContainsKey(i.Id))
            .Select(i => new ReviewDto(
                IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.PullRequestUrl!))
            .ToList();

        var withoutPr = inReview.Count(i => string.IsNullOrWhiteSpace(i.PullRequestUrl));

        var reviewsHeldBack = inReview.Count(i => !string.IsNullOrWhiteSpace(i.PullRequestUrl)
            && (conflicted.ContainsKey(i.Id) || failed.ContainsKey(i.Id)));

        return new AttentionDto(reviews, withoutPr, questions, conflicts, failingBuilds, reviewsHeldBack);
    }
}
