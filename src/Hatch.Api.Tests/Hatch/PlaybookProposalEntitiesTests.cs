using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The storage a playbook proposal rests on: the two tables, what happens to a
/// change when its playbook or its proposal goes, and the helpers that put a
/// <see cref="PlaybookDto"/> and a <see cref="PlaybookPatchRequest"/> in a
/// column. Both are records holding lists, and record equality compares a list
/// by reference, so a round trip is compared by what each serialises to.
/// </summary>
public class PlaybookProposalEntitiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AProposalWithTwoChanges_RoundTrips()
    {
        var (db, first, second) = await NewAsync();
        var firstBefore = DtoOf(first);
        var secondBefore = DtoOf(second);
        var firstPatch = new PlaybookPatchRequest(null, null, ["task", "bug"], "a new prompt", "opus", null,
            Budget: "5", DeniedTools: ["Bash"], IsCheck: true);
        var secondPatch = new PlaybookPatchRequest(null, null, null, null, null, null, ProjectId: "");

        db.PlaybookProposals.Add(new EfHatchPlaybookProposal
        {
            Reason = "the prompt for review is too thin",
            Author = "a-key",
            State = EfHatchPlaybookProposal.Pending,
            CreatedAt = Now,
            Changes =
            [
                Change(first, firstBefore, firstPatch),
                Change(second, secondBefore, secondPatch),
            ],
        });
        await db.SaveChangesAsync();

        var read = await db.PlaybookProposals.Include(p => p.Changes).SingleAsync();
        var changes = read.Changes.OrderBy(c => c.Id).ToList();

        Assert.Equal(2, changes.Count);
        Assert.Equal(first.Id, changes[0].PlaybookId);
        Assert.Equal(second.Id, changes[1].PlaybookId);
        Assert.Equal(Now, changes[0].BasedOnUpdatedAt);
        Assert.Equal(Json(firstBefore), Json(EfHatchPlaybookProposalChange.ReadBefore(changes[0].Before)));
        Assert.Equal(Json(secondBefore), Json(EfHatchPlaybookProposalChange.ReadBefore(changes[1].Before)));
        Assert.Equal(Json(firstPatch), Json(EfHatchPlaybookProposalChange.ReadPatch(changes[0].Patch)));
        Assert.Equal(Json(secondPatch), Json(EfHatchPlaybookProposalChange.ReadPatch(changes[1].Patch)));

        var patch = EfHatchPlaybookProposalChange.ReadPatch(changes[0].Patch);
        Assert.Equal(new[] { "task", "bug" }, patch.Types);
        Assert.Equal(new[] { "Bash" }, patch.DeniedTools);
        Assert.Equal("", EfHatchPlaybookProposalChange.ReadPatch(changes[1].Patch).ProjectId);
        Assert.Null(EfHatchPlaybookProposalChange.ReadPatch(changes[1].Patch).Prompt);
    }

    [Fact]
    public async Task DeletingThePlaybook_LeavesTheChange_WithNoPlaybook()
    {
        var (db, first, second) = await NewAsync();
        var proposal = await ProposeAsync(db, first, second);
        var firstId = first.Id;

        // The in-memory provider only applies SetNull to a child it is tracking,
        // where Postgres does it for every row, so the change is loaded first.
        await db.PlaybookProposalChanges.ToListAsync();
        db.Playbooks.Remove(first);
        await db.SaveChangesAsync();

        var changes = await db.PlaybookProposalChanges.OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(2, changes.Count);
        Assert.Null(changes[0].PlaybookId);
        Assert.Equal(second.Id, changes[1].PlaybookId);
        Assert.Equal(proposal.Id, changes[0].ProposalId);
        Assert.Equal(firstId, EfHatchPlaybookProposalChange.ReadBefore(changes[0].Before).Id);
    }

    [Fact]
    public async Task DeletingTheProposal_RemovesItsChanges()
    {
        var (db, first, second) = await NewAsync();
        var proposal = await ProposeAsync(db, first, second);

        var loaded = await db.PlaybookProposals.Include(p => p.Changes).SingleAsync(p => p.Id == proposal.Id);
        db.PlaybookProposals.Remove(loaded);
        await db.SaveChangesAsync();

        Assert.Empty(await db.PlaybookProposalChanges.ToListAsync());
        Assert.Equal(2, await db.Playbooks.CountAsync());
    }

    [Fact]
    public void TheEventKind_FitsTheColumn()
    {
        Assert.True(EfHatchIssueEvent.PlaybookProposed.Length <= EfHatchIssueEvent.MaxKindLength);
    }

    // ---- PlaybookProposalChangeRequest ----

    [Fact]
    public void ToPatch_CarriesEveryField()
    {
        var request = new PlaybookProposalChangeRequest(
            PlaybookId: 7, FromStatusId: 1, ToStatusId: 2, Types: ["task"], Prompt: "p", Model: "opus",
            Effort: "high", Budget: "5", BriefLimit: "9", Shape: "leaf", DeniedTools: ["Bash"],
            ProjectId: "3", IsCheck: true);

        var patch = request.ToPatch();

        Assert.Equal(1, patch.FromStatusId);
        Assert.Equal(2, patch.ToStatusId);
        Assert.Equal(new[] { "task" }, patch.Types);
        Assert.Equal("p", patch.Prompt);
        Assert.Equal("opus", patch.Model);
        Assert.Equal("high", patch.Effort);
        Assert.Equal("5", patch.Budget);
        Assert.Equal("9", patch.BriefLimit);
        Assert.Equal("leaf", patch.Shape);
        Assert.Equal(new[] { "Bash" }, patch.DeniedTools);
        Assert.Equal("3", patch.ProjectId);
        Assert.True(patch.IsCheck);
    }

    [Fact]
    public void ARequestWithOnlyAPlaybookId_NamesNoField()
    {
        var request = new PlaybookProposalChangeRequest(7);

        Assert.False(request.NamesAField);
        Assert.Equal(new PlaybookPatchRequest(null, null, null, null, null, null), request.ToPatch());
    }

    public static TheoryData<PlaybookProposalChangeRequest> OneField => new()
    {
        new(7, FromStatusId: 1),
        new(7, ToStatusId: 1),
        new(7, Types: []),
        new(7, Prompt: ""),
        new(7, Model: "opus"),
        new(7, Effort: "low"),
        new(7, Budget: ""),
        new(7, BriefLimit: ""),
        new(7, Shape: "any"),
        new(7, DeniedTools: []),
        new(7, ProjectId: ""),
        new(7, IsCheck: false),
    };

    [Theory]
    [MemberData(nameof(OneField))]
    public void AnyOneField_IsNamed(PlaybookProposalChangeRequest request)
    {
        Assert.True(request.NamesAField);
    }

    // ---- Helpers ----

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Web);

    private static async Task<(HatchContext Db, EfHatchPlaybook First, EfHatchPlaybook Second)> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "in review", SortOrder = 40 };
        db.AddRange(todo, doing, review);
        await db.SaveChangesAsync();

        var first = Playbook(todo.Id, doing.Id);
        var second = Playbook(doing.Id, review.Id);
        db.Playbooks.AddRange(first, second);
        await db.SaveChangesAsync();

        return (db, first, second);
    }

    private static EfHatchPlaybook Playbook(int from, int to) => new()
    {
        FromStatusId = from,
        ToStatusId = to,
        Types = "",
        Shape = EfHatchPlaybook.DefaultShape,
        Model = EfHatchPlaybook.DefaultModel,
        Effort = EfHatchPlaybook.DefaultEffort,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static PlaybookDto DtoOf(EfHatchPlaybook p) => new(
        p.Id, p.FromStatusId, "from", p.ToStatusId, "to", [], p.Shape, p.EffectivePrompt, null, p.Model, p.Effort,
        null, null, ["Read"], p.IsCheck, p.UpdatedAt, null, null, null);

    private static EfHatchPlaybookProposalChange Change(
        EfHatchPlaybook playbook, PlaybookDto before, PlaybookPatchRequest patch) => new()
    {
        ProposalId = 0,
        PlaybookId = playbook.Id,
        Before = EfHatchPlaybookProposalChange.WriteBefore(before),
        Patch = EfHatchPlaybookProposalChange.WritePatch(patch),
        BasedOnUpdatedAt = playbook.UpdatedAt,
    };

    private static async Task<EfHatchPlaybookProposal> ProposeAsync(
        HatchContext db, EfHatchPlaybook first, EfHatchPlaybook second)
    {
        var proposal = new EfHatchPlaybookProposal
        {
            Reason = "why",
            Author = "a-key",
            State = EfHatchPlaybookProposal.Pending,
            CreatedAt = Now,
            Changes =
            [
                Change(first, DtoOf(first), new PlaybookPatchRequest(null, null, null, "one", null, null)),
                Change(second, DtoOf(second), new PlaybookPatchRequest(null, null, null, "two", null, null)),
            ],
        };
        db.PlaybookProposals.Add(proposal);
        await db.SaveChangesAsync();
        return proposal;
    }
}
