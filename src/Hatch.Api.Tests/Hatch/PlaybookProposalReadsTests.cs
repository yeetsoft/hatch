using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The two reads <see cref="PlaybookProposalsController"/> answers: the queue,
/// filtered and ordered by state, and one proposal by id - each change on the
/// wire carrying <c>before</c>, <c>after</c>, <c>changedFields</c> and whether
/// it has gone out of date. The submission route does not exist yet, so every
/// proposal here is inserted straight onto the context.
/// </summary>
public class PlaybookProposalReadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThePendingList_IsOldestFirst_AndOmitsApprovedOrRejected()
    {
        var h = await NewAsync();
        var first = await h.ProposeAsync(createdAt: Now);
        await h.ProposeAsync(createdAt: Now.AddMinutes(1), state: EfHatchPlaybookProposal.Approved);
        var third = await h.ProposeAsync(createdAt: Now.AddMinutes(2));
        await h.ProposeAsync(createdAt: Now.AddMinutes(3), state: EfHatchPlaybookProposal.Rejected);

        var rows = Value(await h.Proposals.GetProposals(null, default));

        Assert.Equal([first.Id, third.Id], rows.Select(r => r.Id));
    }

    [Fact]
    public async Task AllState_HasEveryState()
    {
        var h = await NewAsync();
        await h.ProposeAsync(state: EfHatchPlaybookProposal.Pending);
        await h.ProposeAsync(state: EfHatchPlaybookProposal.Approved);
        await h.ProposeAsync(state: EfHatchPlaybookProposal.Rejected);

        var rows = Value(await h.Proposals.GetProposals("all", default));

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task ABogusState_Is400()
    {
        var h = await NewAsync();

        var result = await h.Proposals.GetProposals("bogus", default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("\"bogus\" is not a state - pending, approved, rejected or all", bad.Value);
    }

    [Fact]
    public async Task AMissingId_Is404()
    {
        var h = await NewAsync();

        var result = await h.Proposals.GetProposal(12345, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task After_IsBeforeWithThePatchApplied()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync(patch: new PlaybookPatchRequest(null, null, null, null, "opus", null, Budget: "9"));

        var read = await h.ReadAsync(proposal.Id);
        var change = Assert.Single(read.Changes);

        Assert.Equal(new[] { "model", "budget" }, change.ChangedFields);
        Assert.Equal("opus", change.After.Model);
        Assert.Equal(9, change.After.Budget);
        Assert.Equal("sonnet", change.Before.Model);
        Assert.Null(change.Before.Budget);
    }

    [Fact]
    public async Task APatchedPromptEqualToTheDefault_ReadsBackAsThatText_AndIsNotAChangedField()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync(
            defaultPrompt: "the prescribed text",
            patch: new PlaybookPatchRequest(null, null, null, "the prescribed text", null, null));

        var read = await h.ReadAsync(proposal.Id);
        var change = Assert.Single(read.Changes);

        Assert.Equal("the prescribed text", change.After.Prompt);
        Assert.DoesNotContain("prompt", change.ChangedFields);
    }

    [Fact]
    public async Task AnEditedLiveRow_IsOutOfDate()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();
        h.Playbook.UpdatedAt = Now.AddHours(1);
        await h.Db.SaveChangesAsync();

        var read = await h.ReadAsync(proposal.Id);
        var change = Assert.Single(read.Changes);

        Assert.True(change.OutOfDate);
        Assert.Equal("edited since it was proposed", change.OutOfDateReason);
    }

    [Fact]
    public async Task ADeletedPlaybook_IsOutOfDate()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();
        h.Db.Playbooks.Remove(h.Playbook);
        await h.Db.SaveChangesAsync();

        var read = await h.ReadAsync(proposal.Id);
        var change = Assert.Single(read.Changes);

        Assert.True(change.OutOfDate);
        Assert.Equal("deleted since it was proposed", change.OutOfDateReason);
    }

    [Fact]
    public async Task AnUntouchedRow_IsNotOutOfDate()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();

        var read = await h.ReadAsync(proposal.Id);
        var change = Assert.Single(read.Changes);

        Assert.False(change.OutOfDate);
        Assert.Null(change.OutOfDateReason);
    }

    [Fact]
    public async Task AChangeMovingToStatusId_CarriesTheNewColumnsName()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync(patch: new PlaybookPatchRequest(null, h.Review, null, null, null, null));

        var read = await h.ReadAsync(proposal.Id);
        var change = Assert.Single(read.Changes);

        Assert.Equal("in review", change.After.ToStatusName);
    }

    [Fact]
    public async Task AProposalWithAnIssue_CarriesItsKey()
    {
        var h = await NewAsync();
        var issue = h.FileIssue();
        await h.Db.SaveChangesAsync();
        var proposal = await h.ProposeAsync(issueId: issue.Id);

        var read = await h.ReadAsync(proposal.Id);

        Assert.Equal(IssueKey.Format("A", issue.Number), read.IssueKey);
    }

    [Fact]
    public async Task AProposalWithNoIssue_CarriesNoKey()
    {
        var h = await NewAsync();
        var proposal = await h.ProposeAsync();

        var read = await h.ReadAsync(proposal.Id);

        Assert.Null(read.IssueKey);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required PlaybookProposalsController Proposals { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }
        public required int Review { get; init; }
        public required int ProjectId { get; init; }
        public required EfHatchPlaybook Playbook { get; init; }

        private int nextIssueNumber = 1;

        public async Task<EfHatchPlaybookProposal> ProposeAsync(
            string state = EfHatchPlaybookProposal.Pending, DateTimeOffset? createdAt = null,
            PlaybookPatchRequest? patch = null, string? defaultPrompt = null, long? issueId = null)
        {
            var before = BeforeDto(defaultPrompt);
            var proposal = new EfHatchPlaybookProposal
            {
                Reason = "why",
                IssueId = issueId,
                Author = "a-key",
                State = state,
                CreatedAt = createdAt ?? Now,
                Changes =
                [
                    new EfHatchPlaybookProposalChange
                    {
                        ProposalId = 0,
                        PlaybookId = Playbook.Id,
                        Before = EfHatchPlaybookProposalChange.WriteBefore(before),
                        Patch = EfHatchPlaybookProposalChange.WritePatch(
                            patch ?? new PlaybookPatchRequest(null, null, null, null, null, null)),
                        BasedOnUpdatedAt = Playbook.UpdatedAt,
                    },
                ],
            };
            Db.PlaybookProposals.Add(proposal);
            await Db.SaveChangesAsync();
            return proposal;
        }

        public EfHatchIssue FileIssue()
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = nextIssueNumber++,
                Type = "task",
                Title = "a ticket",
                Description = "",
                StatusId = Todo,
                Rank = 1024,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.Issues.Add(issue);
            return issue;
        }

        public async Task<PlaybookProposalDto> ReadAsync(int id) => Value(await Proposals.GetProposal(id, default));

        private PlaybookDto BeforeDto(string? defaultPrompt) => new(
            Playbook.Id, Todo, "todo", InProgress, "in progress", [], EfHatchPlaybook.DefaultShape,
            defaultPrompt ?? Playbook.EffectivePrompt, defaultPrompt, Playbook.Model, Playbook.Effort,
            null, null, [], false, Playbook.UpdatedAt, null, null, null);
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var inProgress = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "in review", SortOrder = 40 };
        var project = new EfHatchProject { Key = "A", Name = "A", CreatedAt = Now };
        db.AddRange(todo, inProgress, review, project);
        await db.SaveChangesAsync();

        var playbook = new EfHatchPlaybook
        {
            FromStatusId = todo.Id,
            ToStatusId = inProgress.Id,
            Types = "",
            Shape = EfHatchPlaybook.DefaultShape,
            Prompt = "do it",
            Model = "sonnet",
            Effort = "medium",
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Playbooks.Add(playbook);
        await db.SaveChangesAsync();

        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var time = new FakeTimeProvider(Now);

        return new Harness
        {
            Db = db,
            Proposals = new PlaybookProposalsController(db, caller, time),
            Todo = todo.Id,
            InProgress = inProgress.Id,
            Review = review.Id,
            ProjectId = project.Id,
            Playbook = playbook,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor.</summary>
    private sealed class StubCallerIdentity : ICallerIdentity
    {
        public EfPerson? Person { get; set; }

        public EfApiKey? Key { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }
}
