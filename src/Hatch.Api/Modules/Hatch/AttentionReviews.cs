using System.Text;
using Hatch.Api.Ef;
using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The pull requests in the review column, read the way the attention panel
/// reads them: which ones wait on a person, which are held back by a conflict or
/// a failed build, and the rest of what that one read finds.
///
/// <see cref="AttentionController"/> and <see cref="BoardHealthController"/> both
/// call this, so the panel's count and the board health's last bar cannot
/// disagree about which rows are a person's.
/// </summary>
internal static class AttentionReviews
{
    /// <summary>One issue in the review column, as the read needs it.</summary>
    internal sealed class InReviewRow
    {
        public long Id { get; init; }
        public int ProjectId { get; init; }
        public string ProjectKey { get; init; } = "";
        public int Number { get; init; }
        public string Type { get; init; } = "";
        public string Title { get; init; } = "";
        public string? PullRequestUrl { get; init; }
        public string? NoCode { get; init; }
        public string? WaitingPrTitle { get; init; }
        public DateTimeOffset? PullRequestClosedAt { get; init; }
        public string Landing { get; init; } = "";
        public bool ChecksRequired { get; init; }
        public bool CheckApprovesMerge { get; init; }
        public bool NoCodeCloses { get; init; }
        public bool RunnersWorkContributions { get; init; }
        public string? ContributionPullRequestUrl { get; init; }
        public string? ContributionOriginInstance { get; init; }
        public string? ContributionOriginKey { get; init; }
        public string? PullRequestMergeState { get; init; }
        public string? PullRequestHeadSha { get; init; }
        public DateTimeOffset? PullRequestHeadSince { get; init; }
        public string? RecordedBranch { get; init; }
    }

    /// <summary>Everything one read of the review column finds.</summary>
    internal sealed record Read(
        List<InReviewRow> InReview,
        Dictionary<int, IReadOnlyList<EfHatchProjectRepository>> Bound,
        Dictionary<long, IReadOnlyList<EfHatchLanding>> LandingsByIssue,
        IReadOnlyList<EfHatchTrunkBuild> LandedBuilds,
        Dictionary<long, IReadOnlyList<MergeCheckDto>> Conflicted,
        List<ConflictDto> Conflicts,
        Dictionary<long, IReadOnlyList<BuildCheckDto>> Failed,
        List<FailingBuildDto> FailingBuilds,
        List<ContributionHeldDto> ContributionsHeld,
        HashSet<long> HeldIds,
        List<(long IssueId, string ProjectKey, ReviewDto Dto)> Reviews);

