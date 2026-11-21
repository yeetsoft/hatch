using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// An ambition's capabilities: rows read in order and written one at a time,
/// the position that stays 1 to n through every write, the one event each
/// write leaves on the ambition, and the line a key may not cross.
/// </summary>
public class IssueCapabilitiesControllerTests
{
    // ---- Reading ----

    [Fact]
    public async Task AFreshAmbition_ReadsNoCapabilities()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        Assert.Empty(await h.GetAsync(ambition));
    }

    // ---- Adding ----

    [Fact]
    public async Task AnAdd_AppendsAtTheEnd_AndReadsBackInOrder()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var first = await h.AddAsync(ambition, "first", "see the first");
        var second = await h.AddAsync(ambition, "second", "see the second");

        Assert.Equal(1, first.Position);
        Assert.Equal(2, second.Position);
        Assert.Equal("a person", first.CreatedBy);
        Assert.Equal(new[] { "first", "second" }, (await h.GetAsync(ambition)).Select(c => c.Statement));
        Assert.Equal("see the second", (await h.GetAsync(ambition))[1].HowToTell);
    }

    [Fact]
    public async Task AnAddAtAPosition_InsertsThere_AndShiftsTheRestDown()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");
        await h.AddAsync(ambition, "b", "b?");
        await h.AddAsync(ambition, "c", "c?");

        var inserted = await h.AddAsync(ambition, "new", "new?", position: 2);

        Assert.Equal(2, inserted.Position);
        var read = await h.GetAsync(ambition);
        Assert.Equal(new[] { "a", "new", "b", "c" }, read.Select(c => c.Statement));
        Assert.Equal(new[] { 1, 2, 3, 4 }, read.Select(c => c.Position));
    }

    [Fact]
    public async Task AnAddAtPositionOne_PutsItFirst_AndAtNPlusOne_PutsItLast()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "b", "b?");

        await h.AddAsync(ambition, "a", "a?", position: 1);
        await h.AddAsync(ambition, "c", "c?", position: 3);

        Assert.Equal(new[] { "a", "b", "c" }, (await h.GetAsync(ambition)).Select(c => c.Statement));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task AnAddOutsideOneToNPlusOne_IsRefused_AndChangesNothing(int position)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PostCapability(ambition, new CapabilityCreateRequest("x", "x?", position), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Single(await h.GetAsync(ambition));
        Assert.Single(await h.EventsAsync(ambition));
    }

    [Theory]
    [InlineData("", "how")]
    [InlineData("   ", "how")]
    [InlineData("what", "")]
    [InlineData("what", "  ")]
    public async Task AnAddWithABlankLine_IsRefused_AndChangesNothing(string statement, string howToTell)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var result = await h.Controller.PostCapability(ambition, new CapabilityCreateRequest(statement, howToTell), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await h.GetAsync(ambition));
        Assert.Empty(await h.EventsAsync(ambition));
    }

    [Fact]
    public async Task AnAddTooLong_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var result = await h.Controller.PostCapability(
            ambition, new CapabilityCreateRequest(new string('x', EfHatchCapability.MaxStatementLength + 1), "how"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AnAdd_WritesOneEvent_WithTheIdPositionAndStatement()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var added = await h.AddAsync(ambition, "first", "how");

        var evt = Assert.Single(await h.EventsAsync(ambition));
        Assert.Equal(EfHatchIssueEvent.CapabilityAdded, evt.Kind);
        var payload = evt.Payload!.Value;
        Assert.Equal(added.Id, payload.GetProperty("id").GetInt64());
        Assert.Equal(1, payload.GetProperty("position").GetInt32());
        Assert.Equal("first", payload.GetProperty("statement").GetString());
        Assert.Equal("how", payload.GetProperty("howToTell").GetString());
    }

    // ---- Editing ----

    [Fact]
    public async Task AnEdit_IsReflectedOnTheNextRead_AndAFieldLeftOutKeepsItsValue()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "was", "how was");

        await h.EditAsync(ambition, row.Id, statement: "is");
        var afterStatement = (await h.GetAsync(ambition)).Single();
        Assert.Equal("is", afterStatement.Statement);
        Assert.Equal("how was", afterStatement.HowToTell);

        await h.EditAsync(ambition, row.Id, howToTell: "how is");
        var afterHow = (await h.GetAsync(ambition)).Single();
        Assert.Equal("is", afterHow.Statement);
        Assert.Equal("how is", afterHow.HowToTell);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, "")]
    [InlineData(null, "  ")]
    public async Task AnEditSendingABlankField_IsRefused_AndChangesNothing(string? statement, string? howToTell)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "keep", "keep?");

        var result = await h.Controller.PatchCapability(ambition, row.Id, new CapabilityPatchRequest(statement, howToTell), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        var read = (await h.GetAsync(ambition)).Single();
        Assert.Equal("keep", read.Statement);
        Assert.Equal("keep?", read.HowToTell);
        Assert.Single(await h.EventsAsync(ambition));
    }

    [Fact]
    public async Task AnEdit_WritesOneEvent_WithWholeValuesEachSide()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "was", "how was");

        await h.EditAsync(ambition, row.Id, statement: "is");

        var evt = (await h.EventsAsync(ambition)).Last();
        Assert.Equal(EfHatchIssueEvent.CapabilityEdited, evt.Kind);
        var payload = evt.Payload!.Value;
        Assert.Equal(row.Id, payload.GetProperty("id").GetInt64());
        Assert.Equal(1, payload.GetProperty("position").GetInt32());
        Assert.Equal("was", payload.GetProperty("from").GetProperty("statement").GetString());
        Assert.Equal("how was", payload.GetProperty("from").GetProperty("howToTell").GetString());
        Assert.Equal("is", payload.GetProperty("to").GetProperty("statement").GetString());
        Assert.Equal("how was", payload.GetProperty("to").GetProperty("howToTell").GetString());
    }

    [Fact]
    public async Task AnEditThatChangesNothing_WritesNoEvent_AndLeavesUpdatedAtWhereItWas()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "same", "same?");
        var was = (await h.ReadAsync(ambition)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.EditAsync(ambition, row.Id, statement: "same", howToTell: "same?");
        await h.EditAsync(ambition, row.Id);

        Assert.Single(await h.EventsAsync(ambition));
        Assert.Equal(was, (await h.ReadAsync(ambition)).UpdatedAt);
        Assert.Equal(row.UpdatedAt, (await h.GetAsync(ambition)).Single().UpdatedAt);
    }

    [Fact]
    public async Task AnEditOfAnUnknownId_IsNotFound()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var result = await h.Controller.PatchCapability(ambition, 9999, new CapabilityPatchRequest("x"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AnEditOfAnotherAmbitionsCapability_IsNotFound()
    {
        var h = await NewAsync();
        var one = await h.FileAsync("ambition");
        var other = await h.FileAsync("ambition");
        var theirs = await h.AddAsync(other, "theirs", "theirs?");

        var result = await h.Controller.PatchCapability(one, theirs.Id, new CapabilityPatchRequest("mine"), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal("theirs", (await h.GetAsync(other)).Single().Statement);
    }

    // ---- Reordering ----

    [Fact]
    public async Task AnOrder_IsReflectedOnTheNextRead_AndKeepsPositionsOneToN()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");
        var c = await h.AddAsync(ambition, "c", "c?");

        var put = await h.OrderAsync(ambition, c.Id, a.Id, b.Id);

        Assert.Equal(new[] { "c", "a", "b" }, put.Select(x => x.Statement));
        var read = await h.GetAsync(ambition);
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, read.Select(x => x.Id));
        Assert.Equal(new[] { 1, 2, 3 }, read.Select(x => x.Position));
    }

    [Fact]
    public async Task AnOrder_WritesOneEvent_WithTheOldAndNewIds()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");

        await h.OrderAsync(ambition, b.Id, a.Id);

        var evt = (await h.EventsAsync(ambition)).Last();
        Assert.Equal(EfHatchIssueEvent.CapabilityMoved, evt.Kind);
        var payload = evt.Payload!.Value;
        Assert.Equal(new[] { a.Id, b.Id }, payload.GetProperty("from").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(new[] { b.Id, a.Id }, payload.GetProperty("to").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(3, (await h.EventsAsync(ambition)).Count);
    }

    [Fact]
    public async Task AnOrderAlreadyInForce_WritesNoEvent()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");
        var was = (await h.ReadAsync(ambition)).UpdatedAt;

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.OrderAsync(ambition, a.Id, b.Id);

        Assert.Equal(2, (await h.EventsAsync(ambition)).Count);
        Assert.Equal(was, (await h.ReadAsync(ambition)).UpdatedAt);
    }

    [Fact]
    public async Task AnOrderThatMissesAnId_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        await h.AddAsync(ambition, "b", "b?");

        var result = await h.Controller.PutOrder(ambition, new CapabilityOrderRequest([a.Id]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(new[] { "a", "b" }, (await h.GetAsync(ambition)).Select(x => x.Statement));
        Assert.Equal(2, (await h.EventsAsync(ambition)).Count);
    }

    [Fact]
    public async Task AnOrderThatRepeatsAnId_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        await h.AddAsync(ambition, "b", "b?");

        var result = await h.Controller.PutOrder(ambition, new CapabilityOrderRequest([a.Id, a.Id]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(new[] { "a", "b" }, (await h.GetAsync(ambition)).Select(x => x.Statement));
    }

    [Fact]
    public async Task AnOrderNamingAForeignId_IsRefused_AndChangesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var other = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        var foreign = await h.AddAsync(other, "theirs", "theirs?");

        var result = await h.Controller.PutOrder(ambition, new CapabilityOrderRequest([foreign.Id]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(new[] { a.Id }, (await h.GetAsync(ambition)).Select(x => x.Id));
        Assert.Equal(new[] { foreign.Id }, (await h.GetAsync(other)).Select(x => x.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    public async Task AnOrderNamingAnIdThatIsNowhere_IsRefused_AndChangesNothing(long id)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PutOrder(ambition, new CapabilityOrderRequest([id]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(new[] { "a" }, (await h.GetAsync(ambition)).Select(x => x.Statement));
    }

    // ---- Removing ----

    [Fact]
    public async Task ARemove_DropsItFromTheNextRead_AndLeavesNoGap()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");
        await h.AddAsync(ambition, "c", "c?");

        Assert.IsType<NoContentResult>(await h.Controller.DeleteCapability(ambition, b.Id, default));

        var read = await h.GetAsync(ambition);
        Assert.Equal(new[] { "a", "c" }, read.Select(c => c.Statement));
        Assert.Equal(new[] { 1, 2 }, read.Select(c => c.Position));
    }

    [Fact]
    public async Task ARemove_WritesOneEvent_WithTheIdPositionAndStatement()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");

        await h.Controller.DeleteCapability(ambition, b.Id, default);

        var evt = (await h.EventsAsync(ambition)).Last();
        Assert.Equal(EfHatchIssueEvent.CapabilityRemoved, evt.Kind);
        var payload = evt.Payload!.Value;
        Assert.Equal(b.Id, payload.GetProperty("id").GetInt64());
        Assert.Equal(2, payload.GetProperty("position").GetInt32());
        Assert.Equal("b", payload.GetProperty("statement").GetString());
        Assert.Equal("b?", payload.GetProperty("howToTell").GetString());
    }

    [Fact]
    public async Task ARemoveOfAnUnknownId_IsNotFound_AndWritesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        Assert.IsType<NotFoundResult>(await h.Controller.DeleteCapability(ambition, 9999, default));
        Assert.Empty(await h.EventsAsync(ambition));
    }

    [Fact]
    public async Task PositionsStayDense_ThroughAnAdd_ARemoveAndAReorder()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        await h.AddAsync(ambition, "b", "b?");
        await h.AddAsync(ambition, "c", "c?");
        await h.AddAsync(ambition, "d", "d?", position: 1);
        await h.Controller.DeleteCapability(ambition, a.Id, default);
        await h.OrderAsync(ambition, (await h.GetAsync(ambition)).Select(x => x.Id).Reverse().ToArray());

        var stored = await h.Db.Capabilities.AsNoTracking().OrderBy(x => x.Position).ToListAsync();

        Assert.Equal(new[] { 1, 2, 3 }, stored.Select(x => x.Position));
        Assert.Equal(new[] { "c", "b", "d" }, stored.Select(x => x.Statement));
    }

    // ---- Verdicts ----

    [Theory]
    [InlineData(EfHatchCapabilityVerdict.Holds)]
    [InlineData(EfHatchCapabilityVerdict.DoesNotHold)]
    [InlineData(EfHatchCapabilityVerdict.CannotTell)]
    public async Task AVerdict_WritesARow_AndOneCapabilityJudgedEvent(string verdict)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var written = await h.JudgeAsync(ambition, row.Id, verdict, "the code", "abc1234");

        Assert.Equal(verdict, written.Verdict);
        Assert.Equal("the code", written.LookedAt);
        Assert.Equal("abc1234", written.Sha);
        Assert.Equal("a person", written.Author);
        Assert.Equal(verdict, Assert.Single(await h.VerdictsAsync(ambition, row.Id)).Verdict);

        var evt = Assert.Single(await h.EventsAsync(ambition), e => e.Kind == EfHatchIssueEvent.CapabilityJudged);
        var payload = evt.Payload!.Value;
        Assert.Equal(row.Id, payload.GetProperty("id").GetInt64());
        Assert.Equal(1, payload.GetProperty("position").GetInt32());
        Assert.Equal(verdict, payload.GetProperty("verdict").GetString());
        Assert.Equal("abc1234", payload.GetProperty("sha").GetString());
        Assert.Equal("a person", payload.GetProperty("author").GetString());
    }

    [Fact]
    public async Task AVerdictWithAnUnknownWord_IsRefused_NamingTheThree_AndWritesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PostVerdict(ambition, row.Id, new CapabilityVerdictRequest("maybe", "x", "abc"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("a verdict is one of holds, does_not_hold and cannot_tell", bad.Value);
        Assert.Empty(await h.VerdictsAsync(ambition, row.Id));
    }

    [Theory]
    [InlineData("", "abc")]
    [InlineData("  ", "abc")]
    public async Task AVerdictWithABlankLookedAt_IsRefused_AndWritesNothing(string lookedAt, string sha)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PostVerdict(ambition, row.Id, new CapabilityVerdictRequest("holds", lookedAt, sha), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("lookedAt may not be blank", bad.Value);
        Assert.Empty(await h.VerdictsAsync(ambition, row.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AVerdictWithABlankSha_IsRefused_AndWritesNothing(string sha)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PostVerdict(ambition, row.Id, new CapabilityVerdictRequest("holds", "x", sha), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("sha may not be blank", bad.Value);
        Assert.Empty(await h.VerdictsAsync(ambition, row.Id));
    }

    [Fact]
    public async Task AVerdictWithAnOverLongLookedAt_IsRefused_NamingTheBound()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PostVerdict(
            ambition, row.Id, new CapabilityVerdictRequest("holds", new string('x', EfHatchCapabilityVerdict.MaxLookedAtLength + 1), "abc"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"lookedAt is at most {EfHatchCapabilityVerdict.MaxLookedAtLength} characters", bad.Value);
        Assert.Empty(await h.VerdictsAsync(ambition, row.Id));
    }

    [Fact]
    public async Task AVerdictWithAnOverLongSha_IsRefused_NamingTheBound()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var result = await h.Controller.PostVerdict(
            ambition, row.Id, new CapabilityVerdictRequest("holds", "x", new string('a', EfHatchCapabilityVerdict.MaxShaLength + 1)), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"sha is at most {EfHatchCapabilityVerdict.MaxShaLength} characters", bad.Value);
        Assert.Empty(await h.VerdictsAsync(ambition, row.Id));
    }

    [Fact]
    public async Task AVerdictOnAnUnknownCapability_IsNotFound()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var result = await h.Controller.PostVerdict(ambition, 9999, new CapabilityVerdictRequest("holds", "x", "abc"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AVerdictOnAnotherAmbitionsCapability_IsNotFound()
    {
        var h = await NewAsync();
        var one = await h.FileAsync("ambition");
        var other = await h.FileAsync("ambition");
        var theirs = await h.AddAsync(other, "theirs", "theirs?");

        var result = await h.Controller.PostVerdict(one, theirs.Id, new CapabilityVerdictRequest("holds", "x", "abc"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AKey_MayWriteAVerdict_PastTheImplementationColumn()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");
        h.ActAsKey();

        foreach (var column in new[] { h.Review, h.Done, h.Parked })
        {
            await h.PutInAsync(ambition, column);
            await h.JudgeAsync(ambition, row.Id, EfHatchCapabilityVerdict.Holds);
        }

        Assert.Equal(3, (await h.VerdictsAsync(ambition, row.Id)).Length);
    }

    [Fact]
    public async Task GetVerdicts_ListsNewestFirst()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var first = await h.JudgeAsync(ambition, row.Id, EfHatchCapabilityVerdict.CannotTell);
        var second = await h.JudgeAsync(ambition, row.Id, EfHatchCapabilityVerdict.Holds);

        Assert.Equal([second.Id, first.Id], (await h.VerdictsAsync(ambition, row.Id)).Select(v => v.Id));
    }

    [Fact]
    public async Task ANewCapability_ReadsNotChecked_WithNoNewestVerdict()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        var added = await h.AddAsync(ambition, "a", "a?");

        Assert.Equal("not_checked", added.State);
        Assert.Null(added.NewestVerdict);
    }

    [Fact]
    public async Task ACapabilityWithOneVerdict_ReadsItsState()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        var written = await h.JudgeAsync(ambition, row.Id, EfHatchCapabilityVerdict.Holds);

        var read = (await h.GetAsync(ambition)).Single();
        Assert.Equal(EfHatchCapabilityVerdict.Holds, read.State);
        Assert.Equal(written.Id, read.NewestVerdict!.Id);
    }

    [Fact]
    public async Task ACapabilityWithTwoVerdicts_ReadsTheSecondsState()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");

        await h.JudgeAsync(ambition, row.Id, EfHatchCapabilityVerdict.CannotTell);
        var second = await h.JudgeAsync(ambition, row.Id, EfHatchCapabilityVerdict.Holds);

        var read = (await h.GetAsync(ambition)).Single();
        Assert.Equal(EfHatchCapabilityVerdict.Holds, read.State);
        Assert.Equal(second.Id, read.NewestVerdict!.Id);
    }

    [Fact]
    public async Task TwoCapabilities_KeepSeparateStates()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");

        await h.JudgeAsync(ambition, a.Id, EfHatchCapabilityVerdict.Holds);

        var read = await h.GetAsync(ambition);
        Assert.Equal(EfHatchCapabilityVerdict.Holds, read.Single(c => c.Id == a.Id).State);
        Assert.Equal("not_checked", read.Single(c => c.Id == b.Id).State);
    }

    [Fact]
    public async Task PatchAndPutOrder_AnswersCarryState()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.AddAsync(ambition, "a", "a?");
        var b = await h.AddAsync(ambition, "b", "b?");
        await h.JudgeAsync(ambition, a.Id, EfHatchCapabilityVerdict.Holds);

        var patched = await h.EditAsync(ambition, a.Id, statement: "a, edited");
        Assert.Equal(EfHatchCapabilityVerdict.Holds, patched.State);

        var ordered = await h.OrderAsync(ambition, b.Id, a.Id);
        Assert.Equal(EfHatchCapabilityVerdict.Holds, ordered.Single(c => c.Id == a.Id).State);
        Assert.Equal("not_checked", ordered.Single(c => c.Id == b.Id).State);
    }

    // ---- Not an ambition ----

    [Theory]
    [InlineData("task")]
    [InlineData("story")]
    [InlineData("epic")]
    public async Task EveryRoute_OnAnIssueThatIsNotAnAmbition_IsRefused_NamingItsType_AndChangesNothing(string type)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(type);
        var expected = $"only an ambition takes capabilities - {issue} is a {type}";

        var results = new IActionResult?[]
        {
            (await h.Controller.GetCapabilities(issue, default)).Result,
            (await h.Controller.PostCapability(issue, new CapabilityCreateRequest("s", "h"), default)).Result,
            (await h.Controller.PatchCapability(issue, 1, new CapabilityPatchRequest("s"), default)).Result,
            (await h.Controller.PutOrder(issue, new CapabilityOrderRequest([]), default)).Result,
            await h.Controller.DeleteCapability(issue, 1, default),
            (await h.Controller.GetVerdicts(issue, 1, default)).Result,
            (await h.Controller.PostVerdict(issue, 1, new CapabilityVerdictRequest("holds", "x", "abc"), default)).Result,
        };

        foreach (var result in results)
        {
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(expected, bad.Value);
        }

        Assert.Empty(await h.Db.Capabilities.ToListAsync());
        Assert.Empty(await h.EventsAsync(issue));
    }

    // ---- The column line for a key ----

    [Fact]
    public async Task AKey_MayReadAnywhere_ButWriteOnlyLeftOfTheImplementationColumn()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var seeded = await h.AddAsync(ambition, "a", "a?");
        h.ActAsKey();

        foreach (var (column, name) in new[] { (h.Doing, "implementation"), (h.Review, "past it"), (h.Done, "terminal"), (h.Parked, "deferred") })
        {
            await h.PutInAsync(ambition, column);

            Assert.True((await h.GetAsync(ambition)).Length >= 1, $"a key reads in {name}");
            await AssertRefusedAsync(h, ambition, seeded.Id, name);
        }

        Assert.Single(await h.GetAsync(ambition));
        Assert.Single(await h.EventsAsync(ambition));

        foreach (var column in new[] { h.Inbox, h.Todo })
        {
            await h.PutInAsync(ambition, column);

            var added = await h.AddAsync(ambition, "from a key", "k?");
            await h.EditAsync(ambition, added.Id, statement: "edited by a key");
            await h.OrderAsync(ambition, added.Id, seeded.Id);
            Assert.IsType<NoContentResult>(await h.Controller.DeleteCapability(ambition, added.Id, default));
        }
    }

    [Fact]
    public async Task AKey_IsRefusedAt403_WithTheSentence()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        h.ActAsKey();
        await h.PutInAsync(ambition, h.Review);

        var result = await h.Controller.PostCapability(ambition, new CapabilityCreateRequest("s", "h"), default);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, refused.StatusCode);
        Assert.Equal("writing a capability past the implementation column is a person's call, not an agent's", refused.Value);

        await h.PutInAsync(ambition, h.Parked);
        var deferred = Assert.IsType<ObjectResult>((await h.Controller.PostCapability(ambition, new CapabilityCreateRequest("s", "h"), default)).Result);
        Assert.Equal(403, deferred.StatusCode);
    }

    [Fact]
    public async Task APerson_IsNeverRefused_OnTheColumnLine()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        foreach (var column in new[] { h.Todo, h.Doing, h.Review, h.Done, h.Parked })
        {
            await h.PutInAsync(ambition, column);

            var added = await h.AddAsync(ambition, "from a person", "p?");
            await h.EditAsync(ambition, added.Id, statement: "edited");
            await h.OrderAsync(ambition, (await h.GetAsync(ambition)).Select(x => x.Id).Reverse().ToArray());
            Assert.IsType<NoContentResult>(await h.Controller.DeleteCapability(ambition, added.Id, default));
        }
    }

    private static async Task AssertRefusedAsync(Harness h, string ambition, long id, string where)
    {
        var results = new object?[]
        {
            (await h.Controller.PostCapability(ambition, new CapabilityCreateRequest("s", "h"), default)).Result,
            (await h.Controller.PatchCapability(ambition, id, new CapabilityPatchRequest("s"), default)).Result,
            (await h.Controller.PutOrder(ambition, new CapabilityOrderRequest([id]), default)).Result,
            await h.Controller.DeleteCapability(ambition, id, default),
        };

        foreach (var result in results)
        {
            var refused = Assert.IsType<ObjectResult>(result);
            Assert.True(refused.StatusCode == 403, $"a key is refused with 403 in the {where} column");
        }
    }

    // ---- Deleting the ambition ----

    [Fact]
    public async Task DeletingTheAmbition_RemovesItsCapabilities_AndOnlyItsOwn()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var other = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");
        await h.AddAsync(ambition, "b", "b?");
        await h.AddAsync(other, "theirs", "theirs?");

        Assert.IsType<NoContentResult>(await h.Issues.DeleteIssue(ambition, default));

        var left = await h.Db.Capabilities.AsNoTracking().ToListAsync();
        Assert.Equal(new[] { "theirs" }, left.Select(c => c.Statement));
    }

    // ---- Retyping an ambition ----

    [Fact]
    public async Task RetypingAnAmbitionThatHoldsCapabilities_IsRefused_CountingThem()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.AddAsync(ambition, "a", "a?");
        await h.AddAsync(ambition, "b", "b?");

        var two = await h.RetypeAsync(ambition, "epic");
        Assert.Equal($"{ambition} holds 2 capabilities, so it cannot stop being an ambition", two);

        var single = await h.FileAsync("ambition");
        await h.AddAsync(single, "only", "only?");
        Assert.Equal($"{single} holds 1 capability, so it cannot stop being an ambition", await h.RetypeAsync(single, "epic"));

        Assert.Equal("ambition", (await h.ReadAsync(ambition)).Type);
    }

    [Fact]
    public async Task TheRetypeRefusal_ClearsOnceEveryCapabilityIsRemoved()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var row = await h.AddAsync(ambition, "a", "a?");
        Assert.NotNull(await h.RetypeAsync(ambition, "epic"));

        await h.Controller.DeleteCapability(ambition, row.Id, default);

        Assert.Null(await h.RetypeAsync(ambition, "epic"));
        Assert.Equal("epic", (await h.ReadAsync(ambition)).Type);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueCapabilitiesController Controller { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int Inbox { get; init; }
        public required int Todo { get; init; }
        public required int Doing { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }
        public required int Parked { get; init; }

        /// <summary>Files an issue of <paramref name="type"/> and answers its key.</summary>
        public async Task<string> FileAsync(string type)
        {
            var created = Created(await Issues.CreateIssue(
                new IssueCreateRequest(ProjectId, type, $"a {type}", null, null, null, null), default));
            return created.Key;
        }

        public async Task PutInAsync(string key, int statusId)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            var issue = await Db.Issues.WithKey(projectKey, number).SingleAsync();
            issue.StatusId = statusId;
            await Db.SaveChangesAsync();
        }

        public void ActAsKey()
        {
            Caller.Person = null;
            Caller.Key = new EfApiKey { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };
        }

        public async Task<CapabilityDto[]> GetAsync(string key) => Value(await Controller.GetCapabilities(key, default));

        public async Task<CapabilityDto> AddAsync(string key, string statement, string howToTell, int? position = null)
        {
            var result = await Controller.PostCapability(key, new CapabilityCreateRequest(statement, howToTell, position), default);
            return result.Result is CreatedAtActionResult created
                ? (CapabilityDto)created.Value!
                : throw new InvalidOperationException($"expected a created capability, got {result.Result}");
        }

        public async Task<CapabilityDto> EditAsync(string key, long id, string? statement = null, string? howToTell = null) =>
            Value(await Controller.PatchCapability(key, id, new CapabilityPatchRequest(statement, howToTell), default));

        public async Task<CapabilityDto[]> OrderAsync(string key, params long[] ids) =>
            Value(await Controller.PutOrder(key, new CapabilityOrderRequest(ids), default));

        public async Task<CapabilityVerdictDto> JudgeAsync(string key, long id, string verdict, string lookedAt = "the code", string sha = "abc1234")
        {
            var result = await Controller.PostVerdict(key, id, new CapabilityVerdictRequest(verdict, lookedAt, sha), default);
            return result.Result is CreatedAtActionResult created
                ? (CapabilityVerdictDto)created.Value!
                : throw new InvalidOperationException($"expected a created verdict, got {result.Result}");
        }

        public async Task<CapabilityVerdictDto[]> VerdictsAsync(string key, long id) => Value(await Controller.GetVerdicts(key, id, default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The refusal a retype got, or null when it went through.</summary>
        public async Task<string?> RetypeAsync(string key, string type)
        {
            var result = await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, type, null, null, null, null, null, null, null), default);
            return result.Result is ObjectResult { Value: string refusal } ? refusal : null;
        }

        /// <summary>The ambition's capability events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind.StartsWith("capability_", StringComparison.Ordinal))
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 40 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 90, IsTerminal = true };
        var parked = new EfHatchStatus { Name = "parked", SortOrder = 100, IsDeferred = true };
        db.AddRange(inbox, todo, doing, review, done, parked);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Controller = new IssueCapabilitiesController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
            Inbox = inbox.Id,
            Todo = todo.Id,
            Doing = doing.Id,
            Review = review.Id,
            Done = done.Id,
            Parked = parked.Id,
        };
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

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

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}
