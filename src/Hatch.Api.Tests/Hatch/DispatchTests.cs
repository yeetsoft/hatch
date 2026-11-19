using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="Dispatch"/> itself - the first tests against it directly rather
/// than through <see cref="WorkController"/>, which
/// <see cref="WorkControllerTests"/> already covers byte for byte for a
/// project without the repo-sourced setting on (that suite's own assertions,
/// unchanged, are the evidence for that case; nothing here repeats them).
///
/// These are about the one thing HA-807 adds: a repo-sourced project's
/// candidate list - the resolved <c>.hatch/playbooks.yaml</c> copy's rows laid
/// beside the board-wide table rows - and the five sentences
/// <see cref="Dispatch.Blocked"/> folds every move of such a project with
/// when that resolution is not a clean, matchable copy.
/// </summary>
public class DispatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    // ---- Matching: the file's own layer beside the board-wide one ----

    [Fact]
    public async Task MatchAsync_ARepoSourcedProjectsTransitionTheFileCovers_GetsTheFilesRow()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress, model: "fable")]);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Equal("fable", playbook.Model);
    }

    [Fact]
    public async Task MatchAsync_AFileRowsQuietLimitSeconds_IsOnTheMatchedPlaybook()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress, quietLimitSeconds: 1500)]);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Equal(1500, playbook.QuietLimitSeconds);
    }

    [Fact]
    public async Task MatchAsync_AFileRowWithNoQuietLimitSeconds_LeavesItNullOnTheMatchedPlaybook()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress)]);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Null(playbook.QuietLimitSeconds);
    }

    [Fact]
    public async Task MatchAsync_AFileRowsMaxRunSeconds_IsOnTheMatchedPlaybook()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress, maxRunSeconds: 3600)]);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Equal(3600, playbook.MaxRunSeconds);
    }

    [Fact]
    public async Task MatchAsync_AFileRowWithNoMaxRunSeconds_LeavesItNullOnTheMatchedPlaybook()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress)]);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Null(playbook.MaxRunSeconds);
    }

    [Fact]
    public async Task MatchAsync_AFileRowAndAMoreSpecificBoardWideRow_TheFileRowWinsRegardless()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress, model: "fable")]);
        await h.AddPlaybookAsync(h.Todo, h.InProgress, types: "story", shape: "leaf", model: "sonnet");

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        // The board-wide row names the type and the shape - strictly more
        // specific than the file row, which names neither - and still loses:
        // the file's own layer beats the board-wide one before specificity is
        // ever weighed.
        Assert.NotNull(playbook);
        Assert.Equal("fable", playbook.Model);
    }

    [Fact]
    public async Task MatchAsync_AMoveTheFileHasNoRowFor_FallsThroughToTheBoardWideRow()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress, model: "fable")]);
        await h.AddPlaybookAsync(h.InProgress, h.Review, model: "sonnet");

        var playbook = (await h.Dispatch.MatchAsync(h.InProgress, h.Review, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Equal("sonnet", playbook.Model);
    }

    [Fact]
    public async Task MatchAsync_TwoFileRowsTiedOnSpecificity_TheEarlierPositionWins()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows:
        [
            FileRow(h.Todo, h.InProgress, model: "fable"),
            FileRow(h.Todo, h.InProgress, model: "opus"),
        ]);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.NotNull(playbook);
        Assert.Equal("fable", playbook.Model);
    }

    [Fact]
    public async Task MatchAsync_ATableRowNamingTheRepoSourcedProject_IsNeverLoadedAsACandidate()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        await h.AddRepositoryAsync(baseBranch: "main");
        // No PlaybookFiles row at all - this project's own resolution folds
        // every move, but that is not what this test is about: it is about
        // whether its own table row, bypassing the setting's own refusal to
        // create one, is ever loaded as a candidate for Match at all.
        await h.AddPlaybookAsync(h.Todo, h.InProgress, model: "sonnet", projectId: h.ProjectId);

        var playbook = (await h.Dispatch.MatchAsync(h.Todo, h.InProgress, "story", false, h.ProjectId, default))?.Playbook;

        Assert.Null(playbook);
    }

    // ---- Tiers: a project's level orders the walk (HA-2355) ----

    [Fact]
    public async Task ScanAsync_WalksEmergencyThenAnExpeditedProjectThenTheRestThenWhatIsPaused()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress, model: "sonnet");
        var fast = await h.AddProjectAsync("FST");
        var held = await h.AddProjectAsync("HLD");
        (await h.Db.Projects.SingleAsync(p => p.Id == fast)).Priority = global::Hatch.Contracts.ProjectPriorities.Expedited;
        (await h.Db.Projects.SingleAsync(p => p.Id == held)).Priority = global::Hatch.Contracts.ProjectPriorities.Paused;
        await h.Db.SaveChangesAsync();

        var pausedProjectsRow = await h.FileAsync("story", "in the paused project", h.Todo, projectId: held);
        var normalProjectsExpedited = await h.FileAsync("story", "expedited, normal project", h.Todo);
        var expeditedProjectsNormal = await h.FileAsync("story", "normal, expedited project", h.Todo, projectId: fast);
        var emergency = await h.FileAsync("bug", "emergency, normal project", h.Todo);
        normalProjectsExpedited.Priority = global::Hatch.Contracts.PriorityLevels.Expedited;
        emergency.Priority = global::Hatch.Contracts.PriorityLevels.Emergency;
        await h.Db.SaveChangesAsync();

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Equal(
            new[] { emergency, expeditedProjectsNormal, normalProjectsExpedited, pausedProjectsRow }.Select(i => i.Id),
            scan.Rows.Select(r => r.Issue.Id));
        Assert.Contains("ranks emergency", RowFor(scan, expeditedProjectsNormal).Blocked);
        // The emergency is the first clear row, so it is the one both below it name.
        Assert.Contains("ranks emergency", RowFor(scan, normalProjectsExpedited).Blocked);
        Assert.Equal(global::Hatch.Contracts.ProjectPriorities.Paused, RowFor(scan, pausedProjectsRow).ProjectLevel);
        Assert.StartsWith("project HLD is paused", RowFor(scan, pausedProjectsRow).Blocked);
    }

    // ---- Folding: a bad resolution folds every move ----

    [Fact]
    public async Task ScanAsync_ARepoSourcedProjectBoundToNoRepository_FoldsEveryMoveAndLeavesATableSourcedProjectAlone()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        // No ProjectRepositories row at all - the setting was turned on, and
        // the one repository it was bound to was removed afterward.
        var inTodo = await h.FileAsync("story", "in todo", h.Todo);
        var inProgress = await h.FileAsync("story", "in progress", h.InProgress);

        var otherProjectId = await h.AddProjectAsync("OPS");
        await h.AddPlaybookAsync(h.Todo, h.InProgress, model: "sonnet");
        var otherIssue = await h.FileAsync("story", "a table-sourced project's own issue", h.Todo, projectId: otherProjectId);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var expected = $"{h.ProjectKey} is repo-sourced but is bound to no repository - bind one on the Projects page";
        Assert.Equal(expected, RowFor(scan, inTodo).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(scan, inTodo).BlockedKind);
        Assert.Equal(expected, RowFor(scan, inProgress).Blocked);

        // The other project names no setting at all, and reads its board-wide
        // playbook exactly as it always has.
        Assert.Null(RowFor(scan, otherIssue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ABaseBranchNamingNoRowAndMoreThanOneTrunkOnTheRepository_FoldsEveryMove()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(); // no base branch set
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress)]);
        await h.PlaybookFileAsync(repo, "trunk2", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress)]);

        var inTodo = await h.FileAsync("story", "in todo", h.Todo);
        var inProgress = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var expected = $"{h.ProjectKey} names no base branch and more than one .hatch/playbooks.yaml trunk exists " +
                       $"for {repo.Canonical} - set the base branch on the Projects page";
        Assert.Equal(expected, RowFor(scan, inTodo).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(scan, inTodo).BlockedKind);
        Assert.Equal(expected, RowFor(scan, inProgress).Blocked);
    }

    [Fact]
    public async Task ScanAsync_NoRunnerHasEverReadTheFile_FoldsEveryMove()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        // No PlaybookFiles row at all for this canonical and trunk.

        var inTodo = await h.FileAsync("story", "in todo", h.Todo);
        var inProgress = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var expected = $"no runner has read .hatch/playbooks.yaml on main of {repo.Remote} yet";
        Assert.Equal(expected, RowFor(scan, inTodo).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(scan, inTodo).BlockedKind);
        Assert.Equal(expected, RowFor(scan, inProgress).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnAbsentFile_FoldsEveryMove()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        var file = await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.AbsentState, sha: new string('c', 40));

        var inTodo = await h.FileAsync("story", "in todo", h.Todo);
        var inProgress = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var expected = $".hatch/playbooks.yaml is missing on main at {file.Sha}";
        Assert.Equal(expected, RowFor(scan, inTodo).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(scan, inTodo).BlockedKind);
        Assert.Equal(expected, RowFor(scan, inProgress).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnInvalidFile_FoldsEveryMoveWithItsOwnProblem()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.InvalidState, problem: "entry 1: \"from\" is not a column");

        var inTodo = await h.FileAsync("story", "in todo", h.Todo);
        var inProgress = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var expected = "entry 1: \"from\" is not a column - fix it on the trunk";
        Assert.Equal(expected, RowFor(scan, inTodo).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(scan, inTodo).BlockedKind);
        Assert.Equal(expected, RowFor(scan, inProgress).Blocked);
    }

    // ---- A good copy is not itself a fold ----

    [Fact]
    public async Task ScanAsync_AnEmptyPlaybooksList_FallsThroughToTheBoardWideRowRatherThanFolding()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: []);
        await h.AddPlaybookAsync(h.Todo, h.InProgress, model: "sonnet");

        var issue = await h.FileAsync("story", "in todo", h.Todo);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Null(RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AGoodCopyWithNoRowAndNoBoardWideRowEither_NamesTheFileInTheFallback()
    {
        var h = await NewAsync();
        await h.SetRepoSourcedAsync();
        var repo = await h.AddRepositoryAsync(baseBranch: "main");
        // The file speaks for a different transition than the one the issue
        // needs, and nothing board-wide speaks for this one either.
        await h.PlaybookFileAsync(repo, "main", EfHatchPlaybookFile.ParsedState, rows: [FileRow(h.Todo, h.InProgress)]);

        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Equal(
            "no playbook covers \"in progress\" to \"review\" for a story - add one to .hatch/playbooks.yaml on main, or a board-wide one on the Playbooks page",
            RowFor(scan, issue).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(scan, issue).BlockedKind);
    }

    // ---- Tags: a project's required tags against the runner's declared ones ----

    private const string TagSentence = "AER-1 is worked only by a runner advertising";

    [Theory]
    [InlineData(new string[0], "macos and xcode", "none")]
    [InlineData(new[] { "linux" }, "macos and xcode", "linux")]
    [InlineData(new[] { "macos" }, "macos and xcode", "macos")]
    [InlineData(new[] { "xcode", "linux" }, "macos and xcode", "xcode and linux")]
    public async Task ScanAsync_AnImplementationRowWhoseProjectRequiresTagsTheCallerLacks_FoldsWithTheTagSentence(
        string[] has, string required, string advertised)
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(has));

        var row = RowFor(scan, issue);
        Assert.Equal($"{TagSentence} {required} - this runner advertises {advertised}", row.Blocked);
        Assert.Equal(BlockedKinds.Other, row.BlockedKind);
        Assert.Null(row.WaitsOn);
    }

    [Theory]
    [InlineData("macos,xcode")]
    [InlineData("xcode,macos,linux")]
    public async Task ScanAsync_AnImplementationRowWhoseRequiredTagsTheCallerHas_IsClear(string declared)
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of(declared.Split(',')));

        Assert.Null(RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ThreeRequiredTags_ReadAsACommaListWithAnAnd()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("a", "b", "c");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));

        Assert.Equal($"{TagSentence} a, b and c - this runner advertises none", RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AProjectRequiringNothing_IsClearForADeclarationOfNone()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));

        Assert.Null(RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_TheMoveIntoImplementation_IsTagGated()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["macos", "xcode"]));

        Assert.Equal(
            $"{TagSentence} macos and xcode - this runner advertises none", RowFor(folded, issue).Blocked);
        Assert.Null(RowFor(clear, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_APlaybookRowOutOfTheFirstColumn_IsTagGated()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddPlaybookAsync(h.Inbox, h.Todo);
        var issue = await h.FileAsync("story", "in inbox", h.Inbox);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["macos", "xcode"]));
        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Unjudged);

        Assert.Equal(
            $"{TagSentence} macos and xcode - this runner advertises none", RowFor(folded, issue).Blocked);
        Assert.Null(RowFor(clear, issue).Blocked);
        Assert.Null(RowFor(unjudged, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnExpressHopInATagRequiringProject_IsTagGated()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["macos", "xcode"]));
        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Unjudged);

        Assert.Equal(
            $"{TagSentence} macos and xcode - this runner advertises none", RowFor(folded, issue).Blocked);
        Assert.True(RowFor(clear, issue).Hop);
        Assert.Null(RowFor(clear, issue).Blocked);
        Assert.Null(RowFor(unjudged, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnEpicHopInATagRequiringProject_IsTagGated()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.TickWipAsync(h.InProgress, h.Review);
        var running = await h.FileAsync("epic", "running above it", h.InProgress);
        var epic = await h.FileAsync("epic", "E", h.Todo, parentId: running.Id);
        await h.FileAsync("story", "under E", h.Todo, parentId: epic.Id);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["macos", "xcode"]));
        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Unjudged);

        Assert.Equal(
            $"{IssueKey.Format(h.ProjectKey, epic.Number)} is worked only by a runner advertising macos and xcode - this runner advertises none",
            RowFor(folded, epic).Blocked);
        Assert.True(RowFor(clear, epic).Hop);
        Assert.Equal(HopKinds.Epic, RowFor(clear, epic).HopKind);
        Assert.Null(RowFor(clear, epic).Blocked);
        Assert.Null(RowFor(unjudged, epic).Blocked);
    }

    [Fact]
    public async Task ScanAsync_NoDeclarationOrAnUnjudgedOne_FoldsNothingOnTags()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var none = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);
        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Unjudged);

        Assert.Null(RowFor(none, issue).Blocked);
        Assert.Null(RowFor(unjudged, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AReviewRowWithAConflict_FoldsOnTags()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        var issue = await h.FileAsync("story", "in review", h.Review);
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Conflicted);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));

        var row = RowFor(scan, issue);
        Assert.Equal($"{TagSentence} macos and xcode - this runner advertises none", row.Blocked);
        Assert.Equal(BlockedKinds.Other, row.BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_AReviewRowNobodyHasChecked_IsFoldedByTheReviewJudgeNotTheTags()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        var issue = await h.FileAsync("story", "in review", h.Review);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));

        Assert.Equal("no runner has checked its branch against the trunk yet", RowFor(scan, issue).Blocked);
    }

    // ---- A contribution's branch: held for a person unless the project says runners work it ----

    [Fact]
    public async Task ScanAsync_AConflictedContribution_IsHeldForAPersonWhileTheSettingIsOff()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Review, h.Review);
        var issue = await h.FileAsync("story", "from outside", h.Review);
        await h.AddContributionAsync(issue, "https://example.com/o/r/pull/9");
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Conflicted);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), issue);

        Assert.Equal(ReviewWork.ContributionHeld, row.Blocked);
        Assert.Equal(BlockedKinds.Person, row.BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_AContributionWithAFailedBuild_IsHeldForAPersonWhileTheSettingIsOff()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Review, h.Review);
        var issue = await h.FileAsync("story", "from outside", h.Review);
        await h.AddContributionAsync(issue, "https://example.com/o/r/pull/9");
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);
        await h.AddBuildCheckAsync(issue, BuildVerdicts.Failed);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), issue);

        Assert.Equal(ReviewWork.ContributionHeld, row.Blocked);
        Assert.Equal(BlockedKinds.Person, row.BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_AContributionsConflictAndFailedBuild_AreDispatchedWhileTheSettingIsOn()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Review, h.Review);
        await h.RunnersWorkContributionsAsync();
        var conflicted = await h.FileAsync("story", "conflicts", h.Review);
        await h.AddContributionAsync(conflicted, "https://example.com/o/r/pull/9");
        await h.AddMergeCheckAsync(conflicted, MergeVerdicts.Conflicted);
        var failing = await h.FileAsync("story", "fails", h.Review);
        await h.AddContributionAsync(failing, "https://example.com/o/r/pull/10");
        await h.AddMergeCheckAsync(failing, MergeVerdicts.Clean);
        await h.AddBuildCheckAsync(failing, BuildVerdicts.Failed);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Null(RowFor(scan, conflicted).Blocked);
        Assert.Equal(WorkKinds.Conflicts, RowFor(scan, conflicted).Kind);
        Assert.Null(RowFor(scan, failing).Blocked);
        Assert.Equal(WorkKinds.Build, RowFor(scan, failing).Kind);
    }

    [Fact]
    public async Task ScanAsync_AContributionRecordWithNoPullRequest_IsDispatchedWhateverTheSetting()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Review, h.Review);
        var conflicted = await h.FileAsync("story", "conflicts", h.Review);
        await h.AddContributionAsync(conflicted, null);
        await h.AddMergeCheckAsync(conflicted, MergeVerdicts.Conflicted);

        var off = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);
        await h.RunnersWorkContributionsAsync();
        var on = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Null(RowFor(off, conflicted).Blocked);
        Assert.Null(RowFor(on, conflicted).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ACleanPassingContribution_IsFoldedByTheJudgesOwnSentence()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "from outside", h.Review);
        await h.AddContributionAsync(issue, "https://example.com/o/r/pull/9");
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);
        await h.AddBuildCheckAsync(issue, BuildVerdicts.Passed);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), issue);

        Assert.Equal("its branch merges cleanly with main and its build passes - nothing for an agent to do; it waits for an owner or approver to approve it", row.Blocked);
        Assert.Equal(BlockedKinds.Other, row.BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_ACleanPassingExpressIssue_SaysExpressDoesNotCarryItPastReview()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "express, clean and green", h.Review);
        await h.ExpressAsync(issue);
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);
        await h.AddBuildCheckAsync(issue, BuildVerdicts.Passed);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), issue);

        Assert.Equal(
            "its branch merges cleanly with main and its build passes - nothing for an agent to do; express does not carry it past review, so it waits for an owner or approver to approve it",
            row.Blocked);
    }

    [Fact]
    public async Task ScanAsync_ACleanPassingPreApprovedIssueThatReachesTheFold_KeepsTheBareSentence()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "pre-approved, no pull request", h.Review);
        await h.PreApprovedAsync(issue);
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);
        await h.AddBuildCheckAsync(issue, BuildVerdicts.Passed);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), issue);

        Assert.Equal("its branch merges cleanly with main and its build passes - nothing for an agent to do", row.Blocked);
    }

    [Fact]
    public async Task ScanAsync_ACleanIssueWithNoBuildVerdict_AlsoSaysItWaitsOnAnApprover()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "clean, no build verdict", h.Review);
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), issue);

        Assert.Equal(
            "its branch merges cleanly with main - nothing for an agent to do; it waits for an owner or approver to approve it",
            row.Blocked);
    }

    [Fact]
    public async Task ScanAsync_ARepositoryTheCallerLacksAndTagsItLacks_NamesTheRepositoryFirst()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddRepositoryAsync();
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.From(["https://example.com/other"], false, false), false, default,
            tags: TagDeclaration.Of([]));

        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it", RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ATagFoldedRow_HidesNoDeadlockRing()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("macos", "xcode");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var p = await h.FileAsync("story", "p", h.InProgress);
        var c = await h.FileAsync("task", "c", h.Todo, parentId: p.Id);
        var x = await h.FileAsync("story", "x", h.Todo);
        await h.DependsAsync(c, x);
        await h.DependsAsync(x, p);

        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Unjudged);
        var judged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Of([]));

        foreach (var scan in new[] { unjudged, judged })
            foreach (var issue in new[] { p, c, x })
                Assert.True(RowFor(scan, issue).Deadlocked);

        Assert.StartsWith("deadlocked - ", RowFor(judged, p).Blocked);
        Assert.Contains("is worked only by a runner advertising", RowFor(judged, p).Blocked);
    }

    // ---- Tags: a column's own required tags, beside the project/ancestor/own union ----

    [Fact]
    public async Task ScanAsync_TheMoveOutOfATaggedColumn_IsTagGated()
    {
        var h = await NewAsync();
        await h.TagColumnAsync(h.Todo, "macos", "xcode");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));

        var row = RowFor(scan, issue);
        Assert.Equal(
            $"{key} in todo is worked only by a runner advertising macos and xcode - this runner advertises none",
            row.Blocked);
        Assert.Equal(BlockedKinds.Other, row.BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_AHopOutOfATaggedColumn_IsTagGated()
    {
        var h = await NewAsync();
        await h.TagColumnAsync(h.Todo, "gpu");
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["gpu"]));

        Assert.Equal(
            $"{key} in todo is worked only by a runner advertising gpu - this runner advertises none",
            RowFor(folded, issue).Blocked);
        Assert.True(RowFor(clear, issue).Hop);
        Assert.Null(RowFor(clear, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ATagFoldedRowWithTheTagOnTheColumnItself_HidesNoDeadlockRing()
    {
        var h = await NewAsync();
        await h.TagColumnAsync(h.Todo, "macos", "xcode");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var p = await h.FileAsync("story", "p", h.InProgress);
        var c = await h.FileAsync("task", "c", h.Todo, parentId: p.Id);
        var x = await h.FileAsync("story", "x", h.Todo);
        await h.DependsAsync(c, x);
        await h.DependsAsync(x, p);

        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Unjudged);
        var judged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Of([]));

        foreach (var scan in new[] { unjudged, judged })
            foreach (var issue in new[] { p, c, x })
                Assert.True(RowFor(scan, issue).Deadlocked);

        Assert.StartsWith("deadlocked - ", RowFor(judged, c).Blocked);
        Assert.Contains("in todo is worked only by a runner advertising", RowFor(judged, c).Blocked);
    }

    // ---- Omitted runners: a ticket kept off a runner, its own or an ancestor's say-so ----

    private static async Task OmitAsync(Harness h, EfHatchIssue issue, string runner, string by, string? reason = "no toolchain")
    {
        h.Db.Add(new EfHatchIssueRunnerOmission
        {
            IssueId = issue.Id, RunnerName = runner, By = by, At = Now.AddMinutes(-3), Reason = reason,
        });
        await h.Db.SaveChangesAsync();
    }

    private static RunnerAsking Asking(string name) => new(name, Now.AddMinutes(-5));

    [Fact]
    public async Task ScanAsync_AnIssueWithItsOwnOmission_FoldsForThatRunnerOnlyAndSaysItRecusedItself()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);
        await OmitAsync(h, issue, "gpu-1", by: "gpu-1");

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, asking: Asking("gpu-1"));
        var other = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, asking: Asking("gpu-2"));
        var unnamed = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Equal($"{key} is not worked by gpu-1 - it recused itself 3 minutes ago (no toolchain)", RowFor(folded, issue).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(folded, issue).BlockedKind);
        Assert.Null(RowFor(other, issue).Blocked);
        Assert.Null(RowFor(unnamed, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnIssueUnderAnOmittingGrandparent_FoldsNamingTheGrandparentAndWhoOmittedIt()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var epic = await h.FileAsync("epic", "e", h.Todo);
        var story = await h.FileAsync("story", "s", h.InProgress, parentId: epic.Id);
        var task = await h.FileAsync("task", "t", h.Todo, parentId: story.Id);
        var key = IssueKey.Format(h.ProjectKey, task.Number);
        var epicKey = IssueKey.Format(h.ProjectKey, epic.Number);
        await OmitAsync(h, epic, "gpu-1", by: "operator");

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, asking: Asking("gpu-1"));
        var other = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, asking: Asking("gpu-2"));

        Assert.Equal(
            $"{key} is not worked by gpu-1 - operator omitted it on {epicKey} 3 minutes ago (no toolchain)",
            RowFor(folded, task).Blocked);
        Assert.Equal(BlockedKinds.Other, RowFor(folded, task).BlockedKind);
        Assert.Null(RowFor(other, task).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AHopOmittedForTheRunner_StillFoldsForIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);
        await OmitAsync(h, issue, "gpu-1", by: "operator", reason: null);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, asking: Asking("gpu-1"));
        var other = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, asking: Asking("gpu-2"));

        Assert.Equal($"{key} is not worked by gpu-1 - operator omitted it 3 minutes ago", RowFor(folded, issue).Blocked);
        Assert.False(RowFor(folded, issue).Hop);
        Assert.True(RowFor(other, issue).Hop);
        Assert.Null(RowFor(other, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnIssueTagGatedAndOmittedForTheRunner_ReadsTheTagSentence()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);
        await h.TagAsync(issue, "gpu");
        var key = IssueKey.Format(h.ProjectKey, issue.Number);
        await OmitAsync(h, issue, "gpu-1", by: "operator");

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of([]), asking: Asking("gpu-1"));

        Assert.Equal(
            $"{key} is worked only by a runner advertising gpu - this runner advertises none",
            RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnOmissionFoldedRowOnAChild_HidesNoDeadlockRing()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var p = await h.FileAsync("story", "p", h.InProgress);
        var c = await h.FileAsync("task", "c", h.Todo, parentId: p.Id);
        var x = await h.FileAsync("story", "x", h.Todo);
        await h.DependsAsync(c, x);
        await h.DependsAsync(x, p);
        await OmitAsync(h, c, "gpu-1", by: "operator");

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, asking: Asking("gpu-1"));

        foreach (var issue in new[] { p, c, x })
            Assert.True(RowFor(scan, issue).Deadlocked);

        Assert.StartsWith("deadlocked - ", RowFor(scan, c).Blocked);
        Assert.Contains("is not worked by gpu-1", RowFor(scan, c).Blocked);
    }

    // ---- Dangerous: a runner that refuses it is never handed a flagged issue's code ----

    private static async Task FlagAsync(Harness h, EfHatchIssue issue, string reason = "load-tests the API")
    {
        issue.Dangerous = reason;
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ScanAsync_AFlaggedImplementationRow_FoldsForARunnerThatRefusesDangerousWork()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);
        await FlagAsync(h, issue);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of([], refusesDangerous: true));

        var row = RowFor(scan, issue);
        Assert.Equal($"{key} is flagged dangerous (load-tests the API) - this runner refuses dangerous work", row.Blocked);
        Assert.Equal(BlockedKinds.Other, row.BlockedKind);
        Assert.Null(row.WaitsOn);
    }

    [Fact]
    public async Task ScanAsync_ATaskUnderAFlaggedStory_FoldsOnTheStorysReason()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var story = await h.FileAsync("story", "s", h.Todo);
        await FlagAsync(h, story);
        var task = await h.FileAsync("task", "t", h.InProgress, parentId: story.Id);
        var key = IssueKey.Format(h.ProjectKey, task.Number);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of([], refusesDangerous: true));

        Assert.Equal(
            $"{key} is flagged dangerous (load-tests the API) - this runner refuses dangerous work",
            RowFor(scan, task).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AFlaggedImplementationRow_IsClearForARunnerThatDoesNotRefuseItAndForAnUnjudgedScan()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress);
        await FlagAsync(h, issue);

        foreach (var tags in new[] { TagDeclaration.Of([]), TagDeclaration.Unjudged })
        {
            var scan = await h.Dispatch.ScanAsync(
                0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: tags);

            Assert.Null(RowFor(scan, issue).Blocked);
        }
    }

    [Fact]
    public async Task ScanAsync_AFlaggedStoryInTodo_TheMoveIntoImplementation_IsNotDangerFolded()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);
        await FlagAsync(h, issue);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of([], refusesDangerous: true));

        Assert.Null(RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AFlaggedHop_IsNotDangerFolded()
    {
        var h = await NewAsync();
        await h.TagColumnAsync(h.Todo, "gpu");
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await FlagAsync(h, issue);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of(["gpu"], refusesDangerous: true));

        var row = RowFor(scan, issue);
        Assert.True(row.Hop);
        Assert.Null(row.Blocked);
    }

    [Fact]
    public async Task ScanAsync_ADangerFoldedRow_HidesNoDeadlockRing()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var p = await h.FileAsync("story", "p", h.InProgress);
        await FlagAsync(h, p);
        var c = await h.FileAsync("task", "c", h.Todo, parentId: p.Id);
        var x = await h.FileAsync("story", "x", h.Todo);
        await h.DependsAsync(c, x);
        await h.DependsAsync(x, p);

        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Unjudged);
        var judged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Of([], refusesDangerous: true));

        foreach (var scan in new[] { unjudged, judged })
            foreach (var issue in new[] { p, c, x })
                Assert.True(RowFor(scan, issue).Deadlocked);

        Assert.StartsWith("deadlocked - ", RowFor(judged, p).Blocked);
        Assert.Contains("is flagged dangerous", RowFor(judged, p).Blocked);
    }

    // ---- Dangerous: the reason an issue carries, or the nearest ancestor's ----

    [Fact]
    public async Task EffectiveDangerous_ATaskUnderAFlaggedStory_ReadsTheStorysReasonAndKey_AndTheStoryItsOwnWithNoKey()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "s", h.Todo);
        story.Dangerous = "load-tests the API";
        await h.Db.SaveChangesAsync();
        var task = await h.FileAsync("task", "t", h.Todo, parentId: story.Id);

        var tree = await PriorityTree.ForAsync(h.Db, new StubActorDirectory(), default);

        Assert.Equal(("load-tests the API", IssueKey.Format(h.ProjectKey, story.Number)), tree.EffectiveDangerous(task.Id));
        Assert.Equal(("load-tests the API", (string?)null), tree.EffectiveDangerous(story.Id));
    }

    // ---- Omitted runners: the union of the ancestors' rows, the root-most winning ----

    [Fact]
    public async Task EffectiveOmittedRunners_ATwoLevelsDownTask_ReadsTheEpicsOmission_AndTheRootMostWinsOverItsOwn()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e", h.Todo);
        var story = await h.FileAsync("story", "s", h.Todo, parentId: epic.Id);
        var task = await h.FileAsync("task", "t", h.Todo, parentId: story.Id);
        var epicKey = IssueKey.Format(h.ProjectKey, epic.Number);
        h.Db.Add(new EfHatchIssueRunnerOmission { IssueId = epic.Id, RunnerName = "gpu-1", By = "operator", At = Now });
        await h.Db.SaveChangesAsync();

        var tree = await PriorityTree.ForAsync(h.Db, new StubActorDirectory(), default);

        var effective = Assert.Single(tree.EffectiveOmittedRunners(task.Id));
        Assert.Equal("gpu-1", effective.Runner);
        Assert.Equal(epicKey, effective.FromKey);
        Assert.Empty(tree.OwnOmittedRunners(task.Id));

        h.Db.Add(new EfHatchIssueRunnerOmission { IssueId = task.Id, RunnerName = "gpu-1", By = "operator", At = Now });
        await h.Db.SaveChangesAsync();

        tree = await PriorityTree.ForAsync(h.Db, new StubActorDirectory(), default);

        var still = Assert.Single(tree.EffectiveOmittedRunners(task.Id));
        Assert.Equal("gpu-1", still.Runner);
        Assert.Equal(epicKey, still.FromKey);
        var own = Assert.Single(tree.OwnOmittedRunners(task.Id));
        Assert.Null(own.FromKey);
    }

    // ---- Tags: the union an ancestor and the issue's own add to the project's ----

    [Fact]
    public async Task ScanAsync_AStoryUnderATaggedEpicInAProjectRequiringNothing_FoldsOnTheAncestorsTagAndIsClearWhenDeclared()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e", h.Todo);
        await h.TagAsync(epic, "gpu");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress, parentId: epic.Id);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        Assert.Equal(
            $"{key} is worked only by a runner advertising gpu - this runner advertises none",
            RowFor(folded, issue).Blocked);

        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["gpu"]));
        Assert.Null(RowFor(clear, issue).Blocked);
    }

    [Theory]
    [InlineData(new object[] { new[] { "gpu", "macos" } })]
    [InlineData(new object[] { new[] { "linux", "macos" } })]
    [InlineData(new object[] { new[] { "linux", "gpu" } })]
    public async Task ScanAsync_ATaskUnderATaggedEpicInAProjectRequiringATag_FoldsListingProjectThenAncestorThenOwn(
        string[] has)
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("linux");
        var epic = await h.FileAsync("epic", "e", h.Todo);
        await h.TagAsync(epic, "gpu");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("task", "in progress", h.InProgress, parentId: epic.Id);
        await h.TagAsync(issue, "macos");
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(has));

        Assert.StartsWith($"{key} is worked only by a runner advertising linux, gpu and macos", RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ATaskUnderATaggedEpicInAProjectRequiringATag_IsClearWithAllThreeDeclared()
    {
        var h = await NewAsync();
        await h.RequireTagsAsync("linux");
        var epic = await h.FileAsync("epic", "e", h.Todo);
        await h.TagAsync(epic, "gpu");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("task", "in progress", h.InProgress, parentId: epic.Id);
        await h.TagAsync(issue, "macos");

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            tags: TagDeclaration.Of(["linux", "gpu", "macos", "extra"]));

        Assert.Null(RowFor(scan, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_TheMoveIntoImplementation_IsTagGatedByAnAncestorsTagEither()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e", h.InProgress);
        await h.TagAsync(epic, "gpu");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo, parentId: epic.Id);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["gpu"]));

        Assert.Equal(
            $"{key} is worked only by a runner advertising gpu - this runner advertises none",
            RowFor(folded, issue).Blocked);
        Assert.Null(RowFor(clear, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AHopInATagRequiringAncestor_IsTagGatedForATaglessCaller()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e", h.InProgress);
        await h.TagAsync(epic, "gpu");
        var issue = await h.FileAsync("story", "carried across", h.Todo, parentId: epic.Id);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var folded = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of([]));
        var clear = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Of(["gpu"]));

        Assert.Equal(
            $"{key} is worked only by a runner advertising gpu - this runner advertises none",
            RowFor(folded, issue).Blocked);
        Assert.True(RowFor(clear, issue).Hop);
        Assert.Null(RowFor(clear, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_ATagFoldedRowWithTheTagOnAnAncestor_HidesNoDeadlockRing()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e", h.Todo);
        await h.TagAsync(epic, "gpu");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var p = await h.FileAsync("story", "p", h.InProgress, parentId: epic.Id);
        var c = await h.FileAsync("task", "c", h.Todo, parentId: p.Id);
        var x = await h.FileAsync("story", "x", h.Todo);
        await h.DependsAsync(c, x);
        await h.DependsAsync(x, p);

        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Unjudged);
        var judged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, tags: TagDeclaration.Of([]));

        foreach (var scan in new[] { unjudged, judged })
            foreach (var issue in new[] { p, c, x })
                Assert.True(RowFor(scan, issue).Deadlocked);

        Assert.StartsWith("deadlocked - ", RowFor(judged, p).Blocked);
        Assert.Contains("is worked only by a runner advertising", RowFor(judged, p).Blocked);
    }

    [Fact]
    public async Task ScanAsync_NoDeclarationOrAnUnjudgedOne_FoldsNothingOnAnAncestorsTagEither()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "e", h.Todo);
        await h.TagAsync(epic, "gpu");
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var issue = await h.FileAsync("story", "in progress", h.InProgress, parentId: epic.Id);

        var none = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);
        var unjudged = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, tags: TagDeclaration.Unjudged);

        Assert.Null(RowFor(none, issue).Blocked);
        Assert.Null(RowFor(unjudged, issue).Blocked);
    }

    // ---- Projects: a runner's own project binding ----

    [Fact]
    public async Task ScanAsync_AnIssueInTheBoundProject_IsClear()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, projects: [h.ProjectKey]);

        Assert.Null(RowFor(scan, issue).Blocked);
    }

    [Theory]
    [InlineData(new[] { "FIL" }, "FIL")]
    [InlineData(new[] { "FIL", "OPS" }, "FIL and OPS")]
    public async Task ScanAsync_AnIssueOutsideTheBoundProjects_FoldsWithTheProjectSentence(
        string[] bound, string list)
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        foreach (var project in bound)
            if (project != h.ProjectKey) await h.AddProjectAsync(project);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, projects: bound);

        var row = RowFor(scan, issue);
        Assert.Equal($"{key} is in project {h.ProjectKey} - this runner is bound to {list}", row.Blocked);
        Assert.Equal(BlockedKinds.Other, row.BlockedKind);
        Assert.Null(row.WaitsOn);
    }

    [Fact]
    public async Task ScanAsync_NoProjectsOrAnEmptyList_FoldsNothingOnProjects()
    {
        var h = await NewAsync();
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var issue = await h.FileAsync("story", "in todo", h.Todo);

        var none = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);
        var empty = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, projects: []);

        Assert.Null(RowFor(none, issue).Blocked);
        Assert.Null(RowFor(empty, issue).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnUnknownProjectKey_RefusesTheScan()
    {
        var h = await NewAsync();

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, projects: ["NOPE"]);

        Assert.Contains("NOPE", scan.Failure);
    }

    [Fact]
    public async Task ScanAsync_AProjectFoldedRow_HidesNoDeadlockRing()
    {
        var h = await NewAsync();
        await h.AddProjectAsync("FIL");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        await h.AddPlaybookAsync(h.InProgress, h.Review);
        var p = await h.FileAsync("story", "p", h.InProgress);
        var c = await h.FileAsync("task", "c", h.Todo, parentId: p.Id);
        var x = await h.FileAsync("story", "x", h.Todo);
        await h.DependsAsync(c, x);
        await h.DependsAsync(x, p);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default,
            markDeadlocks: true, projects: ["FIL"]);

        foreach (var issue in new[] { p, c, x })
            Assert.True(RowFor(scan, issue).Deadlocked);

        Assert.StartsWith("deadlocked - ", RowFor(scan, p).Blocked);
        Assert.Contains("is in project", RowFor(scan, p).Blocked);
    }

    [Fact]
    public async Task ScanAsync_AnAncestorOutsideTheBoundProjects_FoldsEveryRowAndClearsNone()
    {
        var h = await NewAsync();
        var filId = await h.AddProjectAsync("FIL");
        await h.AddPlaybookAsync(h.Todo, h.InProgress);
        var epic = await h.FileAsync("epic", "e", h.Todo, projectId: filId);
        var story = await h.FileAsync("story", "s", h.Todo, projectId: filId, parentId: epic.Id);
        var task = await h.FileAsync("task", "t", h.Todo, projectId: filId, parentId: story.Id);

        var scan = await h.Dispatch.ScanAsync(
            0, IssueKey.Format("FIL", epic.Number), null, RepositoryDeclaration.Undeclared, false, default,
            projects: [h.ProjectKey]);

        Assert.Null(scan.Failure);
        Assert.NotEmpty(scan.Rows);
        Assert.All(scan.Rows, row => Assert.NotNull(row.Blocked));
    }

    [Fact]
    public async Task ScanAsync_AMergeColumnRowOutsideTheBoundProjects_KeepsTheMergeSentence()
    {
        var h = await NewAsync();
        await h.AddProjectAsync("FIL");
        var merges = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();

        var issue = await h.FileAsync("story", "merging", merges.Id);
        issue.PullRequestUrl = "https://example.com/pr/1";
        await h.Db.SaveChangesAsync();
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);

        var unbound = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);
        var bound = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, projects: ["FIL"]);

        var sentence = RowFor(bound, issue).Blocked;
        Assert.DoesNotContain("bound to", sentence);
        Assert.DoesNotContain("is in project", sentence);
        Assert.Equal(RowFor(unbound, issue).Blocked, sentence);
    }

    [Fact]
    public async Task ScanAsync_AHopOnAnIssueOutsideTheBoundProjects_IsFoldedByTheProjectSentence()
    {
        var h = await NewAsync();
        await h.AddProjectAsync("FIL");
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);
        var key = IssueKey.Format(h.ProjectKey, issue.Number);

        var scan = await h.Dispatch.ScanAsync(
            0, null, null, RepositoryDeclaration.Undeclared, false, default, projects: ["FIL"]);

        var row = RowFor(scan, issue);
        Assert.Equal($"{key} is in project {h.ProjectKey} - this runner is bound to FIL", row.Blocked);
    }

    // ---- The merge train's own head, read on its own (HA-2092) ----

    [Fact]
    public async Task TrainHeadsAsync_NamesTheEligibleHeadBehindAHeldRowAheadOfIt()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "merges", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();

        var held = await h.FileAsync("story", "stuck", merges.Id, rank: 1);
        held.PullRequestUrl = "https://example.com/o/r/pull/1";
        await h.Db.SaveChangesAsync();
        await h.AddMergeCheckAsync(held, MergeVerdicts.None);

        var eligible = await h.FileAsync("story", "ready", merges.Id, rank: 2);
        eligible.PullRequestUrl = "https://example.com/o/r/pull/2";
        await h.Db.SaveChangesAsync();
        await h.AddMergeCheckAsync(eligible, MergeVerdicts.Clean);

        var heads = await h.Dispatch.TrainHeadsAsync(Now, default);

        var head = Assert.Single(heads);
        Assert.Equal(IssueKey.Format(h.ProjectKey, eligible.Number), head.Key);
        Assert.Equal(WorkKinds.Update, head.Kind);
    }

    [Fact]
    public async Task TrainHeadsAsync_AProjectWithNoMergesColumnConfigured_IsEmpty()
    {
        var h = await NewAsync();

        Assert.Empty(await h.Dispatch.TrainHeadsAsync(Now, default));
    }

    [Fact]
    public async Task TrainHeadsAsync_AMergesColumnWithOnlyHeldRows_IsEmpty()
    {
        var h = await NewAsync();
        var merges = new EfHatchStatus { Name = "merges", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(merges);
        await h.Db.SaveChangesAsync();

        var held = await h.FileAsync("story", "stuck", merges.Id);
        held.PullRequestUrl = "https://example.com/o/r/pull/1";
        await h.Db.SaveChangesAsync();
        await h.AddMergeCheckAsync(held, MergeVerdicts.None);

        Assert.Empty(await h.Dispatch.TrainHeadsAsync(Now, default));
    }

    // ---- The approve hop yields to the check (HA-1592) ----

    [Fact]
    public async Task ScanAsync_APreApprovedIssueWithACheckRowCoveringTheMove_IsNotAnApproveHop()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "ready, and a check also covers it", h.Review);
        var approved = new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true };
        h.Db.Add(approved);
        await h.Db.SaveChangesAsync();
        await h.SetCheckApprovesMergeAsync();
        await h.AddPlaybookAsync(h.Review, approved.Id, types: "story", isCheck: true);
        await h.PreApprovedAsync(issue);
        issue.PullRequestUrl = "https://example.com/o/r/pull/1";
        await h.Db.SaveChangesAsync();
        await h.AddMergeCheckAsync(issue, MergeVerdicts.Clean);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var row = RowFor(scan, issue);
        Assert.False(row.Hop);
        Assert.Null(row.HopKind);
        Assert.Null(row.Blocked);
        Assert.Equal(approved.Id, row.To!.Id);
    }

    // ---- Deadlocks: the rows no pass will ever clear on its own ----

    private static readonly EfHatchStatus DeadlockStatus = new() { Name = "todo", SortOrder = 10 };

    private static ScanRow DRow(long id, IReadOnlyList<long>? waitsOn = null, bool anyOf = false) =>
        new(
            new EfHatchIssue
            {
                Id = id, ProjectId = 1, Number = (int)id, Type = "task", Title = "t", Description = "",
                StatusId = DeadlockStatus.Id, Rank = id, CreatedBy = "operator", CreatedAt = Now, UpdatedAt = Now,
            },
            DeadlockStatus, null, null, null, "normal", false,
            WaitsOn: waitsOn, AnyOf: anyOf);

    [Fact]
    public void Find_ATwoRing_MarksExactlyTheRing()
    {
        var rows = new[] { DRow(1, [2]), DRow(2, [1]) };

        Assert.Equal(new HashSet<long> { 1, 2 }, Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_AThreeRing_MarksExactlyTheRing()
    {
        var rows = new[] { DRow(1, [2]), DRow(2, [3]), DRow(3, [1]) };

        Assert.Equal(new HashSet<long> { 1, 2, 3 }, Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_ARowWaitingOnARingAndOneTwoHopsBehind_BothAreMarked()
    {
        var rows = new[] { DRow(1, [2]), DRow(2, [1]), DRow(3, [1]), DRow(4, [3]) };

        Assert.Equal(new HashSet<long> { 1, 2, 3, 4 }, Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_AChainEndingInARowThatWaitsOnNothing_NothingIsMarked()
    {
        var rows = new[] { DRow(1, []), DRow(2, [1]), DRow(3, [2]) };

        Assert.Empty(Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_AChainEndingInAnIdAbsentFromTheRows_NothingIsMarked()
    {
        var rows = new[] { DRow(1, [99]), DRow(2, [1]) };

        Assert.Empty(Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_ARingPlusARowAlsoWaitingOnAClearRow_TheAllOfStillMarksIt()
    {
        var rows = new[] { DRow(1, [2]), DRow(2, [1]), DRow(3, [1, 4]), DRow(4, []) };

        Assert.Equal(new HashSet<long> { 1, 2, 3 }, Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_AnyOfWithOneMovableHolder_IsNotMarked()
    {
        // 2 waits on 1 or 3, any one is enough - 1 is clear, so 2 is movable
        // even though 3 is stuck forever in its own ring with 4.
        var rows = new[] { DRow(1, []), DRow(2, [1, 3], anyOf: true), DRow(3, [4]), DRow(4, [3]) };

        Assert.Equal(new HashSet<long> { 3, 4 }, Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_AnyOfWithEveryHolderInARing_IsMarked()
    {
        var rows = new[] { DRow(1, [2]), DRow(2, [1]), DRow(3, [1, 2], anyOf: true) };

        Assert.Equal(new HashSet<long> { 1, 2, 3 }, Deadlocks.Find(rows));
    }

    [Fact]
    public void Find_AnyOfOverAnEmptyList_IsMovable()
    {
        var rows = new[] { DRow(1, [], anyOf: true) };

        Assert.Empty(Deadlocks.Find(rows));
    }

    // ---- MatchWeave: the leftmost from-column covering epic ----

    private static EfHatchPlaybook WeaveRow(int id, int fromSort, string types = "", int? projectId = null) => new()
    {
        Id = id,
        FromStatusId = fromSort,
        FromStatus = new EfHatchStatus { Name = $"c{fromSort}", SortOrder = fromSort },
        ToStatusId = fromSort + 1,
        Types = types,
        Shape = EfHatchPlaybook.AnyShape,
        Prompt = "do the thing",
        Model = "sonnet",
        Effort = "high",
        ProjectId = projectId,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public void MatchWeave_PicksTheLeftmostFromColumnCoveringEpic()
    {
        var rows = new List<EfHatchPlaybook> { WeaveRow(1, 30, "epic"), WeaveRow(2, 10, "epic"), WeaveRow(3, 20, "epic") };

        Assert.Equal(2, Dispatch.MatchWeave(rows, 1)!.Id);
    }

    [Fact]
    public void MatchWeave_IgnoresARowThatDoesNotCoverEpic()
    {
        var rows = new List<EfHatchPlaybook> { WeaveRow(1, 10, "task,bug"), WeaveRow(2, 20, "epic") };

        Assert.Equal(2, Dispatch.MatchWeave(rows, 1)!.Id);
    }

    [Fact]
    public void MatchWeave_TakesEmptyTypesAsCovering()
    {
        var rows = new List<EfHatchPlaybook> { WeaveRow(1, 10), WeaveRow(2, 20, "epic") };

        Assert.Equal(1, Dispatch.MatchWeave(rows, 1)!.Id);
    }

    [Fact]
    public void MatchWeave_PrefersTheProjectRowOverTheBoardRowForTheSameColumn()
    {
        var rows = new List<EfHatchPlaybook> { WeaveRow(1, 10, "epic"), WeaveRow(2, 10, "epic", projectId: 7) };

        Assert.Equal(2, Dispatch.MatchWeave(rows, 7)!.Id);
    }

    [Fact]
    public void MatchWeave_IgnoresAProjectRowForAnotherProject()
    {
        var rows = new List<EfHatchPlaybook> { WeaveRow(1, 10, "epic", projectId: 8), WeaveRow(2, 20, "epic") };

        Assert.Equal(2, Dispatch.MatchWeave(rows, 7)!.Id);
    }

    [Fact]
    public void MatchWeave_NothingCoveringEpic_IsNull()
    {
        var rows = new List<EfHatchPlaybook> { WeaveRow(1, 10, "task"), WeaveRow(2, 20, "bug,story") };

        Assert.Null(Dispatch.MatchWeave(rows, 1));
        Assert.Null(Dispatch.MatchWeave([], 1));
    }

    [Fact]
    public void MatchWeave_OnAStockShapedSet_ReturnsTheBreakdownRow()
    {
        var rows = new List<EfHatchPlaybook>
        {
            WeaveRow(1, 20, "epic"),
            WeaveRow(2, 20),
            WeaveRow(3, 30),
            WeaveRow(4, 30, "task,bug"),
            WeaveRow(5, 10, "epic"),
        };

        Assert.Equal(5, Dispatch.MatchWeave(rows, 1)!.Id);
    }

    // ---- StalledFold: SL-418's one shared held/stall rule ----

    [Fact]
    public void StalledFold_Held_IsFoldedOnItsOwnSentence()
    {
        var fold = Dispatch.StalledFold(held: true, stalledAt: null, stalledWhy: null, letGo: 0, resumeSeconds: 900, resumeLimit: 3, now: Now);

        Assert.NotNull(fold);
        Assert.StartsWith("held -", fold.Sentence);
        Assert.Equal(BlockedKinds.Person, fold.Kind);
    }

    [Fact]
    public void StalledFold_HeldAndStalled_StillReadsAsHeld()
    {
        var fold = Dispatch.StalledFold(held: true, stalledAt: Now, stalledWhy: "left a half-finished merge", letGo: 0, resumeSeconds: 900, resumeLimit: 3, now: Now);

        Assert.NotNull(fold);
        Assert.StartsWith("held -", fold.Sentence);
        Assert.Equal(BlockedKinds.Person, fold.Kind);
    }

    [Fact]
    public void StalledFold_AResumeSecondsOfZero_NeverResumesUnattended()
    {
        var fold = Dispatch.StalledFold(held: false, stalledAt: Now, stalledWhy: null, letGo: 0, resumeSeconds: 0, resumeLimit: 3, now: Now.AddDays(1));

        Assert.NotNull(fold);
        Assert.Contains("never resumes a marked ticket unattended", fold.Sentence);
        Assert.Equal(BlockedKinds.Person, fold.Kind);
    }

    [Fact]
    public void StalledFold_InsideTheResumeWindow_IsFoldedAsOther()
    {
        var fold = Dispatch.StalledFold(held: false, stalledAt: Now, stalledWhy: null, letGo: 0, resumeSeconds: 900, resumeLimit: 3, now: Now.AddMinutes(5));

        Assert.NotNull(fold);
        Assert.Contains("resumes itself after", fold.Sentence);
        Assert.Equal(BlockedKinds.Other, fold.Kind);
    }

    [Fact]
    public void StalledFold_PastTheWindowAtTheResumeLimit_IsHeldForAPerson()
    {
        var fold = Dispatch.StalledFold(held: false, stalledAt: Now, stalledWhy: null, letGo: 3, resumeSeconds: 900, resumeLimit: 3, now: Now.AddMinutes(20));

        Assert.NotNull(fold);
        Assert.Contains("held for a person now", fold.Sentence);
        Assert.Equal(BlockedKinds.Person, fold.Kind);
    }

    [Fact]
    public void StalledFold_PastTheWindowUnderTheResumeLimit_IsNull()
    {
        Assert.Null(Dispatch.StalledFold(held: false, stalledAt: Now, stalledWhy: null, letGo: 2, resumeSeconds: 900, resumeLimit: 3, now: Now.AddMinutes(20)));
    }

    [Fact]
    public void StalledFold_NeitherHeldNorStalled_IsNull()
    {
        Assert.Null(Dispatch.StalledFold(held: false, stalledAt: null, stalledWhy: null, letGo: 0, resumeSeconds: 900, resumeLimit: 3, now: Now));
    }

    // ---- A project line: nothing inherited crosses it, and no hop is carried over it ----

    // The one hold for a parent in progress across a line that may not pull the child (HA-2482).
    private static string PullSentence(string parentKey, string projectKey = "AER") =>
        $"{parentKey} is in progress, but the person who started it has no say in {projectKey}, so a person moves it";

    private const string ParentNotStartedSentence = "its parent has not reached the implementation column, so nothing pulls it forward yet";

    [Fact]
    public async Task PriorityTree_AParentInAnotherProject_HandsDownNoPreApprovalAndNoPriority()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
        epic.Priority = global::Hatch.Contracts.PriorityLevels.Expedited;
        await h.PreApprovedAsync(epic);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);
        var sibling = await h.FileAsync("story", "same project", h.Todo, projectId: other, parentId: epic.Id);

        var tree = await PriorityTree.ForAsync(h.Db, new StubActorDirectory(), default);

        Assert.Equal((PreApprovalLevels.None, (string?)null), tree.EffectivePreApproval(story.Id));
        Assert.Equal((PriorityLevels.Normal, (string?)null), tree.Effective(story.Id));
        Assert.Equal(PreApprovalLevels.PreApproved, tree.EffectivePreApproval(sibling.Id).Level);
        Assert.Equal(global::Hatch.Contracts.PriorityLevels.Expedited, tree.Effective(sibling.Id).Level);
    }

    [Fact]
    public async Task PriorityTree_AChainThatLeavesAndComesBack_PassesOverTheMiddleRow()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var top = await h.FileAsync("epic", "top", h.Todo);
        await h.PreApprovedAsync(top);
        var middle = await h.FileAsync("epic", "middle", h.Todo, projectId: other, parentId: top.Id);
        await h.ExpressAsync(middle);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: middle.Id);

        var tree = await PriorityTree.ForAsync(h.Db, new StubActorDirectory(), default);

        Assert.Equal(
            (PreApprovalLevels.PreApproved, (string?)IssueKey.Format(h.ProjectKey, top.Number)),
            tree.EffectivePreApproval(story.Id));
    }

    // ---- A level crosses a project line on its setter's authority (HA-2466) ----

    /// <summary>
    /// An epic in project OTH carrying <paramref name="level"/> set by <paramref name="setter"/>, and a
    /// story under it in the harness's own project, AER.
    /// </summary>
    private static async Task<(EfHatchIssue Epic, EfHatchIssue Story, string EpicKey)> CrossAsync(
        Harness h, int level, Guid? setter, int otherProject)
    {
        var epic = await h.FileAsync("epic", "E", h.Todo, projectId: otherProject);
        await h.SetLevelAsync(epic, level, setter);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);
        return (epic, story, IssueKey.Format("OTH", epic.Number));
    }

    [Theory]
    [InlineData(ProjectMemberRole.Approver)]
    [InlineData(ProjectMemberRole.Owner)]
    public async Task PriorityTree_APreApprovalSetByAnApproverOfTheChildsProject_CrossesTheLine(string role)
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, role);
        var (_, story, epicKey) = await CrossAsync(h, PreApprovalLevels.PreApproved, ada.Id, other);

        var tree = await h.TreeAsync();

        Assert.Equal((PreApprovalLevels.PreApproved, (string?)epicKey), tree.EffectivePreApproval(story.Id));
        Assert.Null(tree.PassedOver(story.Id).PreApproval);
    }

    public enum Stranger { NoRole, RoleOnlyInTheOtherProject, Deleted, ScopeNone, ScopeSomeWithoutTheProject, Unrecorded }

    [Theory]
    [InlineData(Stranger.NoRole)]
    [InlineData(Stranger.RoleOnlyInTheOtherProject)]
    [InlineData(Stranger.Deleted)]
    [InlineData(Stranger.ScopeNone)]
    [InlineData(Stranger.ScopeSomeWithoutTheProject)]
    [InlineData(Stranger.Unrecorded)]
    public async Task PriorityTree_APreApprovalWhoseSetterHasNoSayInTheChildsProject_IsPassedOver(Stranger kind)
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var third = await h.AddProjectAsync("THR");
        var setter = kind == Stranger.Deleted ? Guid.NewGuid() : h.Actors.AddPerson("Ada").Id;
        switch (kind)
        {
            case Stranger.RoleOnlyInTheOtherProject:
                await h.MemberAsync(other, setter, ProjectMemberRole.Owner);
                break;
            case Stranger.Deleted:
                // The membership row lingers; the person is gone from the directory.
                await h.MemberAsync(h.ProjectId, setter, ProjectMemberRole.Owner);
                break;
            case Stranger.ScopeNone:
                await h.MemberAsync(h.ProjectId, setter, ProjectMemberRole.Owner);
                await h.SetScopeAsync(setter, PersonScopeMode.None);
                break;
            case Stranger.ScopeSomeWithoutTheProject:
                await h.MemberAsync(h.ProjectId, setter, ProjectMemberRole.Owner);
                await h.SetScopeAsync(setter, PersonScopeMode.Some, third);
                break;
        }

        var (_, story, epicKey) = await CrossAsync(h, PreApprovalLevels.PreApproved, kind == Stranger.Unrecorded ? null : setter, other);

        var tree = await h.TreeAsync();

        Assert.Equal((PreApprovalLevels.None, (string?)null), tree.EffectivePreApproval(story.Id));
        Assert.Equal(new PassedOverLevel(PreApprovalLevels.PreApproved, epicKey), tree.PassedOver(story.Id).PreApproval);
    }

    [Fact]
    public async Task PriorityTree_AnApproverWhoseScopeNamesTheProject_CrossesTheLine()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Approver);
        await h.SetScopeAsync(ada.Id, PersonScopeMode.Some, h.ProjectId);
        var (_, story, _) = await CrossAsync(h, PreApprovalLevels.PreApproved, ada.Id, other);

        Assert.Equal(PreApprovalLevels.PreApproved, (await h.TreeAsync()).EffectivePreApproval(story.Id).Level);
    }

    [Fact]
    public async Task PriorityTree_TheJudgementIsLive_TakingTheRoleAwayOrNarrowingTheScopeChangesTheNextRead()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Approver);
        var (epic, story, epicKey) = await CrossAsync(h, PreApprovalLevels.PreApproved, ada.Id, other);
        Assert.Equal(PreApprovalLevels.PreApproved, (await h.TreeAsync()).EffectivePreApproval(story.Id).Level);

        h.Db.ProjectMembers.RemoveRange(await h.Db.ProjectMembers.ToListAsync());
        await h.Db.SaveChangesAsync();

        var after = await h.TreeAsync();
        Assert.Equal(PreApprovalLevels.None, after.EffectivePreApproval(story.Id).Level);
        Assert.Equal(epicKey, after.PassedOver(story.Id).PreApproval!.Value.Key);
        // Nothing was copied onto the descendant, and the epic's own row is untouched.
        Assert.Equal(PreApprovalLevels.None, (await h.Db.Issues.AsNoTracking().SingleAsync(i => i.Id == story.Id)).PreApproval);
        Assert.Equal(PreApprovalLevels.PreApproved, (await h.Db.Issues.AsNoTracking().SingleAsync(i => i.Id == epic.Id)).PreApproval);

        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Approver);
        Assert.Equal(PreApprovalLevels.PreApproved, (await h.TreeAsync()).EffectivePreApproval(story.Id).Level);

        await h.SetScopeAsync(ada.Id, PersonScopeMode.None);
        Assert.Equal(PreApprovalLevels.None, (await h.TreeAsync()).EffectivePreApproval(story.Id).Level);
    }

    [Fact]
    public async Task PriorityTree_ExpressNeedsASetterWhoMayMoveIntoTheColumnThatFeedsImplementation()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        // The column feeding implementation ("in progress") is "todo": make it protected,
        // so moving into it takes an approver, as CanMoveIntoAsync reads it.
        (await h.Db.Statuses.SingleAsync(s => s.Id == h.Todo)).IsProtected = true;
        await h.Db.SaveChangesAsync();
        var plain = h.Actors.AddPerson("Plain");
        var approver = h.Actors.AddPerson("Approver");
        await h.MemberAsync(h.ProjectId, approver.Id, ProjectMemberRole.Approver);

        var (_, byPlain, _) = await CrossAsync(h, PreApprovalLevels.Express, plain.Id, other);
        var (_, byApprover, _) = await CrossAsync(h, PreApprovalLevels.Express, approver.Id, other);

        var tree = await h.TreeAsync();
        Assert.Equal(PreApprovalLevels.None, tree.EffectivePreApproval(byPlain.Id).Level);
        Assert.NotNull(tree.PassedOver(byPlain.Id).PreApproval);
        Assert.Equal(PreApprovalLevels.Express, tree.EffectivePreApproval(byApprover.Id).Level);
    }

    [Fact]
    public async Task PriorityTree_ExpressFromAnyLivePerson_CrossesWhereNoColumnIsProtected()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var plain = h.Actors.AddPerson("Plain");
        var (_, story, _) = await CrossAsync(h, PreApprovalLevels.Express, plain.Id, other);

        Assert.Equal(PreApprovalLevels.Express, (await h.TreeAsync()).EffectivePreApproval(story.Id).Level);
    }

    [Fact]
    public async Task PriorityTree_APriorityCrossesFromALivePerson_AndNotFromADeletedOneOrNoOne()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var live = h.Actors.AddPerson("Ada");

        async Task<(EfHatchIssue Story, string Key)> At(Guid? setter)
        {
            var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
            await h.SetLevelAsync(epic, PreApprovalLevels.None, setter, priority: PriorityLevels.Expedited);
            return (await h.FileAsync("story", "S", h.Todo, parentId: epic.Id), IssueKey.Format("OTH", epic.Number));
        }

        var (byLive, liveKey) = await At(live.Id);
        var (byDeleted, deletedKey) = await At(Guid.NewGuid());
        var (byNobody, nobodyKey) = await At(null);

        var tree = await h.TreeAsync();
        Assert.Equal((PriorityLevels.Expedited, (string?)liveKey), tree.Effective(byLive.Id));
        Assert.Null(tree.PassedOver(byLive.Id).Priority);
        Assert.Equal((PriorityLevels.Normal, (string?)null), tree.Effective(byDeleted.Id));
        Assert.Equal(new PassedOverLevel(PriorityLevels.Expedited, deletedKey), tree.PassedOver(byDeleted.Id).Priority);
        Assert.Equal((PriorityLevels.Normal, (string?)null), tree.Effective(byNobody.Id));
        Assert.Equal(nobodyKey, tree.PassedOver(byNobody.Id).Priority!.Value.Key);
    }

    // ---- PriorityTree.PreApprovalSetter: the row EffectivePreApproval settled on, never a different one (HA-2481) ----

    [Fact]
    public async Task PriorityTree_PreApprovalSetter_OwnRow_IsItsOwnSetter()
    {
        var h = await NewAsync();
        var setter = h.Actors.AddPerson("Ada");
        var issue = await h.FileAsync("story", "S", h.Todo);
        await h.SetLevelAsync(issue, PreApprovalLevels.Express, setter.Id);

        var tree = await h.TreeAsync();
        Assert.Equal(setter.Id, tree.PreApprovalSetter(issue.Id));
    }

    [Fact]
    public async Task PriorityTree_PreApprovalSetter_InheritedFromAnAncestor_IsTheAncestorsSetter_NotTheDescendants()
    {
        var h = await NewAsync();
        var setter = h.Actors.AddPerson("Ada");
        var epic = await h.FileAsync("epic", "E", h.Todo);
        await h.SetLevelAsync(epic, PreApprovalLevels.PreApproved, setter.Id);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);

        var tree = await h.TreeAsync();
        Assert.Equal(setter.Id, tree.PreApprovalSetter(story.Id));
    }

    [Fact]
    public async Task PriorityTree_PreApprovalSetter_NoLevelAnywhere_IsNull()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "S", h.Todo);

        var tree = await h.TreeAsync();
        Assert.Null(tree.PreApprovalSetter(issue.Id));
    }

    [Fact]
    public async Task PriorityTree_PreApprovalSetter_LevelWithNoSetterRecorded_IsNull()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "S", h.Todo);
        await h.SetLevelAsync(issue, PreApprovalLevels.Express, null);

        var tree = await h.TreeAsync();
        Assert.Equal(PreApprovalLevels.Express, tree.EffectivePreApproval(issue.Id).Level);
        Assert.Null(tree.PreApprovalSetter(issue.Id));
    }

    [Fact]
    public async Task PriorityTree_InsideOneProject_NoTestIsMade_ASetterWithNoRoleAtAllIsHonoured()
    {
        var h = await NewAsync();
        var nobody = Guid.NewGuid();
        var epic = await h.FileAsync("epic", "E", h.Todo);
        await h.SetLevelAsync(epic, PreApprovalLevels.PreApproved, nobody, priority: PriorityLevels.Expedited);
        var recorded = await h.FileAsync("story", "recorded", h.Todo, parentId: epic.Id);
        var unrecordedEpic = await h.FileAsync("epic", "E2", h.Todo);
        await h.SetLevelAsync(unrecordedEpic, PreApprovalLevels.PreApproved, null);
        var unrecorded = await h.FileAsync("story", "unrecorded", h.Todo, parentId: unrecordedEpic.Id);

        var tree = await h.TreeAsync();

        Assert.Equal(PreApprovalLevels.PreApproved, tree.EffectivePreApproval(recorded.Id).Level);
        Assert.Equal(PriorityLevels.Expedited, tree.Effective(recorded.Id).Level);
        Assert.Equal(PreApprovalLevels.PreApproved, tree.EffectivePreApproval(unrecorded.Id).Level);
        Assert.Null(tree.PassedOver(recorded.Id).PreApproval);
    }

    [Fact]
    public async Task PriorityTree_APassedOverLevel_LetsTheWalkCarryOnUp()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Owner);
        var top = await h.FileAsync("epic", "top", h.Todo);
        await h.SetLevelAsync(top, PreApprovalLevels.PreApproved, null);
        var middle = await h.FileAsync("epic", "middle", h.Todo, projectId: other, parentId: top.Id);
        await h.SetLevelAsync(middle, PreApprovalLevels.Express, null);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: middle.Id);

        var tree = await h.TreeAsync();

        Assert.Equal((PreApprovalLevels.PreApproved, (string?)IssueKey.Format(h.ProjectKey, top.Number)), tree.EffectivePreApproval(story.Id));
        Assert.Equal(IssueKey.Format("OTH", middle.Number), tree.PassedOver(story.Id).PreApproval!.Value.Key);
    }

    [Fact]
    public async Task PriorityTree_AndProjectAccess_AgreeOnWhoMayApproveAndWhoMayMoveIntoAProtectedColumn()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        (await h.Db.Statuses.SingleAsync(s => s.Id == h.Todo)).IsProtected = true;
        await h.Db.SaveChangesAsync();
        var protectedColumn = await h.Db.Statuses.AsNoTracking().SingleAsync(s => s.Id == h.Todo);

        foreach (var role in new string?[] { ProjectMemberRole.Owner, ProjectMemberRole.Approver, null })
        {
            var person = h.Actors.AddPerson($"p-{role ?? "none"}");
            if (role is not null) await h.MemberAsync(h.ProjectId, person.Id, role);
            h.Actors.Me = person;
            var access = new ProjectAccess(h.Db, h.Actors);

            var (_, preApproved, _) = await CrossAsync(h, PreApprovalLevels.PreApproved, person.Id, other);
            var (_, express, _) = await CrossAsync(h, PreApprovalLevels.Express, person.Id, other);
            var tree = await h.TreeAsync();

            Assert.Equal(
                await access.CanApproveAsync(h.ProjectId, default),
                tree.EffectivePreApproval(preApproved.Id).Level == PreApprovalLevels.PreApproved);
            Assert.Equal(
                await access.CanMoveIntoAsync(h.ProjectId, protectedColumn, default),
                tree.EffectivePreApproval(express.Id).Level == PreApprovalLevels.Express);
        }
    }

    // The three arms that read a level get no check of their own: a level the walk
    // does not honour is not a level, and carries nothing.

    [Fact]
    public async Task ScanAsync_TheExpressHop_FiresOnAnHonouredLevelAcrossTheLine_AndNotOnAPassedOverOne()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.TickExpressSkipsAsync(h.Todo);
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Approver);

        // task -> story (AER) -> epic (OTH): the task's own parent is in its project, so the
        // other-project hold does not apply and the level is what decides.
        async Task<EfHatchIssue> TaskUnder(Guid? setter)
        {
            var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
            await h.SetLevelAsync(epic, PreApprovalLevels.Express, setter);
            var story = await h.FileAsync("story", "S", h.InProgress, parentId: epic.Id);
            return await h.FileAsync("task", "T", h.Todo, parentId: story.Id);
        }

        var honoured = await TaskUnder(ada.Id);
        var passed = await TaskUnder(Guid.NewGuid());

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Equal(HopKinds.Express, RowFor(scan, honoured).HopKind);
        Assert.Null(RowFor(scan, passed).HopKind);
        Assert.StartsWith("express on OTH-", RowFor(scan, passed).Blocked);
        Assert.Contains("not honoured in AER", RowFor(scan, passed).Blocked);
        Assert.Equal(BlockedKinds.Person, RowFor(scan, passed).BlockedKind);
    }

    private static async Task<(EfHatchIssue Epic, EfHatchIssue Story)> ReviewStoryUnderOtherEpicAsync(Harness h, int other, Guid? setter)
    {
        var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
        await h.SetLevelAsync(epic, PreApprovalLevels.PreApproved, setter);
        var story = await h.FileAsync("story", "in review", h.Review, parentId: epic.Id);
        story.PullRequestUrl = "https://example.com/o/r/pull/1";
        await h.Db.SaveChangesAsync();
        await h.AddMergeCheckAsync(story, MergeVerdicts.Clean);
        await h.AddBuildCheckAsync(story, BuildVerdicts.Passed);
        return (epic, story);
    }

    [Fact]
    public async Task ScanAsync_TheApproveHop_CarriesAnHonouredLevelAcrossTheLine_AndNothingElse()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        h.Db.Add(new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true });
        await h.Db.SaveChangesAsync();
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Approver);
        var (_, honoured) = await ReviewStoryUnderOtherEpicAsync(h, other, ada.Id);
        var (_, passed) = await ReviewStoryUnderOtherEpicAsync(h, other, Guid.NewGuid());

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Equal(HopKinds.Approve, RowFor(scan, honoured).HopKind);
        Assert.Null(RowFor(scan, passed).HopKind);
    }

    [Fact]
    public async Task ScanAsync_TheCloseHop_CarriesAnHonouredLevelAcrossTheLine_AndNothingElse()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(h.ProjectId, ada.Id, ProjectMemberRole.Approver);

        async Task<EfHatchIssue> Closable(Guid? setter)
        {
            var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
            await h.SetLevelAsync(epic, PreApprovalLevels.PreApproved, setter);
            var story = await h.FileAsync("story", "ready to close", h.Review, parentId: epic.Id);
            await h.FileAsync("task", "the only child", h.Done, parentId: story.Id);
            return story;
        }

        var honoured = await Closable(ada.Id);
        var passed = await Closable(null);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Equal(HopKinds.Close, RowFor(scan, honoured).HopKind);
        Assert.Null(RowFor(scan, passed).HopKind);
    }

    [Fact]
    public async Task ScanAsync_AReviewChildUnderAnEpicPreApprovedByAStranger_IsNotApprovedMergedOrClosed_UntilAnApproverSetsTheLevelOnTheChild()
    {
        // The epic's own first check: a child in one project stands in review with a clean
        // branch under a pre-approved epic in another, whose level a person with no rights
        // in the child's project set. The loop does not carry it. An approver of the child's
        // project sets the level on the child, and the next pass does.
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        h.Db.Add(new EfHatchStatus { Name = "approved", SortOrder = 37, IsProtected = true, MergesPullRequest = true });
        await h.Db.SaveChangesAsync();
        var stranger = h.Actors.AddPerson("Stranger");
        await h.MemberAsync(other, stranger.Id, ProjectMemberRole.Owner);
        var (epic, child) = await ReviewStoryUnderOtherEpicAsync(h, other, stranger.Id);
        var epicKey = IssueKey.Format("OTH", epic.Number);

        var first = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), child);

        Assert.False(first.Hop);
        Assert.Null(first.HopKind);
        Assert.Equal($"pre-approved on {epicKey}, not honoured in AER - its setter has no say there, so a person moves it", first.Blocked);
        Assert.Equal(BlockedKinds.Person, first.BlockedKind);

        var approver = h.Actors.AddPerson("Approver");
        await h.MemberAsync(h.ProjectId, approver.Id, ProjectMemberRole.Approver);
        await h.SetLevelAsync(child, PreApprovalLevels.PreApproved, approver.Id);

        var second = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), child);

        Assert.Equal(HopKinds.Approve, second.HopKind);
        Assert.Null(second.Blocked);
    }

    [Fact]
    public async Task PriorityTree_AParentInAnotherProject_StillHandsDownTagsADangerousReasonAndOmissions()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
        await h.TagAsync(epic, "macos");
        epic.Dangerous = "load test";
        h.Db.RunnerOmissions.Add(new EfHatchIssueRunnerOmission { IssueId = epic.Id, RunnerName = "runner-a", By = "operator", At = Now });
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);
        await h.Db.SaveChangesAsync();

        var tree = await PriorityTree.ForAsync(h.Db, new StubActorDirectory(), default);

        Assert.Contains("macos", tree.EffectiveRunnerTags(story.Id).All);
        Assert.Equal("load test", tree.EffectiveDangerous(story.Id).Reason);
        Assert.Contains(tree.EffectiveOmittedRunners(story.Id), o => o.Runner == "runner-a");
    }

    [Fact]
    public async Task ScanAsync_AnExpressEpicInAnotherProject_CarriesNothingUnderIt()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.TickExpressSkipsAsync(h.Todo);
        var epic = await h.FileAsync("epic", "E", h.Todo, projectId: other);
        await h.ExpressAsync(epic);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Null(RowFor(scan, story).HopKind);
        Assert.Equal(
            $"express on {IssueKey.Format("OTH", epic.Number)}, not honoured in AER - its setter has no say there, so a person moves it",
            RowFor(scan, story).Blocked);
        Assert.Equal(BlockedKinds.Person, RowFor(scan, story).BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_ARunningEpicInAnotherProjectWithNoStarter_DoesNotCarryItsStoryUnder()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Todo);
        var epic = await h.FileAsync("epic", "E", h.InProgress, projectId: other);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Null(RowFor(scan, story).HopKind);
        Assert.Equal(PullSentence(IssueKey.Format("OTH", epic.Number)), RowFor(scan, story).Blocked);
        Assert.Equal(BlockedKinds.Person, RowFor(scan, story).BlockedKind);
    }

    [Fact]
    public async Task ScanAsync_ARunningEpicInAnotherProjectWithNoStarter_DoesNotCarryASubEpicOrPullAChild()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickParentPullsAsync(h.Todo);
        var running = await h.FileAsync("epic", "running", h.InProgress, projectId: other);
        var sub = await h.FileAsync("epic", "sub", h.Todo, parentId: running.Id);
        await h.FileAsync("story", "under sub", h.Todo, parentId: sub.Id);
        var task = await h.FileAsync("task", "T", h.Todo, parentId: running.Id);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        Assert.Null(RowFor(scan, sub).HopKind);
        Assert.Equal(PullSentence(IssueKey.Format("OTH", running.Number)), RowFor(scan, sub).Blocked);
        Assert.Null(RowFor(scan, task).HopKind);
        Assert.Equal(PullSentence(IssueKey.Format("OTH", running.Number)), RowFor(scan, task).Blocked);
    }

    // ---- A pull crosses a project line on the starter's say (HA-2482) ----

    public enum Hop { Epic, Under, Parent }

    /// <summary>
    /// A parent running in project OTH, started by <paramref name="starter"/>, and a child in the
    /// harness's own project AER standing in To Do for <paramref name="hop"/> to carry: a sub-epic with a
    /// story under it, a story under an express-skipping column, or a task under a pulling one.
    /// </summary>
    private static async Task<(EfHatchIssue Parent, EfHatchIssue Child)> RunningAcrossTheLineAsync(
        Harness h, Hop hop, int other, Guid? starter)
    {
        // A task is pulled by a running story: a story under a running epic is analysed, not carried.
        var parent = await h.FileAsync(hop == Hop.Parent ? "story" : "epic", "running", h.InProgress, projectId: other);
        parent.StartedBy = starter;
        await h.Db.SaveChangesAsync();

        switch (hop)
        {
            case Hop.Epic:
                await h.TickWipAsync(h.InProgress, h.Review);
                var sub = await h.FileAsync("epic", "sub", h.Todo, parentId: parent.Id);
                await h.FileAsync("story", "under sub", h.Todo, parentId: sub.Id);
                return (parent, sub);
            case Hop.Under:
                await h.TickWipAsync(h.InProgress, h.Review);
                await h.TickExpressSkipsAsync(h.Todo);
                return (parent, await h.FileAsync("story", "S", h.Todo, parentId: parent.Id));
            default:
                await h.TickParentPullsAsync(h.Todo);
                return (parent, await h.FileAsync("task", "T", h.Todo, parentId: parent.Id));
        }
    }

    private static string KindOf(Hop hop) => hop switch { Hop.Epic => HopKinds.Epic, Hop.Under => HopKinds.Under, _ => HopKinds.Parent };

    /// <summary>One of the ways of having no say in AER, as a starter id - or null where nobody was recorded.</summary>
    private static async Task<Guid?> StarterAsync(Harness h, Stranger kind, int other, int third)
    {
        if (kind == Stranger.Unrecorded) return null;

        var person = kind == Stranger.Deleted ? Guid.NewGuid() : h.Actors.AddPerson("Ada").Id;
        switch (kind)
        {
            case Stranger.RoleOnlyInTheOtherProject:
                await h.MemberAsync(other, person, ProjectMemberRole.Owner);
                break;
            case Stranger.Deleted:
                await h.MemberAsync(h.ProjectId, person, ProjectMemberRole.Owner);
                break;
            case Stranger.ScopeNone:
                await h.SetScopeAsync(person, PersonScopeMode.None);
                break;
            case Stranger.ScopeSomeWithoutTheProject:
                await h.SetScopeAsync(person, PersonScopeMode.Some, third);
                break;
        }

        return person;
    }

    [Theory]
    [InlineData(Hop.Epic, Stranger.Deleted)]
    [InlineData(Hop.Epic, Stranger.ScopeNone)]
    [InlineData(Hop.Epic, Stranger.Unrecorded)]
    [InlineData(Hop.Under, Stranger.Deleted)]
    [InlineData(Hop.Under, Stranger.ScopeSomeWithoutTheProject)]
    [InlineData(Hop.Under, Stranger.Unrecorded)]
    [InlineData(Hop.Parent, Stranger.ScopeNone)]
    [InlineData(Hop.Parent, Stranger.ScopeSomeWithoutTheProject)]
    [InlineData(Hop.Parent, Stranger.Unrecorded)]
    [InlineData(Hop.Parent, Stranger.Deleted)]
    public async Task ScanAsync_AParentAcrossTheLineWhoseStarterHasNoSay_CarriesNothingAndGivesTheSentence(Hop hop, Stranger kind)
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var third = await h.AddProjectAsync("THR");
        var starter = await StarterAsync(h, kind, other, third);
        var (parent, child) = await RunningAcrossTheLineAsync(h, hop, other, starter);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), child);

        Assert.False(row.Hop);
        Assert.Null(row.HopKind);
        Assert.Equal(PullSentence(IssueKey.Format("OTH", parent.Number)), row.Blocked);
        Assert.Equal(BlockedKinds.Person, row.BlockedKind);
    }

    [Theory]
    [InlineData(Hop.Epic)]
    [InlineData(Hop.Under)]
    [InlineData(Hop.Parent)]
    public async Task ScanAsync_AParentAcrossTheLineWhoseStarterHasTheProjectInScope_CarriesTheChildAsInsideOneProject(Hop hop)
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        var (_, child) = await RunningAcrossTheLineAsync(h, hop, other, ada.Id);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), child);

        Assert.True(row.Hop);
        Assert.Equal(KindOf(hop), row.HopKind);
        Assert.Null(row.Blocked);
    }

    [Fact]
    public async Task ScanAsync_APullAcrossTheLine_StopsWhenTheStartersScopeIsTakenAwayAndResumesWhenItIsGivenBack()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        var (parent, child) = await RunningAcrossTheLineAsync(h, Hop.Parent, other, ada.Id);

        async Task<ScanRow> ScanAsync() =>
            RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), child);

        Assert.Equal(HopKinds.Parent, (await ScanAsync()).HopKind);

        await h.SetScopeAsync(ada.Id, PersonScopeMode.None);
        var withheld = await ScanAsync();
        Assert.Null(withheld.HopKind);
        Assert.Equal(PullSentence(IssueKey.Format("OTH", parent.Number)), withheld.Blocked);

        h.Db.PersonScopes.RemoveRange(await h.Db.PersonScopes.ToListAsync());
        await h.Db.SaveChangesAsync();
        Assert.Equal(HopKinds.Parent, (await ScanAsync()).HopKind);
    }

    [Fact]
    public async Task MayPull_IntoAProtectedColumn_NeedsTheStarterToOwnOrApproveTheChildsProject()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        var bea = h.Actors.AddPerson("Bea");
        var cal = h.Actors.AddPerson("Cal");
        await h.MemberAsync(h.ProjectId, bea.Id, ProjectMemberRole.Approver);
        await h.MemberAsync(h.ProjectId, cal.Id, ProjectMemberRole.Owner);
        await h.MemberAsync(other, ada.Id, ProjectMemberRole.Owner);
        var statuses = await h.Db.Statuses.AsNoTracking().ToListAsync();
        var plain = statuses.First(s => s.Id == h.InProgress);
        var protectedColumn = new EfHatchStatus { Id = 99, Name = "gated", SortOrder = 35, IsProtected = true };

        var authority = (await h.TreeAsync()).Authority;

        Assert.True(authority.MayPull(ada.Id, h.ProjectId, plain));
        Assert.False(authority.MayPull(ada.Id, h.ProjectId, protectedColumn));
        Assert.True(authority.MayPull(bea.Id, h.ProjectId, protectedColumn));
        Assert.True(authority.MayPull(cal.Id, h.ProjectId, protectedColumn));
        Assert.False(authority.MayPull(null, h.ProjectId, plain));
    }

    [Fact]
    public async Task ScanAsync_AParentAcrossTheLineThatHasNotStarted_FallsThroughToTheOrdinaryHold()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.TickParentPullsAsync(h.Todo);
        var epic = await h.FileAsync("epic", "waiting", h.Todo, projectId: other);
        var task = await h.FileAsync("task", "T", h.Todo, parentId: epic.Id);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), task);

        Assert.Null(row.HopKind);
        Assert.Equal(ParentNotStartedSentence, row.Blocked);
        Assert.Equal(BlockedKinds.Family, row.BlockedKind);
    }

    [Theory]
    [InlineData(Hop.Epic, false)]
    [InlineData(Hop.Epic, true)]
    [InlineData(Hop.Under, false)]
    [InlineData(Hop.Under, true)]
    [InlineData(Hop.Parent, false)]
    [InlineData(Hop.Parent, true)]
    public async Task ScanAsync_InsideOneProject_AParentStartedByNobodyOrByAPersonWithNoRole_PullsAsBefore(Hop hop, bool roleless)
    {
        var h = await NewAsync();
        var starter = roleless ? h.Actors.AddPerson("Ada").Id : (Guid?)null;
        var (_, child) = await RunningAcrossTheLineAsync(h, hop, h.ProjectId, starter);

        var row = RowFor(await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default), child);

        Assert.Equal(KindOf(hop), row.HopKind);
        Assert.Null(row.Blocked);
    }

    [Fact]
    public async Task ToDtosAsync_PullPassedOver_NamesTheParentOfAWithheldChildAndIsNullOtherwise()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var ada = h.Actors.AddPerson("Ada");
        var (parent, held) = await RunningAcrossTheLineAsync(h, Hop.Parent, other, null);
        var inside = await h.FileAsync("task", "same project", h.Todo);
        var honouredParent = await h.FileAsync("epic", "also running", h.InProgress, projectId: other);
        honouredParent.StartedBy = ada.Id;
        var honoured = await h.FileAsync("task", "honoured", h.Todo, parentId: honouredParent.Id);
        var notStarted = await h.FileAsync("epic", "waiting", h.Todo, projectId: other);
        var early = await h.FileAsync("task", "early", h.Todo, parentId: notStarted.Id);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var rows = await h.Db.Issues.ToListAsync();
        var dtos = await IssueProjection.ToDtosAsync(h.Db, h.Actors, rows, TestClaims.With(), Now, default);

        Assert.Equal(IssueKey.Format("OTH", parent.Number), dtos[held.Id].PullPassedOver);
        Assert.Null(dtos[inside.Id].PullPassedOver);
        Assert.Null(dtos[honoured.Id].PullPassedOver);
        Assert.Null(dtos[early.Id].PullPassedOver);
        Assert.Null(dtos[parent.Id].PullPassedOver);
    }

    [Fact]
    public async Task ScanAsync_AChildInAnotherProjectThatIsNotDone_HoldsTheEpicOutOfCloseWhoeverAsks()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        var epic = await h.FileAsync("epic", "E", h.Review);
        await h.PreApprovedAsync(epic);
        await h.FileAsync("story", "open, elsewhere", h.InProgress, projectId: other, parentId: epic.Id);
        var statuses = await h.Db.Statuses.OrderBy(s => s.SortOrder).ToListAsync();

        var everyone = await FamilyGate.ForAsync(h.Db, new StubActorDirectory(), statuses, default);
        var scoped = await FamilyGate.ForAsync(ScopeSeed.Request(h.Options, ScopeSeed.Only(h.ProjectId)), new StubActorDirectory(), statuses, default);

        Assert.NotNull(everyone.EpicFold(epic.Id, statuses));
        Assert.Equal(everyone.EpicFold(epic.Id, statuses), scoped.EpicFold(epic.Id, statuses));
        Assert.Single(scoped.EpicOpenChildren(epic.Id, statuses));

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);
        Assert.NotEqual(HopKinds.Close, RowFor(scan, epic).HopKind);
    }

    [Fact]
    public async Task ScanAsync_AStoryFiledUnderAnEpicInAnotherProject_IsMatchedAndPausedByItsOwnProject()
    {
        var h = await NewAsync();
        var other = await h.AddProjectAsync("OTH");
        await h.AddPlaybookAsync(h.Todo, h.InProgress, projectId: h.ProjectId, model: "sonnet");
        var epic = await h.FileAsync("epic", "E", h.InProgress, projectId: other);
        var story = await h.FileAsync("story", "S", h.InProgress, parentId: epic.Id);
        await h.AddPlaybookAsync(h.InProgress, h.Review, projectId: h.ProjectId);

        var scan = await h.Dispatch.ScanAsync(0, null, null, RepositoryDeclaration.Undeclared, false, default);

        var row = RowFor(scan, story);
        Assert.Null(row.Blocked);
        Assert.Equal(h.Review, row.To!.Id);
    }

    // ---- Helpers ----

    private static PlaybookFileRow FileRow(
        int from, int to, IReadOnlyList<string>? types = null, string shape = "any", string model = "sonnet",
        int? quietLimitSeconds = null, int? maxRunSeconds = null) =>
        new(from, to, types ?? [], shape, "do the thing", model, "high", null, null, [], false, quietLimitSeconds, maxRunSeconds);

    private static ScanRow RowFor(Scan scan, EfHatchIssue issue) =>
        scan.Rows.Single(r => r.Issue.Id == issue.Id);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required DbContextOptions<HatchContext> Options { get; init; }
        public required Dispatch Dispatch { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required int ProjectId { get; init; }
        public required string ProjectKey { get; init; }
        public required int Inbox { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }

        private int next = 1;
        private int nextRepoOrder;

        public async Task<EfHatchIssue> FileAsync(string type, string title, int statusId, long rank = 1024, int? projectId = null, long? parentId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = projectId ?? ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = rank,
                ParentId = parentId,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>One issue made to wait on another, written straight to the table.</summary>
        public async Task DependsAsync(EfHatchIssue issue, EfHatchIssue blocker)
        {
            Db.Dependencies.Add(new EfHatchIssueDependency
            {
                IssueId = issue.Id,
                DependsOnId = blocker.Id,
                CreatedBy = "hatch-agent",
                CreatedAt = Now,
            });

            await Db.SaveChangesAsync();
        }

        public async Task<int> AddProjectAsync(string key)
        {
            var project = new EfHatchProject { Key = key, Name = key, CreatedAt = Now };
            Db.Add(project);
            await Db.SaveChangesAsync();
            return project.Id;
        }

        public async Task SetRepoSourcedAsync(bool on = true, int? projectId = null)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == (projectId ?? ProjectId));
            project.RepoSourced = on;
            await Db.SaveChangesAsync();
        }

        public async Task RequireTagsAsync(params string[] tags)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            project.RunnerTags = RunnerTagColumn.Write(tags);
            await Db.SaveChangesAsync();
        }

        /// <summary>An issue's own required tags, written straight to its row - see <see cref="RequireTagsAsync"/> for the project's.</summary>
        public async Task TagAsync(EfHatchIssue issue, params string[] tags)
        {
            issue.RunnerTags = RunnerTagColumn.Write(tags);
            await Db.SaveChangesAsync();
        }

        /// <summary>Turns the WIP section on with no limits - what an epic hop needs to be seen entering it.</summary>
        public async Task TickWipAsync(params int[] statusIds)
        {
            foreach (var id in statusIds)
            {
                var status = await Db.Statuses.FirstAsync(s => s.Id == id);
                status.IsWip = true;
            }

            await Db.SaveChangesAsync();
        }

        public async Task ExpressAsync(EfHatchIssue issue)
        {
            issue.PreApproval = PreApprovalLevels.Express;
            await Db.SaveChangesAsync();
        }

        public async Task TickParentPullsAsync(int statusId)
        {
            var status = await Db.Statuses.FirstAsync(s => s.Id == statusId);
            status.ParentPulls = true;
            await Db.SaveChangesAsync();
        }

        public async Task TickExpressSkipsAsync(int statusId)
        {
            var status = await Db.Statuses.FirstAsync(s => s.Id == statusId);
            status.ExpressSkips = true;
            await Db.SaveChangesAsync();
        }

        /// <summary>A column's own required tags, written straight to its row - see <see cref="RequireTagsAsync"/> for the project's.</summary>
        public async Task TagColumnAsync(int statusId, params string[] tags)
        {
            var status = await Db.Statuses.FirstAsync(s => s.Id == statusId);
            status.RunnerTags = RunnerTagColumn.Write(tags);
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A runner's verdict, written straight to the table. A project binding
        /// no repository counts every verdict, so any remote will do.
        /// </summary>
        public async Task AddMergeCheckAsync(EfHatchIssue issue, string verdict)
        {
            var remote = "https://example.com/o/r";
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical!,
                Trunk = "main",
                TrunkSha = new string('a', 40),
                Verdict = verdict,
                Branch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? "aer-1-thing" : null,
                BranchSha = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? new string('b', 40) : null,
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>A build verdict on the sha <see cref="AddMergeCheckAsync"/> gives a clean branch.</summary>
        public async Task AddBuildCheckAsync(EfHatchIssue issue, string verdict)
        {
            var remote = "https://example.com/o/r";
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical!,
                Branch = "aer-1-thing",
                Sha = new string('b', 40),
                ShaSince = Now,
                Verdict = verdict,
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        public async Task AddContributionAsync(EfHatchIssue issue, string? pullRequestUrl)
        {
            Db.Contributions.Add(new EfHatchContribution
            {
                IssueId = issue.Id, PullRequestUrl = pullRequestUrl, HeadBranch = "feature/thing",
                AuthorLogin = "octocat", AuthorAssociation = "NONE", FiledAt = Now, FiledBy = "runner-1",
            });

            await Db.SaveChangesAsync();
        }

        public async Task RunnersWorkContributionsAsync()
        {
            (await Db.Projects.SingleAsync(p => p.Id == ProjectId)).RunnersWorkContributions = true;
            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchProjectRepository> AddRepositoryAsync(
            string remote = "https://example.com/o/r", string? baseBranch = null, int? projectId = null)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            var repo = new EfHatchProjectRepository
            {
                ProjectId = projectId ?? ProjectId,
                Remote = remote,
                Canonical = canonical!,
                BaseBranch = baseBranch,
                SortOrder = nextRepoOrder++,
                CreatedAt = Now,
            };
            Db.Add(repo);
            await Db.SaveChangesAsync();
            return repo;
        }

        public async Task<EfHatchPlaybookFile> PlaybookFileAsync(
            EfHatchProjectRepository repo, string trunk, string state,
            IReadOnlyList<PlaybookFileRow>? rows = null, string? problem = null, string? sha = null)
        {
            var file = new EfHatchPlaybookFile
            {
                Remote = repo.Remote,
                Canonical = repo.Canonical,
                Trunk = trunk,
                Sha = sha ?? new string('a', 40),
                State = state,
                Rows = rows is null ? null : EfHatchPlaybookFile.WriteRows(rows),
                Problem = problem,
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            };

            Db.PlaybookFiles.Add(file);
            await Db.SaveChangesAsync();
            return file;
        }

        public async Task AddPlaybookAsync(
            int from, int to, string types = "", string model = "sonnet", string shape = "any",
            int? briefLimit = null, int? projectId = null, bool isCheck = false)
        {
            Db.Playbooks.Add(new EfHatchPlaybook
            {
                FromStatusId = from,
                ToStatusId = to,
                Types = types,
                Shape = shape,
                Prompt = "do the thing",
                Model = model,
                Effort = "high",
                BriefLimit = briefLimit,
                ProjectId = projectId,
                IsCheck = isCheck,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        public async Task SetCheckApprovesMergeAsync(bool on = true)
        {
            var project = await Db.Projects.SingleAsync(p => p.Id == ProjectId);
            project.CheckApprovesMerge = on;
            await Db.SaveChangesAsync();
        }

        /// <summary>A role on a project, written straight to the table.</summary>
        public async Task MemberAsync(int projectId, Guid personId, string role)
        {
            Db.ProjectMembers.Add(new EfHatchProjectMember { ProjectId = projectId, PersonId = personId, Role = role, CreatedAt = Now });
            await Db.SaveChangesAsync();
        }

        /// <summary>A level and the person who set it, written straight to the row. A null person is a level no one recorded.</summary>
        public async Task SetLevelAsync(EfHatchIssue issue, int preApproval, Guid? setter, int? priority = null)
        {
            issue.PreApproval = preApproval;
            issue.PreApprovalSetBy = preApproval == PreApprovalLevels.None ? null : setter;
            if (priority is { } p)
            {
                issue.Priority = p;
                issue.PrioritySetBy = setter;
            }

            await Db.SaveChangesAsync();
        }

        public async Task SetScopeAsync(Guid personId, string mode, params int[] projects)
        {
            Db.PersonScopes.Add(new EfHatchPersonScope { PersonId = personId, Mode = mode, UpdatedBy = "test", UpdatedAt = Now });
            Db.PersonScopeProjects.AddRange(projects.Select(id => new EfHatchPersonScopeProject { PersonId = personId, ProjectId = id }));
            await Db.SaveChangesAsync();
        }

        public Task<PriorityTree> TreeAsync() => PriorityTree.ForAsync(Db, Actors, default);

        public async Task PreApprovedAsync(EfHatchIssue issue)
        {
            issue.PreApproval = PreApprovalLevels.PreApproved;
            await Db.SaveChangesAsync();
        }
    }

    // ---- ModelFold: a pinned id is judged against the runner's own list ----

    private static string? FoldPinned(string pinned, ModelDeclaration declared)
    {
        var issue = new EfHatchIssue { ProjectId = 1, Number = 7, Type = "task", Title = "pinned", StatusId = 1, Rank = 1, CreatedBy = "test", CreatedAt = Now, UpdatedAt = Now, Project = new EfHatchProject { Key = "AER", Name = "Aerie", CreatedAt = Now }, ModelOverride = pinned };
        return Dispatch.ModelFold(issue, new EfHatchStatus { Name = "todo", SortOrder = 20 }, null, declared);
    }

    [Fact]
    public void ModelFold_APinnedIdTheRunnerLists_IsNotFolded() =>
        Assert.Null(FoldPinned("alpha:1b", ModelDeclaration.Of(["alpha:1b", "beta:2b"], "ollama")));

    [Fact]
    public void ModelFold_APinnedIdMissingFromANonEmptyList_IsFoldedWithTheNotOfferedSentence() =>
        Assert.Equal(
            ModelFamilies.NotOffered("AER-7", "todo", "alpha:1b", "ollama"),
            FoldPinned("alpha:1b", ModelDeclaration.Of(["beta:2b"], "ollama")));

    [Fact]
    public void ModelFold_AnEmptyList_IsJudgedByAgentAlone() =>
        Assert.Null(FoldPinned("alpha:1b", ModelDeclaration.Of([], "ollama")));

    [Fact]
    public void ModelFold_AListOfAliasesOnlyOfTheSameAgent_IsJudgedByAgentAlone()
    {
        Assert.Null(FoldPinned("claude-made-up-1", ModelDeclaration.Of(["haiku", "sonnet"], "claude")));
        Assert.Null(FoldPinned("alpha:1b", ModelDeclaration.Of(["haiku"], "ollama")));
    }

    [Fact]
    public void ModelFold_APinnedIdOfAnotherAgent_KeepsTheWrongAgentSentence() =>
        Assert.Equal(
            ModelFamilies.Default.WrongAgent("alpha:1b", "claude"),
            FoldPinned("alpha:1b", ModelDeclaration.Of(["beta:2b"], "claude")));

    [Fact]
    public void ModelFold_ARunnerWithNoAgent_IsNotFoldedOnAPinnedId() =>
        Assert.Null(FoldPinned("alpha:1b", ModelDeclaration.Of(["beta:2b"], null)));


    // ---- ResolveModel and the local model: a required ticket runs a row's local model, and nowhere else ----

    private static EfHatchIssue ModelIssue(string? modelOverride = null) =>
        new() { ProjectId = 1, Number = 7, Type = "task", Title = "local", StatusId = 1, Rank = 1, CreatedBy = "test", CreatedAt = Now, UpdatedAt = Now, Project = new EfHatchProject { Key = "AER", Name = "Aerie", CreatedAt = Now }, ModelOverride = modelOverride };

    private static EfHatchPlaybook ModelRow(string model, string? localModel) =>
        new() { FromStatusId = 1, ToStatusId = 2, Types = "", Shape = "any", Model = model, Effort = "high", LocalModel = localModel, CreatedAt = Now, UpdatedAt = Now };

    private static string? FoldRequired(ModelDeclaration declared) =>
        Dispatch.ModelFold(ModelIssue(), new EfHatchStatus { Name = "todo", SortOrder = 20 }, ModelRow("sonnet", "alpha:1b"), declared, LocalModelLevels.Require);

    [Fact]
    public void ResolveModel_AnOverride_BeatsRequire() =>
        Assert.Equal(("opus", false), Dispatch.ResolveModel(ModelIssue("opus"), ModelRow("sonnet", "alpha:1b"), LocalModelLevels.Require));

    [Fact]
    public void ResolveModel_RequireWithARowLocalModel_ResolvesToIt() =>
        Assert.Equal(("alpha:1b", true), Dispatch.ResolveModel(ModelIssue(), ModelRow("sonnet", "alpha:1b"), LocalModelLevels.Require));

    [Fact]
    public void ResolveModel_RequireWithNoRowLocalModel_ResolvesToTheRowsModel()
    {
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), ModelRow("sonnet", null), LocalModelLevels.Require));
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), ModelRow("sonnet", "  "), LocalModelLevels.Require));
    }

    [Theory]
    [InlineData(LocalModelLevels.None)]
    [InlineData(LocalModelLevels.Prefer)]
    public void ResolveModel_NoneAndPrefer_LeaveTheRowsModel(int level) =>
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), ModelRow("sonnet", "alpha:1b"), level));

    [Fact]
    public void ResolveModel_NoPlaybook_NamesNoModel() =>
        Assert.Equal((null, false), Dispatch.ResolveModel(ModelIssue(), null, LocalModelLevels.Require));

    [Fact]
    public void ModelFold_ARequiredMove_OnACloudRunner_IsFoldedWithTheLocalRequiredSentence() =>
        Assert.Equal(
            ModelFamilies.LocalRequired("AER-7", "todo", "alpha:1b", "claude"),
            FoldRequired(ModelDeclaration.Of(["haiku", "sonnet"], "claude")));

    [Fact]
    public void ModelFold_ARequiredMove_OnARunnerListingTheId_IsNotFolded() =>
        Assert.Null(FoldRequired(ModelDeclaration.Of(["alpha:1b", "beta:2b"], "ollama")));

    [Fact]
    public void ModelFold_ARequiredMove_OnARunnerOfTheAgentLackingTheId_IsFoldedAsNotOffered() =>
        Assert.Equal(
            ModelFamilies.NotOffered("AER-7", "todo", "alpha:1b", "ollama"),
            FoldRequired(ModelDeclaration.Of(["beta:2b"], "ollama")));

    [Fact]
    public void ModelFold_WithoutRequire_IgnoresTheRowsLocalModel() =>
        Assert.Null(Dispatch.ModelFold(
            ModelIssue(), new EfHatchStatus { Name = "todo", SortOrder = 20 }, ModelRow("sonnet", "alpha:1b"),
            ModelDeclaration.Of(["haiku", "sonnet"], "claude"), LocalModelLevels.Prefer));

    [Fact]
    public void RefusalFold_ARequiredMove_ReadsTheLocalModel()
    {
        var refusal = new RunnerRefusal("night", ["alpha:1b"]);
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        Assert.NotNull(Dispatch.RefusalFold(ModelIssue(), todo, ModelRow("sonnet", "alpha:1b"), refusal, LocalModelLevels.Require));
        Assert.Null(Dispatch.RefusalFold(ModelIssue(), todo, ModelRow("sonnet", "alpha:1b"), refusal));
    }

    // ---- A preferred local model: the asker that can run it gets it, and the rest are left for a free one ----

    private static readonly EfHatchStatus TodoColumn = new() { Name = "todo", SortOrder = 20 };

    [Fact]
    public void ResolveModel_Prefer_AnOllamaAskerListingTheId_ResolvesToIt() =>
        Assert.Equal(("alpha:1b", true), Dispatch.ResolveModel(
            ModelIssue(), ModelRow("sonnet", "alpha:1b"), LocalModelLevels.Prefer, ModelDeclaration.Of(["alpha:1b"], "ollama")));

    [Fact]
    public void ResolveModel_Prefer_ACloudAsker_AnAskerLackingTheId_OrNoAsker_ResolvesToTheRowsModel()
    {
        var row = ModelRow("sonnet", "alpha:1b");
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), row, LocalModelLevels.Prefer, ModelDeclaration.Of(["haiku", "sonnet"], "claude")));
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), row, LocalModelLevels.Prefer, ModelDeclaration.Of(["beta:2b"], "ollama")));
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), row, LocalModelLevels.Prefer, ModelDeclaration.Unjudged));
        Assert.Equal(("sonnet", false), Dispatch.ResolveModel(ModelIssue(), row, LocalModelLevels.Prefer));
    }

    [Fact]
    public void ResolveModel_Prefer_AnOverrideBeatsTheLocalModel() =>
        Assert.Equal(("opus", false), Dispatch.ResolveModel(
            ModelIssue("opus"), ModelRow("sonnet", "alpha:1b"), LocalModelLevels.Prefer, ModelDeclaration.Of(["alpha:1b"], "ollama")));

    [Fact]
    public void ModelFold_Prefer_WithAFreeRunner_FoldsACloudAskerWithTheLeftForSentence() =>
        Assert.Equal(
            ModelFamilies.LeftForLocal("AER-7", "todo", "gpu", "alpha:1b"),
            Dispatch.ModelFold(
                ModelIssue(), TodoColumn, ModelRow("sonnet", "alpha:1b"), ModelDeclaration.Of(["haiku", "sonnet"], "claude"),
                LocalModelLevels.Prefer, ("gpu", "alpha:1b")));

    [Fact]
    public void ModelFold_Prefer_WithAFreeRunner_NeverFoldsAnOllamaAskerListingTheId() =>
        Assert.Null(Dispatch.ModelFold(
            ModelIssue(), TodoColumn, ModelRow("sonnet", "alpha:1b"), ModelDeclaration.Of(["alpha:1b"], "ollama"),
            LocalModelLevels.Prefer, ("gpu", "alpha:1b")));

    [Fact]
    public void ModelFold_Prefer_WithNoFreeRunner_LeavesACloudAskerAlone() =>
        Assert.Null(Dispatch.ModelFold(
            ModelIssue(), TodoColumn, ModelRow("sonnet", "alpha:1b"), ModelDeclaration.Of(["haiku", "sonnet"], "claude"),
            LocalModelLevels.Prefer));

    [Fact]
    public void ModelFold_FreeRunner_IsReadOnlyAtPrefer_AndNeverAgainstAnOverride()
    {
        var cloud = ModelDeclaration.Of(["haiku", "sonnet"], "claude");
        Assert.Null(Dispatch.ModelFold(ModelIssue(), TodoColumn, ModelRow("sonnet", "alpha:1b"), cloud, LocalModelLevels.None, ("gpu", "alpha:1b")));
        Assert.Null(Dispatch.ModelFold(ModelIssue("haiku"), TodoColumn, ModelRow("sonnet", "alpha:1b"), cloud, LocalModelLevels.Prefer, ("gpu", "alpha:1b")));
    }

    [Theory]
    [InlineData(LocalModelLevels.Prefer, "alpha:1b", null, false, true)]
    [InlineData(LocalModelLevels.Prefer, "alpha:1b", null, true, false)]
    [InlineData(LocalModelLevels.Prefer, "alpha:1b", "opus", false, false)]
    [InlineData(LocalModelLevels.Prefer, null, null, false, false)]
    [InlineData(LocalModelLevels.Require, "alpha:1b", null, false, false)]
    [InlineData(LocalModelLevels.None, "alpha:1b", null, false, false)]
    public void FellBack_IsTrueOnlyForAPreferringMoveRunningOnTheRowsModel(int level, string? localModel, string? modelOverride, bool local, bool expected) =>
        Assert.Equal(expected, Dispatch.FellBack(ModelIssue(modelOverride), ModelRow("sonnet", localModel), level, local));

    [Fact]
    public void RunsLocally_NeedsAJudgedRunnerOfTheIdsAgentListingTheId()
    {
        Assert.True(Dispatch.RunsLocally(ModelDeclaration.Of(["alpha:1b"], "ollama"), "alpha:1b"));
        Assert.False(Dispatch.RunsLocally(ModelDeclaration.Of(["alpha:1b"], "claude"), "alpha:1b"));
        Assert.False(Dispatch.RunsLocally(ModelDeclaration.Of(["beta:2b"], "ollama"), "alpha:1b"));
        Assert.False(Dispatch.RunsLocally(ModelDeclaration.Of(["alpha:1b"], null), "alpha:1b"));
        Assert.False(Dispatch.RunsLocally(ModelDeclaration.Unjudged, "alpha:1b"));
        Assert.False(Dispatch.RunsLocally(null, "alpha:1b"));
    }

    private static async Task<Harness> NewAsync()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, inbox, todo, doing, review, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Options = options,
            Actors = actors,
            Dispatch = new Dispatch(db, actors, TestClaims.With(), time),
            ProjectId = project.Id,
            ProjectKey = project.Key,
            Inbox = inbox.Id,
            Todo = todo.Id,
            InProgress = doing.Id,
            Review = review.Id,
            Done = done.Id,
        };
    }
}
