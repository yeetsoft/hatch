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
public class AttentionController(HatchContext db, Runners runners, TimeProvider time) : ControllerBase
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

        var exhausted = await ExhaustedRunnersAsync(ct);

        // A board too short to have a review column has nothing in review, and
        // that is an answer rather than an error: the section draws its empty
        // state and the control stays quiet about a half that cannot exist here.
        if (Columns.AwaitingReview(statuses) is not { } review)
            return new AttentionDto([], 0, questions, [], ExhaustedRunners: exhausted);

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
                i.ProjectId,
                ProjectKey = i.Project!.Key,
                i.Number,
                i.Type,
                i.Title,
                i.PullRequestUrl,
            })
            .ToListAsync(ct);

        var reviewIds = inReview.Select(i => i.Id).ToList();
        var projectIds = inReview.Select(i => i.ProjectId).Distinct().ToList();

        // The repositories each issue's project still binds - what ReviewWork's
        // own "counted" rule needs, and what neither list below loaded before
        // this row also had to say whether the branch is current and whether
        // its build passed.
        var bound = (await db.ProjectRepositories.AsNoTracking()
                .Where(r => projectIds.Contains(r.ProjectId))
                .ToListAsync(ct))
            .GroupBy(r => r.ProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchProjectRepository>)g.ToList());

        // Every merge verdict for the column, not only the conflicted ones:
        // a row's build/up-to-date state needs the clean ones too, so this
        // reads once and both Conflicts and Reviews are built from it.
        var allMerges = await db.MergeChecks.AsNoTracking()
            .Where(m => reviewIds.Contains(m.IssueId))
            .OrderBy(m => m.Canonical)
            .ToListAsync(ct);
        var mergesByIssue = allMerges.GroupBy(m => m.IssueId).ToDictionary(g => g.Key, g => g.ToList());

        // An issue conflicts if any repository's verdict says so, and it is
        // listed with only those - a clean repository beside a conflicted one
        // is not part of the sentence. Listed whether or not it carries a pull
        // request: the branch conflicts either way. Deliberately not narrowed
        // to bound repositories, as before this task.
        var conflicted = allMerges
            .Where(m => m.Verdict == MergeVerdicts.Conflicted)
            .GroupBy(m => m.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MergeCheckDto>)g.Select(IssueMergeChecks.Project).ToList());

        var conflicts = inReview
            .Where(i => conflicted.ContainsKey(i.Id))
            .Select(i => new ConflictDto(
                IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.PullRequestUrl, conflicted[i.Id]))
            .ToList();

        // The same for the build: every verdict for the column, so a row's
        // build state can be computed from it below.
        var allBuilds = await db.BuildChecks.AsNoTracking()
            .Where(b => reviewIds.Contains(b.IssueId))
            .OrderBy(b => b.Canonical)
            .ToListAsync(ct);
        var buildsByIssue = allBuilds.GroupBy(b => b.IssueId).ToDictionary(g => g.Key, g => g.ToList());

        // Listed with only its failed verdicts, plus a pending one that already
        // carries a failing check - a check that has failed counts while the
        // rest are still running. Not counted towards the badge, as the
        // conflicts are not, and not narrowed to bound repositories, as before.
        var failed = allBuilds
            .Where(b => b.Verdict == BuildVerdicts.Failed
                || (b.Verdict == BuildVerdicts.Pending && b.Failing != null))
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
            .Select(i =>
            {
                var repos = bound.TryGetValue(i.ProjectId, out var r) ? r : [];
                var merges = mergesByIssue.TryGetValue(i.Id, out var m) ? (IReadOnlyList<EfHatchMergeCheck>)m : [];
                var builds = buildsByIssue.TryGetValue(i.Id, out var b) ? (IReadOnlyList<EfHatchBuildCheck>)b : [];

                // The same rules ReviewWork.Judge dispatches on: only the
                // repositories the project still binds, and only builds about
                // the branch as its own clean merge check reads it.
                var counted = ReviewWork.Counted(repos, merges);
                var clean = counted.Where(v => v.Verdict == MergeVerdicts.Clean).ToList();
                var about = ReviewWork.AboutTheBranch(clean, builds);

                var buildState = clean.Count == 0 ? ReviewBuildStates.Unknown
                    : about.Any(x => x.Verdict == BuildVerdicts.Failed
                        || (x.Verdict == BuildVerdicts.Pending && EfHatchBuildCheck.ReadFailing(x.Failing).Count > 0))
                        ? ReviewBuildStates.Failure
                    : about.Count == clean.Count && about.All(x => x.Verdict == BuildVerdicts.Passed)
                        ? ReviewBuildStates.Success
                    : ReviewBuildStates.Unknown;

                bool? holdsTrunk = clean.Any(m => m.HoldsTrunk == false) ? false
                    : clean.Count > 0 && clean.All(m => m.HoldsTrunk == true) ? true
                    : null;

                return new ReviewDto(
                    IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.PullRequestUrl!,
                    buildState, holdsTrunk, clean.FirstOrDefault()?.Trunk);
            })
            .ToList();

        var withoutPr = inReview.Count(i => string.IsNullOrWhiteSpace(i.PullRequestUrl));

        var reviewsHeldBack = inReview.Count(i => !string.IsNullOrWhiteSpace(i.PullRequestUrl)
            && (conflicted.ContainsKey(i.Id) || failed.ContainsKey(i.Id)));

        return new AttentionDto(reviews, withoutPr, questions, conflicts, failingBuilds, reviewsHeldBack, exhausted);
    }

    /// <summary>
    /// Every runner out of Claude usage that is still being heard from, soonest
    /// reset first. Nothing sweeps this: a reset that has passed, or a runner
    /// that has gone quiet, simply is not in the list the next time somebody
    /// asks - the same lazy expiry the rest of <see cref="Runners"/> uses.
    /// </summary>
    private async Task<IReadOnlyList<ExhaustedRunnerDto>> ExhaustedRunnersAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();

        var candidates = await db.Runners.AsNoTracking()
            .Where(r => r.ExhaustedUntil != null)
            .Select(r => new { r.Name, r.Where, r.LastSeenAt, r.ExhaustedUntil })
            .ToListAsync(ct);

        return candidates
            .Where(r => r.ExhaustedUntil!.Value > now && runners.IsHere(r.LastSeenAt, now))
            .OrderBy(r => r.ExhaustedUntil)
            .Select(r => new ExhaustedRunnerDto(r.Name, r.Where, r.ExhaustedUntil!.Value))
            .ToList();
    }
}