    internal static async Task<Read> ReadAsync(
        HatchContext db, Dispatch dispatch, Runners runners, DateTimeOffset now, PriorityTree tree,
        List<EfHatchStatus> statuses, EfHatchStatus review, CancellationToken ct)
    {
        var inReview = await db.Issues.AsNoTracking()
            .Where(i => i.StatusId == review.Id)
            .OrderBy(i => i.Rank)
            .ThenBy(i => i.Id)
            .Select(i => new InReviewRow
            {
                Id = i.Id,
                ProjectId = i.ProjectId,
                ProjectKey = i.Project!.Key,
                Number = i.Number,
                Type = i.Type,
                Title = i.Title,
                PullRequestUrl = i.PullRequestUrl,
                NoCode = i.NoCode,
                WaitingPrTitle = i.WaitingPrTitle,
                PullRequestClosedAt = i.PullRequestClosedAt,
                Landing = i.Project!.Landing,
                ChecksRequired = i.Project.ChecksRequired,
                CheckApprovesMerge = i.Project.CheckApprovesMerge,
                NoCodeCloses = i.Project.NoCodeCloses,
                RunnersWorkContributions = i.Project.RunnersWorkContributions,
                ContributionPullRequestUrl = i.Contribution != null ? i.Contribution.PullRequestUrl : null,
                ContributionOriginInstance = i.Contribution != null ? i.Contribution.OriginInstance : null,
                ContributionOriginKey = i.Contribution != null ? i.Contribution.OriginKey : null,
                PullRequestMergeState = i.PullRequestMergeState,
                PullRequestHeadSha = i.PullRequestHeadSha,
                PullRequestHeadSince = i.PullRequestHeadSince,
                RecordedBranch = i.Contribution != null && i.Contribution.HeadBranch != null && i.Contribution.HeadBranch != ""
                    ? i.Contribution.HeadBranch
                    : i.PullRequestHeadBranch,
            })
            .ToListAsync(ct);

        // A task waiting on its story's pull request has none of its own by
        // design - the story opens the one - and no branch of its own to have a
        // conflict or a build on, so it is none of this panel's rows or counts.
        // See PriorityTree.WaitingOn.
        inReview = inReview.Where(i => tree.WaitingOn(i.Id, statuses) is null).ToList();

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

        // A trunk-landing issue has no pull request to review: its landing, and
        // the build of the trunk it landed in, are what LandingWork.Judge reads.
        // Only read when one is in the column, as Dispatch.LandedAsync does.
        var landingIds = inReview
            .Where(i => LandingWork.Applies(i.Landing, i.PullRequestUrl))
            .Select(i => i.Id)
            .ToList();

        Dictionary<long, IReadOnlyList<EfHatchLanding>> landingsByIssue = [];
        IReadOnlyList<EfHatchTrunkBuild> landedBuilds = [];
        if (landingIds.Count > 0)
        {
            landingsByIssue = (await db.Landings.AsNoTracking()
                    .Where(l => landingIds.Contains(l.IssueId))
                    .OrderBy(l => l.Canonical)
                    .ToListAsync(ct))
                .GroupBy(l => l.IssueId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchLanding>)g.ToList());

            var wanted = landingsByIssue.Values.SelectMany(l => l).Select(l => l.Canonical).Distinct().ToList();
            landedBuilds = await db.TrunkBuilds.AsNoTracking()
                .Where(t => wanted.Contains(t.Canonical))
                .ToListAsync(ct);
        }

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

        // A contribution's branch the project has not asked a runner to work:
        // the judge would dispatch it as conflict or build work, so the loop
        // leaves it and a person acts. It is listed once, here, and not again
        // under the conflicts or the failing builds or the held-back count.
        var contributionsHeld = new List<ContributionHeldDto>();
        var heldIds = new HashSet<long>();
        foreach (var i in inReview)
        {
            var judged = ReviewWork.Judge(
                i.NoCode,
                bound.TryGetValue(i.ProjectId, out var heldRepos) ? heldRepos.OrderBy(r => r.SortOrder).ToList() : [],
                mergesByIssue.TryGetValue(i.Id, out var heldMerges) ? heldMerges : [],
                buildsByIssue.TryGetValue(i.Id, out var heldBuilds) ? heldBuilds : []);

            if (!ReviewWork.HeldContribution(i.RunnersWorkContributions, i.ContributionPullRequestUrl, judged)) continue;

            heldIds.Add(i.Id);
            contributionsHeld.Add(new ContributionHeldDto(
                IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.ContributionPullRequestUrl!,
                judged.Work ?? ReviewWork.ContributionHeld, i.ContributionOriginInstance, i.ContributionOriginKey));
        }

        conflicts = conflicts.Where(c => !contributionsHeld.Any(h => h.Key == c.Key)).ToList();

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
            .Where(i => failed.ContainsKey(i.Id) && !heldIds.Contains(i.Id))
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
        var reviewCandidates = inReview
            .Where(i => !string.IsNullOrWhiteSpace(i.PullRequestUrl)
                && !conflicted.ContainsKey(i.Id)
                && !failed.ContainsKey(i.Id))
            .ToList();

