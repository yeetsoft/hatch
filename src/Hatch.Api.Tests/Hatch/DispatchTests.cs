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

        var tree = await PriorityTree.ForAsync(h.Db, default);

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

        var tree = await PriorityTree.ForAsync(h.Db, default);

        var effective = Assert.Single(tree.EffectiveOmittedRunners(task.Id));
        Assert.Equal("gpu-1", effective.Runner);
        Assert.Equal(epicKey, effective.FromKey);
        Assert.Empty(tree.OwnOmittedRunners(task.Id));

        h.Db.Add(new EfHatchIssueRunnerOmission { IssueId = task.Id, RunnerName = "gpu-1", By = "operator", At = Now });
        await h.Db.SaveChangesAsync();

        tree = await PriorityTree.ForAsync(h.Db, default);

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
        public required Dispatch Dispatch { get; init; }
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

        public async Task PreApprovedAsync(EfHatchIssue issue)
        {
            issue.PreApproval = PreApprovalLevels.PreApproved;
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, inbox, todo, doing, review, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);

        return new Harness
        {
            Db = db,
            Dispatch = new Dispatch(db, new StubActorDirectory(), TestClaims.With(), time),
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
