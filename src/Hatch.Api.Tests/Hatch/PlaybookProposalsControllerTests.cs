using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <c>POST /api/hatch/playbook-proposals</c>: every refusal in the order
/// <see cref="PlaybookProposalsController.SubmitProposal"/> makes them, that
/// nothing live moves when it saves, and that the event it writes on a named
/// issue matches the one <see cref="PlaybookProposalReadsTests.Harness"/>'s
/// board would already read back.
/// </summary>
public class PlaybookProposalsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ---- Happy path ----

    [Fact]
    public async Task AModelChange_Is201Pending_AuthoredByThePerson()
    {
        var h = await NewAsync();

        var created = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("the model is too weak for this transition", null,
                [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default));

        Assert.Equal("opus", created.Changes[0].After.Model);
        Assert.Equal("pending", created.State);
        Assert.Equal("a person", created.Author);
    }

    [Fact]
    public async Task AModelChange_FromAKey_IsAuthoredByTheKey()
    {
        var h = await NewAsync();
        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var created = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("the model is too weak for this transition", null,
                [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default));

        Assert.Equal("a key", created.Author);
    }

    [Fact]
    public async Task NothingLive_Moves()
    {
        var h = await NewAsync();
        var before = await Snapshot(h.Db);

        await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default);

        var after = await Snapshot(h.Db);
        Assert.Equal(before.Count, await h.Db.Playbooks.CountAsync());
        Assert.Equal(Json(before), Json(after));
    }

    // ---- Reason, changes ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankReason_IsRefused(string? reason)
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest(reason, null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default);

        Assert.Equal("a proposal needs a reason", Reason(result.Result));
    }

    [Fact]
    public async Task AReasonOverTheLimit_IsRefused()
    {
        var h = await NewAsync();
        var tooLong = new string('a', EfHatchComment.MaxBodyLength + 1);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest(tooLong, null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default);

        Assert.Equal($"a reason is at most {EfHatchComment.MaxBodyLength} characters", Reason(result.Result));
    }

    [Fact]
    public async Task NullChanges_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(new PlaybookProposalRequest("why", null, null), default);

        Assert.Equal("a proposal needs at least one change", Reason(result.Result));
    }

    [Fact]
    public async Task EmptyChanges_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(new PlaybookProposalRequest("why", null, []), default);

        Assert.Equal("a proposal needs at least one change", Reason(result.Result));
    }

    // ---- Per-change refusals ----

    [Fact]
    public async Task AChangeNamingNoField_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id)]), default);

        var message = Reason(result.Result);
        Assert.Contains("change 1", message);
        Assert.Contains("names no field to change", message);
    }

    [Fact]
    public async Task AnUnknownPlaybookId_IsRefused()
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [new PlaybookProposalChangeRequest(99999, Model: "opus")]), default);

        var message = Reason(result.Result);
        Assert.Contains("change 1", message);
        Assert.Contains("there is no playbook 99999", message);
    }

    [Fact]
    public async Task AnUnknownModel_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, "fake-9", null);
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        var message = Reason(result.Result);
        Assert.Contains("change 1", message);
        Assert.Contains(expected, message);
    }

    [Fact]
    public async Task ABadEffort_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, null, "bogus");
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ABadShape_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, null, null, Shape: "bogus");
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ABlankPrompt_OnARowWithNoDefault_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, "", null, null);
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ABadBudget_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, null, null, Budget: "abc");
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ABadBriefLimit_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, null, null, BriefLimit: "abc");
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ADeniedToolsRuleThatDoesNotParse_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, null, null, DeniedTools: ["Bash(git push *"]);
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ANonexistentToStatusId_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, 99999, null, null, null, null);
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task ASameColumnRow_NotTheReviewColumn_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(h.Todo, h.Todo, null, null, null, null);
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task APatchOnARepoSourcedProject_IsRefused()
    {
        var h = await NewAsync();
        var patch = new PlaybookPatchRequest(null, null, null, null, null, null, ProjectId: h.ProjectRepoSourced.ToString());
        var expected = await ExpectedRefusalAsync(h, patch);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    [Fact]
    public async Task APatchThatWouldClash_IsRefused()
    {
        var h = await NewAsync();
        var colliding = new EfHatchPlaybook
        {
            FromStatusId = h.Todo, ToStatusId = h.Review, Types = "", Shape = EfHatchPlaybook.DefaultShape,
            Model = "sonnet", Effort = "medium", CreatedAt = Now, UpdatedAt = Now,
        };
        h.Db.Playbooks.Add(colliding);
        await h.Db.SaveChangesAsync();

        var twin = Clone(h.Playbook, h.Playbook.FromStatusId, h.Playbook.ToStatusId);
        h.Db.Playbooks.Add(twin);
        await h.Db.SaveChangesAsync();
        var patch = new PlaybookPatchRequest(null, h.Review, null, null, null, null);
        var playbooks = PlaybooksControllerTests.AtTheBrowser(h.Db, new FakeTimeProvider(Now));
        var expected = Reason((await playbooks.PatchPlaybook(twin.Id, patch, default)).Result);

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default);

        Assert.Contains(expected, Reason(result.Result));
    }

    // ---- Duplicates and pending conflicts ----

    [Fact]
    public async Task TwoChanges_NamingTheSamePlaybook_RefusesTheSecond()
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null,
            [
                new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus"),
                new PlaybookProposalChangeRequest(h.Playbook.Id, Effort: "high"),
            ]), default);

        var message = Reason(result.Result);
        Assert.Contains("change 2", message);
        Assert.Contains($"playbook {h.Playbook.Id} is already changed by change 1 of this proposal", message);
    }

    [Fact]
    public async Task ASecondProposal_OnARowWithAPendingChange_IsRefused_ThenAcceptedOnceTheFirstIsDecided()
    {
        var h = await NewAsync();
        var first = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default));

        var second = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("also why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Effort: "high")]), default);

        Assert.Contains($"proposal {first.Id}", Reason(second.Result));

        var row = await h.Db.PlaybookProposals.SingleAsync(p => p.Id == first.Id);
        row.State = EfHatchPlaybookProposal.Rejected;
        await h.Db.SaveChangesAsync();

        var third = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("also why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Effort: "high")]), default));
        Assert.Equal("high", third.Changes[0].After.Effort);
    }

    // ---- Issue, event ----

    [Fact]
    public async Task AnUnknownIssueKey_Is404()
    {
        var h = await NewAsync();

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", "A-999", [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ANamedIssue_GetsExactlyOnePlaybookProposedEvent()
    {
        var h = await NewAsync();
        var issue = h.FileIssue();
        await h.Db.SaveChangesAsync();
        var key = IssueKey.Format("A", issue.Number);

        var created = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("the prompt needs work", key,
            [
                new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus"),
            ]), default));

        var events = await h.Db.IssueEvents.Where(e => e.IssueId == issue.Id).ToListAsync();
        var one = Assert.Single(events);
        Assert.Equal(EfHatchIssueEvent.PlaybookProposed, one.Kind);
        Assert.Equal("a person", one.Actor);
        Assert.Contains($"\"proposalId\":{created.Id}", one.Payload);
        Assert.Contains("\"changes\":1", one.Payload);
        Assert.Contains("the prompt needs work", one.Payload);
    }

    [Fact]
    public async Task NoIssueNamed_WritesNoEvent()
    {
        var h = await NewAsync();

        await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default);

        Assert.Empty(await h.Db.IssueEvents.ToListAsync());
    }

    // ---- All or nothing ----

    [Fact]
    public async Task AMultiChangeProposal_WhereTheSecondIsBad_SavesNothing()
    {
        var h = await NewAsync();
        var second = new EfHatchPlaybook
        {
            FromStatusId = h.InProgress, ToStatusId = h.Review, Types = "", Shape = EfHatchPlaybook.DefaultShape,
            Model = "sonnet", Effort = "medium", CreatedAt = Now, UpdatedAt = Now,
        };
        h.Db.Playbooks.Add(second);
        await h.Db.SaveChangesAsync();

        var result = await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null,
            [
                new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus"),
                new PlaybookProposalChangeRequest(second.Id, Model: "fake-9"),
            ]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.Db.PlaybookProposals.ToListAsync());
        Assert.Empty(await h.Db.PlaybookProposalChanges.ToListAsync());
    }

    // ---- Out of date after the fact ----

    [Fact]
    public async Task DeletingTheRowAfterSubmission_ReadsBackOutOfDate()
    {
        var h = await NewAsync();
        var created = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [new PlaybookProposalChangeRequest(h.Playbook.Id, Model: "opus")]), default));

        var row = await h.Db.Playbooks.SingleAsync(p => p.Id == h.Playbook.Id);
        h.Db.Playbooks.Remove(row);
        await h.Db.SaveChangesAsync();

        var read = Value(await h.Proposals.GetProposal(created.Id, default));
        var change = Assert.Single(read.Changes);
        Assert.Null(change.PlaybookId);
        Assert.True(change.OutOfDate);
        Assert.Equal("deleted since it was proposed", change.OutOfDateReason);
    }

    // ---- PATCH/proposal parity ----

    [Fact]
    public async Task TheAfterDto_MatchesWhatPatchWouldReturn_ForATwinRow()
    {
        var h = await NewAsync();
        var tracked = await h.Db.Playbooks.SingleAsync(p => p.Id == h.Playbook.Id);
        tracked.DefaultPrompt = "the prescribed text";
        await h.Db.SaveChangesAsync();

        var twin = Clone(tracked, h.InProgress, h.Review);
        h.Db.Playbooks.Add(twin);
        await h.Db.SaveChangesAsync();

        var patch = new PlaybookPatchRequest(null, null, null, "the prescribed text", "opus", null,
            Budget: "9", DeniedTools: ["Bash"]);

        var playbooks = PlaybooksControllerTests.AtTheBrowser(h.Db, new FakeTimeProvider(Now));
        var expected = Value(await playbooks.PatchPlaybook(twin.Id, patch, default));

        var created = Created(await h.Proposals.SubmitProposal(
            new PlaybookProposalRequest("why", null, [ChangeFrom(h.Playbook.Id, patch)]), default));
        var after = created.Changes[0].After;

        // The twin sits at a different transition so it carries none of h.Playbook's own clashes
        // (RefusalAsync re-checks the whole signature on every patch, live row or not) - so the
        // position fields are expected to differ and are normalized away here; everything the
        // patch itself touches - Prompt, Model, Budget, DeniedTools - is compared as given.
        Assert.Equal(
            Json(expected with
            {
                Id = after.Id, UpdatedAt = after.UpdatedAt,
                FromStatusId = after.FromStatusId, FromStatusName = after.FromStatusName,
                ToStatusId = after.ToStatusId, ToStatusName = after.ToStatusName,
            }),
            Json(after));
    }

    // ---- reject-all ----

    [Fact]
    public async Task RejectAll_OnAProposalNamingNoIssue_MarksItRejected_AndLeavesThePlaybookAlone()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();
        h.Time.Advance(TimeSpan.FromMinutes(5));
        var proposals = h.Writing(h.Db, h.Caller, browser: true);

        var result = Value(await proposals.RejectAll(new PlaybookProposalDecisionRequest([proposal.Id]), default));

        Assert.Equal(1, result.Rejected);
        var read = await h.ReadAsync(proposal.Id);
        Assert.Equal("rejected", read.State);
        Assert.Equal("a person", read.DecidedBy);
        Assert.Equal(Now.AddMinutes(5), read.DecidedAt);
        Assert.Equal(Now, h.Playbook.UpdatedAt);
    }

    [Fact]
    public async Task RejectAll_OnAProposalNamingAnIssue_WritesExactlyOneRejectedEvent()
    {
        var h = await NewAsync();
        var issue = h.FileIssue();
        await h.Db.SaveChangesAsync();
        var proposal = await h.ProposeAsync(issueId: issue.Id);
        var proposals = h.Writing(h.Db, h.Caller, browser: true);

        await proposals.RejectAll(new PlaybookProposalDecisionRequest([proposal.Id]), default);

        var events = await h.Db.IssueEvents.Where(e => e.IssueId == issue.Id).ToListAsync();
        var one = Assert.Single(events);
        Assert.Equal(EfHatchIssueEvent.PlaybookProposalRejected, one.Kind);
        Assert.Contains($"\"proposalId\":{proposal.Id}", one.Payload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectAll_WhenTheGivenSet_DoesNotMatchWhatIsPending_Is409_AndChangesNothing(bool extra)
    {
        var h = await NewAsync();
        var first = await h.ProposeAsync();
        var second = await h.ProposeAsync();
        var ids = extra ? new List<int> { first.Id, second.Id, 99999 } : new List<int> { first.Id };
        var proposals = h.Writing(h.Db, h.Caller, browser: true);

        var result = await proposals.RejectAll(new PlaybookProposalDecisionRequest(ids), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("the pending proposals have changed since the page was read - reload", conflict.Value);
        Assert.Equal("pending", (await h.ReadAsync(first.Id)).State);
        Assert.Equal("pending", (await h.ReadAsync(second.Id)).State);
    }

    [Fact]
    public async Task RejectAll_WithNothingPending_Is200WithZero()
    {
        var h = await NewAsync();
        var proposals = h.Writing(h.Db, h.Caller, browser: true);

        var result = Value(await proposals.RejectAll(new PlaybookProposalDecisionRequest([]), default));

        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public async Task RejectAll_AnApiKey_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();
        var caller = new PlaybookProposalReadsTests.StubCallerIdentity
        {
            Key = new EfApiKey { Name = "Claude", Prefix = "hatch_ak_abc", Hash = [], CreatedAt = Now },
        };
        var proposals = h.Writing(h.Db, caller, browser: true);

        var result = await proposals.RejectAll(new PlaybookProposalDecisionRequest([proposal.Id]), default);

        AssertRefused(result.Result);
        Assert.Equal("pending", (await h.ReadAsync(proposal.Id)).State);
    }

    [Fact]
    public async Task RejectAll_AKeylessRunner_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();
        var caller = new PlaybookProposalReadsTests.StubCallerIdentity
        {
            Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner"),
        };
        var proposals = h.Writing(h.Db, caller, browser: true);

        var result = await proposals.RejectAll(new PlaybookProposalDecisionRequest([proposal.Id]), default);

        AssertRefused(result.Result);
        Assert.Equal("pending", (await h.ReadAsync(proposal.Id)).State);
    }

    [Fact]
    public async Task RejectAll_APersonWithNoSecFetchMode_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();
        var proposals = h.Writing(h.Db, new PlaybookProposalReadsTests.StubCallerIdentity(), browser: false);

        var result = await proposals.RejectAll(new PlaybookProposalDecisionRequest([proposal.Id]), default);

        AssertRefused(result.Result);
        Assert.Equal("pending", (await h.ReadAsync(proposal.Id)).State);
    }

    private static void AssertRefused(IActionResult? result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(PlaybookWrites.Sentence, refused.Value);
    }

    // ---- Harness ----

    private static async Task<PlaybookProposalReadsTests.Harness> NewAsync() => await PlaybookProposalReadsTests.NewAsync();

    private static PlaybookProposalChangeRequest ChangeFrom(int playbookId, PlaybookPatchRequest patch) => new(
        playbookId, patch.FromStatusId, patch.ToStatusId, patch.Types, patch.Prompt, patch.Model, patch.Effort,
        patch.Budget, patch.BriefLimit, patch.Shape, patch.DeniedTools, patch.ProjectId, patch.IsCheck);

    /// <summary>
    /// The sentence <c>PATCH /playbooks/{id}</c> would give for the same patch, read off a twin
    /// row at a different transition so it carries none of the live board's own clashes - so a
    /// reworded refusal moves both the controller and this test together.
    /// </summary>
    private static async Task<string> ExpectedRefusalAsync(PlaybookProposalReadsTests.Harness h, PlaybookPatchRequest patch)
    {
        var twin = Clone(h.Playbook, h.InProgress, h.Review);
        h.Db.Playbooks.Add(twin);
        await h.Db.SaveChangesAsync();

        var playbooks = PlaybooksControllerTests.AtTheBrowser(h.Db, new FakeTimeProvider(Now));
        return Reason((await playbooks.PatchPlaybook(twin.Id, patch, default)).Result);
    }

    private static EfHatchPlaybook Clone(EfHatchPlaybook source, int fromStatusId, int toStatusId) => new()
    {
        FromStatusId = fromStatusId,
        ToStatusId = toStatusId,
        Types = source.Types,
        Shape = source.Shape,
        Prompt = source.Prompt,
        DefaultPrompt = source.DefaultPrompt,
        Model = source.Model,
        Effort = source.Effort,
        Budget = source.Budget,
        BriefLimit = source.BriefLimit,
        QuietLimitSeconds = source.QuietLimitSeconds,
        DeniedTools = source.DeniedTools,
        IsCheck = source.IsCheck,
        ProjectId = source.ProjectId,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private sealed record PlaybookSnapshot(
        int Id, int FromStatusId, int ToStatusId, string Types, string Shape, string? Prompt, string? DefaultPrompt,
        string Model, string Effort, int? Budget, int? BriefLimit, int? QuietLimitSeconds, string? DeniedTools,
        bool IsCheck, int? ProjectId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private static async Task<List<PlaybookSnapshot>> Snapshot(HatchContext db) =>
        await db.Playbooks.AsNoTracking().Select(p => new PlaybookSnapshot(
            p.Id, p.FromStatusId, p.ToStatusId, p.Types, p.Shape, p.Prompt, p.DefaultPrompt, p.Model, p.Effort,
            p.Budget, p.BriefLimit, p.QuietLimitSeconds, p.DeniedTools, p.IsCheck, p.ProjectId, p.CreatedAt, p.UpdatedAt))
            .OrderBy(p => p.Id).ToListAsync();

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Web);

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