        // Whether each candidate is a parent - the shape Dispatch.MatchAsync
        // needs for its playbook lookup - scoped to just these rows, never a
        // full FamilyGate.ForAsync: this endpoint already avoids loading more
        // than a rule needs.
        var candidateIds = reviewCandidates.Select(i => i.Id).ToList();
        var hasChildren = candidateIds.Count == 0
            ? new HashSet<long>()
            : (await db.Issues.AsNoTracking()
                .Where(c => c.ParentId != null && candidateIds.Contains(c.ParentId.Value))
                .Select(c => c.ParentId!.Value).Distinct().ToListAsync(ct))
                .ToHashSet();

        var reviews = new List<(long IssueId, string ProjectKey, ReviewDto Dto)>();
        foreach (var i in reviewCandidates)
        {
            // A row a check is going to carry into the merge column under "A
            // passing check approves the merge" is not a person's row here -
            // the move itself speaks for it, the same question Dispatch.ScanAsync
            // and WorkController.RouteAsync ask for the same row.
            if (Columns.Approves(statuses, review, i.CheckApprovesMerge) is { } approved
                && (await dispatch.MatchAsync(review.Id, approved.Id, i.Type, hasChildren.Contains(i.Id), i.ProjectId, ct))?.Playbook is { IsCheck: true })
                continue;

            var repos = bound.TryGetValue(i.ProjectId, out var r) ? r : [];
            var issueMerges = mergesByIssue.TryGetValue(i.Id, out var m) ? (IReadOnlyList<EfHatchMergeCheck>)m : [];
            var builds = buildsByIssue.TryGetValue(i.Id, out var b) ? (IReadOnlyList<EfHatchBuildCheck>)b : [];

            // The same rules ReviewWork.Judge dispatches on: only the
            // repositories the project still binds, and only builds about
            // the branch as its own clean merge check reads it.
            var counted = ReviewWork.Counted(repos, issueMerges);
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
            var playbookFilePath = clean.FirstOrDefault(m => m.PlaybookFilePath is not null)?.PlaybookFilePath;
            var dependencyCheck = clean.FirstOrDefault(m => m.DependencyFilePath is not null);
            var dependencyFilePath = dependencyCheck?.DependencyFilePath;

            // A pre-approved row the judge would carry on its own - merge
            // it, update its branch, or is still waiting on a runner to
            // check it - is not a person's business any more, so it is
            // not a row here at all. One the judge still holds stays, with
            // the same sentence the merge column would show for it.
            string? heldSentence = null;
            if (tree.EffectivePreApproval(i.Id).Level == PreApprovalLevels.PreApproved)
            {
                string? fleetLacks = null;
                if (dependencyCheck is { DependencyFileContent: { } content }
                    && DependencyDeclaration.Parse(Encoding.UTF8.GetBytes(content)) is { Dependencies: { } needs })
                    fleetLacks = await dispatch.FleetLacksAsync(needs, dependencyCheck.Canonical, runners.Cutoff(now), ct);

                var judged = MergeWork.Judge(repos, issueMerges, builds, i.PullRequestClosedAt, i.ChecksRequired, i.PullRequestMergeState, i.PullRequestHeadSha, now, review.Name, pullRequestHeadSince: i.PullRequestHeadSince, recordedBranch: i.RecordedBranch, fleetLacks: fleetLacks);
                if (judged.Kind is WorkKinds.Merge or WorkKinds.Update or WorkKinds.Pending) continue;
                heldSentence = judged.Sentence;
            }

            reviews.Add((i.Id, i.ProjectKey, new ReviewDto(
                IssueKey.Format(i.ProjectKey, i.Number), i.Title, i.Type, i.PullRequestUrl!,
                buildState, holdsTrunk, clean.FirstOrDefault()?.Trunk, heldSentence, playbookFilePath, dependencyFilePath)));
        }

        return new Read(inReview, bound, landingsByIssue, landedBuilds, conflicted, conflicts, failed, failingBuilds, contributionsHeld, heldIds, reviews);
    }
}
