using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What an unattended run takes off the board, and what it is told to do with
/// it.
///
/// These are the rules that decide how an agent spends money and what it is
/// allowed to change, so each one is pinned rather than trusted to a prompt:
/// the board is worked right to left, a terminal column is never an agent's to
/// enter, a transition with no playbook dispatches nothing at all, and which
/// types a move applies to is that playbook row's to say and nowhere else's.
/// </summary>
public class WorkControllerTests
{
    // ---- The link a session writes into a pull request ----

    [Theory]
    [InlineData("https://home.example.com/", "https://home.example.com/apps/hatch/issues/AER-1")]
    [InlineData("", null)]
    [InlineData("home.example.com", null)]
    public async Task Work_IssueUrl_IsTheConfiguredOriginAndNullWithoutOne(string configured, string? expected)
    {
        var h = await NewAsync(configured);
        var issue = await h.FileAsync("story", "a story", h.InProgress);

        Assert.Equal(expected, Value(await h.Work.GetWork(Key(issue), null, default)).IssueUrl);
    }

    // ---- Picking ----

    [Fact]
    public async Task NextWork_TakesTheRightmostColumnBeforeTheLeftmost()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "sitting in todo", h.Todo);
        var advanced = await h.FileAsync("story", "already underway", h.InProgress);

        var work = Value(await h.Work.GetNextWork(0, null, null, default));

        // Both are workable. The one nearer the end of the board wins, because
        // a board worked left to right starts everything and finishes nothing.
        Assert.Equal(Key(advanced), work.Issue.Key);
        Assert.Equal("review", work.ToStatus!.Name);
    }

    [Fact]
    public async Task NextWork_TakesTheTopOfTheColumn()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "top", h.Todo, rank: 1024);
        await h.FileAsync("story", "below it", h.Todo, rank: 2048);

        Assert.Equal(Key(first), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_SkipsAnIssueWhoseReadyDateHasNotArrived()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "waiting on a renewal", h.Todo, rank: 1024, readyAt: Now.AddDays(3));
        var workable = await h.FileAsync("story", "can start now", h.Todo, rank: 2048);

        // The folded card is above it and is passed over anyway - the board
        // hides it for the same reason (schedule.ts).
        Assert.Equal(Key(workable), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_TakesAnIssueReadyLaterToday()
    {
        var h = await NewAsync();
        var today = await h.FileAsync("story", "ready at five", h.Todo, readyAt: Now.AddHours(5));

        // Ready from the start of the day it names, whatever hour was set: a
        // ticket that becomes workable at 5pm is not one nobody may look at
        // over breakfast.
        Assert.Equal(Key(today), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_SaysNothingWhenTheOnlyWorkLeftIsTheOperatorsToJudge()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "waiting on a human", h.Review);
        await h.FileAsync("story", "shipped", h.Done);

        // Review's only exit is terminal and done has no exit at all, so an
        // unattended run has nothing it may do - which is a 204, not a card.
        Assert.IsType<NoContentResult>((await h.Work.GetNextWork(0, null, null, default)).Result);
    }

    // ---- Whose work it is ----

    [Fact]
    public async Task NextWork_SkipsAnIssueAssignedToAPerson()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        var hers = await h.FileAsync("story", "Ada is on this", h.Todo, rank: 1024);
        var workable = await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(hers, personId: ada.Id);

        // A name on a ticket takes it off the night shift. The folded card is
        // above it and is passed over anyway, the same as a ready date.
        Assert.Equal(Key(workable), Value(await h.Work.GetNextWork(0, null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_TakesAnIssueAssignedToAnApiKey()
    {
        var h = await NewAsync();
        var claude = h.Actors.AddKey("Claude");
        var its = await h.FileAsync("story", "assigned to the agent", h.Todo, rank: 1024);
        await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(its, apiKeyId: claude.Id);

        // "people only": assigning a ticket to Claude and having Claude stop
        // picking it up would read backwards.
        Assert.Equal(Key(its), Value(await h.Work.GetNextWork(0, null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_TakesAnIssueWhoseAssignedPersonHasBeenDeleted()
    {
        var h = await NewAsync();
        var hers = await h.FileAsync("story", "was Ada's", h.Todo, rank: 1024);
        await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);

        // The column holds an id the directory does not know, which is what a
        // deleted person looks like from here. It is not assigned, so it is not
        // folded - the liveness rule reaching the dispatcher without a sweeper.
        await h.AssignAsync(hers, personId: Guid.NewGuid());

        Assert.Equal(Key(hers), Value(await h.Work.GetNextWork(0, null, default)).Issue.Key);
    }

    [Fact]
    public async Task Work_ForANamedKey_IsNotFoldedByAnAssignee()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        var hers = await h.FileAsync("story", "Ada is on this", h.Todo, rank: 1024);
        await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(hers, personId: ada.Id);

        // Named by hand, nothing refuses it: this is the loop's policy about
        // what it may *start*, not a fact about the work - the same line the
        // ready date sits on.
        Assert.Null(Value(await h.Work.GetWork(Key(hers), default)).Blocked);
    }

    [Fact]
    public async Task Queue_SaysAnIssueIsAssigned()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        var hers = await h.FileAsync("story", "Ada is on this", h.Todo, rank: 1024);
        await h.AssignAsync(hers, personId: ada.Id);

        // The sentence, not just the fold: hatch.sh prints .blocked verbatim,
        // so this is the whole of what an operator reading a queue is told.
        var folded = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(hers));
        Assert.Equal("assigned to Ada - an unattended pass leaves a person's work alone", folded.Blocked);
    }

    /// <summary>
    /// "Mine held" (docs/hatch.md): plain `next`, without `mine`, still skips a
    /// ticket assigned to the caller's own person exactly as it skips anybody
    /// else's. This is the regression that would fail first if the mine fold
    /// and the person-assignee fold were accidentally merged into one.
    /// </summary>
    [Fact]
    public async Task NextWork_WithoutMine_StillSkipsATicketAssignedToTheCallersOwnPerson()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        var hers = await h.FileAsync("story", "Ada's own", h.Todo, rank: 1024);
        var workable = await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(hers, personId: ada.Id);

        Assert.Equal(Key(workable), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    // ---- --mine ----

    [Fact]
    public async Task Mine_TakesATicketAssignedToThePrincipal()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        var hers = await h.FileAsync("story", "Ada's own", h.Todo, rank: 1024);
        await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(hers, personId: ada.Id);

        Assert.Equal(Key(hers), Value(await h.Work.GetNextWork(0, null, null, null, null, null, true, default)).Issue.Key);
    }

    /// <summary>
    /// The key still needs an owner for --mine to run at all (criterion 8) -
    /// belonging to nobody is refused outright, even for a ticket the key
    /// would otherwise reach through this second, additional match.
    /// </summary>
    [Fact]
    public async Task Mine_TakesATicketAssignedToTheCallingKey()
    {
        var h = await NewAsync();
        var claude = h.Actors.AddKey("Claude");
        h.Actors.Me = claude;
        h.Actors.Principal = h.Actors.AddPerson("Ada");
        var its = await h.FileAsync("story", "the agent's own", h.Todo, rank: 1024);
        await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(its, apiKeyId: claude.Id);

        Assert.Equal(Key(its), Value(await h.Work.GetNextWork(0, null, null, null, null, null, true, default)).Issue.Key);
    }

    [Fact]
    public async Task Mine_SkipsAnUnassignedTicketAndOneAssignedToSomebodyElse()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = h.Actors.AddPerson("Nathan");
        var hers = await h.FileAsync("story", "Ada's own", h.Todo, rank: 1024);
        var nobodys = await h.FileAsync("story", "nobody's", h.Todo, rank: 2048);
        await h.AssignAsync(hers, personId: ada.Id);

        var folded = Value(await h.Work.GetQueue(0, null, null, null, null, true, default));
        Assert.Equal("assigned to Ada, not to you", folded.Single(e => e.Issue.Key == Key(hers)).Blocked);
        Assert.Equal(
            "assigned to nobody - a --mine pass takes only your own",
            folded.Single(e => e.Issue.Key == Key(nobodys)).Blocked);
    }

    [Fact]
    public async Task Mine_WithNoPrincipal_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "workable", h.Todo);

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Work.GetNextWork(0, null, null, null, null, null, true, default)).Result);

        Assert.Equal(
            "this key belongs to nobody, so it has no tickets of its own - an admin sets its owner on the API Keys page",
            refused.Value);
    }

    [Fact]
    public async Task Work_ForANamedKey_IgnoresMineEvenWhenAssignedToSomebodyElse()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        var hers = await h.FileAsync("story", "Ada is on this", h.Todo, rank: 1024);
        await h.AssignAsync(hers, personId: ada.Id);

        // Somebody who names a ticket has already chosen it - `mine` is not a
        // parameter `work/{key}` even reads.
        Assert.Null(Value(await h.Work.GetWork(Key(hers), default)).Blocked);
    }

    [Fact]
    public async Task Mine_StillFoldsALiveClaimHeldBySomebodyElse()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        var hers = await h.FileAsync("story", "Ada's, but somebody else has it", h.Todo, rank: 1024);
        await h.AssignAsync(hers, personId: ada.Id);
        await h.ClaimAsync(hers, by: "Someone else");

        var folded = Value(await h.Work.GetQueue(0, null, null, null, null, true, default))
            .Single(e => e.Issue.Key == Key(hers));
        Assert.NotNull(folded.Blocked);
    }

    [Fact]
    public async Task Mine_StillFoldsAReadyDateNotYetArrived()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        var hers = await h.FileAsync("story", "Ada's, but not yet", h.Todo, rank: 1024, readyAt: Now.AddDays(3));
        await h.AssignAsync(hers, personId: ada.Id);

        var folded = Value(await h.Work.GetQueue(0, null, null, null, null, true, default))
            .Single(e => e.Issue.Key == Key(hers));
        Assert.Equal($"not workable until {IssueMoment.Format(hers.ReadyAt, hers.ReadyAtHasTime)}", folded.Blocked);
    }

    [Fact]
    public async Task Mine_StillFoldsAnUnansweredQuestion()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        h.Actors.Principal = ada;
        var hers = await h.FileAsync("story", "Ada's, but somebody asked something", h.Todo, rank: 1024);
        await h.AssignAsync(hers, personId: ada.Id);
        await h.AskAsync(hers, "which approach?");

        var folded = Value(await h.Work.GetQueue(0, null, null, null, null, true, default))
            .Single(e => e.Issue.Key == Key(hers));
        Assert.Contains("unanswered question", folded.Blocked);
    }

    // ---- One corner of the board ----

    [Fact]
    public async Task NextWork_UnderAnEpic_LeavesTheRestOfTheBoardAlone()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("epic", "the one I am pushing", h.Todo, rank: 4096);
        var story = await h.FileAsync("story", "under mine", h.Todo, rank: 2048, parentId: mine.Id);
        var elsewhere = await h.FileAsync("story", "another epic's, and above it", h.Todo, rank: 1024);

        // Unscoped this is the top of the column and would win outright.
        Assert.Equal(Key(elsewhere), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);

        Assert.Equal(Key(story), Value(await h.Work.GetNextWork(0, Key(mine), null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_UnderAnEpic_ReachesAStoryTwoLevelsDown()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.Todo, rank: 4096);
        var under = await h.FileAsync("epic", "an epic inside it", h.Todo, rank: 2048, parentId: epic.Id);
        var story = await h.FileAsync("story", "the story", h.InProgress, rank: 1024, parentId: under.Id);

        // Right to left still decides inside the scope: the story is further
        // along than the epic above it, and depth has nothing to do with it.
        Assert.Equal(Key(story), Value(await h.Work.GetNextWork(0, Key(epic), null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_UnderAnEpic_PassesOverABlockedChildExactlyAsTheWholeBoardDoes()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.Todo, rank: 4096);
        var asked = await h.FileAsync("story", "waiting on a decision", h.Todo, rank: 1024, parentId: epic.Id);
        var workable = await h.FileAsync("story", "nothing in its way", h.Todo, rank: 2048, parentId: epic.Id);
        await h.AskAsync(asked, "per-node or global?");

        // The scope narrows the candidates and decides nothing about them.
        Assert.Equal(Key(workable), Value(await h.Work.GetNextWork(0, Key(epic), null, default)).Issue.Key);
    }

    [Fact]
    public async Task NextWork_UnderAnEpic_DoesNotOfferTheEpicItself()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "workable, and not the question", h.Todo);

        // "Under AER-1" is a question about what hangs beneath it. Asked of
        // the whole board this epic is the only thing on it and is the answer;
        // asked of itself it is not a candidate for its own scope.
        Assert.Equal(Key(epic), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
        Assert.IsType<NoContentResult>((await h.Work.GetNextWork(0, Key(epic), null, default)).Result);
    }

    [Fact]
    public async Task NextWork_UnderAnEpicWithNothingToDo_IsThe204AnEmptyBoardGives()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "finished", h.Todo, rank: 4096);
        await h.FileAsync("story", "shipped", h.Done, rank: 2048, parentId: epic.Id);
        await h.FileAsync("story", "somebody else's problem", h.Todo, rank: 1024);

        // Not a failure - `hatch.sh` reads it as "nothing to do", which is what
        // it means whether the scope is one epic or the whole tracker.
        Assert.IsType<NoContentResult>((await h.Work.GetNextWork(0, Key(epic), null, default)).Result);
    }

    [Fact]
    public async Task NextWork_UnderAKeyNobodyMinted_IsTheSentenceTheSearchEndpointUses()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "workable", h.Todo);

        var refused = Assert.IsType<BadRequestObjectResult>((await h.Work.GetNextWork(0, "AER-999", null, default)).Result);

        // A scope nobody can name is a typo, not an empty subtree, and it must
        // not read as "the work has run out".
        Assert.Equal("there is no AER-999", refused.Value);
    }

    // ---- What the loop may pick up ----
    //
    // Which types a move applies to is the playbook row's to say and nowhere
    // else's. What is left here is the loop's own policy - the ready date, and
    // nothing else since a dependency replaced the sibling rule. It is
    // `next`-only: a person who names a ticket is giving an instruction, and
    // housekeeping does not overrule it. A dependency is not housekeeping and
    // is asked of a named ticket too - see the dependency section below.

    [Fact]
    public async Task NextWork_TakesTheTopOfTheColumnWhateverTypeThePlaybookNames()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "somebody has to choose what this contains", h.Todo, rank: 1024);
        await h.FileAsync("story", "the unit that ships", h.Todo, rank: 2048);

        // A playbook names epics for todo to in progress, so an epic at the top
        // of the column is the top of the column. The matrix is the one
        // statement of which types a move applies to; a constant here saying it
        // a second time is what shadowed it.
        Assert.Equal(Key(epic), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task Work_OnANamedIssueIgnoresTheLoopsPolicy()
    {
        var h = await NewAsync();
        var held = await h.FileAsync("story", "not until the soak test", h.Todo, readyAt: Now.AddDays(3));
        await h.FileAsync("story", "workable now", h.Todo, rank: 2048);

        // A pass folds it, and the second story is why the board is not empty
        // and the fold has to be read off the scan rather than off a 204.
        var folded = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(held));
        Assert.Contains("not workable until", folded.Blocked);

        // Named by hand, nothing refuses it: a ready date is a decision about
        // what an unattended run may *start*, not a fact about the issue.
        Assert.Null(Value(await h.Work.GetWork(Key(held), null, default)).Blocked);
    }

    [Fact]
    public async Task Work_OnANamedIssueAndOnAPass_AgreeAboutEveryType()
    {
        var h = await NewAsync();
        var filed = new Dictionary<string, string>();
        var rank = 1024L;
        foreach (var type in EfHatchIssue.Types)
            filed[type] = Key(await h.FileAsync(type, $"an ordinary {type}", h.Todo, rank: rank += 1024));

        var queue = Value(await h.Work.GetQueue(0, null, default)).ToDictionary(e => e.Issue.Key, e => e.Blocked);

        // Nothing about a type is the pass's to decide any more, so the two
        // verdicts cannot differ on one. Asserted per type, so a failure says
        // which type stopped agreeing rather than that something did.
        foreach (var (type, key) in filed)
            Assert.Equal((type, Value(await h.Work.GetWork(key, null, default)).Blocked), (type, queue[key]));
    }

    // ---- What it waits on ----
    //
    // A dependency gates one move: the one into the column an agent writes the
    // code in, which on this board is todo to in progress exactly as it is on a
    // stock one. Everything left of it still moves, an issue already in it
    // finishes, and satisfied means terminal - a blocker in review still
    // blocks, or the second story starts on the first one's unmerged branch.

    [Fact]
    public async Task NextWork_PassesOverAnIssueWaitingOnUnfinishedWork()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review, rank: 1024);
        var second = await h.FileAsync("story", "phase two", h.Todo, rank: 1024);
        await h.DependsAsync(second, first);

        var next = await h.FileAsync("story", "unrelated", h.Todo, rank: 2048);

        Assert.Equal(Key(next), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task ADependency_DoesNotHoldUpTheColumnsBeforeImplementation()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two", h.Inbox);
        await h.DependsAsync(second, first);

        // The seeded matrix says nothing about inbox to todo, so this test
        // supplies the row - the fold under examination is the dependency's,
        // and a missing playbook would hide it.
        h.Db.Add(Playbook(h.Inbox, h.Todo, "", "sonnet"));
        await h.Db.SaveChangesAsync();

        // Still broken down, still landed in the backlog, still analysed. Only
        // the writing waits.
        Assert.Null(Value(await h.Work.GetWork(Key(second), null, default)).Blocked);
    }

    [Fact]
    public async Task ADependency_DoesNotStopWorkThatHasAlreadyStarted()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two, half written", h.InProgress);
        await h.DependsAsync(second, first);

        // The move into review is not a dependency's to refuse: an issue that
        // is already being written finishes rather than stalling half-done.
        Assert.Null(Value(await h.Work.GetWork(Key(second), null, default)).Blocked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADependency_IsSatisfiedOnlyByATerminalColumn(bool merged)
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", merged ? h.Done : h.Review);
        var second = await h.FileAsync("story", "phase two", h.Todo);
        await h.DependsAsync(second, first);

        var blocked = Value(await h.Work.GetWork(Key(second), null, default)).Blocked;

        if (merged) Assert.Null(blocked);
        else Assert.Contains($"{Key(first)} is not done", blocked);
    }

    [Fact]
    public async Task ADependencyAnAncestorHolds_ReachesEverythingBelowIt()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two", h.Todo);
        await h.DependsAsync(second, first);

        var task = await h.FileAsync("task", "a piece of phase two", h.Todo, parentId: second.Id);

        // The task holds no edge of its own, and an epic-level "phase two after
        // phase one" would say nothing at all if it did not reach down.
        var blocked = Value(await h.Work.GetWork(Key(task), null, default)).Blocked;

        Assert.Contains($"{Key(first)} is not done", blocked);
        Assert.Contains($"{Key(second)} above this", blocked);
    }

    [Fact]
    public async Task NextWork_StartsASecondStoryUnderAParentThatIsAwaitingReview()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("epic", "the effort", h.Todo, rank: 8192);
        await h.FileAsync("story", "already up for review", h.Review, rank: 1024, parentId: parent.Id);
        var next = await h.FileAsync("story", "independent of it", h.Todo, rank: 1024, parentId: parent.Id);

        // What the sibling rule refused. Two stories under one epic with no
        // edge between them are two independent pieces of work, and the loop
        // says so by taking the second one.
        Assert.Equal(Key(next), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task Work_OnANamedIssueStillRefusesAnUnmetDependency()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two", h.Todo);
        await h.DependsAsync(second, first);

        // Unlike the ready date, this is a fact about the work rather than
        // housekeeping - somebody who disagrees takes the edge off.
        Assert.Equal(
            $"{Key(first)} is not done, and this cannot be implemented until it is",
            Value(await h.Work.GetWork(Key(second), null, default)).Blocked);
    }

    [Fact]
    public async Task TheSentence_NamesEveryBlockerInKeyOrder()
    {
        var h = await NewAsync();
        var one = await h.FileAsync("story", "phase one", h.Review);
        var two = await h.FileAsync("story", "phase one and a half", h.Todo);
        var three = await h.FileAsync("story", "phase one and three quarters", h.Todo);
        var last = await h.FileAsync("story", "phase two", h.Todo);

        await h.DependsAsync(last, three);
        await h.DependsAsync(last, one);
        await h.DependsAsync(last, two);

        // Key order and not insertion order, so two passes over an unchanged
        // board print the same sentence.
        Assert.Equal(
            $"{Key(one)}, {Key(two)} and {Key(three)} are not done, "
            + "and this cannot be implemented until they are",
            Value(await h.Work.GetWork(Key(last), null, default)).Blocked);
    }

    // ---- Its children are still open ----
    //
    // The mirror of the dependency fold above: that one gates the move in to
    // the implementation column, this one gates the move out of it. A parent
    // standing there is not itself the work while a child is not yet closed -
    // its children are - so it is folded rather than carried into review.

    [Fact]
    public async Task AParentWithAnOpenChild_IsFolded()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the story", h.InProgress);
        await h.FileAsync("task", "not started", h.Todo, parentId: parent.Id);

        Assert.Equal(
            "its children are the work, and some are still open",
            Value(await h.Work.GetWork(Key(parent), null, default)).Blocked);
    }

    [Fact]
    public async Task AParentWithADeferredChild_IsFolded()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the story", h.InProgress);
        await h.FileAsync("task", "shelved, not closed", h.Shelved, parentId: parent.Id);

        // A deferred child is not terminal, so it is still open - the same
        // rule a shelved blocker is held to.
        Assert.Equal(
            "its children are the work, and some are still open",
            Value(await h.Work.GetWork(Key(parent), null, default)).Blocked);
    }

    [Fact]
    public async Task AParentWhoseChildrenAreAllTerminal_IsClear()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the story", h.InProgress);
        await h.FileAsync("task", "shipped", h.Done, parentId: parent.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(parent), null, default)).Blocked);
    }

    [Fact]
    public async Task AChildlessIssue_IsUnaffected()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "nothing under it", h.InProgress);

        Assert.Null(Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    [Fact]
    public async Task AParentWithOpenChildren_IsUnaffectedLeftOfImplementation()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "still being broken down", h.Todo);
        await h.FileAsync("task", "not started", h.Todo, parentId: parent.Id);

        // Nothing else changes: still broken down, still lands in the
        // backlog, still analysed. Only the move out of implementation folds.
        Assert.Null(Value(await h.Work.GetWork(Key(parent), null, default)).Blocked);
    }

    // ---- An epic waits for its own children (HA-125) ----
    //
    // An epic's own version of the rule above: it ignores a deferred child
    // entirely rather than counting it open, and it reaches the epic
    // wherever it stands in the WIP section rather than only the
    // implementation column - see FamilyGate.EpicFold and its guard in
    // Blocked.

    [Fact]
    public async Task AnEpicWhoseChildrenAreAllTerminal_IsClear()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "shipped one", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped two", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped three", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped four", h.Done, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
        Assert.Null(Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(epic)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithTerminalAndDeferredChildren_IsClear()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "shipped one", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped two", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped three", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shelved", h.Shelved, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithOnlyADeferredChild_IsClear()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("task", "shelved, not closed", h.Shelved, parentId: epic.Id);

        // Unlike AParentWithADeferredChild_IsFolded's story, a deferred child
        // does not count against an epic at all - shelving is the operator's
        // call, not a gap the epic is held for.
        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithOneOpenChild_NamesTheCount()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);
        await h.FileAsync("story", "shipped one", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped two", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped three", h.Done, parentId: epic.Id);

        Assert.Equal(
            "1 of its 4 children is not done - an epic is verified once its stories are",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithTwoOpenChildren_NamesTheCount()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "not started one", h.Todo, parentId: epic.Id);
        await h.FileAsync("story", "not started two", h.Todo, parentId: epic.Id);
        await h.FileAsync("story", "shipped one", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped two", h.Done, parentId: epic.Id);

        Assert.Equal(
            "2 of its 4 children are not done - an epic is verified once its stories are",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithADeferredAndAnOpenChild_DoesNotCountTheDeferredOne()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "shelved", h.Shelved, parentId: epic.Id);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);
        await h.FileAsync("story", "shipped one", h.Done, parentId: epic.Id);
        await h.FileAsync("story", "shipped two", h.Done, parentId: epic.Id);

        Assert.Equal(
            "1 of its 3 children is not done - an epic is verified once its stories are",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithOneOpenChildOnly_ReadsItsOnlyChild()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);

        Assert.Equal(
            "its only child is not done - an epic is verified once its stories are",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithNoChildren_IsFolded()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "nothing under it yet", h.InProgress);

        Assert.Equal(
            "nothing is filed under it - an epic runs its stories, and it has none",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithAnOpenGrandchild_IsUnaffected()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        var story = await h.FileAsync("story", "shipped", h.Done, parentId: epic.Id);
        await h.FileAsync("task", "still open", h.Todo, parentId: story.Id);

        // Direct children only - a grandchild left open is the story's own
        // business, not this fold's.
        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AParentWithADeferredChild_IsStillFoldedWhenItIsAStory()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var parent = await h.FileAsync("story", "the story", h.InProgress);
        await h.FileAsync("task", "shelved, not closed", h.Shelved, parentId: parent.Id);

        // The regression this guard has to hold: the epic's own rule does not
        // also exempt a story - AParentWithADeferredChild_IsFolded's sentence,
        // unchanged.
        Assert.Equal(
            "its children are the work, and some are still open",
            Value(await h.Work.GetWork(Key(parent), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithAnOpenChild_IsUnaffectedWhenTheColumnIsNotFlaggedWip()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);

        // Guarded to from.IsWip - an un-flagged board leaves an epic with an
        // open child dispatched exactly as before this fold existed.
        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicInTodoWithAnOpenChild_IsNotHeldByThisFold()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "still being broken down", h.Todo);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicInReviewWithAnOpenChild_IsFoldedByTheReviewRuleNotThisOne()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var epic = await h.FileAsync("epic", "the epic", h.Review);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);

        // The fold applies only to the move to the next column: an epic
        // sitting in review, on the move to itself, is folded by ReviewWork's
        // own rule - the !conflicts guard keeps this fold off that move
        // entirely.
        Assert.Equal(
            "no runner has checked its branch against the trunk yet",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task AnEpicWithAnUnansweredQuestionAndAnOpenChild_IsFoldedByTheQuestionFirst()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        await h.FileAsync("story", "not started", h.Todo, parentId: epic.Id);
        await h.AskAsync(epic, "which way?");

        Assert.Contains("unanswered question", Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task GetNextWork_PassesOverAHeldEpicToTheNextClearRow()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "the epic", h.InProgress);
        var child = await h.FileAsync("task", "not started", h.Todo, parentId: epic.Id);

        Assert.Equal(Key(child), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    // ---- Which checkout a runner declares ----
    //
    // Opt-in, not a default: a request carrying none of remote, standing or
    // clones is undeclared and folds nothing at all, which is what keeps an
    // older CLI and the issue page working unchanged after this landed. Once a
    // caller does declare, the fold sits beside the dependency gate and shares
    // its restriction to the column an agent writes code in - a wrong checkout
    // is a fact about the work, so it refuses a named dispatch exactly as a
    // pass would, whoever is asking and whatever they already hold.

    [Theory]
    [InlineData("https://example.com/o/r.git")]
    [InlineData("git@example.com:o/r.git")]
    [InlineData("example.com/o/r/")]
    public async Task Work_MatchesAnySpellingOfADeclaredRemote(string spelling)
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "on the bound remote", h.Todo);

        Assert.Null(Value(await h.Work.GetWork(Key(issue), remote: [spelling], ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_FoldsARunnerWithNoneOfTheProjectsRemotes()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "bound elsewhere", h.Todo);

        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it",
            Value(await h.Work.GetWork(
                Key(issue), remote: ["https://example.com/other.git"], standing: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_NamesEveryBoundRemoteWhenNoneMatch()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/one");
        await h.BindRepositoryAsync("https://example.com/o/two");
        var issue = await h.FileAsync("story", "bound to two, has neither", h.Todo);

        Assert.Equal(
            "bound to https://example.com/o/one and https://example.com/o/two, and this runner has no checkout of it",
            Value(await h.Work.GetWork(Key(issue), standing: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_ARunnerThatWillCloneIsNotFoldedByAMissingCheckout()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "bound elsewhere", h.Todo);

        Assert.Null(Value(await h.Work.GetWork(Key(issue), clones: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_UndeclaredFoldsNothing()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "bound elsewhere", h.Todo);

        // Every pre-existing test in this file already proves this by
        // construction - this one names it.
        Assert.Null(Value(await h.Work.GetWork(Key(issue), ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_AnUnboundProjectIsWorkableFromAStandingCheckout()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "nothing bound yet", h.Todo);

        Assert.Null(Value(await h.Work.GetWork(Key(issue), standing: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_AnUnboundProjectIsFoldedWithoutAStandingCheckout()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "nothing bound yet", h.Todo);

        Assert.Equal(
            $"{Key(issue)} is bound to no repository - bind one on the Projects page, or run the loop inside a checkout",
            Value(await h.Work.GetWork(Key(issue), standing: false, ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_NamesEachRepositorysMatchedRemoteAndThePrimary()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/one");
        await h.BindRepositoryAsync("https://example.com/o/two");
        await h.BindRepositoryAsync("https://example.com/o/three");
        var issue = await h.FileAsync("story", "three remotes", h.Todo);

        var work = Value(await h.Work.GetWork(
            Key(issue), remote: ["https://example.com/o/one.git", "https://example.com/o/two.git"], ct: default));

        // The primary matching is enough - the dispatch is not folded - and
        // every entry names which of the caller's own spellings matched it, in
        // the project's own order.
        Assert.Null(work.Blocked);
        Assert.Equal(3, work.Repositories.Count);
        Assert.True(work.Repositories[0].Primary);
        Assert.False(work.Repositories[1].Primary);
        Assert.False(work.Repositories[2].Primary);
        Assert.Equal("https://example.com/o/one.git", work.Repositories[0].MatchedRemote);
        Assert.Equal("https://example.com/o/two.git", work.Repositories[1].MatchedRemote);
        Assert.Null(work.Repositories[2].MatchedRemote);
    }

    [Fact]
    public async Task Work_FoldsARunnerHoldingOnlyANonPrimaryRepository()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/one");
        await h.BindRepositoryAsync("https://example.com/o/two");
        var issue = await h.FileAsync("story", "the primary is elsewhere", h.Todo);

        // Every session spawns in the primary's checkout, so holding a later
        // repository is no better than holding none.
        Assert.Equal(
            "its primary repository is https://example.com/o/one, and this runner has no checkout of it",
            Value(await h.Work.GetWork(Key(issue), remote: ["https://example.com/o/two.git"], ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_TheRepositoryFoldAppliesBeforeImplementation()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "still being broken down", h.Inbox);
        h.Db.Add(Playbook(h.Inbox, h.Todo, "", "sonnet"));
        await h.Db.SaveChangesAsync();

        // Every session runs in the primary's checkout, whatever the move, so
        // the fold is not confined to the column the code is written in.
        const string sentence = "bound to https://example.com/o/r, and this runner has no checkout of it";
        Assert.Equal(
            sentence,
            Value(await h.Work.GetWork(
                Key(issue), remote: ["https://example.com/other.git"], standing: true, ct: default)).Blocked);
        Assert.Equal(
            sentence,
            Only(await h.Work.GetQueue(0, null, standing: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Work_AHopOutsideImplementationIsNotFoldedByARepository()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "carried across", h.Inbox);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Inbox);

        // A hop runs no session, so it needs no checkout - unless it lands in
        // implementation, which Hop_IsFoldedByARepositoryTheRunnerLacks pins.
        var entry = Only(await h.Work.GetQueue(0, null, standing: true, ct: default));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
    }

    [Fact]
    public async Task Work_ARunnersOwnClaimDoesNotWaiveAMismatchedRepository()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "claimed by the caller itself", h.Todo);
        var token = await h.ClaimAsync(issue);

        // Unlike a claim, which the holder's own token waives, a wrong
        // checkout is a fact about the work - it refuses whoever asks,
        // including the one runner already holding this issue.
        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it",
            Value(await h.Work.GetWork(
                Key(issue), heldToken: token, remote: ["https://example.com/other.git"], standing: true,
                ct: default)).Blocked);
    }

    // ---- Refusals ----

    /// <summary>
    /// Nothing is ever dispatched into a terminal column. An issue in review is
    /// dispatched to review itself - there is something to do there, and only
    /// the operator moves it on - so the sentence saying so is unreachable from
    /// that column and stays for any other whose next column is terminal.
    /// </summary>
    [Fact]
    public async Task Work_NeverEntersATerminalColumn_AnIssueInReviewIsDispatchedToReview()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "ready for a verdict", h.Review);

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        Assert.Equal("review", work.ToStatus!.Name);
        Assert.Equal(work.FromStatus.Id, work.ToStatus.Id);
        Assert.Equal(WorkKinds.Conflicts, work.Kind);
        Assert.DoesNotContain("only the operator", work.Blocked);
    }

    [Fact]
    public async Task Work_StillRefusesTheTerminalNextColumnForAnyOtherColumn()
    {
        // Two terminal columns, so the column before the second one is not the
        // review column and its next column is terminal - which is the sentence
        // the rest of the board keeps.
        var h = await NewAsync();
        var shipped = new EfHatchStatus { Name = "shipped", SortOrder = 50, IsTerminal = true };
        var between = new EfHatchStatus { Name = "between", SortOrder = 45 };
        h.Db.AddRange(shipped, between);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("task", "waiting", between.Id);

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        Assert.Equal("shipped", work.ToStatus!.Name);
        Assert.Equal(WorkKinds.Advance, work.Kind);
        Assert.Equal("the next column is \"shipped\", and only the operator moves work there", work.Blocked);
    }

    /// <summary>
    /// On a board with no terminal column the review column is the rightmost
    /// one and is where work ends: it is not dispatched to itself, and there is
    /// still nowhere for it to go.
    /// </summary>
    [Fact]
    public async Task Work_DoesNotDispatchReviewToItselfWhenNoTerminalColumnStandsAfterIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "at the end of the board", h.Review);
        h.Db.Statuses.Remove(await h.Db.Statuses.SingleAsync(s => s.Id == h.Done));
        await h.Db.SaveChangesAsync();

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        Assert.Null(work.ToStatus);
        Assert.Contains("nowhere for this to go", work.Blocked);
        Assert.Equal(WorkKinds.Advance, work.Kind);
        Assert.DoesNotContain(Key(issue), Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Work_RefusesWhenThereIsNoPlaybookForTheTransition()
    {
        var h = await NewAsync();
        h.Db.Playbooks.RemoveRange(h.Db.Playbooks.Where(p => p.FromStatusId == h.Todo));
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync("story", "specified, undispatchable", h.Todo);

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        Assert.Contains("no playbook covers", work.Blocked);
        Assert.Null(work.Playbook);
    }

    [Fact]
    public async Task Work_OnANamedIssueAnswersEvenWhenItIsBlocked()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "shipped", h.Done);

        // A person who asked for this key is owed the sentence saying why it
        // cannot move, not a 404.
        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        Assert.Equal(Key(issue), work.Issue.Key);
        Assert.Null(work.ToStatus);
        Assert.NotNull(work.Blocked);
    }

    [Fact]
    public async Task Work_RefusesAnIssueHoldingAnUnansweredQuestion()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "asked and waiting", h.Todo);
        await h.AskAsync(issue, "per-node or global?");

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        // Not a missing playbook and not a terminal column: this one is waiting
        // on a person, and dispatching at it would produce a second session
        // asking the same thing or guessing at the answer.
        Assert.Contains("unanswered question", work.Blocked);
        Assert.NotNull(work.Playbook);
    }

    // ---- A lapsed stall question ----

    [Fact]
    public async Task ALapsedStallQuestion_DoesNotFoldTheIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "stalled a while ago", h.Todo);
        await h.AskStallAsync(issue, Now);

        h.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        Assert.Null(Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    [Fact]
    public async Task AFreshStallQuestion_StillFoldsTheIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "just asked", h.Todo);
        await h.AskStallAsync(issue, Now);

        // Not yet five minutes old.
        h.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.Contains("unanswered question", Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    [Fact]
    public async Task AProseQuestion_FoldsHoweverLongItWaits()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "asked in prose", h.Todo);
        await h.AskAsync(issue, "what should this be called?");

        h.Time.Advance(TimeSpan.FromDays(1));

        // No options at all - never a stall question, whatever its body reads
        // like, so it never lapses.
        Assert.Contains("unanswered question", Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    [Fact]
    public async Task AStallQuestionOnARecentlyTouchedIssue_StillFolds()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "somebody is still looking", h.Todo);
        await h.AskStallAsync(issue, Now);

        h.Time.Advance(TimeSpan.FromMinutes(10));

        // The question itself is ten minutes old, well past the window - but
        // something happened to the issue seconds ago, so the untouched half
        // of the lapse is not met and the question still folds.
        await h.LogEventAsync(issue, EfHatchIssueEvent.Commented, null, h.Time.GetUtcNow());

        Assert.Contains("unanswered question", Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    [Fact]
    public async Task Queue_NamesTheLapseOnARowClearOnlyBecauseOfIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "stalled a while ago", h.Todo);
        await h.AskStallAsync(issue, Now);

        h.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var row = Only(await h.Work.GetQueue(0, null, default));
        Assert.Null(row.Blocked);
        Assert.Equal("its stall question lapsed after 5 minutes untouched", row.ClearNote);
    }

    [Fact]
    public async Task Queue_NamesNoLapseOnAnOrdinaryClearRow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "nothing special", h.Todo);

        Assert.Null(Only(await h.Work.GetQueue(0, null, default)).ClearNote);
        Assert.Null(Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    // ---- The count of let-go increments ----

    [Fact]
    public async Task LetGo_IsZeroForAnIssueWithNoTrailAtAll()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "never claimed", h.Todo);

        Assert.Equal(0, Value(await h.Work.GetWork(Key(issue), null, default)).LetGo);
    }

    [Fact]
    public async Task LetGo_CountsTrailingDroppedReleases()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "let go twice in a row", h.Todo);

        await Released(h, issue, Now, ClaimOutcomes.Dropped);
        await Released(h, issue, Now.AddMinutes(1), ClaimOutcomes.Dropped);

        Assert.Equal(2, Value(await h.Work.GetWork(Key(issue), null, default)).LetGo);
    }

    [Fact]
    public async Task LetGo_StopsAtAWorkedRelease()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "worked, then dropped once", h.Todo);

        await Released(h, issue, Now, ClaimOutcomes.Worked);
        await Released(h, issue, Now.AddMinutes(1), ClaimOutcomes.Dropped);

        Assert.Equal(1, Value(await h.Work.GetWork(Key(issue), null, default)).LetGo);
    }

    [Fact]
    public async Task LetGo_StopsAtAStatusChange()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "moved, then dropped once", h.Todo);

        await h.LogEventAsync(issue, EfHatchIssueEvent.StatusChanged, new { from = "Backlog", to = "Todo" }, Now);
        await Released(h, issue, Now.AddMinutes(1), ClaimOutcomes.Dropped);

        Assert.Equal(1, Value(await h.Work.GetWork(Key(issue), null, default)).LetGo);
    }

    [Fact]
    public async Task LetGo_StopsAtAPersonsAnswer()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "answered, then dropped once", h.Todo);

        await h.LogEventAsync(issue, EfHatchIssueEvent.Answered, new { questionId = 1 }, Now);
        await Released(h, issue, Now.AddMinutes(1), ClaimOutcomes.Dropped);

        Assert.Equal(1, Value(await h.Work.GetWork(Key(issue), null, default)).LetGo);
    }

    [Fact]
    public async Task LetGo_SkipsOverAReleaseWithNoOutcome_AndAnAnswerWrittenByALapse()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "skipped over rather than counted or stopped at", h.Todo);

        await Released(h, issue, Now, ClaimOutcomes.Dropped);
        await h.LogEventAsync(issue, EfHatchIssueEvent.Answered, new { questionId = 1, lapsed = true }, Now.AddMinutes(1));
        await Released(h, issue, Now.AddMinutes(2), outcome: null);
        await Released(h, issue, Now.AddMinutes(3), ClaimOutcomes.Dropped);

        Assert.Equal(2, Value(await h.Work.GetWork(Key(issue), null, default)).LetGo);
    }

    /// <summary>A release event, in the <c>{ from, to, outcome }</c> shape the controller writes - a null <c>outcome</c> reads back the same as one left out entirely, since nothing here asks for the difference.</summary>
    private static Task Released(Harness h, EfHatchIssue issue, DateTimeOffset at, string? outcome) =>
        h.LogEventAsync(
            issue,
            EfHatchIssueEvent.ClaimReleased,
            new { from = "Nathan on host:/checkout", to = (string?)null, outcome },
            at);

    [Fact]
    public async Task NextWork_PassesOverAnIssueWaitingOnAnAnswer()
    {
        var h = await NewAsync();
        var asked = await h.FileAsync("story", "waiting on a decision", h.Todo, rank: 1024);
        var workable = await h.FileAsync("story", "nothing in its way", h.Todo, rank: 2048);
        await h.AskAsync(asked, "per-node or global?");

        // Folded past exactly as a card whose ready date has not arrived is,
        // and for the same reason: it is not workable yet.
        Assert.Equal(Key(workable), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task Work_DispatchesOnceTheQuestionHasBeenAnswered()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "asked and answered", h.Todo);
        var question = await h.AskAsync(issue, "per-node or global?");
        await h.AnswerAsync(issue, question, "per-node");

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        Assert.Null(work.Blocked);
    }

    [Fact]
    public async Task Work_CarriesTheAnsweredQuestionsSoTheNextSessionDoesNotReopenThem()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "decided", h.Todo);
        var question = await h.AskAsync(issue, "per-node or global?");
        await h.AnswerAsync(issue, question, "per-node");

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        var carried = Assert.Single(work.Questions);
        Assert.Equal("per-node or global?", carried.Body);
        Assert.Equal("per-node", Assert.Single(carried.Answers).Body);
    }

    [Fact]
    public async Task Work_CarriesTheUnreadMessagesAndNotTheOnesAlreadyDelivered()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "watched", h.Todo);
        await h.SayAsync(issue, EfHatchComment.Message, "the first, already read", deliveredAt: Now);
        await h.SayAsync(issue, EfHatchComment.Message, "use the other table");
        await h.SayAsync(issue, EfHatchComment.Note, "sha abc123");

        var work = Value(await h.Work.GetWork(Key(issue), null, default));

        // Only the unread message rides the dispatch: a delivered one has been
        // read, and a note was never said to the agent.
        var carried = Assert.Single(work.Messages!);
        Assert.Equal("use the other table", carried.Body);
        Assert.Null(carried.DeliveredAt);
    }

    [Fact]
    public async Task Work_CarriesNoMessagesWhenThereAreNone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("task", "quiet", h.Todo);

        Assert.Empty(Value(await h.Work.GetWork(Key(issue), null, default)).Messages!);
    }

    // ---- Matching ----

    [Fact]
    public async Task Work_PrefersThePlaybookThatNamesTheType()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "a big one", h.Todo);
        var task = await h.FileAsync("task", "a small one", h.Todo);

        Assert.Equal("opus", Value(await h.Work.GetWork(Key(epic), null, default)).Playbook!.Model);
        Assert.Equal("sonnet", Value(await h.Work.GetWork(Key(task), null, default)).Playbook!.Model);
    }

    [Fact]
    public async Task Work_FallsBackToTheCatchAllForATypeNobodyNamed()
    {
        var h = await NewAsync();
        var bug = await h.FileAsync("bug", "unnamed by any specific row", h.Todo);

        var playbook = Value(await h.Work.GetWork(Key(bug), null, default)).Playbook;

        Assert.NotNull(playbook);
        Assert.Empty(playbook.Types);
    }

    [Fact]
    public async Task Work_PrefersThePlaybookThatNamesTheTypeOverOneThatOnlyNamesTheShape()
    {
        var h = await NewAsync();
        h.Db.Add(Playbook(h.Todo, h.InProgress, "task", "type-wins"));
        h.Db.Add(Playbook(h.Todo, h.InProgress, "", "shape-wins", shape: "leaf"));
        await h.Db.SaveChangesAsync();

        var task = await h.FileAsync("task", "a leaf that both rows could speak for", h.Todo);

        Assert.Equal("type-wins", Value(await h.Work.GetWork(Key(task), null, default)).Playbook!.Model);
    }

    [Fact]
    public async Task Work_PrefersThePlaybookThatNamesTheShapeOverABareAnyRow()
    {
        var h = await NewAsync();
        h.Db.Add(Playbook(h.Todo, h.InProgress, "", "shape-wins", shape: "leaf"));
        await h.Db.SaveChangesAsync();

        var task = await h.FileAsync("task", "a leaf with no children", h.Todo);

        // The seeded catch-all ("", any, sonnet) also covers this issue; the
        // shape-specific row must outrank it even though neither names a type.
        Assert.Equal("shape-wins", Value(await h.Work.GetWork(Key(task), null, default)).Playbook!.Model);
    }

    [Fact]
    public async Task Work_OnATrueTieInSpecificity_TheOlderRowWins()
    {
        var h = await NewAsync();
        var older = Playbook(h.Todo, h.InProgress, "task", "older-wins");
        var newer = Playbook(h.Todo, h.InProgress, "task", "newer-loses");
        h.Db.Add(older);
        await h.Db.SaveChangesAsync();
        h.Db.Add(newer);
        await h.Db.SaveChangesAsync();

        Assert.True(older.Id < newer.Id);

        var task = await h.FileAsync("task", "matched by two rows tied on specificity", h.Todo);

        Assert.Equal("older-wins", Value(await h.Work.GetWork(Key(task), null, default)).Playbook!.Model);
    }

    [Fact]
    public async Task Work_ALeafShapedPlaybookDoesNotCoverAnIssueWithChildren()
    {
        var h = await NewAsync();
        h.Db.Add(Playbook(h.Todo, h.InProgress, "story", "leaf-only", shape: "leaf"));
        await h.Db.SaveChangesAsync();

        var parent = await h.FileAsync("story", "has a child", h.Todo);
        await h.FileAsync("task", "the child", h.Todo, parentId: parent.Id);

        // The seeded catch-all is the only row left that can cover it - the
        // leaf row does not, because this issue is a parent.
        Assert.Equal("sonnet", Value(await h.Work.GetWork(Key(parent), null, default)).Playbook!.Model);
    }

    [Fact]
    public async Task Work_AParentShapedPlaybookDoesNotCoverAChildlessIssue()
    {
        var h = await NewAsync();
        h.Db.Add(Playbook(h.Todo, h.InProgress, "story", "parent-only", shape: "parent"));
        await h.Db.SaveChangesAsync();

        var story = await h.FileAsync("story", "no children", h.Todo);

        Assert.Equal("sonnet", Value(await h.Work.GetWork(Key(story), null, default)).Playbook!.Model);
    }

    /// <summary>
    /// The pair SeedCloseoutPlaybook seeds onto the implementation -&gt; review
    /// transition: a childless story still dispatches with the implementation
    /// (leaf) prompt, and a story whose only child is already closed dispatches
    /// with the closeout (parent) prompt instead - isParent is total children,
    /// not open ones, so a closed child is enough to make this issue a parent.
    /// </summary>
    [Fact]
    public async Task Work_AChildlessStoryGetsTheLeafModelAndAStoryWithOnlyClosedChildrenGetsTheParentModel()
    {
        var h = await NewAsync();
        h.Db.Add(Playbook(h.InProgress, h.Review, "", "leaf-model", shape: "leaf"));
        h.Db.Add(Playbook(h.InProgress, h.Review, "", "parent-model", shape: "parent"));
        await h.Db.SaveChangesAsync();

        var childless = await h.FileAsync("story", "no children", h.InProgress);
        var closedOut = await h.FileAsync("story", "its only child is already done", h.InProgress);
        await h.FileAsync("task", "the closed child", h.Done, parentId: closedOut.Id);

        Assert.Equal("leaf-model", Value(await h.Work.GetWork(Key(childless), null, default)).Playbook!.Model);
        Assert.Equal("parent-model", Value(await h.Work.GetWork(Key(closedOut), null, default)).Playbook!.Model);
    }

    // ---- The issue's own model and effort ----

    [Fact]
    public async Task Work_WithNoOverride_ReportsThePlaybooksOwnValues()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "an ordinary one", h.Todo);

        var playbook = Value(await h.Work.GetWork(Key(task), null, default)).Playbook!;

        Assert.Equal("sonnet", playbook.Model);
        Assert.Equal("high", playbook.Effort);
    }

    [Fact]
    public async Task Work_ReportsAModelOverrideInPlaceOfThePlaybooks()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "harder than its column suggests", h.Todo);
        await h.OverrideAsync(task, model: "opus");

        var playbook = Value(await h.Work.GetWork(Key(task), null, default)).Playbook!;

        // The value that won, in place - and the effort still the row's own,
        // because the two are independent.
        Assert.Equal("opus", playbook.Model);
        Assert.Equal("high", playbook.Effort);
    }

    [Fact]
    public async Task Work_ReportsAnEffortOverrideTheSameWay()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "subtle rather than large", h.Todo);
        await h.OverrideAsync(task, effort: "max");

        var playbook = Value(await h.Work.GetWork(Key(task), null, default)).Playbook!;

        Assert.Equal("sonnet", playbook.Model);
        Assert.Equal("max", playbook.Effort);
    }

    [Fact]
    public async Task Work_ReportsBothOverrides_AndTheRowThatSpoke()
    {
        var h = await NewAsync();
        var task = await h.FileAsync("task", "both", h.Todo);
        await h.OverrideAsync(task, model: "haiku", effort: "low");

        var work = Value(await h.Work.GetWork(Key(task), null, default));

        Assert.Equal("haiku", work.Playbook!.Model);
        Assert.Equal("low", work.Playbook.Effort);

        // The dispatch names the playbook that spoke as well as the values
        // that won: everything but the two fields is still the matched row's.
        Assert.Equal("do the thing", work.Playbook.Prompt);
        Assert.Equal(h.Todo, work.Playbook.FromStatusId);
        Assert.Equal(h.InProgress, work.Playbook.ToStatusId);

        // ...and the override rides the same payload, which is how a printed
        // line says where the value came from.
        Assert.Equal("haiku", work.Issue.ModelOverride);
        Assert.Equal("low", work.Issue.EffortOverride);
    }

    [Fact]
    public async Task Work_NextReportsTheOverrideToo()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the only thing on the board", h.Todo);
        await h.OverrideAsync(story, model: "opus");

        // One fold point, both endpoints: `next` and `{key}` reach the same
        // resolve, so there is no second place to remember.
        Assert.Equal("opus", Value(await h.Work.GetNextWork(0, null, null, default)).Playbook!.Model);
    }

    [Fact]
    public async Task Work_AnOverrideOnAParent_DoesNotReachItsChild()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the expensive one", h.Todo, rank: 4096);
        var task = await h.FileAsync("task", "under it", h.Todo, rank: 1024, parentId: story.Id);
        await h.OverrideAsync(story, model: "opus", effort: "max");

        var playbook = Value(await h.Work.GetWork(Key(task), null, default)).Playbook!;

        // This issue only. An epic set to opus does not spend opus on its
        // stories - a task that needs the big model says so itself.
        Assert.Equal("sonnet", playbook.Model);
        Assert.Equal("high", playbook.Effort);
    }

    [Fact]
    public async Task Work_AnOverrideChangesWhatADispatchCosts_NeverWhetherOneHappens()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "nowhere to go from here", h.Inbox);
        await h.OverrideAsync(story, model: "opus", effort: "max");

        var work = Value(await h.Work.GetWork(Key(story), null, default));

        // Nothing in the refusal ladder learns about overrides: an issue with
        // no playbook for its next move still dispatches nothing.
        Assert.Null(work.Playbook);
        Assert.Contains("no playbook covers", work.Blocked);
    }

    [Fact]
    public async Task Work_CarriesTheChildrenSoTheAgentCanDescend()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "parent", h.Todo);
        await h.FileAsync("task", "first", h.Todo, parentId: story.Id);
        await h.FileAsync("task", "second", h.Todo, parentId: story.Id);

        var work = Value(await h.Work.GetWork(Key(story), null, default));

        Assert.Equal(["first", "second"], work.Children.Select(c => c.Title));
    }

    // ---- The scan ----
    //
    // What a whole pass would do, rather than what its first step is. These
    // pin the two properties the endpoint exists for: it is the same walk
    // `next` takes, and every fold it makes says why.

    [Fact]
    public async Task Queue_ReportsTheBoardInTheOrderTheDispatcherWalksIt()
    {
        var h = await NewAsync();
        var filed = await h.FileAsync("story", "in the inbox", h.Inbox);
        var waiting = await h.FileAsync("story", "below the top of todo", h.Todo, rank: 2048);
        var top = await h.FileAsync("story", "top of todo", h.Todo, rank: 1024);
        var underway = await h.FileAsync("story", "underway", h.InProgress);
        var judged = await h.FileAsync("story", "awaiting the operator", h.Review);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        // Rightmost column first, then top of the column down - the scheduling
        // policy, written out rather than implied by which card came back.
        Assert.Equal(
            new[] { judged, underway, top, waiting, filed }.Select(Key),
            queue.Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Queue_ListsEveryExpeditedCandidateBeforeEveryOtherOne()
    {
        var h = await NewAsync();
        var judged = await h.FileAsync("story", "awaiting the operator", h.Review);
        var underway = await h.FileAsync("story", "underway", h.InProgress);
        var hurry = await h.FileAsync("bug", "the one somebody is waiting on", h.Inbox);

        await h.ExpediteAsync(hurry);

        // An expedited bug in the leftmost column, above a non-expedited story
        // in the rightmost one. Right to left is the policy; this is the one
        // thing that comes before it.
        Assert.Equal(
            new[] { hurry, judged, underway }.Select(Key),
            Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Queue_ListsEveryEmergencyCandidateBeforeEveryExpeditedOneBeforeEveryOtherOne()
    {
        var h = await NewAsync();
        var judged = await h.FileAsync("story", "awaiting the operator", h.Review);
        var underway = await h.FileAsync("story", "underway", h.InProgress);
        var hurry = await h.FileAsync("bug", "expedited, in the leftmost column", h.Inbox);
        var alarm = await h.FileAsync("bug", "emergency, further along", h.Todo);

        await h.ExpediteAsync(hurry);
        await h.EmergencyAsync(alarm);

        // Emergency before expedited before everything else, whatever column
        // each sits in - three files rather than two.
        Assert.Equal(
            new[] { alarm, hurry, judged, underway }.Select(Key),
            Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Queue_ListsATaskInheritingEmergencyFromItsEpicBeforeOneExpeditedOnItsOwnRow()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the emergency epic", h.Todo);
        await h.EmergencyAsync(epic);
        var inherited = await h.FileAsync("task", "inherits emergency, own row normal", h.InProgress, parentId: epic.Id);
        var hurry = await h.FileAsync("bug", "expedited on its own row, in an earlier column", h.Inbox);
        await h.ExpediteAsync(hurry);

        // Emergency - the inherited task, then the epic itself (InProgress is
        // right of Todo) - before expedited, whatever column each sits in.
        Assert.Equal(
            new[] { inherited, epic, hurry }.Select(Key),
            Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Queue_KeepsTheBoardsOwnOrderInsideEachHalf()
    {
        var h = await NewAsync();
        var judged = await h.FileAsync("story", "awaiting the operator", h.Review);
        var top = await h.FileAsync("story", "top of todo", h.Todo, rank: 1024);
        var below = await h.FileAsync("story", "below it", h.Todo, rank: 2048);
        var hurryLeft = await h.FileAsync("story", "hurried, in the inbox", h.Inbox);
        var hurryRight = await h.FileAsync("story", "hurried, further along", h.Review, rank: 2048);

        await h.ExpediteAsync(hurryLeft);
        await h.ExpediteAsync(hurryRight);

        // Rightmost column first and (Rank, Id) within a column, applied twice:
        // once to the expedited candidates and once to everything else.
        Assert.Equal(
            new[] { hurryRight, hurryLeft, judged, top, below }.Select(Key),
            Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task NextWork_TakesTheFirstUnblockedRowOfThatQueue()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "awaiting the operator", h.Review);
        var hurry = await h.FileAsync("story", "the hurried one", h.Todo);

        await h.ExpediteAsync(hurry);

        var queue = Value(await h.Work.GetQueue(0, null, default));
        var work = Value(await h.Work.GetNextWork(0, null, null, default));

        // Still one walk, reported and acted on. The queue expedite publishes
        // is the queue next takes.
        Assert.Equal(Key(hurry), work.Issue.Key);
        Assert.Equal(queue.First(e => e.Blocked is null).Issue.Key, work.Issue.Key);
    }

    [Fact]
    public async Task AnExpeditedIssueThatIsBlocked_IsFoldedWithTheSameSentenceAndThePassCarriesOn()
    {
        var h = await NewAsync();
        var hurried = await h.FileAsync("story", "hurried, waiting on a person", h.Todo, rank: 1024);
        var ordinary = await h.FileAsync("story", "waiting on a person", h.Todo, rank: 2048);
        var next = await h.FileAsync("story", "the one behind them", h.Todo, rank: 3072);
        await h.AskAsync(hurried, "per-node or global?");
        await h.AskAsync(ordinary, "per-node or global?");

        await h.ExpediteAsync(hurried);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        // Expedite carries nothing past its own reason: the row is considered
        // first, folded with exactly the sentence the same fold gives an
        // ordinary issue, and the pass goes on to the next one.
        Assert.Equal(Key(hurried), queue[0].Issue.Key);
        Assert.NotNull(queue[0].Blocked);
        Assert.Equal(queue.Single(e => e.Issue.Key == Key(ordinary)).Blocked, queue[0].Blocked);
        Assert.Equal(Key(next), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task AnEmergencyIssueThatIsBlocked_IsFoldedWithTheSameSentenceAndThePassCarriesOn()
    {
        var h = await NewAsync();
        var alarmed = await h.FileAsync("story", "emergency, waiting on a person", h.Todo, rank: 1024);
        var ordinary = await h.FileAsync("story", "waiting on a person", h.Todo, rank: 2048);
        var next = await h.FileAsync("story", "the one behind them", h.Todo, rank: 3072);
        await h.AskAsync(alarmed, "per-node or global?");
        await h.AskAsync(ordinary, "per-node or global?");

        await h.EmergencyAsync(alarmed);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        // Priority carries nothing past its own reason: the row is considered
        // first, folded with exactly the sentence the same fold gives an
        // ordinary issue, and the pass goes on to the next one.
        Assert.Equal(Key(alarmed), queue[0].Issue.Key);
        Assert.NotNull(queue[0].Blocked);
        Assert.Equal(queue.Single(e => e.Issue.Key == Key(ordinary)).Blocked, queue[0].Blocked);
        Assert.Equal(Key(next), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task Queue_LeavesOutTheColumnsWithNowhereToGo_AndListsReview()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "shipped", h.Done);
        var live = await h.FileAsync("story", "still going", h.Todo);
        var up = await h.FileAsync("story", "up for review", h.Review);

        // Done is terminal, so the dispatcher never reaches it. An issue it
        // never reaches is not one the pass skipped, and shipped work is not a
        // backlog. Review has somewhere to go - itself - so its issues are
        // listed, each either a conflict to fix or a row saying why not.
        Assert.Equal(
            [Key(up), Key(live)],
            Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    /// <summary>
    /// Nothing comes off the shelf on a pass's say-so. The refusal names the
    /// column rather than saying "there is nowhere for this to go", which is
    /// true of a deferred column and no use to anybody reading a queue.
    /// </summary>
    [Fact]
    public async Task Queue_FoldsPastAShelvedIssueAndSaysWhy()
    {
        var h = await NewAsync();
        var parked = await h.FileAsync("story", "not now", h.Shelved);

        var entry = Value(await h.Work.GetQueue(0, null, default)).SingleOrDefault(e => e.Issue.Key == Key(parked));

        // Either it is not in the queue at all, or it is there refused - both
        // are "nothing is dispatched from here", and only the second has a
        // sentence to check.
        if (entry is not null) Assert.Contains("deferred", entry.Blocked);

        Assert.DoesNotContain(
            Key(parked),
            Value(await h.Work.GetQueue(0, null, default)).Where(e => e.Blocked is null).Select(e => e.Issue.Key));
    }

    /// <summary>
    /// Named by hand rather than found by a scan, which is the path that hands
    /// out an actual refusal to read - and the one somebody hits when they
    /// wonder why the loop is ignoring a ticket they parked last week.
    /// </summary>
    [Fact]
    public async Task Work_RefusesAShelvedIssueByName()
    {
        var h = await NewAsync();
        var parked = await h.FileAsync("story", "not now", h.Shelved);

        var work = Value(await h.Work.GetWork(Key(parked), null, default));

        Assert.Contains("deferred", work.Blocked);
    }

    /// <summary>
    /// The board closes over the gap. "In progress" advances into review even
    /// though a deferred column is sorted between them, which is the whole of
    /// why the geometry is measured off the drawn columns.
    /// </summary>
    [Fact]
    public async Task Queue_AdvancesPastADeferredColumnRatherThanIntoIt()
    {
        var h = await NewAsync();
        var live = await h.FileAsync("story", "being written", h.InProgress);

        var entry = Only(await h.Work.GetQueue(0, null, default));

        Assert.Equal(Key(live), entry.Issue.Key);
        Assert.Equal(h.Review, entry.ToStatus?.Id);
        Assert.Null(entry.Blocked);
    }

    [Fact]
    public async Task Queue_FirstClearEntryIsWhatNextReturns()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "awaiting the operator", h.Review);
        await h.FileAsync("epic", "not workable until the soak test ends", h.Todo, rank: 1024, readyAt: Now.AddDays(3));
        await h.FileAsync("story", "waiting on a renewal", h.Todo, rank: 2048, readyAt: Now.AddDays(3));
        await h.FileAsync("story", "the one it should take", h.Todo, rank: 3072);
        await h.FileAsync("story", "below it", h.Todo, rank: 4096);

        var queue = Value(await h.Work.GetQueue(0, null, default));
        var work = Value(await h.Work.GetNextWork(0, null, null, default));

        // The property the endpoint exists for: one walk, reported and acted
        // on. Two loops that could disagree about the order of the board is
        // precisely the bug the scan is here to expose.
        Assert.Equal(queue.First(e => e.Blocked is null).Issue.Key, work.Issue.Key);
    }

    [Fact]
    public async Task Queue_AgreesWithNextWhenThereIsNothingToDo()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "awaiting the operator", h.Review);
        var asked = await h.FileAsync("story", "waiting on a person", h.Todo);
        await h.AskAsync(asked, "per-node or global?");

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.All(queue, e => Assert.NotNull(e.Blocked));
        Assert.IsType<NoContentResult>((await h.Work.GetNextWork(0, null, null, default)).Result);
    }

    [Fact]
    public async Task Queue_OnAnEmptyBoardIsEmptyRatherThanRefused()
    {
        var h = await NewAsync();

        Assert.Empty(Value(await h.Work.GetQueue(0, null, default)));
    }

    // ---- ...and every fold it makes, named ----

    [Fact]
    public async Task Queue_NamesAReadyDateThatHasNotArrived()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "waiting on a renewal", h.Todo, readyAt: Now.AddDays(3));

        Assert.Contains("not workable until", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Queue_NamesAnUnansweredQuestion()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "waiting on a person", h.Todo);
        await h.AskAsync(issue, "how should retries be scoped?");

        Assert.Contains("unanswered question", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Queue_NamesTheUnfinishedWorkAnIssueWaitsOn()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two", h.Todo);
        await h.DependsAsync(second, first);

        var blocked = Value(await h.Work.GetQueue(0, null, default))
            .Single(e => e.Issue.Key == Key(second)).Blocked;

        Assert.Equal(
            $"{Key(first)} is not done, and this cannot be implemented until it is",
            blocked);
    }

    [Fact]
    public async Task Queue_NamesARepositoryTheRunnerLacks()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.FileAsync("story", "bound elsewhere", h.Todo);

        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it",
            Only(await h.Work.GetQueue(0, null, standing: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Queue_NamesTheAncestorHoldingTheEdge()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two", h.Todo);
        await h.DependsAsync(second, first);
        var task = await h.FileAsync("task", "a piece of phase two", h.Todo, parentId: second.Id);

        var blocked = Value(await h.Work.GetQueue(0, null, default))
            .Single(e => e.Issue.Key == Key(task)).Blocked;

        // Whose edge it is matters to whoever reads the queue: the fix is on
        // the story, not on the task in front of them.
        Assert.Equal(
            $"{Key(first)} is not done, and {Key(second)} above this cannot be implemented until it is",
            blocked);
    }

    [Fact]
    public async Task Queue_NamesAColumnAndTypeNoPlaybookCovers()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "somebody's paragraph", h.Inbox);

        // Nothing in the seeded matrix speaks for inbox to todo here, which is
        // the difference between "no work left" and "no instructions left" -
        // and a loop that could not tell them apart would report a finished
        // board every night.
        Assert.Equal(
            "no playbook covers \"inbox\" to \"todo\" for a story - add one on the Playbooks page",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Queue_FoldsATypeOnlyWhereNoPlaybookCoversIt()
    {
        var h = await NewAsync();
        await h.FileAsync("epic", "somebody's paragraph", h.Inbox);

        var blocked = Only(await h.Work.GetQueue(0, null, default)).Blocked;

        // One statement of which types a move applies to, and it is the
        // playbook's. A type is a reason only where no row covers the
        // transition for it, and then the sentence names the fix rather than
        // an unwritten rule about what a run picks up.
        Assert.Equal(
            "no playbook covers \"inbox\" to \"todo\" for an epic - add one on the Playbooks page",
            blocked);
        Assert.DoesNotContain("unattended run", blocked);
    }

    [Fact]
    public async Task Queue_NamesTheNextColumnBeingTerminalForAColumnThatIsNotReview()
    {
        var h = await NewAsync();
        h.Db.AddRange(
            new EfHatchStatus { Name = "between", SortOrder = 45 },
            new EfHatchStatus { Name = "shipped", SortOrder = 50, IsTerminal = true });
        await h.Db.SaveChangesAsync();
        await h.FileAsync("story", "awaiting the operator", (await h.Db.Statuses.SingleAsync(s => s.Name == "between")).Id);

        Assert.Equal(
            "the next column is \"shipped\", and only the operator moves work there",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Queue_SaysTheVerdictBeforeItSaysThePlaybookIsMissing()
    {
        var h = await NewAsync();
        await h.FileAsync("epic", "an epic in review", h.Review);

        // Both are true. A branch nobody has checked is the one worth
        // printing: writing a conflict playbook would not make this issue an
        // agent's to work until a runner has looked.
        Assert.Equal(
            "no runner has checked its branch against the trunk yet",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Queue_OrdersEachColumnExactlyAsTheBoardDoes()
    {
        var h = await NewAsync();

        // Interleaved ranks and out-of-order ids across two columns, so that
        // neither the insertion order nor the id can pass for the sort.
        await h.FileAsync("story", "todo, third", h.Todo, rank: 4096);
        await h.FileAsync("story", "in progress, second", h.InProgress, rank: 2048);
        await h.FileAsync("story", "todo, first", h.Todo, rank: 1024);
        await h.FileAsync("bug", "in progress, first", h.InProgress, rank: 1024);
        await h.FileAsync("task", "todo, second", h.Todo, rank: 2048);
        await h.FileAsync("epic", "in progress, third", h.InProgress, rank: 4096);

        var board = Value(await new BoardController(h.Db, h.Actors, TestClaims.With(), h.Time).GetBoard(default));
        var queue = Value(await h.Work.GetQueue(0, null, default));

        // Per column, not flat: the board is ordered by status id and the
        // queue is walked right to left, and what agrees is the sequence
        // inside a column. BoardController serves (StatusId, Rank, Id) and the
        // scan serves (Rank, Id) per column - a card's place in the queue is
        // its place on the board, and nothing between them re-sorts.
        foreach (var column in new[] { h.Todo, h.InProgress })
            Assert.Equal(
                board.Issues.Where(c => c.StatusId == column).Select(c => c.Key),
                queue.Where(e => e.FromStatus.Id == column).Select(e => e.Issue.Key));
    }

    // ---- Express, the hop ----
    //
    // An express issue standing in a column marked ExpressSkips is carried on
    // with no session, as long as it has no unanswered question. Every other
    // fold still holds it exactly as it holds any other issue - these tests
    // reuse each fold's own setup from above and assert its own sentence.

    [Fact]
    public async Task Queue_ListsAnExpressIssueInATickedColumnAsClearAndAHop()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var entry = Only(await h.Work.GetQueue(0, null, default));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);

        var work = Value(await h.Work.GetNextWork(0, null, null, default));
        Assert.Equal(Key(issue), work.Issue.Key);
        Assert.True(work.Hop);
        Assert.Null(work.Playbook);
    }

    [Fact]
    public async Task Hop_IsFoldedByALiveClaimSomebodyElseHolds()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "somebody's already on this", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.ClaimAsync(issue, by: "Ada");

        var blocked = Only(await h.Work.GetQueue(0, null, default)).Blocked;

        Assert.NotNull(blocked);
        Assert.False(Only(await h.Work.GetQueue(0, null, default)).Hop);
    }

    [Fact]
    public async Task Hop_IsFoldedByAReadyDateThatHasNotArrived()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "waiting on a renewal", h.Todo, readyAt: Now.AddDays(3));
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        Assert.Contains("not workable until", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Hop_IsFoldedByAPersonAssignee()
    {
        var h = await NewAsync();
        var ada = h.Actors.AddPerson("Ada");
        var issue = await h.FileAsync("story", "Ada is on this", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.AssignAsync(issue, personId: ada.Id);

        Assert.Contains("assigned to Ada", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Hop_IsFoldedByAnUnansweredQuestion()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "waiting on a person", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.AskAsync(issue, "per-node or global?");

        Assert.Contains("unanswered question", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Hop_IsFoldedByAnUnmetDependencyOnTheMoveIntoInProgress()
    {
        var h = await NewAsync();
        var first = await h.FileAsync("story", "phase one", h.Review);
        var second = await h.FileAsync("story", "phase two", h.Todo);
        await h.DependsAsync(second, first);
        await h.ExpressAsync(second);
        await h.TickExpressSkipsAsync(h.Todo);

        var blocked = Value(await h.Work.GetQueue(0, null, default))
            .Single(e => e.Issue.Key == Key(second)).Blocked;

        Assert.Equal($"{Key(first)} is not done, and this cannot be implemented until it is", blocked);
    }

    [Fact]
    public async Task Hop_IsFoldedByARepositoryTheRunnerLacks()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "bound elsewhere", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it",
            Only(await h.Work.GetQueue(0, null, standing: true, ct: default)).Blocked);
    }

    [Fact]
    public async Task Hop_IsRefusedWhereTheNextColumnIsTerminal()
    {
        var h = await NewAsync();
        h.Db.AddRange(
            new EfHatchStatus { Name = "between", SortOrder = 45 },
            new EfHatchStatus { Name = "shipped", SortOrder = 50, IsTerminal = true });
        await h.Db.SaveChangesAsync();
        var between = await h.Db.Statuses.SingleAsync(s => s.Name == "between");
        var issue = await h.FileAsync("story", "the last mile", between.Id);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(between.Id);

        Assert.Equal(
            "the next column is \"shipped\", and only the operator moves work there",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task ATickedColumnWithAPlaybook_IsAHopAndNotASession()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "would otherwise dispatch normally", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        // Todo -> InProgress has a playbook seeded in NewAsync - the point is
        // that the hop takes it anyway, and hands out no playbook.
        var work = Value(await h.Work.GetNextWork(0, null, null, default));

        Assert.True(work.Hop);
        Assert.Null(work.Playbook);
    }

    [Fact]
    public async Task AnIssueThatIsNotExpress_InATickedColumn_IsUnchanged()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "an ordinary story", h.Todo);
        await h.TickExpressSkipsAsync(h.Todo);

        var work = Value(await h.Work.GetNextWork(0, null, null, default));

        Assert.False(work.Hop);
        Assert.NotNull(work.Playbook);
    }

    [Fact]
    public async Task AnExpressIssue_InAnUntickedColumn_IsUnchanged()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "express, but nothing skips todo", h.Todo);
        await h.ExpressAsync(issue);

        var work = Value(await h.Work.GetNextWork(0, null, null, default));

        Assert.False(work.Hop);
        Assert.NotNull(work.Playbook);
    }

    [Fact]
    public async Task ATickedDeferredColumn_HopsNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "shelved and express", h.Shelved);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Shelved);

        var entry = Value(await h.Work.GetQueue(0, null, default)).SingleOrDefault(e => e.Issue.Key == Key(issue));
        if (entry is not null) Assert.False(entry.Hop);

        Assert.DoesNotContain(
            Key(issue),
            Value(await h.Work.GetQueue(0, null, default)).Where(e => e.Hop).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Express_ChangesNoOrderInTheQueue()
    {
        var h = await NewAsync();
        var judged = await h.FileAsync("story", "awaiting the operator", h.Review);
        var top = await h.FileAsync("story", "top of todo", h.Todo, rank: 1024);
        var hop = await h.FileAsync("story", "carried across", h.Todo, rank: 2048);
        await h.ExpressAsync(hop);
        await h.TickExpressSkipsAsync(h.Todo);

        // Express is not expedite: it floats nothing, so the row still sits
        // exactly where its rank puts it.
        Assert.Equal(
            new[] { judged, top, hop }.Select(Key),
            Value(await h.Work.GetQueue(0, null, default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Hop_MovesTheIssueExactlyOneColumn_ToTheBottom()
    {
        var h = await NewAsync();
        var already = await h.FileAsync("story", "already in the target column", h.InProgress, rank: 1024);
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var moved = Value(await h.Work.HopWork(Key(issue), null, null, null, default));
        var row = await h.Db.Issues.FirstAsync(i => i.Id == issue.Id);

        Assert.Equal(h.InProgress, moved.StatusId);
        Assert.Equal(h.InProgress, row.StatusId);
        Assert.True(row.Rank > already.Rank);
    }

    [Fact]
    public async Task Hop_WritesOneStatusChangedEventNamingTheCallerAndCarryingExpress()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        await h.Work.HopWork(Key(issue), null, null, null, default);

        var row = await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == issue.Id);
        var e = Assert.Single(row.Events);
        Assert.Equal(EfHatchIssueEvent.StatusChanged, e.Kind);
        Assert.Equal("hatch-loop", e.Actor);
        Assert.True(System.Text.Json.JsonDocument.Parse(e.Payload!).RootElement.GetProperty("express").GetBoolean());
    }

    [Fact]
    public async Task Hop_OnAnIssueThatIsNotAHop_Is409AndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "an ordinary story", h.Todo);

        var result = await h.Work.HopWork(Key(issue), null, null, null, default);

        Assert.Equal(StatusCodes.Status409Conflict, ((ObjectResult)result.Result!).StatusCode);
        Assert.Empty((await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == issue.Id)).Events);
    }

    [Fact]
    public async Task Hop_OnABlockedIssue_Is409CarryingTheFoldsSentenceAndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "waiting on a person", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.AskAsync(issue, "per-node or global?");

        var result = await h.Work.HopWork(Key(issue), null, null, null, default);

        var response = (ObjectResult)result.Result!;
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Contains("unanswered question", response.Value?.ToString());
        Assert.Empty((await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == issue.Id)).Events);
    }

    [Fact]
    public async Task Hop_ThroughAKey_IsAllowed()
    {
        // WorkController's own harness authenticates every call as a key
        // (StubCallerIdentity.Key) - the same caller every other test in this
        // file already uses. This test exists to say so out loud: a hop
        // inherits the class's Hatch scope rather than a person-only route,
        // because the loop is exactly who calls one.
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "carried across", h.Todo);
        await h.ExpressAsync(issue);
        await h.TickExpressSkipsAsync(h.Todo);

        var moved = Value(await h.Work.HopWork(Key(issue), null, null, null, default));

        Assert.Equal(h.InProgress, moved.StatusId);
    }

    // ---- Parent pulls, the hop ----
    //
    // A child standing in a column flagged ParentPulls, whose parent stands in
    // the implementation column and none of whose siblings is in flight, is a
    // hop too - carried across with no session, the same as an express issue,
    // but naming the other reason. See HA-149.

    [Fact]
    public async Task Queue_ListsAPulledChildAsClearAndAHopCarryingTheParentKind()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the epic", h.InProgress);
        var child = await h.FileAsync("task", "the child", h.Todo, parentId: parent.Id);
        await h.TickParentPullsAsync(h.Todo);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(child));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Parent, entry.HopKind);

        var work = Value(await h.Work.GetWork(Key(child), null, default));
        Assert.True(work.Hop);
        Assert.Equal(HopKinds.Parent, work.HopKind);
        Assert.Null(work.Playbook);
    }

    [Fact]
    public async Task Hop_OnAPulledChild_MovesItAndWritesPulledRatherThanExpress()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the epic", h.InProgress);
        var child = await h.FileAsync("task", "the child", h.Todo, parentId: parent.Id);
        await h.TickParentPullsAsync(h.Todo);

        var moved = Value(await h.Work.HopWork(Key(child), null, null, null, default));
        Assert.Equal(h.InProgress, moved.StatusId);

        var row = await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == child.Id);
        var e = Assert.Single(row.Events);
        Assert.Equal(EfHatchIssueEvent.StatusChanged, e.Kind);
        var payload = System.Text.Json.JsonDocument.Parse(e.Payload!).RootElement;
        Assert.True(payload.GetProperty("pulled").GetBoolean());
        Assert.False(payload.TryGetProperty("express", out _));
    }

    [Fact]
    public async Task AChildWhoseParentHasNotStarted_IsFoldedNamingThat()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the epic", h.Todo);
        var child = await h.FileAsync("task", "the child", h.Todo, parentId: parent.Id);
        await h.TickParentPullsAsync(h.Todo);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(child));

        Assert.Equal(
            "its parent has not reached the implementation column, so nothing pulls it forward yet",
            entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task AChildWithASiblingInFlight_IsFoldedNamingThat()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the epic", h.InProgress);
        await h.FileAsync("task", "already pulled through", h.InProgress, parentId: parent.Id);
        var child = await h.FileAsync("task", "the child", h.Inbox, parentId: parent.Id);
        await h.TickParentPullsAsync(h.Inbox);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(child));

        Assert.Equal(
            "a sibling is already in flight, so only one child is pulled through at a time",
            entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task ADeferredSibling_NeverHoldsBackTheOneThatPulls()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the epic", h.InProgress);
        await h.FileAsync("task", "shelved, not in flight", h.Shelved, parentId: parent.Id);
        var child = await h.FileAsync("task", "the child", h.Inbox, parentId: parent.Id);
        await h.TickParentPullsAsync(h.Inbox);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(child));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Parent, entry.HopKind);
    }

    [Fact]
    public async Task AChildlessIssueInAParentPullsColumn_KeepsTheNoPlaybookFold()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "nobody's parent and nobody's child", h.Inbox);
        await h.TickParentPullsAsync(h.Inbox);

        Assert.Equal(
            "no playbook covers \"inbox\" to \"todo\" for a story - add one on the Playbooks page",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task AnExpressChildInAParentPullsColumn_StillHopsAsExpress_WhenTheParentHasNotStarted()
    {
        var h = await NewAsync();
        var parent = await h.FileAsync("story", "the epic", h.Todo);
        var child = await h.FileAsync("task", "the child", h.Todo, parentId: parent.Id);
        await h.ExpressAsync(child);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.TickParentPullsAsync(h.Todo);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(child));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Express, entry.HopKind);
    }

    // ---- The pull: an epic and its stories (HA-113) ----
    //
    // An epic standing in a column outside the WIP section whose next column
    // is inside it, with something filed under it, is carried in with no
    // session - the same hop an express issue gets, naming its own reason.
    // While it stands inside the section, a story or bug under it, itself
    // standing in a column ticked ExpressSkips, is carried the same way,
    // naming the epic that carried it.

    [Fact]
    public async Task Queue_ListsAnEpicEnteringTheSectionAsClearAndAHopCarryingTheEpicKind()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "E", h.Todo);
        await h.FileAsync("story", "under E", h.Todo, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(epic));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Epic, entry.HopKind);
        Assert.Null(entry.HopUnder);

        var work = Value(await h.Work.GetWork(Key(epic), null, default));
        Assert.True(work.Hop);
        Assert.Equal(HopKinds.Epic, work.HopKind);
        Assert.Null(work.Playbook);
    }

    [Fact]
    public async Task Hop_OnAnEpicEnteringTheSection_MovesItAndWritesEpicOnTheTrail()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "E", h.Todo);
        await h.FileAsync("story", "under E", h.Todo, parentId: epic.Id);

        var moved = Value(await h.Work.HopWork(Key(epic), null, null, null, default));
        Assert.Equal(h.InProgress, moved.StatusId);

        var row = await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == epic.Id);
        var e = Assert.Single(row.Events);
        Assert.Equal(EfHatchIssueEvent.StatusChanged, e.Kind);
        var payload = System.Text.Json.JsonDocument.Parse(e.Payload!).RootElement;
        Assert.True(payload.GetProperty("epic").GetBoolean());
        Assert.False(payload.TryGetProperty("express", out _));
    }

    [Fact]
    public async Task AnEpicWithNoChildren_IsFoldedNamingNothingIsFiledUnderIt()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "E", h.Todo);

        var entry = Only(await h.Work.GetQueue(0, null, default));

        Assert.Equal(FamilyGate.NothingUnder, entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task Hop_OnAChildlessEpicEnteringTheSection_Is409CarryingThatSentenceAndWritesNothing()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "E", h.Todo);

        var result = await h.Work.HopWork(Key(epic), null, null, null, default);

        var response = (ObjectResult)result.Result!;
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal(FamilyGate.NothingUnder, response.Value?.ToString());
        Assert.Empty((await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == epic.Id)).Events);
    }

    [Fact]
    public async Task AnEpicWithOnlyATaskUnderIt_IsCarried()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "E", h.Todo);
        await h.FileAsync("task", "just a task", h.Todo, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(epic));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Epic, entry.HopKind);
    }

    [Fact]
    public async Task AFullEpicSlice_FoldsAnEpicEnteringTheSection()
    {
        var h = await NewAsync();
        await h.EpicWipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("epic", "already inside", h.InProgress, rank: 256);

        var epic = await h.FileAsync("epic", "waiting to get in", h.Todo, rank: 1024);
        await h.FileAsync("story", "under it", h.Todo, parentId: epic.Id, rank: 1025);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(epic));
        Assert.Equal(
            "the WIP section is full - 1 of 1 epics are in it - nothing more is pulled in until something leaves",
            entry.Blocked);
        Assert.False(entry.Hop);

        var result = await h.Work.HopWork(Key(epic), null, null, null, default);
        var response = (ObjectResult)result.Result!;
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Contains("the WIP section is full", response.Value?.ToString());
    }

    [Fact]
    public async Task Queue_ListsAStoryUnderARunningEpicInATickedColumnAsAHopCarryingUnderTheEpic()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Under, entry.HopKind);
        Assert.Equal(Key(epic), entry.HopUnder);

        var work = Value(await h.Work.GetWork(Key(story), null, default));
        Assert.True(work.Hop);
        Assert.Equal(HopKinds.Under, work.HopKind);
        Assert.Equal(Key(epic), work.HopUnder);
        Assert.Null(work.Playbook);
    }

    [Fact]
    public async Task Hop_OnAStoryUnderARunningEpic_MovesItAndWritesUnderOnTheTrail()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);

        var moved = Value(await h.Work.HopWork(Key(story), null, null, null, default));
        Assert.Equal(h.Todo, moved.StatusId);

        var row = await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == story.Id);
        var e = Assert.Single(row.Events);
        var payload = System.Text.Json.JsonDocument.Parse(e.Payload!).RootElement;
        Assert.Equal(Key(epic), payload.GetProperty("under").GetString());
        Assert.False(payload.TryGetProperty("express", out _));
        Assert.False(payload.TryGetProperty("epic", out _));
    }

    [Fact]
    public async Task ABugUnderARunningEpic_IsCarriedTheSameWay()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var bug = await h.FileAsync("bug", "a bug under E", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(bug));

        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Under, entry.HopKind);
    }

    [Fact]
    public async Task ATaskUnderARunningEpic_IsNotCarried()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var task = await h.FileAsync("task", "a task under E", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(task));

        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task ABugUnderAStoryUnderAnEpic_IsNotCarried()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S, not itself in the ticked column", h.InProgress, parentId: epic.Id);
        var bug = await h.FileAsync("bug", "under the story, not under E", h.Inbox, parentId: story.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(bug));

        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task TheStoriesPull_HoldsWithTheEpicInReviewToo()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.Review);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Under, entry.HopKind);
    }

    [Fact]
    public async Task AStoryUnderARunningEpic_InAnUntickedColumn_IsFoldedByTheOrdinaryPlaybookRule()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S, nothing ticks inbox", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.Equal(
            "no playbook covers \"inbox\" to \"todo\" for a story - add one on the Playbooks page",
            entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task AStoryUnderAnEpicThatIsNotRunning_IsUnaffected()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E, not inside the section", h.Todo);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task ARunningEpicsStory_FoldedByAnUnansweredQuestionBeforeTheHop()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);
        await h.AskAsync(story, "per-node or global?");

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.Contains("unanswered question", entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task ARunningEpicsStory_FoldedByALiveClaimOnTheEpic()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);
        await h.ClaimAsync(epic, by: "Ada");

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.NotNull(entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task AStoryWithAnUnmetDependency_IsStillCarriedToToDo_TheDependencyOnlyGatesTheMoveIntoInProgress()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var blocker = await h.FileAsync("story", "not done yet", h.Todo);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);
        await h.DependsAsync(story, blocker);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Under, entry.HopKind);
    }

    [Fact]
    public async Task WithToDoTicked_AStoryUnderE_InToDo_IsHeldByTheSectionLimitAndEpicLimit()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 256);

        var epic = await h.FileAsync("epic", "E", h.InProgress, rank: 512);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id, rank: 1024);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            entry.Blocked);
        Assert.False(entry.Hop);
    }

    [Fact]
    public async Task GoToWorkUnderE_CarriesEsStories()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Inbox);
        var epic = await h.FileAsync("epic", "E", h.InProgress);
        var story = await h.FileAsync("story", "S", h.Inbox, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, Key(epic), default)).Single(e => e.Issue.Key == Key(story));

        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Under, entry.HopKind);
    }

    [Fact]
    public async Task AnExpressIssueInATickedReviewWithAConflict_IsNotAHop()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Review);
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "up for review", h.Review);
        await h.ExpressAsync(issue);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        var entry = Only(await h.Work.GetQueue(0, null, default));
        Assert.Null(entry.Blocked);
        Assert.False(entry.Hop);
        Assert.Equal(WorkKinds.Conflicts, entry.Kind);

        var result = await h.Work.HopWork(Key(issue), null, null, null, default);
        Assert.Equal(StatusCodes.Status409Conflict, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal(h.Review, (await h.Db.Issues.FirstAsync(i => i.Id == issue.Id)).StatusId);
    }

    [Fact]
    public async Task AStoryUnderARunningEpic_StandingInATickedReviewWithAConflict_IsNotAHop()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Review);
        await h.ConflictPlaybookAsync();
        var epic = await h.FileAsync("epic", "E", h.Review);
        var story = await h.FileAsync("story", "S, up for review too", h.Review, parentId: epic.Id);
        await h.VerdictAsync(story, MergeVerdicts.Conflicted, files: ["a.txt"]);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));
        Assert.False(entry.Hop);
        Assert.Equal(WorkKinds.Conflicts, entry.Kind);

        var result = await h.Work.HopWork(Key(story), null, null, null, default);
        Assert.Equal(StatusCodes.Status409Conflict, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal(h.Review, (await h.Db.Issues.FirstAsync(i => i.Id == story.Id)).StatusId);
    }

    [Fact]
    public async Task TheOverlap_AStoryUnderARunningEpicInAColumnTickedBoth_HopsAsUnderNotParent()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Todo);
        await h.TickParentPullsAsync(h.Todo);
        var epic = await h.FileAsync("epic", "E, in the implementation column", h.InProgress);
        var story = await h.FileAsync("story", "S", h.Todo, parentId: epic.Id);

        var entry = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(story));

        Assert.Null(entry.Blocked);
        Assert.True(entry.Hop);
        Assert.Equal(HopKinds.Under, entry.HopKind);
        Assert.Equal(Key(epic), entry.HopUnder);
    }

    // ---- An issue in review ----

    [Fact]
    public async Task Review_AConflictedBranchIsClear_AndTheDispatchSaysConflicts()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "stopped merging", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        var entry = Only(await h.Work.GetQueue(0, null, default));

        Assert.Null(entry.Blocked);
        Assert.Equal(WorkKinds.Conflicts, entry.Kind);
        Assert.Equal(entry.FromStatus.Id, entry.ToStatus!.Id);

        var work = Value(await h.Work.GetWork(Key(issue), null, default));
        Assert.Null(work.Blocked);
        Assert.Equal(WorkKinds.Conflicts, work.Kind);
        Assert.NotNull(work.Playbook);
        Assert.Equal(MergeVerdicts.Conflicted, Assert.Single(work.Issue.MergeChecks!).Verdict);
    }

    [Fact]
    public async Task Review_NoVerdictAtAllIsFoldedAsUnchecked()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.FileAsync("story", "nobody has looked", h.Review);

        Assert.Equal(
            "no runner has checked its branch against the trunk yet",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_TheUncheckedSentenceNamesTheBoundTrunk()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r", baseBranch: "develop");
        await h.FileAsync("story", "nobody has looked", h.Review);

        Assert.Equal(
            "no runner has checked its branch against develop yet",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_ACleanBranchIsFoldedAndNamesItsTrunk()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "still merges", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean, trunk: "develop");

        Assert.Equal(
            "its branch merges cleanly with develop - nothing for an agent to do",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_NoBranchIsFolded()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "merged already", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.None);

        Assert.Equal("no branch on origin is named for it", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_MoreThanOneBranchIsFolded_AndSaysWhatToDo()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "two candidates", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Ambiguous);

        Assert.Equal(
            "more than one branch on origin is named for it - delete the ones that are not its branch",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AMissingConflictPlaybookIsTheLastFold()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "stopped merging", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        Assert.Equal(
            "no playbook covers \"review\" to \"review\" for a story - add one on the Playbooks page",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AConflictPlaybookNarrowedToOtherTypesLeavesTheIssueFolded()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync(types: "bug");
        var issue = await h.FileAsync("story", "stopped merging", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        Assert.Contains("no playbook covers", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AnUnansweredQuestionOutranksTheConflict()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "stopped merging, and asked", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);
        await h.AskAsync(issue, "which side should win?");

        Assert.Contains("unanswered question", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AnotherRunnersClaimOutranksTheConflict()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "being fixed", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);
        await h.ClaimAsync(issue);

        Assert.NotNull(Only(await h.Work.GetQueue(0, null, default)).Blocked);
        Assert.DoesNotContain("checked", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AReadyDateInTheFutureOutranksTheConflict()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var later = await h.FileAsync("story", "not yet", h.Review, readyAt: Now.AddDays(3));
        await h.VerdictAsync(later, MergeVerdicts.Conflicted, files: ["a.txt"]);

        Assert.Contains("not workable until", Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_TheRepositoryFoldAppliesToAConflict()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "stopped merging", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it",
            Only(await h.Work.GetQueue(0, null, remote: ["https://example.com/other.git"], standing: false, ct: default)).Blocked);
    }

    [Fact]
    public async Task Review_ADeclaredCheckoutOfTheRepositoryClearsTheFold()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "stopped merging", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        Assert.Null(Only(await h.Work.GetQueue(0, null, remote: ["git@example.com:o/r.git"], ct: default)).Blocked);
    }

    /// <summary>
    /// Dependencies gate the move into the implementation column and nothing
    /// else: a pull request that already exists is not held back by what its
    /// ticket once waited on.
    /// </summary>
    [Fact]
    public async Task Review_AnUnmetDependencyDoesNotHoldBackAConflict()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var blocker = await h.FileAsync("story", "phase one", h.InProgress);
        var issue = await h.FileAsync("story", "phase two, stopped merging", h.Review);
        await h.DependsAsync(issue, blocker);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        Assert.Null(Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(issue)).Blocked);
    }

    [Fact]
    public async Task Review_AVerdictForARepositoryTheProjectNoLongerBindsIsIgnored()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/kept");
        var issue = await h.FileAsync("story", "conflicts in a repository let go", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, remote: "https://example.com/let-go", files: ["a.txt"]);

        // A verdict about something the project no longer binds is a fact about
        // nothing anybody is asking about: as far as the board can tell, nobody
        // has checked the repository it does bind.
        Assert.Equal(
            "no runner has checked its branch against the trunk yet",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AnIssueConflictsIfAnyOneOfItsRepositoriesDoes()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/one");
        await h.BindRepositoryAsync("https://example.com/two");
        var issue = await h.FileAsync("story", "clean in one, conflicted in the other", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean, remote: "https://example.com/one");
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, remote: "https://example.com/two", files: ["a.txt"]);

        Assert.Null(Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_AnUnboundProjectCountsEveryVerdict()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "no repositories bound", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, remote: "git@example.com:any/thing.git", files: ["a.txt"]);

        Assert.Null(Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Review_TheFirstClearConflictIsWhatNextReturns()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var clean = await h.FileAsync("story", "clean, and first in the column", h.Review, rank: 1024);
        var conflicted = await h.FileAsync("story", "conflicted", h.Review, rank: 2048);
        await h.VerdictAsync(clean, MergeVerdicts.Clean);
        await h.VerdictAsync(conflicted, MergeVerdicts.Conflicted, files: ["a.txt"]);

        var next = Value(await h.Work.GetNextWork(0, null, null, default));

        Assert.Equal(Key(conflicted), next.Issue.Key);
        Assert.Equal(WorkKinds.Conflicts, next.Kind);
    }

    [Fact]
    public async Task Review_IsWalkedBeforeTheColumnsToItsLeft()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.FileAsync("story", "ready to start", h.Todo);
        var conflicted = await h.FileAsync("story", "conflicted", h.Review);
        await h.VerdictAsync(conflicted, MergeVerdicts.Conflicted, files: ["a.txt"]);

        // Rightmost first: whatever is furthest along is pushed over the line
        // before anything new is opened.
        Assert.Equal(Key(conflicted), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task Kind_IsAdvanceForEveryOrdinaryMove_OnBothEndpoints()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "ordinary", h.Todo);

        Assert.Equal(WorkKinds.Advance, Only(await h.Work.GetQueue(0, null, default)).Kind);
        Assert.Equal(WorkKinds.Advance, Value(await h.Work.GetWork(Key(issue), null, default)).Kind);
        Assert.Equal(WorkKinds.Advance, Value(await h.Work.GetNextWork(0, null, null, default)).Kind);
    }

    // ---- An issue in review whose build failed ----

    [Fact]
    public async Task Build_AFailedBuildOnACleanBranchIsClear_AndTheDispatchSaysBuild()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "red", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Failed);

        var entry = Only(await h.Work.GetQueue(0, null, default));
        Assert.Null(entry.Blocked);
        Assert.Equal(WorkKinds.Build, entry.Kind);
        Assert.Equal(entry.FromStatus.Id, entry.ToStatus!.Id);

        var work = Value(await h.Work.GetWork(Key(issue), null, default));
        Assert.Null(work.Blocked);
        Assert.Equal(WorkKinds.Build, work.Kind);
        Assert.NotNull(work.Playbook);
        Assert.Equal(BuildVerdicts.Failed, Assert.Single(work.Issue.BuildChecks!).Verdict);

        var next = Value(await h.Work.GetNextWork(0, null, null, default));
        Assert.Equal(Key(issue), next.Issue.Key);
        Assert.Equal(WorkKinds.Build, next.Kind);
    }

    [Fact]
    public async Task Build_ConflictsComeFirst_WhateverTheBuildSays()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "conflicted and red", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);
        await h.BuildAsync(issue, BuildVerdicts.Failed);

        var entry = Only(await h.Work.GetQueue(0, null, default));
        Assert.Null(entry.Blocked);
        Assert.Equal(WorkKinds.Conflicts, entry.Kind);
        Assert.Equal(WorkKinds.Conflicts, Value(await h.Work.GetWork(Key(issue), null, default)).Kind);
    }

    [Fact]
    public async Task Build_ARunningBuildIsFolded_NamingTheShortSha()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "running", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Pending);

        Assert.Equal(
            $"its build on {new string('b', 7)} is still running",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_ARunningBuildIsFolded_EvenWithAFailingCheckAlreadyNamed()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "one failed, one still running", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Pending, failing: ["api"]);

        Assert.Equal(
            $"its build on {new string('b', 7)} is still running",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_APassingBuildIsFolded()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "green", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean, trunk: "develop");
        await h.BuildAsync(issue, BuildVerdicts.Passed);

        Assert.Equal(
            "its branch merges cleanly with develop and its build passes - nothing for an agent to do",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_NoChecksIsFolded()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "no CI", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.None);

        Assert.Equal(
            "its branch merges cleanly with main and no checks ran on it",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_AVerdictAboutAnotherShaIsNotRead_AndSaysWhichShaWasNotRead()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "stale build", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);

        // A failure, but about a tip the branch has since moved past: it says
        // nothing about the branch as it stands.
        await h.BuildAsync(issue, BuildVerdicts.Failed, sha: new string('c', 40));

        var entry = Only(await h.Work.GetQueue(0, null, default));
        Assert.Equal($"no runner has read its build on {new string('b', 7)} yet", entry.Blocked);
    }

    [Fact]
    public async Task Build_NoBuildVerdictAtAllKeepsTodaysSentence_WhichIsWhatARepositoryThatCannotBeReadCosts()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "gh is not installed here", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean, trunk: "develop");

        Assert.Equal(
            "its branch merges cleanly with develop - nothing for an agent to do",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_TheUncheckedBranchFoldsComeBeforeAnythingAboutTheBuild()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var unchecked_ = await h.FileAsync("story", "no merge check", h.Review, rank: 1024);
        var ambiguous = await h.FileAsync("story", "two branches", h.Review, rank: 2048);
        var none = await h.FileAsync("story", "no branch", h.Review, rank: 3072);
        await h.BuildAsync(unchecked_, BuildVerdicts.Failed);
        await h.VerdictAsync(ambiguous, MergeVerdicts.Ambiguous);
        await h.BuildAsync(ambiguous, BuildVerdicts.Failed);
        await h.VerdictAsync(none, MergeVerdicts.None);
        await h.BuildAsync(none, BuildVerdicts.Failed);

        var rows = Value(await h.Work.GetQueue(0, null, default)).ToDictionary(e => e.Issue.Key, e => e.Blocked);

        Assert.Contains("no runner has checked its branch", rows[Key(unchecked_)]);
        Assert.Contains("more than one branch on origin", rows[Key(ambiguous)]);
        Assert.Equal("no branch on origin is named for it", rows[Key(none)]);
    }

    [Fact]
    public async Task Build_TwoRepositories_OneCleanAndPassedOneCleanAndFailed_IsBuildWork()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/one");
        await h.BindRepositoryAsync("https://example.com/two");
        var issue = await h.FileAsync("story", "green here, red there", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean, remote: "https://example.com/one");
        await h.VerdictAsync(issue, MergeVerdicts.Clean, remote: "https://example.com/two");
        await h.BuildAsync(issue, BuildVerdicts.Passed, remote: "https://example.com/one");
        await h.BuildAsync(issue, BuildVerdicts.Failed, remote: "https://example.com/two");

        var entry = Only(await h.Work.GetQueue(0, null, default));
        Assert.Null(entry.Blocked);
        Assert.Equal(WorkKinds.Build, entry.Kind);
    }

    [Fact]
    public async Task Build_AFailedBuildInARepositoryTheProjectNoLongerBindsDoesNotCount()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/kept");
        var issue = await h.FileAsync("story", "red in a repository let go", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean, remote: "https://example.com/kept");
        await h.BuildAsync(issue, BuildVerdicts.Failed, remote: "https://example.com/let-go");

        Assert.Equal(
            "its branch merges cleanly with main - nothing for an agent to do",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_EveryOtherFoldStillOutranksAFailedBuild()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var asked = await h.FileAsync("story", "red, and asked", h.Review, rank: 1024);
        var claimed = await h.FileAsync("story", "red, and being fixed", h.Review, rank: 2048);
        var later = await h.FileAsync("story", "red, not yet", h.Review, rank: 3072, readyAt: Now.AddDays(3));
        foreach (var issue in new[] { asked, claimed, later })
        {
            await h.VerdictAsync(issue, MergeVerdicts.Clean);
            await h.BuildAsync(issue, BuildVerdicts.Failed);
        }

        await h.AskAsync(asked, "which way?");
        await h.ClaimAsync(claimed);

        var rows = Value(await h.Work.GetQueue(0, null, default)).ToDictionary(e => e.Issue.Key, e => e.Blocked);

        Assert.Contains("unanswered question", rows[Key(asked)]);
        Assert.NotNull(rows[Key(claimed)]);
        Assert.DoesNotContain("checked", rows[Key(claimed)]);
        Assert.Contains("not workable until", rows[Key(later)]);
    }

    [Fact]
    public async Task Build_AMissingReviewPlaybookIsStillTheLastFold()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "red", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Failed);

        Assert.Equal(
            "no playbook covers \"review\" to \"review\" for a story - add one on the Playbooks page",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task Build_TheRepositoryFoldAppliesToBuildWork()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "red", h.Review);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Failed);

        Assert.Equal(
            "bound to https://example.com/o/r, and this runner has no checkout of it",
            Only(await h.Work.GetQueue(0, null, remote: ["https://example.com/other.git"], standing: false, ct: default)).Blocked);
    }

    [Fact]
    public async Task Build_NothingIsEverDispatchedIntoATerminalColumn()
    {
        var h = await NewAsync();
        await h.ConflictPlaybookAsync();
        var issue = await h.FileAsync("story", "shipped, and red", h.Done);
        await h.VerdictAsync(issue, MergeVerdicts.Clean);
        await h.BuildAsync(issue, BuildVerdicts.Failed);

        Assert.DoesNotContain(Value(await h.Work.GetQueue(0, null, default)), e => e.Issue.Key == Key(issue));
        Assert.Contains("is where work ends", Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    [Fact]
    public async Task Build_TheReviewReadCarriesTheBuildVerdicts()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "red", h.Review);
        await h.BuildAsync(issue, BuildVerdicts.Failed, failing: ["api", "CI"]);

        var row = Assert.Single(Value(await h.Work.GetReview(null, true, default)));

        var build = Assert.Single(row.BuildChecks!);
        Assert.Equal(BuildVerdicts.Failed, build.Verdict);
        Assert.Equal(["CI", "api"], build.Failing.Select(f => f.Name).Order(StringComparer.Ordinal));
    }

    // ---- The review read ----

    [Fact]
    public async Task ReviewRead_ListsTheIssuesInReview_WithTheirVerdictsAndRepositories()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var issue = await h.FileAsync("story", "up for review", h.Review);
        await h.FileAsync("story", "still being written", h.InProgress);
        await h.VerdictAsync(issue, MergeVerdicts.Conflicted, files: ["a.txt"]);

        var rows = Value(await h.Work.GetReview(["git@example.com:o/r.git"], null, default));

        var row = Assert.Single(rows);
        Assert.Equal(Key(issue), row.Key);
        Assert.Equal("git@example.com:o/r.git", Assert.Single(row.Repositories).MatchedRemote);
        Assert.Equal(MergeVerdicts.Conflicted, Assert.Single(row.MergeChecks).Verdict);
    }

    /// <summary>
    /// A verdict is a fact about a branch and not work: a claim, a question, a
    /// ready date and a person's name all keep an issue out of the queue and
    /// none of them keeps its branch from having a verdict.
    /// </summary>
    [Fact]
    public async Task ReviewRead_IsNotNarrowedByAClaimAQuestionADateOrAnAssignee()
    {
        var h = await NewAsync();
        var claimed = await h.FileAsync("story", "being worked", h.Review);
        var asked = await h.FileAsync("story", "asked", h.Review);
        var later = await h.FileAsync("story", "not yet", h.Review, readyAt: Now.AddDays(3));
        var assigned = await h.FileAsync("story", "somebody's", h.Review);
        await h.ClaimAsync(claimed);
        await h.AskAsync(asked, "which side?");
        await h.AssignAsync(assigned, personId: Guid.NewGuid());

        var rows = Value(await h.Work.GetReview(null, true, default));

        Assert.Equal(
            [Key(claimed), Key(asked), Key(later), Key(assigned)],
            rows.Select(r => r.Key).Order());
    }

    [Fact]
    public async Task ReviewRead_LeavesOutWhatTheCallerHoldsNoCheckoutFor()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.FileAsync("story", "bound elsewhere", h.Review);

        Assert.Empty(Value(await h.Work.GetReview(["https://example.com/other.git"], true, default)));
    }

    [Fact]
    public async Task ReviewRead_GivesAnUnboundProjectToACallerWithAStandingCheckoutOnly()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync("story", "no repositories bound", h.Review);

        Assert.Equal(Key(issue), Assert.Single(Value(await h.Work.GetReview(null, true, default))).Key);
        Assert.Empty(Value(await h.Work.GetReview(["https://example.com/other.git"], false, default)));
    }

    [Fact]
    public async Task ReviewRead_AnswersNothingToACallerThatDeclaresNothing()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "in review", h.Review);

        Assert.Empty(Value(await h.Work.GetReview(null, null, default)));
    }

    /// <summary>
    /// Not the clone allowance: a poll clones nothing, so a repository this
    /// runner has never cloned is not one it can check.
    /// </summary>
    [Fact]
    public async Task ReviewRead_TakesNoAccountOfARunnerThatWouldClone()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.FileAsync("story", "bound elsewhere", h.Review);

        // `clones` is not a parameter here at all - the declaration is the
        // remotes and the standing checkout, and nothing else.
        Assert.Empty(Value(await h.Work.GetReview(["https://example.com/other.git"], false, default)));
    }

    [Fact]
    public async Task ReviewRead_KeepsTheColumnsOwnOrderAndLeavesOutOtherColumns()
    {
        var h = await NewAsync();
        var second = await h.FileAsync("story", "below", h.Review, rank: 2048);
        var first = await h.FileAsync("story", "top", h.Review, rank: 1024);
        await h.FileAsync("story", "shipped", h.Done);

        Assert.Equal(
            [Key(first), Key(second)],
            Value(await h.Work.GetReview(null, true, default)).Select(r => r.Key));
    }

    // ---- The review playbook ----

    [Fact]
    public async Task Playbooks_AcceptTheReviewColumnNamingItself()
    {
        var h = await NewAsync();

        var created = Assert.IsType<CreatedAtActionResult>(
            (await h.Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(h.Review, h.Review, [], "resolve it", "sonnet", "high"), default)).Result);

        var dto = Assert.IsType<PlaybookDto>(created.Value);
        Assert.Equal(dto.FromStatusId, dto.ToStatusId);
    }

    [Fact]
    public async Task Playbooks_StillRefuseAnyOtherColumnNamingItself_AndSayWhichMay()
    {
        var h = await NewAsync();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(h.Todo, h.Todo, [], "loop", "sonnet", "high"), default)).Result);

        Assert.Equal(
            "a playbook moves an issue between two columns - only the review column, \"review\", may name itself, and that row is the review playbook",
            refused.Value);
    }

    [Fact]
    public async Task Playbooks_RefuseAColumnThatDoesNotExistNamingItself()
    {
        var h = await NewAsync();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(9999, 9999, [], "loop", "sonnet", "high"), default)).Result);

        Assert.Equal("there is no column 9999", refused.Value);
    }

    [Fact]
    public async Task Playbooks_TheReviewColumnIsMeasuredAfterARename()
    {
        var h = await NewAsync();
        (await h.Db.Statuses.SingleAsync(s => s.Id == h.Review)).Name = "Waiting on Nathan";
        await h.Db.SaveChangesAsync();

        Assert.IsType<CreatedAtActionResult>(
            (await h.Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(h.Review, h.Review, [], "resolve it", "sonnet", "high"), default)).Result);
    }

    [Fact]
    public async Task Playbooks_APatchCannotPointAnOrdinaryRowAtItsOwnColumn()
    {
        var h = await NewAsync();
        var row = await h.Db.Playbooks.FirstAsync(p => p.FromStatusId == h.Todo);

        var refused = await h.Playbooks.PatchPlaybook(
            row.Id, new PlaybookPatchRequest(null, h.Todo, null, null, null, null), default);

        Assert.IsType<BadRequestObjectResult>(refused.Result);
    }

    // ---- The shape axis ----

    [Fact]
    public async Task Playbooks_RefuseAShapeThatIsNotOneOfTheThree()
    {
        var h = await NewAsync();

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", "orphan"), default))
            .Result);

        Assert.Equal("a shape is one of any, leaf, parent - not \"orphan\"", refused.Value);
    }

    [Fact]
    public async Task Playbooks_RefuseADuplicateTransitionTypesAndShape()
    {
        var h = await NewAsync();
        await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it", "sonnet", "high", "leaf"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Playbooks.CreatePlaybook(
                new PlaybookCreateRequest(h.Todo, h.InProgress, [], "do it again", "sonnet", "high", "leaf"), default))
            .Result);

        Assert.Equal("there is already a playbook for that transition, those types and that shape", refused.Value);
    }

    [Fact]
    public async Task Playbooks_ASecondRowForTheSameTransitionAndTypesButADifferentShape_IsNotADuplicate()
    {
        var h = await NewAsync();
        await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "leaf work", "sonnet", "high", "leaf"), default);

        var created = await h.Playbooks.CreatePlaybook(
            new PlaybookCreateRequest(h.Todo, h.InProgress, [], "parent work", "sonnet", "high", "parent"), default);

        Assert.IsType<CreatedAtActionResult>(created.Result);
    }

    // ---- The claim ----
    //
    // The sixth condition: an issue somebody is working right now is not one to
    // send a second agent at. The lease is written straight onto the row here -
    // the endpoints that take and release one are IssueClaimTests' business,
    // and these are about what the dispatcher does with the columns.

    [Fact]
    public async Task AnIssueUnderALiveClaim_IsFoldedPastWithTheSentence()
    {
        var h = await NewAsync();
        var held = await h.FileAsync("story", "somebody is on it", h.Todo, rank: 1024);
        var free = await h.FileAsync("story", "nobody is on it", h.Todo, rank: 2048);
        await h.ClaimAsync(held);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.Equal(
            "hatch is working this from somewhere:/checkouts/one, last heard from just now",
            queue.Single(e => e.Issue.Key == Key(held)).Blocked);

        // And the pass takes the next thing rather than stopping.
        Assert.Equal(Key(free), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task AClaimFoldsBeforeAnythingElseCanBeSaidAboutTheIssue()
    {
        var h = await NewAsync();

        // A ticket with no playbook for its move *and* somebody three minutes
        // into it. "No playbook covers this" is a true sentence about the wrong
        // thing: the claim is the only fold that says work is happening now.
        var held = await h.FileAsync("story", "underway", h.Inbox);
        await h.ClaimAsync(held);

        Assert.Equal(
            "hatch is working this from somewhere:/checkouts/one, last heard from just now",
            Only(await h.Work.GetQueue(0, null, default)).Blocked);
    }

    [Fact]
    public async Task AClaimOlderThanTheTtl_FoldsNothing()
    {
        var h = await NewAsync();
        var abandoned = await h.FileAsync("story", "its runner died", h.Todo);
        await h.ClaimAsync(abandoned);

        // No job ran and nobody cleared anything. The clock moved, which is the
        // whole of the expiry rule - and this test fails the day somebody adds
        // a sweeper and makes the predicate decorative.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        Assert.Null(Only(await h.Work.GetQueue(0, null, default)).Blocked);
        Assert.Equal(Key(abandoned), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task AnIssueOneAlreadyHolds_IsNotFoldedOnThatAccount()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("story", "mine", h.Todo);
        var token = await h.ClaimAsync(mine);

        // Re-reading the dispatch for a ticket one already holds is not a
        // conflict - it is what a runner does after taking the lease.
        Assert.Equal(Key(mine), Value(await h.Work.GetNextWork(0, null, token, default)).Issue.Key);
        Assert.Null(Value(await h.Work.GetWork(Key(mine), token, default)).Blocked);
    }

    [Fact]
    public async Task SomebodyElsesToken_DoesNotUnfoldTheirClaim()
    {
        var h = await NewAsync();
        var theirs = await h.FileAsync("story", "theirs", h.Todo);
        await h.ClaimAsync(theirs);

        Assert.NotNull(Only(await h.Work.GetQueue(0, null, default)).Blocked);
        Assert.NotNull(Value(await h.Work.GetWork(Key(theirs), Guid.NewGuid(), default)).Blocked);
    }

    [Fact]
    public async Task ANamedDispatch_IsRefusedByAClaimToo()
    {
        var h = await NewAsync();
        var held = await h.FileAsync("story", "underway", h.Todo);
        await h.ClaimAsync(held);

        // A claim is a fact about the issue, not the loop's own housekeeping:
        // a person naming a ticket somebody is mid-increment on gets the same
        // sentence, and it is the same method that wrote it.
        var work = Value(await h.Work.GetWork(Key(held), null, default));

        Assert.Equal(
            "hatch is working this from somewhere:/checkouts/one, last heard from just now",
            work.Blocked);
    }

    [Fact]
    public async Task ALiveClaim_RidesTheDispatchesIssueAndItsChildren()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.Todo);
        var child = await h.FileAsync("story", "beneath it", h.Todo, parentId: epic.Id);
        await h.ClaimAsync(child, by: "somebody", runner: "elsewhere:/checkouts/two");

        var work = Value(await h.Work.GetWork(Key(epic), null, default));

        // The card a dispatch hands its session says who is on the child, and
        // says it without the token: that is a capability, not a fact.
        var drawn = Assert.Single(work.Children).Claim;
        Assert.NotNull(drawn);
        Assert.Equal("somebody", drawn.ClaimedBy);
        Assert.Equal("elsewhere:/checkouts/two", drawn.Runner);
    }

    // ---- One runner per line of the tree ----
    //
    // A claim holds its line: nobody else is dispatched at an ancestor or a
    // descendant of an issue somebody is working. Siblings and cousins share no
    // line and stay parallel.

    private static string From() => "hatch is working";

    private const string Where = "from somewhere:/checkouts/one, last heard from just now";

    [Fact]
    public async Task AStorysClaim_FoldsItsTask_AndThePassTakesSomethingElse()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story", h.Todo, rank: 1024);
        var task = await h.FileAsync("task", "beneath it", h.Todo, rank: 2048, parentId: story.Id);
        var free = await h.FileAsync("story", "elsewhere", h.Todo, rank: 3072);
        await h.ClaimAsync(story);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.Equal(
            $"{From()} {Key(story)}, above this, {Where}",
            queue.Single(e => e.Issue.Key == Key(task)).Blocked);
        Assert.Null(queue.Single(e => e.Issue.Key == Key(free)).Blocked);
        Assert.Equal(Key(free), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task ATasksClaim_FoldsItsStory_WithTheSentenceSayingBelow()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story", h.Todo, rank: 1024);
        var task = await h.FileAsync("task", "beneath it", h.Todo, rank: 2048, parentId: story.Id);
        await h.ClaimAsync(task);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.Equal(
            $"{From()} {Key(task)}, below this, {Where}",
            queue.Single(e => e.Issue.Key == Key(story)).Blocked);
    }

    [Fact]
    public async Task AClaim_ReachesAtAnyDepth_InBothDirections()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.Todo, rank: 1024);
        var story = await h.FileAsync("story", "the story", h.Todo, rank: 1024, parentId: epic.Id);
        var task = await h.FileAsync("task", "the task", h.Todo, rank: 1024, parentId: story.Id);

        var epicClaim = await h.ClaimAsync(epic);
        var down = Value(await h.Work.GetQueue(0, null, default)).Single(e => e.Issue.Key == Key(task));
        Assert.Equal($"{From()} {Key(epic)}, above this, {Where}", down.Blocked);

        // Let go of the epic and hold the task instead: the epic, two levels up,
        // is folded, and so is the story between.
        epic.ClaimToken = null;
        epic.ClaimHeartbeatAt = null;
        await h.Db.SaveChangesAsync();
        await h.ClaimAsync(task);

        var up = Value(await h.Work.GetQueue(0, null, default));
        Assert.Equal($"{From()} {Key(task)}, below this, {Where}", up.Single(e => e.Issue.Key == Key(epic)).Blocked);
        Assert.Equal($"{From()} {Key(task)}, below this, {Where}", up.Single(e => e.Issue.Key == Key(story)).Blocked);
        Assert.NotEqual(Guid.Empty, epicClaim);
    }

    [Fact]
    public async Task ASiblingsClaim_FoldsNothing()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story", h.Todo, rank: 1024);
        var one = await h.FileAsync("task", "one", h.Todo, rank: 1024, parentId: story.Id);
        var two = await h.FileAsync("task", "two", h.Todo, rank: 2048, parentId: story.Id);
        await h.ClaimAsync(one);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.Null(queue.Single(e => e.Issue.Key == Key(two)).Blocked);
        Assert.Equal(Key(two), Value(await h.Work.GetNextWork(0, null, null, default)).Issue.Key);
    }

    [Fact]
    public async Task ACousinsClaim_FoldsNeitherTheCousinNorItsParent()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic", h.Done, rank: 1024);
        var s1 = await h.FileAsync("story", "s1", h.Todo, rank: 1024, parentId: epic.Id);
        var t1 = await h.FileAsync("task", "t1", h.Todo, rank: 1024, parentId: s1.Id);
        var s2 = await h.FileAsync("story", "s2", h.Todo, rank: 2048, parentId: epic.Id);
        var t2 = await h.FileAsync("task", "t2", h.Todo, rank: 2048, parentId: s2.Id);
        await h.ClaimAsync(t1);

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.Null(queue.Single(e => e.Issue.Key == Key(s2)).Blocked);
        Assert.Null(queue.Single(e => e.Issue.Key == Key(t2)).Blocked);
        Assert.Contains("below this", queue.Single(e => e.Issue.Key == Key(s1)).Blocked);
    }

    [Fact]
    public async Task ARelativesClaimOlderThanTheTtl_FoldsNothing()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "its runner died", h.Todo, rank: 1024);
        var task = await h.FileAsync("task", "beneath it", h.Todo, rank: 2048, parentId: story.Id);
        await h.ClaimAsync(story);

        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        var queue = Value(await h.Work.GetQueue(0, null, default));
        Assert.All(queue, e => Assert.Null(e.Blocked));
        Assert.Null(Value(await h.Work.GetWork(Key(task), null, default)).Blocked);
    }

    [Fact]
    public async Task ANamedDispatch_IsRefusedByARelativesClaimToo()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "underway", h.Todo, rank: 1024);
        var task = await h.FileAsync("task", "beneath it", h.Todo, rank: 2048, parentId: story.Id);
        await h.ClaimAsync(story);

        var work = Value(await h.Work.GetWork(Key(task), null, default));

        Assert.Equal($"{From()} {Key(story)}, above this, {Where}", work.Blocked);
    }

    [Fact]
    public async Task AnIssueWithItsOwnClaim_PrintsItsOwnSentenceRatherThanARelatives()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story", h.Todo, rank: 1024);
        var task = await h.FileAsync("task", "beneath it", h.Todo, rank: 2048, parentId: story.Id);
        await h.ClaimAsync(story, by: "one", runner: "somewhere:/checkouts/one");
        await h.ClaimAsync(task, by: "two", runner: "elsewhere:/checkouts/two");

        var queue = Value(await h.Work.GetQueue(0, null, default));

        Assert.Equal(
            "two is working this from elsewhere:/checkouts/two, last heard from just now",
            queue.Single(e => e.Issue.Key == Key(task)).Blocked);
        Assert.Equal(
            "one is working this from somewhere:/checkouts/one, last heard from just now",
            queue.Single(e => e.Issue.Key == Key(story)).Blocked);
    }

    [Fact]
    public async Task AClaimRead_WithItsOwnToken_FoldsNeitherTheStoryNorItsTask()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story", "the story", h.Todo, rank: 1024);
        var task = await h.FileAsync("task", "beneath it", h.Todo, rank: 2048, parentId: story.Id);
        var token = await h.ClaimAsync(story);

        // A session holding the story must still be able to read its own tasks.
        Assert.Null(Value(await h.Work.GetWork(Key(story), token, default)).Blocked);
        Assert.Null(Value(await h.Work.GetWork(Key(task), token, default)).Blocked);
        Assert.Equal(Key(story), Value(await h.Work.GetNextWork(0, null, token, default)).Issue.Key);
    }

    // ---- One corner of the board, scanned ----

    [Fact]
    public async Task Queue_UnderAnEpic_LeavesTheRestOfTheBoardAlone()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("epic", "the one I am pushing", h.Todo);
        var story = await h.FileAsync("story", "under mine", h.Todo, parentId: mine.Id);
        await h.FileAsync("story", "somebody else's", h.Todo);

        // The same reading of ancestorKey the search filter and the meters
        // use: what hangs beneath the key, and not the key itself.
        Assert.Equal([Key(story)], Value(await h.Work.GetQueue(0, Key(mine), default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Queue_UnderAnEpic_ReordersOnlyWhatIsInsideTheScope()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("epic", "the one I am pushing", h.Todo);
        var judged = await h.FileAsync("story", "under mine, further along", h.Review, parentId: mine.Id);
        var hurry = await h.FileAsync("story", "under mine, hurried", h.Todo, parentId: mine.Id);
        var elsewhere = await h.FileAsync("story", "somebody else's, hurried", h.Review);

        await h.ExpediteAsync(hurry);
        await h.ExpediteAsync(elsewhere);

        // The scope narrows the candidates before either walk, so a hurried
        // ticket outside it is not reached at all - and inside it the float
        // applies exactly as it does to the whole board.
        Assert.Equal(
            new[] { hurry, judged }.Select(Key),
            Value(await h.Work.GetQueue(0, Key(mine), default)).Select(e => e.Issue.Key));
    }

    [Fact]
    public async Task Queue_UnderAKeyNobodyMinted_IsTheSentenceTheSearchEndpointUses()
    {
        var h = await NewAsync();

        var refusal = Assert.IsType<BadRequestObjectResult>((await h.Work.GetQueue(0, "AER-404", default)).Result);
        Assert.Equal("there is no AER-404", refusal.Value);
    }

    [Fact]
    public async Task Queue_CarriesTheTransitionAnIssueIsClearFor()
    {
        var h = await NewAsync();
        await h.FileAsync("story", "ready to go", h.Todo);

        var entry = Only(await h.Work.GetQueue(0, null, default));

        // What a terminal prints on the line where there is no reason: where
        // this issue is, and where the increment would leave it.
        Assert.Null(entry.Blocked);
        Assert.Equal("todo", entry.FromStatus.Name);
        Assert.Equal("in progress", entry.ToStatus!.Name);
    }

    // ---- The one edge that is cut ----

    [Fact]
    public void WritingAPlaybook_IsClosedToAnApiKey()
    {
        var writes = new[] { nameof(PlaybooksController.CreatePlaybook), nameof(PlaybooksController.PatchPlaybook), nameof(PlaybooksController.DeletePlaybook) };

        foreach (var name in writes)
        {
            var guard = typeof(PlaybooksController).GetMethod(name)!
                .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
                .Cast<RequireRoleAttribute>()
                .SingleOrDefault();

            // No scope named is the whole point: an agent that could widen its
            // own prompt and raise its own effort has no fixed point to settle
            // at. The refusal is a property of the route, not of a prompt
            // asking nicely.
            Assert.NotNull(guard);
            Assert.Null(guard.AcceptScope);
        }
    }

    [Fact]
    public void ReadingAPlaybook_IsOpenToAnApiKey()
    {
        var guard = typeof(PlaybooksController).GetMethod(nameof(PlaybooksController.GetPlaybooks))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // An agent has to be able to read what it was dispatched with.
        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- The WIP section ----
    //
    // The tenth condition: a move whose from is outside the section and whose
    // to is inside it is folded while the section has no room for it - the
    // load, not counting this issue, at or over the limit. Read once for a
    // pass and once for a named dispatch, always over the whole board, and
    // placed after the dependency fold and before the review verdict.

    [Fact]
    public async Task WipFold_FoldsAStoryAndABugWaitingToEnterAFullSection()
    {
        var h = await NewAsync();
        await h.WipAsync(2, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);
        await h.FileAsync("story", "already inside too", h.Review, rank: 512);

        var story = await h.FileAsync("story", "waiting to get in", h.Todo, rank: 1024);
        var bug = await h.FileAsync("bug", "waiting to get in too", h.Todo, rank: 2048);

        const string sentence =
            "the WIP section is full - 2 of 2 stories and bugs are in it - nothing more is pulled in until something leaves";

        var queue = Value(await h.Work.GetQueue(0, null, default)).ToDictionary(e => e.Issue.Key, e => e.Blocked);
        Assert.Equal(sentence, queue[Key(story)]);
        Assert.Equal(sentence, queue[Key(bug)]);

        // No key and no title in it, so a digest of two rows folded for the
        // same reason still groups to one line.
        Assert.DoesNotContain(Key(story), sentence);
        Assert.DoesNotContain("waiting", sentence);

        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(story), null, default)).Blocked);
        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(bug), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_FoldsAnExpeditedStoryTheSameWay()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        var expedited = await h.FileAsync("story", "expedited but no room", h.Todo, rank: 1024);
        await h.ExpediteAsync(expedited);

        // Expedite reorders the queue and gates nothing - it is folded exactly
        // as an ordinary story in the same column would be.
        var queue = Value(await h.Work.GetQueue(0, null, default));
        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            Assert.Single(queue, e => e.Issue.Key == Key(expedited)).Blocked);
    }

    [Fact]
    public async Task WipFold_DoesNotFoldATaskOrAnEpicWithNoEpicLimitSet()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        // Neither type the story-and-bug slice counts, and the epic slice has
        // no limit row here - WipFold's SliceFor check folds nothing for either.
        var task = await h.FileAsync("task", "not counted", h.Todo, rank: 1024);
        var epic = await h.FileAsync("epic", "not counted either", h.Todo, rank: 2048);
        await h.FileAsync("task", "filed under it, so the epic arm's own fold does not speak instead", h.Todo, rank: 2049, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(task), null, default)).Blocked);
        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_FoldsAnEpicWaitingToEnterAFullEpicSlice()
    {
        var h = await NewAsync();
        await h.EpicWipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("epic", "already inside", h.InProgress, rank: 512);

        var epic = await h.FileAsync("epic", "waiting to get in", h.Todo, rank: 1024);

        Assert.Equal(
            "the WIP section is full - 1 of 1 epics are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_AStoryIsNeverFoldedByAFullEpicSlice()
    {
        var h = await NewAsync();
        await h.EpicWipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("epic", "already inside", h.InProgress, rank: 512);

        var story = await h.FileAsync("story", "waiting outside", h.Todo, rank: 1024);

        Assert.Null(Value(await h.Work.GetWork(Key(story), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_AnEpicIsNeverFoldedByAFullStoryAndBugSlice()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        var epic = await h.FileAsync("epic", "waiting outside", h.Todo, rank: 1024);
        await h.FileAsync("task", "filed under it", h.Todo, rank: 1025, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(epic), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_DoesNotFoldAMoveInsideTheSectionOrAConflictedReviewDispatchedToItself()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.ConflictPlaybookAsync();

        // Already inside, on its way further into the section - the section
        // being full is never a reason to hold up work already in it.
        var advancing = await h.FileAsync("story", "already inside, advancing", h.InProgress, rank: 512);
        Assert.Null(Value(await h.Work.GetWork(Key(advancing), null, default)).Blocked);

        // Inside, dispatched to itself for a conflict - also never folded by
        // this: a clean branch is said only of an issue in review, and this one
        // is never leaving the section on this move at all.
        var conflicted = await h.FileAsync("story", "in review, conflicted", h.Review, rank: 1024);
        await h.VerdictAsync(conflicted, MergeVerdicts.Conflicted, files: ["a.txt"]);
        var work = Value(await h.Work.GetWork(Key(conflicted), null, default));
        Assert.Null(work.Blocked);
        Assert.Equal(WorkKinds.Conflicts, work.Kind);
    }

    [Fact]
    public async Task WipFold_AtLimitMinusOne_AClaimedStoryStaysClearAndTheNextOneFolds()
    {
        var h = await NewAsync();
        await h.WipAsync(2, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        var first = await h.FileAsync("story", "first in line", h.Todo, rank: 1024);
        var second = await h.FileAsync("story", "second in line", h.Todo, rank: 2048);

        // One below the limit, and nothing has been claimed yet - both are
        // clear.
        Assert.Null(Value(await h.Work.GetWork(Key(first), null, default)).Blocked);
        Assert.Null(Value(await h.Work.GetWork(Key(second), null, default)).Blocked);

        var token = await h.ClaimAsync(first);

        // Claimed, and re-read with the claim's own token: still clear, because
        // the card the claim counts is the very one asking.
        Assert.Null(Value(await h.Work.GetWork(Key(first), token, default)).Blocked);

        var statuses = await h.Db.Statuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var section = await Wip.LoadAsync(h.Db, TestClaims.With(), statuses, h.Time.GetUtcNow(), default);
        Assert.Equal(1, section!.SliceFor("story")!.ClaimedInbound);

        // A second pass, over the same board: the claim now counts against the
        // next story in line.
        Assert.Equal(
            "the WIP section is full - 2 of 2 stories and bugs are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(second), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_TwoRunnersOneSlot_TheSecondsOwnReReadFolds()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);

        var first = await h.FileAsync("story", "runner A's card", h.Todo, rank: 1024);
        var second = await h.FileAsync("story", "runner B's card", h.Todo, rank: 2048);

        var tokenA = await h.ClaimAsync(first, by: "A");

        // While A's claim is the only one live, A's own re-read is clear - the
        // slot is A's to take.
        Assert.Null(Value(await h.Work.GetWork(Key(first), tokenA, default)).Blocked);

        var tokenB = await h.ClaimAsync(second, by: "B");

        // The claim the first runner took is what stops the second from
        // filling the same slot: B's re-read, on its own different token, is
        // folded - the dispatcher never clears more cards into the section
        // than the limit has room for.
        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(second), tokenB, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_OverTheLimit_NothingNewIsPulledInAndEverythingInsideDispatchesAsBefore()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.ConflictPlaybookAsync();

        // Two already inside a section whose limit is one - an override, or a
        // limit lowered after the fact.
        var advancing = await h.FileAsync("story", "already inside, advancing", h.InProgress, rank: 512);
        var conflicted = await h.FileAsync("story", "already inside, in review", h.Review, rank: 1024);
        await h.VerdictAsync(conflicted, MergeVerdicts.Conflicted, files: ["a.txt"]);

        var waiting = await h.FileAsync("story", "waiting outside", h.Todo, rank: 2048);

        // Nothing new is pulled in...
        Assert.Equal(
            "the WIP section is full - 2 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(waiting), null, default)).Blocked);

        // ...and everything already inside dispatches exactly as it would if
        // the section were not over its limit at all.
        Assert.Null(Value(await h.Work.GetWork(Key(advancing), null, default)).Blocked);
        Assert.Null(Value(await h.Work.GetWork(Key(conflicted), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_WhenTheLoadDropsBelowTheLimit_TheNextPassIsClear()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);

        var claimed = await h.FileAsync("story", "claimed inbound", h.Todo, rank: 1024);
        var waiting = await h.FileAsync("story", "waiting behind it", h.Todo, rank: 2048);

        await h.ClaimAsync(claimed);

        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(waiting), null, default)).Blocked);

        // The claim on the way in outlives its TTL - a runner that died rather
        // than a card that shipped, but the load drops exactly the same way.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));

        Assert.Null(Value(await h.Work.GetWork(Key(waiting), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_WithNoLimitSet_NoRowIsFoldedHoweverTheColumnsAreFlagged()
    {
        var h = await NewAsync();

        // Flagged, but with no WipLimits row at all - Wip.LoadAsync returns
        // null, and WipFold treats null as "fold nothing".
        var inProgress = await h.Db.Statuses.FirstAsync(s => s.Id == h.InProgress);
        var review = await h.Db.Statuses.FirstAsync(s => s.Id == h.Review);
        inProgress.IsWip = true;
        review.IsWip = true;
        await h.Db.SaveChangesAsync();

        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);
        var waiting = await h.FileAsync("story", "waiting outside", h.Todo, rank: 1024);

        Assert.Null(Value(await h.Work.GetWork(Key(waiting), null, default)).Blocked);
    }

    [Fact]
    public async Task WipFold_APassScopedToOneEpic_IsFoldedByLoadFromOutsideIt()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);

        // The load is the board's, not the scope's: filled entirely by work
        // outside the epic the pass is scoped to.
        await h.FileAsync("story", "outside the epic, already inside the section", h.InProgress, rank: 512);

        var epic = await h.FileAsync("epic", "the one epic", h.Todo, rank: 1024);
        var child = await h.FileAsync("story", "under the epic, waiting", h.Todo, rank: 1024, parentId: epic.Id);

        var queue = Value(await h.Work.GetQueue(0, Key(epic), default));
        var row = Assert.Single(queue, e => e.Issue.Key == Key(child));

        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            row.Blocked);
    }

    [Fact]
    public async Task WipFold_AClaimHeldByAnother_FoldsBeforeWip()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        var held = await h.FileAsync("story", "somebody else is on it", h.Todo, rank: 1024);
        await h.ClaimAsync(held);

        var blocked = Value(await h.Work.GetWork(Key(held), null, default)).Blocked;
        Assert.Equal(
            "hatch is working this from somewhere:/checkouts/one, last heard from just now", blocked);
    }

    [Fact]
    public async Task WipFold_AnUnansweredQuestion_FoldsBeforeWip()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        var asked = await h.FileAsync("story", "waiting on an answer", h.Todo, rank: 1024);
        await h.AskAsync(asked, "per-node or global?");

        var blocked = Value(await h.Work.GetWork(Key(asked), null, default)).Blocked;
        Assert.Contains("unanswered question", blocked);
    }

    [Fact]
    public async Task WipFold_AMissingRepository_FoldsBeforeWip()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);
        await h.BindRepositoryAsync("https://example.com/o/r");

        var issue = await h.FileAsync("story", "bound elsewhere", h.Todo, rank: 1024);

        var blocked = Value(await h.Work.GetWork(
            Key(issue), remote: ["https://example.com/other.git"], standing: true, ct: default)).Blocked;
        Assert.Equal("bound to https://example.com/o/r, and this runner has no checkout of it", blocked);
    }

    [Fact]
    public async Task WipFold_AnUnmetDependency_FoldsBeforeWip()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);
        await h.FileAsync("story", "already inside", h.InProgress, rank: 512);

        var blocker = await h.FileAsync("story", "phase one", h.Review, rank: 1024);
        var waiting = await h.FileAsync("story", "phase two", h.Todo, rank: 2048);
        await h.DependsAsync(waiting, blocker);

        var blocked = Value(await h.Work.GetWork(Key(waiting), null, default)).Blocked;
        Assert.Contains($"{Key(blocker)} is not done", blocked);
    }

    [Fact]
    public async Task WipFold_AStoryWithNoPlaybookForItsMove_IsFoldedByWipWhileFull()
    {
        var h = await NewAsync();

        // The section is To Do / In Progress here, deliberately, rather than
        // In Progress / Review: inbox to todo is a move this board's seeded
        // matrix says nothing about, which is the only way to see the WIP
        // fold and the missing-playbook fold both reach for the same row.
        await h.WipAsync(1, h.Todo, h.InProgress);
        await h.FileAsync("story", "already inside", h.Todo, rank: 512);

        var issue = await h.FileAsync("story", "no playbook, and no room either", h.Inbox, rank: 1024);

        // Said before the playbook: the section needs other work to leave,
        // and only once it does is a missing playbook worth mentioning at all.
        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(issue), null, default)).Blocked);
    }

    // ---- An epic's own limit ----
    //
    // HA-112: a story or a bug moving from outside the section to inside it,
    // whose parent is an epic, is held to that epic's own WipLimit (null
    // reading as one) - the same fold as above, with the epic's own numbers,
    // asked beside the section-wide one and on by default wherever the
    // section exists at all, with no WipLimits row of its own needed.

    [Fact]
    public async Task EpicFold_FoldsAnEpicsSecondAndThirdStoryWhileItsFirstIsInTheSection()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 1);
        await h.FileAsync("story", "S1, already inside", h.InProgress, rank: 512, parentId: epic.Id);

        var s2 = await h.FileAsync("story", "S2, waiting", h.Todo, rank: 1024, parentId: epic.Id);
        var s3 = await h.FileAsync("story", "S3, waiting", h.Todo, rank: 2048, parentId: epic.Id);
        var x = await h.FileAsync("story", "X, no parent", h.Todo, rank: 4096);

        var otherEpic = await h.FileAsync("epic", "another epic", h.Todo, rank: 8192);
        var otherChild = await h.FileAsync("story", "under another epic", h.Todo, rank: 8200, parentId: otherEpic.Id);

        var sentence = $"{Key(epic)} is at its limit - 1 of 1 of its stories and bugs is in the WIP section - "
            + "nothing more of it is pulled in until one leaves";

        var queue = Value(await h.Work.GetQueue(0, null, default)).ToDictionary(e => e.Issue.Key, e => e.Blocked);
        Assert.Equal(sentence, queue[Key(s2)]);
        Assert.Equal(sentence, queue[Key(s3)]);
        Assert.Null(queue[Key(x)]);
        Assert.Null(queue[Key(otherChild)]);

        var next = Value(await h.Work.GetNextWork(0, null, null, null, null, null, false, default));
        Assert.NotEqual(Key(s2), next.Issue.Key);
        Assert.NotEqual(Key(s3), next.Issue.Key);

        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(s2), null, default)).Blocked);
    }

    [Fact]
    public async Task EpicFold_WithALimitOfTwo_ASecondStoryLandsAndAClaimedOneStaysClearWhileAThirdFolds()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 2);
        await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);

        var s2 = await h.FileAsync("story", "S2", h.Todo, rank: 1024, parentId: epic.Id);
        var s3 = await h.FileAsync("story", "S3", h.Todo, rank: 2048, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(s2), null, default)).Blocked);

        var token = await h.ClaimAsync(s2);
        Assert.Null(Value(await h.Work.GetWork(Key(s2), token, default)).Blocked);

        var sentence = $"{Key(epic)} is at its limit - 2 of 2 of its stories and bugs are in the WIP section - "
            + "nothing more of it is pulled in until one leaves";
        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(s3), null, default)).Blocked);

        var board = Value(await new BoardController(h.Db, h.Actors, TestClaims.With(), h.Time).GetBoard(default));
        var storyBug = board.Wip!.Slices.Single(s => s.Types.SequenceEqual(new[] { "story", "bug" }));
        Assert.Equal(2, storyBug.Load);
        Assert.Equal(1, storyBug.ClaimedInbound);
    }

    [Fact]
    public async Task EpicFold_WhenAStoryLeavesTheNextPassIsClear_ThenFoldsAgainOnceItIsClaimed()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 1);
        var s1 = await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);
        var s2 = await h.FileAsync("story", "S2", h.Todo, rank: 1024, parentId: epic.Id);
        var s3 = await h.FileAsync("story", "S3", h.Todo, rank: 2048, parentId: epic.Id);

        var sentence = $"{Key(epic)} is at its limit - 1 of 1 of its stories and bugs is in the WIP section - "
            + "nothing more of it is pulled in until one leaves";
        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(s2), null, default)).Blocked);

        s1.StatusId = h.Done;
        await h.Db.SaveChangesAsync();

        Assert.Null(Value(await h.Work.GetWork(Key(s2), null, default)).Blocked);

        await h.ClaimAsync(s2);

        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(s3), null, default)).Blocked);
    }

    [Fact]
    public async Task EpicFold_AtLimitMinusOne_AClaimedStoryStaysClearAndASecondClaimFolds()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 2);
        await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);

        var s2 = await h.FileAsync("story", "S2", h.Todo, rank: 1024, parentId: epic.Id);
        var s3 = await h.FileAsync("story", "S3", h.Todo, rank: 2048, parentId: epic.Id);

        var tokenS2 = await h.ClaimAsync(s2);
        Assert.Null(Value(await h.Work.GetWork(Key(s2), tokenS2, default)).Blocked);

        var tokenS3 = await h.ClaimAsync(s3);
        var sentence = $"{Key(epic)} is at its limit - 2 of 2 of its stories and bugs are in the WIP section - "
            + "nothing more of it is pulled in until one leaves";
        Assert.Equal(sentence, Value(await h.Work.GetWork(Key(s3), tokenS3, default)).Blocked);
    }

    [Fact]
    public async Task EpicFold_WhenBothLimitsAreFull_SaysTheSectionsSentence()
    {
        var h = await NewAsync();
        await h.WipAsync(1, h.InProgress, h.Review);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 1);
        await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);
        var s2 = await h.FileAsync("story", "S2", h.Todo, rank: 1024, parentId: epic.Id);

        Assert.Equal(
            "the WIP section is full - 1 of 1 stories and bugs are in it - nothing more is pulled in until something leaves",
            Value(await h.Work.GetWork(Key(s2), null, default)).Blocked);
    }

    [Fact]
    public async Task EpicFold_HoldsAnExpressHop_AndTheHopEndpointIs409AndWritesNothing()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);
        await h.TickExpressSkipsAsync(h.Todo);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 1);
        await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);

        var s2 = await h.FileAsync("story", "S2, express", h.Todo, rank: 1024, parentId: epic.Id);
        await h.ExpressAsync(s2);

        var sentence = $"{Key(epic)} is at its limit - 1 of 1 of its stories and bugs is in the WIP section - "
            + "nothing more of it is pulled in until one leaves";

        var queue = Value(await h.Work.GetQueue(0, null, default));
        var row = Assert.Single(queue, e => e.Issue.Key == Key(s2));
        Assert.Equal(sentence, row.Blocked);

        var result = await h.Work.HopWork(Key(s2), null, null, null, default);
        var response = (ObjectResult)result.Result!;
        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal(sentence, response.Value?.ToString());
        Assert.Empty((await h.Db.Issues.Include(i => i.Events).FirstAsync(i => i.Id == s2.Id)).Events);
    }

    [Fact]
    public async Task EpicFold_NeverHoldsATaskAnEpicOrAnAdvanceAlreadyInsideTheSection()
    {
        var h = await NewAsync();
        await h.TickWipAsync(h.InProgress, h.Review);

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 1);
        var s1 = await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);

        var task = await h.FileAsync("task", "a task under E", h.Todo, rank: 1024, parentId: epic.Id);
        Assert.Null(Value(await h.Work.GetWork(Key(task), null, default)).Blocked);

        var anotherEpic = await h.FileAsync("epic", "another epic moving in", h.Todo, rank: 2048);
        await h.FileAsync("task", "under the other epic", h.Todo, rank: 2049, parentId: anotherEpic.Id);
        Assert.Null(Value(await h.Work.GetWork(Key(anotherEpic), null, default)).Blocked);

        // Inside the section both ways - S1 advancing from In Progress into
        // Review is never held by its own epic's limit.
        Assert.Null(Value(await h.Work.GetWork(Key(s1), null, default)).Blocked);

        var underEpic = await h.FileAsync("story", "a story under E", h.Todo, rank: 4096, parentId: epic.Id);
        var bug = await h.FileAsync("bug", "under that story, not under E", h.Todo, rank: 4100, parentId: underEpic.Id);

        // The bug's own parent is the story, not the epic - never held by E's
        // limit, whatever E's own load.
        Assert.Null(Value(await h.Work.GetWork(Key(bug), null, default)).Blocked);
    }

    [Fact]
    public async Task EpicFold_WithNoColumnFlagged_HoldsNothingAtAll()
    {
        var h = await NewAsync();

        var epic = await h.FileAsync("epic", "E", h.Todo, rank: 256);
        await h.SetEpicLimitAsync(epic, 1);
        await h.FileAsync("story", "S1", h.InProgress, rank: 512, parentId: epic.Id);
        var s2 = await h.FileAsync("story", "S2", h.Todo, rank: 1024, parentId: epic.Id);

        Assert.Null(Value(await h.Work.GetWork(Key(s2), null, default)).Blocked);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }

        /// <summary>Who the house knows. Empty until a test says otherwise, which reads as "nobody is assigned to anything".</summary>
        public required StubActorDirectory Actors { get; init; }
        public required WorkController Work { get; init; }
        public required PlaybooksController Playbooks { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int Inbox { get; init; }
        public required int Todo { get; init; }
        public required int InProgress { get; init; }
        public required int Review { get; init; }
        public required int Done { get; init; }
        public required int Shelved { get; init; }

        private int next = 1;
        private int nextRepoOrder;

        /// <summary>
        /// An issue placed directly, rather than through the create endpoint -
        /// these tests are about where work is picked up from, so the column
        /// and the rank are the inputs and the create path is not under test.
        /// </summary>
        public async Task<EfHatchIssue> FileAsync(
            string type, string title, int statusId,
            long rank = 1024, DateTimeOffset? readyAt = null, long? parentId = null)
        {
            var issue = new EfHatchIssue
            {
                ProjectId = ProjectId,
                Number = next++,
                Type = type,
                Title = title,
                Description = "",
                StatusId = statusId,
                Rank = rank,
                ParentId = parentId,
                ReadyAt = readyAt,
                ReadyAtHasTime = readyAt is not null,
                CreatedBy = "operator",
                CreatedAt = Now,
                UpdatedAt = Now,
            };

            Db.Issues.Add(issue);
            await Db.SaveChangesAsync();
            return issue;
        }

        /// <summary>
        /// An issue given to somebody, written straight to the columns - what
        /// the route that writes them accepts and refuses is
        /// <see cref="AssigneeControllerTests"/>'s business, and these tests are
        /// about what the dispatcher does with an assignee once one is set. An
        /// id the directory does not know is how a deleted person and a revoked
        /// key are written here, which is what they are.
        /// </summary>
        public async Task AssignAsync(EfHatchIssue issue, Guid? personId = null, Guid? apiKeyId = null)
        {
            issue.AssigneePersonId = personId;
            issue.AssigneeApiKeyId = apiKeyId;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// An issue's own model and effort, set straight on the row - what the
        /// route that writes them refuses and permits is
        /// <see cref="IssuePlaybookControllerTests"/>'s business, and these
        /// tests are about what the dispatcher does with them once set.
        /// </summary>
        public async Task OverrideAsync(EfHatchIssue issue, string? model = null, string? effort = null)
        {
            issue.ModelOverride = model;
            issue.EffortOverride = effort;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// <em>This one first</em>, written straight onto the row. What the
        /// route that writes it accepts and refuses - and above all who may
        /// press it - is <see cref="IssueExpediteControllerTests"/>'s business;
        /// these tests are about what the dispatcher does with the level once
        /// it is set.
        /// </summary>
        public async Task ExpediteAsync(EfHatchIssue issue)
        {
            issue.Priority = PriorityLevels.Expedited;
            await Db.SaveChangesAsync();
        }

        /// <summary>The same, one level up - see <see cref="ExpediteAsync"/>.</summary>
        public async Task EmergencyAsync(EfHatchIssue issue)
        {
            issue.Priority = PriorityLevels.Emergency;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// Express, written straight onto the row - what the route that writes
        /// it accepts and refuses is <see cref="IssueExpressControllerTests"/>'s
        /// business; these tests are about what the dispatcher does with the
        /// flag once it is set.
        /// </summary>
        public async Task ExpressAsync(EfHatchIssue issue)
        {
            issue.Express = true;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// <em>Express skips</em>, written straight onto the column - what the
        /// route that writes it accepts and refuses is
        /// <c>StatusesController.PutExpressSkips</c>'s own tests; these tests
        /// are about what the dispatcher does with a ticked column once it is
        /// there.
        /// </summary>
        public async Task TickExpressSkipsAsync(int statusId)
        {
            var status = await Db.Statuses.FirstAsync(s => s.Id == statusId);
            status.ExpressSkips = true;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// <em>ParentPulls</em>, written straight onto the column - what the
        /// route that writes it accepts and refuses is
        /// <c>StatusesController.PutParentPulls</c>'s own tests; these tests
        /// are about what the dispatcher does with a ticked column once it is
        /// there - see HA-149.
        /// </summary>
        public async Task TickParentPullsAsync(int statusId)
        {
            var status = await Db.Statuses.FirstAsync(s => s.Id == statusId);
            status.ParentPulls = true;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// Turns the WIP section on: the named columns count towards it, and
        /// one limit row for stories and bugs - what the settings route accepts
        /// and refuses is out of scope here (see <c>WipMoveTests</c>); these
        /// tests are about what the dispatcher does with the section once it
        /// exists.
        /// </summary>
        public async Task WipAsync(int limit, params int[] statusIds)
        {
            foreach (var id in statusIds)
            {
                var status = await Db.Statuses.FirstAsync(s => s.Id == id);
                status.IsWip = true;
            }

            Db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.StoriesAndBugs, Limit = limit });
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// Turns the WIP section on with no <c>WipLimits</c> row at all - what
        /// HA-112's own setup starts from, since an epic's limit holds whether
        /// or not the section-wide one is set.
        /// </summary>
        public async Task TickWipAsync(params int[] statusIds)
        {
            foreach (var id in statusIds)
            {
                var status = await Db.Statuses.FirstAsync(s => s.Id == id);
                status.IsWip = true;
            }

            await Db.SaveChangesAsync();
        }

        /// <summary>The same as <see cref="WipAsync"/>, for the epic slice.</summary>
        public async Task EpicWipAsync(int limit, params int[] statusIds)
        {
            foreach (var id in statusIds)
            {
                var status = await Db.Statuses.FirstAsync(s => s.Id == id);
                status.IsWip = true;
            }

            Db.WipLimits.Add(new EfHatchWipLimit { Types = EfHatchWipLimit.Epics, Limit = limit });
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// One epic's own <c>WipLimit</c>, written straight onto the row - what
        /// <c>IssueWipLimitController</c> accepts and refuses is its own tests'
        /// business; these tests are about what HA-112's fold does with the
        /// limit once it is set.
        /// </summary>
        public async Task SetEpicLimitAsync(EfHatchIssue epic, int limit)
        {
            epic.WipLimit = limit;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A lease, written straight onto the row. What the claim endpoints
        /// accept and refuse is IssueClaimTests' business; these tests are
        /// about what the dispatcher does with a claim once it is there, which
        /// is why the columns are the input.
        /// </summary>
        public async Task<Guid> ClaimAsync(
            EfHatchIssue issue, string by = "hatch", string runner = "somewhere:/checkouts/one")
        {
            var token = Guid.NewGuid();
            var now = Time.GetUtcNow();

            issue.ClaimToken = token;
            issue.ClaimedBy = by;
            issue.ClaimRunner = runner;
            issue.ClaimedAt = now;
            issue.ClaimHeartbeatAt = now;
            await Db.SaveChangesAsync();

            return token;
        }

        /// <summary>
        /// A question on an issue, written straight to the table - these tests
        /// are about what a question does to a dispatch, and the endpoint that
        /// writes one is covered where the rest of the comment rules are.
        /// </summary>
        public async Task<EfHatchComment> AskAsync(EfHatchIssue issue, string body)
        {
            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = body,
                Kind = EfHatchComment.Question,
                CreatedAt = Now,
            };

            Db.Comments.Add(comment);
            await Db.SaveChangesAsync();
            return comment;
        }

        /// <summary>
        /// A stall question, written straight to the table with the shared
        /// options and its own <c>asked</c> event - what a runner asks when an
        /// increment does nothing, and what the server asks when a fixed build
        /// fails again. These tests are about what a lapsed one does to a
        /// dispatch, not about who asks it.
        /// </summary>
        public async Task<EfHatchComment> AskStallAsync(EfHatchIssue issue, DateTimeOffset at)
        {
            var comment = new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "hatch-agent",
                Body = "an increment did nothing - what next?",
                Kind = EfHatchComment.Question,
                Options = Questions.WriteOptions(StallAnswers.Options()),
                CreatedAt = at,
            };

            Db.Comments.Add(comment);
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "hatch-agent",
                Kind = EfHatchIssueEvent.Asked,
                At = at,
            });

            await Db.SaveChangesAsync();
            return comment;
        }

        /// <summary>
        /// An event written straight to the trail, at whatever instant a test
        /// says - what a scan's newest-event read is judged against, and what
        /// the let-go count folds over.
        /// </summary>
        public async Task LogEventAsync(EfHatchIssue issue, string kind, object? payload, DateTimeOffset at)
        {
            Db.IssueEvents.Add(new EfHatchIssueEvent
            {
                IssueId = issue.Id,
                Actor = "hatch-agent",
                Kind = kind,
                Payload = payload is null ? null : JsonSerializer.Serialize(payload),
                At = at,
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>A comment of any kind, written straight to the table, optionally already delivered.</summary>
        public async Task SayAsync(EfHatchIssue issue, string kind, string body, DateTimeOffset? deliveredAt = null)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "Nathan",
                Body = body,
                Kind = kind,
                CreatedAt = Now,
                DeliveredAt = deliveredAt,
                DeliveredTo = deliveredAt is null ? null : "somewhere:/checkouts/one",
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// One issue made to wait on another, written straight to the table -
        /// these tests are about what an edge does to a dispatch, and what the
        /// route that writes one refuses is
        /// <see cref="IssueDependenciesControllerTests"/>'s business.
        /// </summary>
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

        public async Task AnswerAsync(EfHatchIssue issue, EfHatchComment question, string body)
        {
            Db.Comments.Add(new EfHatchComment
            {
                IssueId = issue.Id,
                Author = "operator",
                Body = body,
                Kind = EfHatchComment.Answer,
                AnswersId = question.Id,
                CreatedAt = Now,
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// The conflict playbook: the review column to itself, for every type -
        /// what the migration seeds, and what an operator deleting the row turns off.
        /// </summary>
        public async Task ConflictPlaybookAsync(string types = "")
        {
            Db.Playbooks.Add(Playbook(Review, Review, types, "sonnet"));
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A runner's verdict, written straight to the table - what the route
        /// that takes them accepts and refuses is <see cref="MergeCheckControllerTests"/>'
        /// business, and these tests are about what the dispatcher does with one.
        /// </summary>
        public async Task VerdictAsync(
            EfHatchIssue issue, string verdict, string remote = "https://example.com/o/r", string trunk = "main",
            params string[] files)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical!,
                Trunk = trunk,
                TrunkSha = new string('a', 40),
                Verdict = verdict,
                Branch = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? "aer-1-thing" : null,
                BranchSha = verdict is MergeVerdicts.Clean or MergeVerdicts.Conflicted ? new string('b', 40) : null,
                Files = files.Length == 0 ? null : string.Join('\n', files),
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A runner's build verdict, written straight to the table, for the same
        /// reason <see cref="VerdictAsync"/> is. About the sha the merge verdict
        /// helper gives its branch unless a test says otherwise.
        /// </summary>
        public async Task BuildAsync(
            EfHatchIssue issue, string verdict, string remote = "https://example.com/o/r", string? sha = null,
            params string[] failing)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical!,
                Branch = "aer-1-thing",
                Sha = sha ?? new string('b', 40),
                ShaSince = Now,
                Verdict = verdict,
                Failing = EfHatchBuildCheck.WriteFailing(
                    (failing.Length == 0 && verdict == BuildVerdicts.Failed ? ["api"] : failing)
                        .Select(n => new FailingCheckDto(n)).ToList()),
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });

            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// A remote bound to the project, written straight to the table in
        /// the order it is called - every issue <see cref="FileAsync"/> files
        /// lands in the one shared <c>AER</c> project, so every test binds
        /// against the same list. What the route that writes these accepts
        /// and refuses is <see cref="ProjectsControllerTests"/>'s business.
        /// </summary>
        public async Task<EfHatchProjectRepository> BindRepositoryAsync(string remote, string? baseBranch = null)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            var repo = new EfHatchProjectRepository
            {
                ProjectId = ProjectId,
                Remote = remote,
                Canonical = canonical!,
                BaseBranch = baseBranch,
                SortOrder = nextRepoOrder++,
                CreatedAt = Now,
            };

            Db.ProjectRepositories.Add(repo);
            await Db.SaveChangesAsync();
            return repo;
        }
    }

    /// <summary>
    /// A board with the shape the Playbooks migration leaves behind, and the
    /// three playbook rows these tests reason about - a type-specific one, a
    /// catch-all beside it, and one for the column further right.
    /// </summary>
    private static async Task<Harness> NewAsync(string publicBaseUrl = "")
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20 };
        var doing = new EfHatchStatus { Name = "in progress", SortOrder = 30 };
        var review = new EfHatchStatus { Name = "review", SortOrder = 35 };
        var done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };

        // Sorted into the middle of the board on purpose. Every landmark below
        // is measured off the columns the board draws, so a siding sitting
        // between two lanes has to change none of them - and if it ever does,
        // half of these tests say so at once.
        var shelved = new EfHatchStatus { Name = "shelved", SortOrder = 32, IsDeferred = true };
        db.AddRange(project, inbox, todo, doing, review, done, shelved);
        await db.SaveChangesAsync();

        db.AddRange(
            Playbook(todo.Id, doing.Id, "epic", "opus"),
            Playbook(todo.Id, doing.Id, "", "sonnet"),
            Playbook(doing.Id, review.Id, "", "sonnet"));
        await db.SaveChangesAsync();

        var actors = new StubActorDirectory();

        // One clock the tests can move, because the whole point of a lazily
        // expiring lease is that nothing has to run for it to end - advancing
        // this is the only way a claim dies.
        var time = new FakeTimeProvider(Now);

        var caller = new StubCallerIdentity
        {
            Key = new EfApiKey { Name = "hatch-loop", Prefix = "hatch_ak_", Hash = [], CreatedAt = Now },
        };

        return new Harness
        {
            Db = db,
            Time = time,
            Actors = actors,
            Work = new WorkController(
                db, actors, TestClaims.With(), time, Options.Create(new AppsOptions { PublicBaseUrl = publicBaseUrl }),
                new RankService(db), caller),
            Playbooks = new PlaybooksController(db, new FakeTimeProvider(Now)),
            Caller = caller,
            ProjectId = project.Id,
            Inbox = inbox.Id,
            Todo = todo.Id,
            InProgress = doing.Id,
            Review = review.Id,
            Done = done.Id,
            Shelved = shelved.Id,
        };
    }

    /// <summary>Whoever the test says is holding the phone - a key, ordinarily, because the loop is what calls a hop.</summary>
    public sealed class StubCallerIdentity : ICallerIdentity
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

    /// <summary>
    /// The one row of a scan of a board with one issue on it - so that a test
    /// about a single fold says which fold and nothing about arithmetic.
    /// </summary>
    private static QueueEntryDto Only(ActionResult<IReadOnlyList<QueueEntryDto>> result) => Assert.Single(Value(result));

    /// <summary>The display key of an issue these tests filed directly.</summary>
    private static string Key(EfHatchIssue issue) => IssueKey.Format("AER", issue.Number);

    private static EfHatchPlaybook Playbook(int from, int to, string types, string model, string shape = "any") => new()
    {
        FromStatusId = from,
        ToStatusId = to,
        Types = types,
        Shape = shape,
        Prompt = "do the thing",
        Model = model,
        Effort = "high",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result?.GetType().Name ?? "nothing"}");
}
