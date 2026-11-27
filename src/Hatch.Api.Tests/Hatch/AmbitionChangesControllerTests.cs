using System.Text.Json;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What changed under the reader's ambitions since an instant: four kinds of
/// line, each pinned with its fields, then the cap, the netting rule, the scope
/// and the three ways the instant is found.
///
/// The harness writes what the live routes write: a <c>created</c> event for
/// every issue and a <c>capability_judged</c> event for every verdict, each at
/// the time the test names, since the read starts from those events.
/// </summary>
public class AmbitionChangesControllerTests
{
    // ---- holds ----

    [Fact]
    public async Task ACapabilityThatCameToHoldInsideTheWindow_IsAHoldsLine_WithItsStatementAndWhatWasLookedAt()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "the ambition", At(1));
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.DoesNotHold, At(2));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(8, 15), lookedAt: "the page at 390px");

        var line = Assert.Single((await h.ReadAsync(Since)).Lines);

        Assert.Equal("holds", line.Kind);
        Assert.Equal(At(8, 15), line.At);
        Assert.Equal(h.Key(ambition), line.AmbitionKey);
        Assert.Equal("the ambition", line.AmbitionTitle);
        Assert.Equal(h.Key(ambition), line.IssueKey);
        Assert.Equal("the ambition", line.IssueTitle);
        Assert.Equal("capability 1", line.Statement);
        Assert.Equal("the page at 390px", line.LookedAt);
        Assert.Null(line.Serves);
        Assert.Null(line.WentAheadOn);
        Assert.Null(line.Question);
        Assert.Null(line.Count);
    }

    [Fact]
    public async Task ACapabilityWithNoVerdictBeforeTheWindow_ThatHoldsInsideIt_IsALine()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        await h.VerdictAsync(await h.CapabilityAsync(ambition, 1), EfHatchCapabilityVerdict.Holds, At(8, 12));

        Assert.Equal("holds", Assert.Single((await h.ReadAsync(Since)).Lines).Kind);
    }

    [Fact]
    public async Task ACapabilityThatHeldThenStoppedHoldingInsideTheWindow_DrawsNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(8, 12));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.DoesNotHold, At(9));

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task ACapabilityThatHeldBeforeTheWindow_AndHeldAgainInsideIt_DrawsNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(3));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(8, 12));

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task ACapabilityThatHeldBeforeTheWindow_AndDippedAndRecoveredInsideIt_DrawsNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var capability = await h.CapabilityAsync(ambition, 1);
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(3));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.DoesNotHold, At(8, 10));
        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(9));

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task ACapabilityThatCameToHoldBeforeTheWindow_DrawsNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        await h.VerdictAsync(await h.CapabilityAsync(ambition, 1), EfHatchCapabilityVerdict.Holds, At(3));

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    // ---- carved ----

    [Fact]
    public async Task AnEpicFiledUnderAnAmbitionInsideTheWindow_IsACarvedLine_WithThePositionsItServes()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "the ambition", At(1));
        var one = await h.CapabilityAsync(ambition, 1);
        await h.CapabilityAsync(ambition, 2);
        var three = await h.CapabilityAsync(ambition, 3);
        var epic = await h.FileAsync("epic", "the epic", At(8, 14), parentId: ambition.Id);
        await h.ServeAsync(epic, three);
        await h.ServeAsync(epic, one);

        var line = Assert.Single((await h.ReadAsync(Since)).Lines);

        Assert.Equal("carved", line.Kind);
        Assert.Equal(At(8, 14), line.At);
        Assert.Equal(h.Key(ambition), line.AmbitionKey);
        Assert.Equal("the ambition", line.AmbitionTitle);
        Assert.Equal(h.Key(epic), line.IssueKey);
        Assert.Equal("the epic", line.IssueTitle);
        Assert.Equal([1, 3], line.Serves);
        Assert.Null(line.Statement);
        Assert.Null(line.LookedAt);
        Assert.Null(line.WentAheadOn);
        Assert.Null(line.Question);
        Assert.Null(line.Count);
    }

    [Fact]
    public async Task AnEpicThatServesNothing_IsACarvedLineWithAnEmptyList()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        await h.FileAsync("epic", "e", At(8, 14), parentId: ambition.Id);

        Assert.Empty(Assert.Single((await h.ReadAsync(Since)).Lines).Serves!);
    }

    [Fact]
    public async Task AnEpicFiledBeforeTheWindow_AndOneBeneathAnotherKindOfIssue_AreNotCarved()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        await h.FileAsync("epic", "old", At(3), parentId: ambition.Id);
        var story = await h.FileAsync("story", "s", At(8, 10), parentId: ambition.Id);
        await h.FileAsync("task", "t", At(8, 11), parentId: story.Id);

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task AnEpicFiledElsewhereBeforeTheWindow_AndMovedUnderAnAmbitionInsideIt_IsNotALine()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var epic = await h.FileAsync("epic", "e", At(3));
        epic.ParentId = ambition.Id;
        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = epic.Id, Actor = "operator", Kind = EfHatchIssueEvent.ParentChanged, At = At(8, 11),
            Payload = JsonSerializer.Serialize(new { to = ambition.Id }),
        });
        await h.Db.SaveChangesAsync();

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task ACapabilityOfAnotherAmbition_IsNotAPositionTheEpicServes()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("ambition", "mine", At(1));
        var theirs = await h.FileAsync("ambition", "theirs", At(1));
        await h.CapabilityAsync(mine, 1);
        var foreign = await h.CapabilityAsync(theirs, 1);
        var epic = await h.FileAsync("epic", "e", At(8, 10), parentId: mine.Id);
        await h.ServeAsync(epic, foreign);

        Assert.Empty(Assert.Single((await h.ReadAsync(Since)).Lines).Serves!);
    }

    // ---- asked ----

    [Fact]
    public async Task TwoHoldQuestionsOnOneIssue_FoldIntoOneLine_WithACount_AndTheNewestOnesWords()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "the ambition", At(1));
        var epic = await h.FileAsync("epic", "the epic", At(2), parentId: ambition.Id);
        await h.AskAsync(epic, "first?\nmore", At(8, 10));
        await h.AskAsync(epic, "\n  second?  \nand more", At(8, 13), level: QuestionLevels.HoldName);

        var line = Assert.Single((await h.ReadAsync(Since)).Lines);

        Assert.Equal("asked", line.Kind);
        Assert.Equal(At(8, 13), line.At);
        Assert.Equal(h.Key(ambition), line.AmbitionKey);
        Assert.Equal("the ambition", line.AmbitionTitle);
        Assert.Equal(h.Key(epic), line.IssueKey);
        Assert.Equal("the epic", line.IssueTitle);
        Assert.Equal("second?", line.Question);
        Assert.Equal(2, line.Count);
        Assert.Null(line.Statement);
        Assert.Null(line.LookedAt);
        Assert.Null(line.Serves);
        Assert.Null(line.WentAheadOn);
    }

    [Fact]
    public async Task AQuestionOnTheAmbitionItself_IsAskedBeneathIt()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        await h.AskAsync(ambition, "which way?", At(8, 10));

        var line = Assert.Single((await h.ReadAsync(Since)).Lines);

        Assert.Equal((h.Key(ambition), h.Key(ambition)), (line.AmbitionKey, line.IssueKey));
    }

    [Fact]
    public async Task OnlyAnOpenHoldQuestionAskedInsideTheWindowBeneathAnAmbition_IsAsked()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var epic = await h.FileAsync("epic", "e", At(2), parentId: ambition.Id);
        var elsewhere = await h.FileAsync("epic", "not beneath any ambition", At(2));
        await h.AskAsync(epic, "at build?", At(8, 10), level: QuestionLevels.BuildName);
        await h.AskAsync(epic, "at merge?", At(8, 10), level: QuestionLevels.MergeName);
        await h.AskAsync(epic, "asked before?", At(3));
        var answered = await h.AskAsync(epic, "answered?", At(8, 11));
        await h.AnswerAsync(answered, ActorKind.Person, At(8, 12), "yes");
        await h.AskAsync(elsewhere, "who cares?", At(8, 10));

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    // ---- decided ----

    [Fact]
    public async Task AMergeQuestionAKeyAnsweredInsideTheWindow_IsADecidedLine_WithWhatItWentAheadOn()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "the ambition", At(1));
        var epic = await h.FileAsync("epic", "the epic", At(2), parentId: ambition.Id);
        var question = await h.AskAsync(epic, "which store?", At(7), level: QuestionLevels.MergeName);
        await h.AnswerAsync(question, ActorKind.Key, At(8, 9), "the first store\nbecause it is there");
        await h.AnswerAsync(question, ActorKind.Key, At(8, 16), "still the first store");

        var line = Assert.Single((await h.ReadAsync(Since)).Lines);

        Assert.Equal("decided", line.Kind);
        Assert.Equal(At(8, 16), line.At);
        Assert.Equal(h.Key(ambition), line.AmbitionKey);
        Assert.Equal("the ambition", line.AmbitionTitle);
        Assert.Equal(h.Key(epic), line.IssueKey);
        Assert.Equal("the epic", line.IssueTitle);
        Assert.Equal("the first store", line.WentAheadOn);
        Assert.Null(line.Statement);
        Assert.Null(line.LookedAt);
        Assert.Null(line.Serves);
        Assert.Null(line.Question);
        Assert.Null(line.Count);
    }

    [Fact]
    public async Task AnUnansweredMergeQuestionWhoseIssueClosedInsideTheWindow_IsDecided_DatedByTheClose_OnTheRecommendedOption()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var epic = await h.FileAsync("epic", "e", At(2), parentId: ambition.Id);
        await h.AskAsync(epic, "which store?", At(7), level: QuestionLevels.MergeName,
            options: [new QuestionOptionDto("the other store"), new QuestionOptionDto("the first store", Recommended: true)]);
        await h.CloseAsync(epic, At(9));

        var line = Assert.Single((await h.ReadAsync(Since)).Lines);

        Assert.Equal("decided", line.Kind);
        Assert.Equal(At(9), line.At);
        Assert.Equal("the first store", line.WentAheadOn);
    }

    [Fact]
    public async Task ADecisionAPersonAnsweredAfterwards_DropsOut()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var epic = await h.FileAsync("epic", "e", At(2), parentId: ambition.Id);
        var question = await h.AskAsync(epic, "which store?", At(7), level: QuestionLevels.MergeName);
        await h.AnswerAsync(question, ActorKind.Key, At(8, 9), "the first store");
        Assert.Single((await h.ReadAsync(Since)).Lines);

        await h.AnswerAsync(question, ActorKind.Person, At(9), "no, the other");

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task ADecisionBeforeTheWindow_AndAHoldQuestionAKeyAnswered_AreNotDecided()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var epic = await h.FileAsync("epic", "e", At(2), parentId: ambition.Id);
        var early = await h.AskAsync(epic, "early?", At(3), level: QuestionLevels.MergeName);
        await h.AnswerAsync(early, ActorKind.Key, At(4), "yes");
        var hold = await h.AskAsync(epic, "hold?", At(7), level: QuestionLevels.HoldName);
        await h.AnswerAsync(hold, ActorKind.Key, At(8, 9), "yes");

        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    // ---- the answer as a whole ----

    [Fact]
    public async Task LinesAreNewestFirst_ThenByKind_ThenByKey()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var capability = await h.CapabilityAsync(ambition, 1);
        var epicA = await h.FileAsync("epic", "a", At(2), parentId: ambition.Id);
        var epicB = await h.FileAsync("epic", "b", At(2), parentId: ambition.Id);

        await h.VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(8, 12));
        var carvedA = await h.FileAsync("epic", "carved a", At(8, 12), parentId: ambition.Id);
        var carvedB = await h.FileAsync("epic", "carved b", At(8, 12), parentId: ambition.Id);
        await h.AskAsync(epicB, "ask b", At(8, 12));
        await h.AskAsync(epicA, "ask a", At(8, 12));
        await h.AskAsync(epicA, "newest", At(9));

        var lines = (await h.ReadAsync(Since)).Lines;

        Assert.Equal(
            [("asked", h.Key(epicA)), ("holds", h.Key(ambition)), ("carved", h.Key(carvedA)), ("carved", h.Key(carvedB)), ("asked", h.Key(epicB))],
            lines.Select(l => (l.Kind, l.IssueKey)));
        Assert.Equal(At(9), lines[0].At);
    }

    [Fact]
    public async Task ThirtyOneLines_GiveThirty_AndOmittedOne_TheOldestLeftOut()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var epics = new List<EfHatchIssue>();
        for (var n = 0; n < 31; n++)
            epics.Add(await h.FileAsync("epic", $"epic {n}", At(8, 10).AddMinutes(n), parentId: ambition.Id));

        var answer = await h.ReadAsync(Since);

        Assert.Equal(30, WhatChanged.MaxLines);
        Assert.Equal(30, answer.Lines.Count);
        Assert.Equal(1, answer.Omitted);
        Assert.DoesNotContain(answer.Lines, l => l.IssueKey == h.Key(epics[0]));
        Assert.Equal(h.Key(epics[30]), answer.Lines[0].IssueKey);
    }

    [Fact]
    public async Task ThirtyLines_LeaveNothingOmitted()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        for (var n = 0; n < 30; n++)
            await h.FileAsync("epic", $"epic {n}", At(8, 10).AddMinutes(n), parentId: ambition.Id);

        var answer = await h.ReadAsync(Since);

        Assert.Equal((30, 0), (answer.Lines.Count, answer.Omitted));
    }

    [Fact]
    public async Task NothingChanged_IsAnEmptyAnswer_AndSoIsABoardWithNoAmbition()
    {
        var h = await NewAsync();
        var empty = await h.ReadAsync(Since);
        Assert.Empty(empty.Lines);
        Assert.Equal(0, empty.Omitted);

        await h.FileAsync("ambition", "a", At(1));
        Assert.Empty((await h.ReadAsync(Since)).Lines);
    }

    [Fact]
    public async Task AProjectId_NarrowsAllFourKinds_ByTheAmbitionsProject()
    {
        var h = await NewAsync();
        var mine = await h.SeedOneOfEachAsync("mine");
        var theirs = await h.SeedOneOfEachAsync("theirs", project: h.Other);

        var all = (await h.ReadAsync(Since)).Lines;
        var narrowed = (await h.ReadAsync(Since, h.ProjectId)).Lines;
        var other = (await h.ReadAsync(Since, h.Other)).Lines;
        var both = (await h.ReadAsync(Since, h.ProjectId, h.Other)).Lines;

        Assert.Equal(8, all.Count);
        Assert.Equal(["asked", "carved", "decided", "holds"], narrowed.Select(l => l.Kind).Order());
        Assert.All(narrowed, l => Assert.Equal(h.Key(mine), l.AmbitionKey));
        Assert.Equal(["asked", "carved", "decided", "holds"], other.Select(l => l.Kind).Order());
        Assert.All(other, l => Assert.Equal(h.Key(theirs), l.AmbitionKey));
        Assert.Equal(8, both.Count);
        Assert.Empty((await h.ReadAsync(Since, 9999)).Lines);
    }

    // ---- the instant ----

    [Fact]
    public async Task AKeysSince_IsGiven_AndIsReadToTheTick()
    {
        var h = await NewAsync(asKey: true);
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var before = await h.FileAsync("epic", "before", At(8, 9).AddMilliseconds(-1), parentId: ambition.Id);
        var at = await h.FileAsync("epic", "at", At(8, 9), parentId: ambition.Id);

        var answer = await h.ReadAsync("2026-03-08T09:00:00.000Z");

        Assert.Equal("given", answer.Basis);
        Assert.Equal(Since, answer.Since);
        Assert.Equal([h.Key(at)], answer.Lines.Select(l => l.IssueKey));
        Assert.DoesNotContain(answer.Lines, l => l.IssueKey == h.Key(before));

        var tick = await h.ReadAsync("2026-03-08T09:00:00.0005Z");
        Assert.Equal(Since.AddTicks(5_000), tick.Since);
    }

    [Fact]
    public async Task ASinceWithNoOffset_IsReadAsUtc_AndOneWithAnOffsetIsMovedToIt()
    {
        var h = await NewAsync();

        Assert.Equal(Since, (await h.ReadAsync("2026-03-08T09:00:00")).Since);
        Assert.Equal(Since, (await h.ReadAsync("2026-03-08T11:00:00+02:00")).Since);
        Assert.Equal(TimeSpan.Zero, (await h.ReadAsync("2026-03-08T11:00:00+02:00")).Since.Offset);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-13-45")]
    [InlineData("1710000000")]
    public async Task ASinceThatIsNotATime_IsA400WithASentence_ForAPersonAndAKey(string since)
    {
        foreach (var asKey in new[] { false, true })
        {
            var h = await NewAsync(asKey);

            var refused = Assert.IsType<BadRequestObjectResult>((await h.Ambitions.GetChanges(since, null, default)).Result);

            Assert.Equal("since is not a time: send an ISO 8601 instant such as 2026-11-20T09:00:00Z", refused.Value);
        }
    }

    [Fact]
    public async Task APersonsReadWithNoSince_TakesTheStoredVisit()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var before = await h.FileAsync("epic", "before the visit", At(8, 9), parentId: ambition.Id);
        var after = await h.FileAsync("epic", "after the visit", At(9, 9), parentId: ambition.Id);
        h.Db.PersonVisits.Add(new EfHatchPersonVisit { PersonId = h.Person!.Id, Page = PersonVisitPage.Ambitions, VisitedAt = At(8, 12) });
        await h.Db.SaveChangesAsync();

        var answer = await h.ReadAsync();

        Assert.Equal("visit", answer.Basis);
        Assert.Equal(At(8, 12), answer.Since);
        Assert.Equal([h.Key(after)], answer.Lines.Select(l => l.IssueKey));
        Assert.DoesNotContain(answer.Lines, l => l.IssueKey == h.Key(before));
    }

    [Fact]
    public async Task ASinceFromAPerson_BeatsTheStoredVisit()
    {
        var h = await NewAsync();
        h.Db.PersonVisits.Add(new EfHatchPersonVisit { PersonId = h.Person!.Id, Page = PersonVisitPage.Ambitions, VisitedAt = At(8, 12) });
        await h.Db.SaveChangesAsync();

        var answer = await h.ReadAsync(Since);

        Assert.Equal(("given", Since), (answer.Basis, answer.Since));
    }

    [Fact]
    public async Task AnotherPersonsVisit_IsNotThisPersons()
    {
        var h = await NewAsync();
        h.Db.PersonVisits.Add(new EfHatchPersonVisit { PersonId = Guid.NewGuid(), Page = PersonVisitPage.Ambitions, VisitedAt = At(8, 12) });
        await h.Db.SaveChangesAsync();

        Assert.Equal("week", (await h.ReadAsync()).Basis);
    }

    [Fact]
    public async Task APersonWithNoVisit_GetsTheLastSevenDays_AndIsToldSo()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition", "a", At(1));
        var inside = await h.FileAsync("epic", "inside the week", Now.AddDays(-6), parentId: ambition.Id);
        await h.FileAsync("epic", "outside the week", Now.AddDays(-8), parentId: ambition.Id);

        var answer = await h.ReadAsync();

        Assert.Equal("week", answer.Basis);
        Assert.Equal(Now.AddDays(-7), answer.Since);
        Assert.Equal([h.Key(inside)], answer.Lines.Select(l => l.IssueKey));
    }

    [Fact]
    public async Task AKeyWithNoSince_GetsTheLastSevenDays_EvenWhereAVisitIsStored()
    {
        var h = await NewAsync(asKey: true);
        h.Db.PersonVisits.Add(new EfHatchPersonVisit { PersonId = Guid.NewGuid(), Page = PersonVisitPage.Ambitions, VisitedAt = At(8, 12) });
        await h.Db.SaveChangesAsync();

        var answer = await h.ReadAsync();

        Assert.Equal(("week", Now.AddDays(-7)), (answer.Basis, answer.Since));
    }

    [Fact]
    public async Task ACallerNobodySignedInAs_GetsTheLastSevenDays()
    {
        var h = await NewAsync(nobody: true);

        Assert.Equal("week", (await h.ReadAsync()).Basis);
    }

    // ---- The harness ----

    private static readonly DateTimeOffset Base = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The 10th of March at nine: when the read is made.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The 8th of March at nine: where most reads in here start.</summary>
    private static readonly DateTimeOffset Since = new(2026, 3, 8, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The given day of March, at the given hour. Day 8 at hour 9 is <see cref="Since"/>.</summary>
    private static DateTimeOffset At(int day, int hour = 0) => Base.AddDays(day - 1).AddHours(hour);

    private class Harness
    {
        public required HatchContext Db { get; init; }
        public required AmbitionsController Ambitions { get; init; }
        public required int ProjectId { get; init; }
        public required int Other { get; init; }
        public required int Todo { get; init; }
        public required int Done { get; init; }
        public EfPerson? Person { get; init; }

        private int next = 1;

        public async Task<EfHatchIssue> FileAsync(string type, string title, DateTimeOffset at, long? parentId = null, int? project = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = project ?? ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = Todo,
                Rank = 1024,
                ParentId = parentId,
                CreatedBy = "operator",
                CreatedAt = at,
                UpdatedAt = at,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();

            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "operator",
                Kind = EfHatchIssueEvent.Created,
                At = at,
                Payload = JsonSerializer.Serialize(new { type, title }),
            });
            await Db.SaveChangesAsync();
            return issue;
        }

        public async Task<EfHatchCapability> CapabilityAsync(EfHatchIssue ambition, int position)
        {
            var capability = new EfHatchCapability
            {
                IssueId = ambition.Id,
                Position = position,
                Statement = $"capability {position}",
                HowToTell = "look",
                CreatedBy = "operator",
                CreatedAt = Base,
                UpdatedAt = Base,
            };

            Db.Capabilities.Add(capability);
            await Db.SaveChangesAsync();
            return capability;
        }

        /// <summary>A verdict and the <c>capability_judged</c> event the route writes beside it.</summary>
        public async Task VerdictAsync(EfHatchCapability capability, string verdict, DateTimeOffset at, string lookedAt = "the page")
        {
            Db.CapabilityVerdicts.Add(new EfHatchCapabilityVerdict
            {
                CapabilityId = capability.Id,
                Verdict = verdict,
                LookedAt = lookedAt,
                Sha = "abc1234",
                Author = "hatch-agent",
                CreatedAt = at,
            });
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = capability.IssueId,
                Actor = "hatch-agent",
                Kind = EfHatchIssueEvent.CapabilityJudged,
                At = at,
                Payload = JsonSerializer.Serialize(new { id = capability.Id, position = capability.Position, verdict, sha = "abc1234", author = "hatch-agent" }),
            });

            await Db.SaveChangesAsync();
        }

        public async Task ServeAsync(EfHatchIssue epic, EfHatchCapability capability)
        {
            Db.CapabilityLinks.Add(new EfHatchCapabilityLink
            {
                CapabilityId = capability.Id,
                IssueId = epic.Id,
                Actor = "operator",
                At = Base,
            });

            await Db.SaveChangesAsync();
        }

        public async Task<EfHatchComment> AskAsync(
            EfHatchIssue issue, string body, DateTimeOffset at, string? level = null, IReadOnlyList<QuestionOptionDto>? options = null)
        {
            var question = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                Level = level,
                Options = Questions.WriteOptions(options),
                CreatedAt = at,
            };

            Db.Comments.Add(question);
            await Db.SaveChangesAsync();
            return question;
        }

        public async Task AnswerAsync(EfHatchComment question, string authorKind, DateTimeOffset at, string body)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = question.IssueId,
                Author = authorKind == ActorKind.Key ? "hatch-agent" : "Ada",
                AuthorKind = authorKind,
                Body = body,
                Kind = EfHatchComment.Answer,
                AnswersId = question.Id,
                CreatedAt = at,
            });

            await Db.SaveChangesAsync();
        }

        public async Task CloseAsync(EfHatchIssue issue, DateTimeOffset at)
        {
            issue.StatusId = Done;
            issue.ClosedAt = at;
            await Db.SaveChangesAsync();
        }

        /// <summary>An ambition with one line of each kind inside the window.</summary>
        public async Task<EfHatchIssue> SeedOneOfEachAsync(string title, int? project = null)
        {
            var ambition = await FileAsync("ambition", title, At(1), project: project);
            var capability = await CapabilityAsync(ambition, 1);
            await VerdictAsync(capability, EfHatchCapabilityVerdict.Holds, At(8, 12));

            await FileAsync("epic", $"{title} carved", At(8, 13), parentId: ambition.Id, project: project);

            var asked = await FileAsync("epic", $"{title} asked", At(2), parentId: ambition.Id, project: project);
            await AskAsync(asked, "which?", At(8, 14));

            var decided = await FileAsync("epic", $"{title} decided", At(2), parentId: ambition.Id, project: project);
            var question = await AskAsync(decided, "which store?", At(7), level: QuestionLevels.MergeName);
            await AnswerAsync(question, ActorKind.Key, At(8, 15), "the first store");

            return ambition;
        }

        public async Task<AmbitionChangesDto> ReadAsync() => Value(await Ambitions.GetChanges(null, null, default));

        public async Task<AmbitionChangesDto> ReadAsync(DateTimeOffset since, params int[] projectIds) =>
            await ReadAsync(since.ToString("o"), projectIds);

        public async Task<AmbitionChangesDto> ReadAsync(string since, params int[] projectIds) =>
            Value(await Ambitions.GetChanges(since, projectIds.Length == 0 ? null : projectIds.ToList(), default));

        /// <summary>The key of an issue filed here, in whichever of the two projects it went to.</summary>
        public string Key(EfHatchIssue issue) =>
            IssueKey.Format(issue.ProjectId == ProjectId ? "AER" : "OPS", issue.Number);
    }

    /// <summary>Two projects, and a board with a column to work in and one to finish in. A signed-in person by default; a key, or nobody, on request.</summary>
    private static async Task<Harness> NewAsync(bool asKey = false, bool nobody = false)
    {
        var db = MeScopeTests.NewContext();

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Base };
        var other = new EfHatchProject { Key = "OPS", Name = "Operations", CreatedAt = Base };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
        db.AddRange(project, other, todo, done);
        await db.SaveChangesAsync();

        var person = asKey || nobody
            ? null
            : new EfPerson { Id = Guid.NewGuid(), Name = "Ada", Role = PersonRole.User, CreatedAt = Base, UpdatedAt = Base };
        var key = asKey ? new EfApiKey { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Base } : null;

        var actors = new StubActorDirectory
        {
            Principal = person is null ? null : new Actor(ActorKind.Person, person.Id, person.Name),
        };

        return new Harness
        {
            Db = db,
            Ambitions = new AmbitionsController(db, actors, new MeScopeTests.StubCaller(null, person, key), new FakeTimeProvider(Now)),
            ProjectId = project.Id,
            Other = other.Id,
            Todo = todo.Id,
            Done = done.Id,
            Person = person,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}
