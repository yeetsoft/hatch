using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Microsoft.AspNetCore.Http;
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

    // ---- QuietLimitSeconds: setting, changing, clearing ----

    [Fact]
    public async Task AQuietLimit_IsNullByDefault_OnTheWire()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        Assert.Null(row.QuietLimitSeconds);
        Assert.Null((await h.ReadAsync(row.Id)).QuietLimitSeconds);
    }

    [Fact]
    public async Task AQuietLimit_IsSetAndReadBack()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var patched = await h.SetAsync(row.Id, quietLimitSeconds: "1500");

        Assert.Equal(1500, patched.QuietLimitSeconds);
        Assert.Equal(1500, (await h.ReadAsync(row.Id)).QuietLimitSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AQuietLimit_ABlankValue_ClearsIt(string blank)
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, quietLimitSeconds: "1500");

        var cleared = await h.SetAsync(row.Id, quietLimitSeconds: blank);

        Assert.Null(cleared.QuietLimitSeconds);
    }

    [Fact]
    public async Task PatchingSomethingElse_WithQuietLimitOmitted_LeavesItUnchanged()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        await h.SetAsync(row.Id, quietLimitSeconds: "1500");

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, "a new prompt", null, null), default));

        Assert.Equal("a new prompt", patched.Prompt);
        Assert.Equal(1500, patched.QuietLimitSeconds);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public async Task AQuietLimit_ABadValue_IsRefused_AndWritesNothing(string bad)
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();

        var refusal = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, QuietLimitSeconds: bad), default);

        Assert.Equal(
            $"a quiet limit is a whole number of one or more, in seconds - not \"{bad}\"",
            Reason(refusal.Result));
        Assert.Null((await h.ReadAsync(row.Id)).QuietLimitSeconds);
    }

    // ---- QuietLimitSeconds: create ----

    [Fact]
    public async Task AQuietLimit_CanBeSetOnCreate()
    {
        var h = await NewAsync();

        var created = await h.CreateAsync(quietLimitSeconds: "1500");

        Assert.Equal(1500, created.QuietLimitSeconds);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public async Task ABadQuietLimitOnCreate_IsRefused(string bad)
    {
        var h = await NewAsync();

        var refusal = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", QuietLimitSeconds: bad), default);

        Assert.Equal(
            $"a quiet limit is a whole number of one or more, in seconds - not \"{bad}\"",
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
        Assert.False(guard.AcceptKey);

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
        Assert.False(guard.AcceptKey);

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

    // ---- Who may write ----

    [Fact]
    public async Task AnApiKey_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var playbooks = Writing(h.Db, new StubCallerIdentity { ApiKey = new EfApiKey { Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now } }, browser: true);

        var refusal = await playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, "5"), default);

        AssertRefused(refusal.Result);
        Assert.Null((await h.ReadAsync(row.Id)).Budget);
    }

    [Fact]
    public async Task AKeylessRunner_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var runner = new StubCallerIdentity { Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner") };
        var playbooks = Writing(h.Db, runner, browser: true);

        var refusal = await playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, "5"), default);

        AssertRefused(refusal.Result);
        Assert.Null((await h.ReadAsync(row.Id)).Budget);
    }

    [Fact]
    public async Task APersonWithNoSecFetchMode_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var playbooks = Writing(h.Db, new StubCallerIdentity(), browser: false);

        var refusal = await playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, "5"), default);

        AssertRefused(refusal.Result);
        Assert.Null((await h.ReadAsync(row.Id)).Budget);
    }

    [Fact]
    public async Task APersonAtTheBrowser_MayWrite()
    {
        var h = await NewAsync();
        var row = await h.CreateAsync();
        var playbooks = Writing(h.Db, new StubCallerIdentity(), browser: true);

        var patched = Value(await playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, null, null, null, null, null, "5"), default));

        Assert.Equal(5, patched.Budget);
    }

    private static void AssertRefused(IActionResult? result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(PlaybookWrites.Sentence, refused.Value);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    // ---- UnreachableReason: filled on every read, from the board as it stands ----

    private const string Never = " - this row can never run";

    [Fact]
    public async Task ARowToAColumnTheDispatcherNeverPicks_ReadsBackWithTheSentenceNamingTheRealTarget()
    {
        var h = await NewAsync();
        var board = await h.FiveColumnsAsync();
        var row = await h.CreateRowAsync(board.InProgress, board.Approved);

        Assert.Equal(
            "a dispatch from \"in progress\" goes to \"review\", not \"approved\"" + Never,
            (await h.ReadAsync(row.Id)).UnreachableReason);
    }

    [Fact]
    public async Task ARowTheDispatcherPicks_ReadsBackWithNoSentence()
    {
        var h = await NewAsync();
        var board = await h.FiveColumnsAsync();
        var todoToDoing = await h.CreateRowAsync(board.Todo, board.InProgress);
        var review = await h.CreateRowAsync(board.Review, board.Review);
        var check = await h.CreateRowAsync(board.Review, board.Approved, isCheck: true);

        Assert.Null((await h.ReadAsync(todoToDoing.Id)).UnreachableReason);
        Assert.Null((await h.ReadAsync(review.Id)).UnreachableReason);
        Assert.Null((await h.ReadAsync(check.Id)).UnreachableReason);
    }

    [Fact]
    public async Task ARowFromTheTerminalColumn_ReadsBackWithTheTerminalSentence()
    {
        var h = await NewAsync();
        var board = await h.FiveColumnsAsync();
        var row = await h.CreateRowAsync(board.Done, board.Todo);

        Assert.Equal(
            "nothing is dispatched from \"done\" - it is a terminal column, so this row can never run",
            (await h.ReadAsync(row.Id)).UnreachableReason);
    }

    [Fact]
    public async Task TheSentence_FollowsTheBoard_NotTheRow()
    {
        var h = await NewAsync();
        var board = await h.FiveColumnsAsync();
        var row = await h.CreateRowAsync(board.Todo, board.InProgress);
        var updatedAt = (await h.ReadAsync(row.Id)).UpdatedAt;
        Assert.Null((await h.ReadAsync(row.Id)).UnreachableReason);

        // Pushing "review" to the left of "in progress" makes "review" the column after "todo".
        await h.MoveColumnAsync(board.Review, 15);
        var flipped = await h.ReadAsync(row.Id);

        Assert.Equal(
            "a dispatch from \"todo\" goes to \"review\", not \"in progress\"" + Never,
            flipped.UnreachableReason);
        Assert.Equal(updatedAt, flipped.UpdatedAt);

        await h.MoveColumnAsync(board.Review, 30);
        var restored = await h.ReadAsync(row.Id);

        Assert.Null(restored.UnreachableReason);
        Assert.Equal(updatedAt, restored.UpdatedAt);
    }

    [Fact]
    public async Task CreatingAnUnreachableRow_Succeeds_AndReturnsTheSentence()
    {
        var h = await NewAsync();
        var board = await h.FiveColumnsAsync();

        var created = await h.CreateRowAsync(board.InProgress, board.Approved);

        Assert.Equal(
            "a dispatch from \"in progress\" goes to \"review\", not \"approved\"" + Never,
            created.UnreachableReason);
    }

    [Fact]
    public async Task PatchingARowUnreachable_Succeeds_AndReturnsTheSentence_AndPatchingItBackClearsIt()
    {
        var h = await NewAsync();
        var board = await h.FiveColumnsAsync();
        var row = await h.CreateRowAsync(board.Todo, board.InProgress);
        Assert.Null(row.UnreachableReason);

        var patched = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, board.Approved, null, null, null, null), default));

        Assert.Equal(
            "a dispatch from \"todo\" goes to \"in progress\", not \"approved\"" + Never,
            patched.UnreachableReason);

        var mended = Value(await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, board.InProgress, null, null, null, null), default));

        Assert.Null(mended.UnreachableReason);
    }

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
            bool? isCheck = null, string? quietLimitSeconds = null) =>
            Created(await Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(Todo, InProgress, [], "do it", "sonnet", "high", budget, briefLimit,
                    ProjectId: projectId, DeniedTools: deniedTools, IsCheck: isCheck, QuietLimitSeconds: quietLimitSeconds), default));

        public async Task<PlaybookDto> SetAsync(
            int id, string? budget = null, string? briefLimit = null, string? projectId = null, IReadOnlyList<string>? deniedTools = null,
            bool? isCheck = null, string? prompt = null, string? quietLimitSeconds = null) =>
            Value(await Playbooks.PatchPlaybook(
                id, new PlaybookPatchRequest(null, null, null, prompt, null, null, budget, briefLimit,
                    ProjectId: projectId, DeniedTools: deniedTools, IsCheck: isCheck, QuietLimitSeconds: quietLimitSeconds), default));

        /// <summary>There is no API to set a prescribed text, so a test sets it on the row.</summary>
        public async Task SetDefaultPromptAsync(int id)
        {
            (await Db.Playbooks.SingleAsync(p => p.Id == id)).DefaultPrompt = "the prescribed text";
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// Lays a five-column board on this harness's database: the two columns it
        /// starts with, moved to 10 and 20, then review (30), approved (40, protected
        /// and merging the pull request) and done (50, terminal).
        /// </summary>
        public async Task<Board> FiveColumnsAsync()
        {
            (await Db.Statuses.SingleAsync(s => s.Id == Todo)).SortOrder = 10;
            (await Db.Statuses.SingleAsync(s => s.Id == InProgress)).SortOrder = 20;
            var review = new EfHatchStatus { Name = "review", SortOrder = 30 };
            var approved = new EfHatchStatus { Name = "approved", SortOrder = 40, IsProtected = true, MergesPullRequest = true };
            var done = new EfHatchStatus { Name = "done", SortOrder = 50, IsTerminal = true };
            Db.AddRange(review, approved, done);
            await Db.SaveChangesAsync();
            return new Board(Todo, InProgress, review.Id, approved.Id, done.Id);
        }

        public async Task<PlaybookDto> CreateRowAsync(int from, int to, bool isCheck = false) =>
            Created(await Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(from, to, [], "do it", "sonnet", "high", IsCheck: isCheck), default));

        /// <summary>Moves a column through the context, the way an operator's reorder does: no playbook row is written.</summary>
        public async Task MoveColumnAsync(int statusId, int sortOrder)
        {
            (await Db.Statuses.SingleAsync(s => s.Id == statusId)).SortOrder = sortOrder;
            await Db.SaveChangesAsync();
        }

        public async Task<PlaybookDto> ReadAsync(int id) =>
            Value(await Playbooks.GetPlaybooks(default)).Single(p => p.Id == id);
    }

    private sealed record Board(int Todo, int InProgress, int Review, int Approved, int Done);

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
            Playbooks = AtTheBrowser(db, new FakeTimeProvider(Now), families),
            Todo = todo.Id,
            InProgress = doing.Id,
            ProjectA = projectA.Id,
            ProjectRepoSourced = projectRepoSourced.Id,
        };
    }

    /// <summary>A controller whose caller is a plain person, at a browser - what every write below is made by unless a test says otherwise.</summary>
    internal static PlaybooksController AtTheBrowser(HatchContext db, TimeProvider time, ModelFamilies? families = null) =>
        Writing(db, new StubCallerIdentity(), browser: true, time, families);

    private static PlaybooksController Writing(
        HatchContext db, StubCallerIdentity caller, bool browser, TimeProvider? time = null, ModelFamilies? families = null)
    {
        var http = new DefaultHttpContext();
        if (browser) http.Request.Headers["Sec-Fetch-Mode"] = "navigate";
        return new PlaybooksController(db, time ?? new FakeTimeProvider(Now), caller, families)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
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
