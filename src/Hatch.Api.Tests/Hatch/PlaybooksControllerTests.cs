using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one numeric field a row carries: set, read back, and cleared the same
/// way <see cref="IssueWipLimitControllerTests"/> exercises a wip limit. That
/// a key cannot write to a playbook at all is
/// <c>AdminSurfaceTests</c>'s business already, so it is not repeated here -
/// this is only the parse.
/// </summary>
public class PlaybooksControllerTests
{
    // ---- Setting, changing, clearing ----

    [Fact]
    public async Task ABudget_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, "5");

        Assert.Equal(5, patched.Budget);
        Assert.Equal(5, (await h.ReadAsync(row.Id)).Budget);
    }

    [Fact]
    public async Task AnEmptyString_ClearsIt()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, "5");

        var cleared = await h.SetAsync(row.Id, "");

        Assert.Null(cleared.Budget);
    }

    [Fact]
    public async Task Whitespace_ClearsIt_TheSameAsEmpty()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, "5");

        var cleared = await h.SetAsync(row.Id, "   ");

        Assert.Null(cleared.Budget);
    }

    [Fact]
    public async Task PatchingSomethingElse_WithBudgetOmitted_LeavesItUnchanged()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, budget: "5");

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal("a new prompt", patched.Prompt);
        Assert.Equal(5, patched.Budget);
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task ABadValue_IsRefused_AndWritesNothing(string bad)
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, bad), default);

        Assert.Equal(
            $"a budget is a whole number of one or more, in millions of tokens - not \"{bad}\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).Budget);
    }

    // ---- Create ----

    [Fact]
    public async Task ABudget_CanBeSetOnCreate()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync(budget: "5");

        Assert.Equal(5, created.Budget);
    }

    [Fact]
    public async Task ANullBudgetOnCreate_MeansNoBudget()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync();

        Assert.Null(created.Budget);
    }

    [Fact]
    public async Task ABadBudgetOnCreate_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", "abc"), default);

        Assert.Equal(
            "a budget is a whole number of one or more, in millions of tokens - not \"abc\"",
            Reason(refusal.Result));
    }

    // ---- BriefLimit: setting, changing, clearing ----

    [Fact]
    public async Task ABriefLimit_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, briefLimit: "5");

        Assert.Equal(5, patched.BriefLimit);
        Assert.Equal(5, (await h.ReadAsync(row.Id)).BriefLimit);
    }

    [Fact]
    public async Task ABriefLimit_AnEmptyString_ClearsIt()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, briefLimit: "5");

        var cleared = await h.SetAsync(row.Id, briefLimit: "");

        Assert.Null(cleared.BriefLimit);
    }

    [Fact]
    public async Task ABriefLimit_Whitespace_ClearsIt_TheSameAsEmpty()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, briefLimit: "5");

        var cleared = await h.SetAsync(row.Id, briefLimit: "   ");

        Assert.Null(cleared.BriefLimit);
    }

    [Fact]
    public async Task PatchingSomethingElse_WithBriefLimitOmitted_LeavesItUnchanged()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, briefLimit: "5");

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal("a new prompt", patched.Prompt);
        Assert.Equal(5, patched.BriefLimit);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task ABriefLimit_ABadValue_IsRefused_AndWritesNothing(string bad)
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, null, bad), default);

        Assert.Equal(
            $"a brief limit is a whole number of one or more, in characters - not \"{bad}\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).BriefLimit);
    }

    // ---- BriefLimit: create ----

    [Fact]
    public async Task ABriefLimit_CanBeSetOnCreate()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync(briefLimit: "5");

        Assert.Equal(5, created.BriefLimit);
    }

    [Fact]
    public async Task ANullBriefLimitOnCreate_MeansNoBriefLimit()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync();

        Assert.Null(created.BriefLimit);
    }

    [Fact]
    public async Task ABadBriefLimitOnCreate_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", null, "abc"), default);

        Assert.Equal(
            "a brief limit is a whole number of one or more, in characters - not \"abc\"",
            Reason(refusal.Result));
    }

    // ---- The one edge that is cut ----

    [Fact]
    public void DeniedTools_IsClosedToAnApiKey()
    {
        var guard = typeof(PlaybooksController)
            .GetMethod(nameof(PlaybooksController.PatchPlaybook))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);

        Assert.Empty(typeof(PlaybooksController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public void IsCheck_IsClosedToAnApiKey()
    {
        var guard = typeof(PlaybooksController)
            .GetMethod(nameof(PlaybooksController.PatchPlaybook))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);

        Assert.Empty(typeof(PlaybooksController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    // ---- Project ----

    [Fact]
    public async Task ABoardWideRow_AndAProjectRow_OnTheSameTransitionTypesAndShape_AreBothAccepted()
    {
        var h = await NewAsync();
        await h.CreateAsync();

        var created = await h.CreateAsync(projectId: h.ProjectA.ToString());

        Assert.Equal(h.ProjectA, created.ProjectId);
    }

    [Fact]
    public async Task TwoRows_ForTheSameProject_OnTheSameTransitionTypesAndShape_AreRefused()
    {
        var h = await NewAsync();
        await h.CreateAsync(projectId: h.ProjectA.ToString());

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", ProjectId: h.ProjectA.ToString()), default);

        Assert.Equal(
            "there is already a playbook for that transition, those types and that shape",
            Reason(refusal.Result));
    }

    [Fact]
    public async Task ANullProjectIdOnPatch_LeavesItAlone()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync(projectId: h.ProjectA.ToString());

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal(h.ProjectA, patched.ProjectId);
    }

    [Fact]
    public async Task AnEmptyProjectId_ClearsItBackToBoardWide()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync(projectId: h.ProjectA.ToString());

        var cleared = await h.SetAsync(row.Id, projectId: "");

        Assert.Null(cleared.ProjectId);
    }

    [Fact]
    public async Task AParsedProjectId_SetsIt()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, projectId: h.ProjectA.ToString());

        Assert.Equal(h.ProjectA, patched.ProjectId);
    }

    [Fact]
    public async Task ANonexistentProjectId_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, ProjectId: "999"), default);

        Assert.Equal("there is no project 999", Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).ProjectId);
    }

    [Fact]
    public async Task ABadProjectId_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, ProjectId: "abc"), default);

        Assert.Equal(
            "a project is a whole number of one or more - not \"abc\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).ProjectId);
    }

    [Fact]
    public async Task ACreateNamingARepoSourcedProject_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", ProjectId: h.ProjectRepoSourced.ToString()), default);

        Assert.Equal(
            "project R reads its playbooks from .hatch/playbooks.yaml on its trunk, not from a row here",
            Reason(refusal.Result));
    }

    [Fact]
    public async Task APatchSettingProjectIdToARepoSourcedProject_IsRefused()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, ProjectId: h.ProjectRepoSourced.ToString()), default);

        Assert.Equal(
            "project R reads its playbooks from .hatch/playbooks.yaml on its trunk, not from a row here",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).ProjectId);
    }

    [Fact]
    public async Task APatchSettingProjectIdToARepoSourcedProject_FromADifferentProject_IsRefused()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync(projectId: h.ProjectA.ToString());

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, ProjectId: h.ProjectRepoSourced.ToString()), default);

        Assert.Equal(
            "project R reads its playbooks from .hatch/playbooks.yaml on its trunk, not from a row here",
            Reason(refusal.Result));
        Assert.Equal(h.ProjectA, (await h.ReadAsync(row.Id)).ProjectId);
    }

    [Fact]
    public async Task APatchClearingProjectIdOnARepoSourcedRow_Succeeds()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var tracked = await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id);
        tracked.ProjectId = h.ProjectRepoSourced;
        await h.Db.SaveChangesAsync();

        var cleared = await h.SetAsync(row.Id, projectId: "");

        Assert.Null(cleared.ProjectId);
    }

    // ---- DeniedTools ----

    [Fact]
    public async Task ADeniedTools_CanBeSetOnCreate()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync(deniedTools: ["Bash"]);

        Assert.Equal(["Bash"], created.DeniedTools);
    }

    [Fact]
    public async Task ANullDeniedToolsOnCreate_MeansNoDeniedTools()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync();

        Assert.Empty(created.DeniedTools);
    }

    [Fact]
    public async Task ADeniedTools_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, deniedTools: ["Bash", "WebFetch"]);

        Assert.Equal(["Bash", "WebFetch"], patched.DeniedTools);
        Assert.Equal(["Bash", "WebFetch"], (await h.ReadAsync(row.Id)).DeniedTools);
    }

    [Fact]
    public async Task AnEmptyList_ClearsDeniedTools()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, deniedTools: ["Bash"]);

        var cleared = await h.SetAsync(row.Id, deniedTools: []);

        Assert.Empty(cleared.DeniedTools);
    }

    [Fact]
    public async Task PatchingSomethingElse_WithDeniedToolsOmitted_LeavesItUnchanged()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, deniedTools: ["Bash"]);

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal("a new prompt", patched.Prompt);
        Assert.Equal(["Bash"], patched.DeniedTools);
    }

    [Fact]
    public async Task ATooManyDeniedTools_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var tooMany = Enumerable.Range(0, EfHatchPlaybook.MaxDeniedTools + 1).Select(i => $"Tool{i}").ToList();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, DeniedTools: tooMany), default);

        Assert.Equal(
            $"a playbook denies at most {EfHatchPlaybook.MaxDeniedTools} tools - not {tooMany.Count}",
            Reason(refusal.Result));
        Assert.Empty((await h.ReadAsync(row.Id)).DeniedTools);
    }

    [Fact]
    public async Task ADeniedToolRuleTooLong_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var tooLong = new string('a', EfHatchPlaybook.MaxToolRuleLength + 1);

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, DeniedTools: [tooLong]), default);

        Assert.Equal(
            $"a denied tool rule is at most {EfHatchPlaybook.MaxToolRuleLength} characters",
            Reason(refusal.Result));
        Assert.Empty((await h.ReadAsync(row.Id)).DeniedTools);
    }

    [Fact]
    public async Task AnUnknownToolName_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, DeniedTools: ["Frobnicate"]), default);

        Assert.Equal(
            "a tool rule is a built-in name, \"mcp__…\", or \"*\", each optionally followed by one (pattern) - not \"Frobnicate\"",
            Reason(refusal.Result));
        Assert.Empty((await h.ReadAsync(row.Id)).DeniedTools);
    }

    [Fact]
    public async Task AMalformedPattern_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, DeniedTools: ["Bash(git push *"]), default);

        Assert.Equal(
            "a tool rule is a built-in name, \"mcp__…\", or \"*\", each optionally followed by one (pattern) - not \"Bash(git push *\"",
            Reason(refusal.Result));
        Assert.Empty((await h.ReadAsync(row.Id)).DeniedTools);
    }

    [Fact]
    public async Task AnUnknownToolNameOnCreate_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", DeniedTools: ["Frobnicate"]), default);

        Assert.Equal(
            "a tool rule is a built-in name, \"mcp__…\", or \"*\", each optionally followed by one (pattern) - not \"Frobnicate\"",
            Reason(refusal.Result));
    }

    [Fact]
    public async Task AMalformedPatternOnCreate_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", DeniedTools: ["Bash(git push *"]), default);

        Assert.Equal(
            "a tool rule is a built-in name, \"mcp__…\", or \"*\", each optionally followed by one (pattern) - not \"Bash(git push *\"",
            Reason(refusal.Result));
    }

    // ---- IsCheck ----

    [Fact]
    public async Task AnIsCheck_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync(isCheck: true);
        Assert.True(row.IsCheck);

        var toFalse = await h.SetAsync(row.Id, isCheck: false);
        Assert.False(toFalse.IsCheck);
        Assert.False((await h.ReadAsync(row.Id)).IsCheck);

        var toTrue = await h.SetAsync(row.Id, isCheck: true);
        Assert.True(toTrue.IsCheck);
        Assert.True((await h.ReadAsync(row.Id)).IsCheck);
    }

    // ---- Model families ----

    [Fact]
    public async Task APinnedId_OfASecondFamily_IsRefusedByDefaultAndAcceptedWithItsFamilyListed()
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "fake-1", "high"), default);

        Assert.Equal(
            "a model is one of haiku, sonnet, opus, fable, or a pinned id of a supported agent " +
            "(claude-opus-5 for claude; gpt-6-luna for codex; grok-4.7 for grok; qwen3-coder:30b for ollama) - not \"fake-1\"",
            Reason(refusal.Result));

        var families = new ModelFamilies([
            ModelFamilies.Default.Rows[0],
            new ModelFamily("fake", "^fake-[a-z0-9-]+$", "fake-1"),
        ]);
        var withFamilies = await NewAsync(families);

        var created = Created(await withFamilies.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(withFamilies.Todo, withFamilies.InProgress, [], "do it", "fake-1", "high"), default));

        Assert.Equal("fake-1", created.Model);
    }

    [Fact]
    public async Task APinnedId_OfTheOllamaFamily_IsAccepted()
    {
        var h = await NewAsync();

        var created = Created(await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "qwen3-coder:30b", "high"), default));

        Assert.Equal("qwen3-coder:30b", created.Model);
    }

    // ---- DefaultPrompt ----

    [Fact]
    public async Task ANullPrompt_WithADefaultPromptSet_ReadsBackTheDefaultAsItsPrompt()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var playbook = await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id);
        playbook.Prompt = null;
        playbook.DefaultPrompt = "the prescribed text";
        await h.Db.SaveChangesAsync();

        var read = await h.ReadAsync(row.Id);

        Assert.Equal("the prescribed text", read.Prompt);
        Assert.Equal("the prescribed text", read.DefaultPrompt);
    }

    [Fact]
    public async Task APromptSet_OverADefaultPrompt_ReadsBackTheOverride()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var playbook = await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id);
        playbook.Prompt = "the operator's own words";
        playbook.DefaultPrompt = "the prescribed text";
        await h.Db.SaveChangesAsync();

        var read = await h.ReadAsync(row.Id);

        Assert.Equal("the operator's own words", read.Prompt);
        Assert.Equal("the prescribed text", read.DefaultPrompt);
    }

    // ---- Prompt: patch ----

    [Fact]
    public async Task ANullPrompt_LeavesAnOverrideAlone()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetDefaultPromptAsync(row.Id);

        var patched = await h.SetAsync(row.Id);

        Assert.Equal("do it", patched.Prompt);
        Assert.Equal("do it", (await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id)).Prompt);
    }

    [Fact]
    public async Task AnEmptyPrompt_ClearsTheOverride_AndReadsBackTheDefault()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetDefaultPromptAsync(row.Id);

        var cleared = await h.SetAsync(row.Id, prompt: "");

        Assert.Equal("the prescribed text", cleared.Prompt);
        Assert.Null((await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id)).Prompt);
    }

    [Fact]
    public async Task Whitespace_ClearsTheOverride_TheSameAsEmpty()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetDefaultPromptAsync(row.Id);

        var cleared = await h.SetAsync(row.Id, prompt: "   ");

        Assert.Equal("the prescribed text", cleared.Prompt);
        Assert.Null((await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id)).Prompt);
    }

    [Fact]
    public async Task AnEmptyPrompt_OnARowWithNoDefault_IsRefused_AndWritesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "", null, null), default);

        Assert.Equal(
            "a playbook needs a prompt - it is the whole instruction the agent gets",
            Reason(refusal.Result));
        Assert.Equal("do it", (await h.ReadAsync(row.Id)).Prompt);
    }

    [Fact]
    public async Task APromptEqualToTheDefault_ComesBackAsNoOverride()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetDefaultPromptAsync(row.Id);

        var patched = await h.SetAsync(row.Id, prompt: "the prescribed text");

        Assert.Equal("the prescribed text", patched.Prompt);
        Assert.Null((await h.Db.Playbooks.SingleAsync(p => p.Id == row.Id)).Prompt);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required PlaybooksController Playbooks { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }
        public required int ProjectA { get; init; }
        public required int ProjectRepoSourced { get; init; }

        public async Task<PlaybookDto> CreateAsync(
            string? budget = null, string? briefLimit = null, string? projectId = null, IReadOnlyList<string>? deniedTools = null,
            bool? isCheck = null) =>
            Created(await Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(Todo, InProgress, [], "do it", "sonnet", "high", budget, briefLimit,
                    ProjectId: projectId, DeniedTools: deniedTools, IsCheck: isCheck), default));

        public async Task<PlaybookDto> SetAsync(
            int id, string? budget = null, string? briefLimit = null, string? projectId = null, IReadOnlyList<string>? deniedTools = null,
            bool? isCheck = null, string? prompt = null) =>
            Value(await Playbooks.PatchPlaybook(
                id, new PlaybookPatchRequest(null, null, null, prompt, null, null, budget, briefLimit,
                    ProjectId: projectId, DeniedTools: deniedTools, IsCheck: isCheck), default));

        /// <summary>There is no API to set a prescribed text, so a test sets it on the row.</summary>
        public async Task SetDefaultPromptAsync(int id)
        {
            (await Db.Playbooks.SingleAsync(p => p.Id == id)).DefaultPrompt = "the prescribed text";
            await Db.SaveChangesAsync();
        }

        public async Task<PlaybookDto> ReadAsync(int id) =>
            Value(await Playbooks.GetPlaybooks(default)).Single(p => p.Id == id);
    }

    private static async Task<Harness> NewAsync(ModelFamilies? families = null)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var projectA = new EfHatchProject { Key = "A", Name = "A", CreatedAt = Now };
        var projectRepoSourced = new EfHatchProject { Key = "R", Name = "R", CreatedAt = Now, RepoSourced = true };
        db.AddRange(todo, doing, projectA, projectRepoSourced);
        await db.SaveChangesAsync();

        return new Harness
        {
            Db = db,
            Playbooks = new PlaybooksController(db, new FakeTimeProvider(Now), families),
            Todo = todo.Id,
            InProgress = doing.Id,
            ProjectA = projectA.Id,
            ProjectRepoSourced = projectRepoSourced.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}
