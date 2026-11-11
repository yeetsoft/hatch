using System.Net;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Models.Auth;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.DeviceMapping;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The runners: what a heartbeat writes and what it must never overwrite, what
/// the read says about a runner that has stopped answering, and the one
/// property the control surface rests on - that an agent cannot raise its own
/// bounds.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class RunnersControllerTests
{
    private const string Runner = "here:/checkouts/one";

    // ---- Saying hello ----

    [Fact]
    public async Task AHeartbeat_PutsTheRunnerOnTheBoard()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Line: "reading the board"));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(Runner, runner.Name);
        Assert.Equal("loop", runner.Kind);
        Assert.Equal("running", runner.State);
        Assert.Equal("reading the board", runner.Line);
        Assert.Equal(Now, runner.FirstSeenAt);
        Assert.Equal(Now, runner.LastSeenAt);
        Assert.Null(runner.ClaimKey);
    }

    [Fact]
    public async Task TheHeartbeatAnswer_NamesWhoTheRunnerWorksFor()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal("Nathan", instruction.For);
    }

    [SkippableFact]
    public async Task AKeyWithNoOwner_FallsBackToTheKeysOwnName()
    {
        var h = await NewAsync(program: true, connectionString: await ConnectAsync());

        // The stub key is called "hatch", and nobody has set it an owner - the
        // directory's principal is null, the same as an admin never having
        // visited the API Keys page. A key-bearing beat lands as a session now
        // (HA-2166), which is why this needs a real Postgres.
        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal("hatch", instruction.For);
    }

    [Fact]
    public async Task ASecondHeartbeat_MovesTheClockAndNotTheFirstSighting()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(Now, runner.FirstSeenAt);
        Assert.Equal(Now.AddMinutes(4), runner.LastSeenAt);
    }

    [Fact]
    public async Task AnEscapedName_IsTheNameTheRunnerCallsItself()
    {
        var h = await NewAsync();

        // Kestrel leaves %2F encoded in the path on purpose, so a runner whose
        // name is a path arrives here still carrying them.
        await h.BeatAsync("here:%2Fcheckouts%2Fone", new RunnerHeartbeatRequest(Kind: "loop"));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(Runner, runner.Name);
    }

    [Fact]
    public async Task ANamelessRunner_IsRefused()
    {
        var h = await NewAsync();

        var refusal = await h.Runners.Heartbeat("   ", new RunnerHeartbeatRequest(), default);

        Assert.Contains("a runner names itself", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWideLine_IsTruncatedRatherThanEndingTheNight()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: new string('x', 900)));

        var runner = Assert.Single(await h.ListAsync());
        Assert.Equal(EfHatchRunner.MaxLineLength, runner.Line!.Length);
    }

    [Fact]
    public async Task AnEmptyLine_ClearsItAndAnAbsentOneLeavesItAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: "waiting"));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal("waiting", (await h.OneAsync()).Line);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: ""));
        Assert.Null((await h.OneAsync()).Line);
    }

    // ---- What it serves ----

    [Fact]
    public async Task AFirstHeartbeatNamingRemotes_StoresTheCanonicalForm()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/repo.git"]));

        var runner = await h.OneAsync();
        Assert.Equal(["example.com/owner/repo"], runner.Repositories);
    }

    [Fact]
    public async Task ALaterHeartbeat_OverwritesTheRepositoriesAPriorOneReported()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/repo.git"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/other.git"]));

        Assert.Equal(["example.com/owner/other"], (await h.OneAsync()).Repositories);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: []));
        Assert.Empty((await h.OneAsync()).Repositories);
    }

    [Fact]
    public async Task ABeatWithNoRemotes_LeavesAPreviouslyReportedRowUntouched()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["git@example.com:owner/repo.git"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Equal(["example.com/owner/repo"], (await h.OneAsync()).Repositories);
    }

    [Fact]
    public async Task AnInvalidRemote_CanonicalisesToNullAndIsDroppedRatherThanRefusingTheHeartbeat()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Remotes: ["not a url"]));

        Assert.Empty((await h.OneAsync()).Repositories);
    }

    [Fact]
    public async Task Clones_IsSeededOverwrittenAndLeftAloneTheSameWayRemotesIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Clones: true));
        Assert.True((await h.OneAsync()).Clones);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Clones: false));
        Assert.False((await h.OneAsync()).Clones);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.False((await h.OneAsync()).Clones);
    }

    /// <summary>Whether this runner was started with do-my-work or --mine, on the same terms Clones is.</summary>
    [Fact]
    public async Task Mine_IsSeededOverwrittenAndLeftAloneTheSameWayCloneIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Mine: true));
        Assert.True((await h.OneAsync()).Mine);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Mine: false));
        Assert.False((await h.OneAsync()).Mine);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.False((await h.OneAsync()).Mine);
    }

    [Fact]
    public async Task Standing_IsSeededOverwrittenAndLeftAloneTheSameWayMineIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Standing: true));
        Assert.True((await h.OneAsync()).Standing);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Standing: false));
        Assert.False((await h.OneAsync()).Standing);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.False((await h.OneAsync()).Standing);
    }

    // ---- Models ----

    [Fact]
    public async Task AFirstHeartbeatNamingModels_StoresThemInTheOrderGiven()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Models: ["haiku", "sonnet"]));

        Assert.Equal(["haiku", "sonnet"], (await h.OneAsync()).Models);
    }

    [Fact]
    public async Task ALaterHeartbeat_OverwritesTheModelsAPriorOneReported()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Models: ["haiku", "sonnet"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Models: ["opus"]));

        Assert.Equal(["opus"], (await h.OneAsync()).Models);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Models: []));
        Assert.Empty((await h.OneAsync()).Models);
    }

    [Fact]
    public async Task ABeatWithNoModels_LeavesAPreviouslyReportedRowUntouched()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Models: ["haiku", "sonnet"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Equal(["haiku", "sonnet"], (await h.OneAsync()).Models);
    }

    [Fact]
    public async Task ARowThatNeverReportedModels_ReadsThemEmpty()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Empty((await h.OneAsync()).Models);
    }

    [Fact]
    public async Task AModelOfAnotherAgent_IsRefusedBeforeAnythingOnTheRowChanges()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Agent: "claude", Models: ["haiku"]));
        var seenAt = (await h.RowAsync()).LastSeenAt;
        h.Time.Advance(TimeSpan.FromSeconds(10));

        // Not BeatAsync: it throws on anything but a value, and this refusal
        // is the assertion.
        var refusal = await h.Runners.Heartbeat(
            Runner, new RunnerHeartbeatRequest(Agent: "claude", Models: ["gpt-6-luna"]), default);

        var owner = ModelFamilies.Default.AgentOf("gpt-6-luna");
        Assert.Contains(
            $"\"gpt-6-luna\" is a {owner} model - this runner's agent is claude",
            Reason(refusal.Result), StringComparison.Ordinal);
        var row = await h.RowAsync();
        Assert.Equal("haiku", row.Models);
        Assert.Equal(seenAt, row.LastSeenAt);
    }

    [Fact]
    public async Task AModelThatIsNoKnownAliasOrId_IsRefusedWithTheRulesSentenceAndLeavesTheRowAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Models: ["haiku"]));

        var refusal = await h.Runners.Heartbeat(Runner, new RunnerHeartbeatRequest(Models: ["nonsense"]), default);

        Assert.Equal(ModelFamilies.Default.Refusal("nonsense"), Reason(refusal.Result));
        Assert.Equal("haiku", (await h.RowAsync()).Models);
    }

    [Fact]
    public async Task ARefusedFirstBeat_CreatesNoRow()
    {
        var h = await NewAsync();

        var refusal = await h.Runners.Heartbeat(Runner, new RunnerHeartbeatRequest(Models: ["nonsense"]), default);

        Assert.Equal(ModelFamilies.Default.Refusal("nonsense"), Reason(refusal.Result));
        Assert.Empty(await h.Db.LegacyRunners.ToListAsync());
    }

    // ---- Has ----

    [Fact]
    public async Task AFirstHeartbeatNamingDependencies_StoresThemInTheOrderGiven()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: ["dotnet@10.0.100", "node@22.11.0"]));

        Assert.Equal(["dotnet@10.0.100", "node@22.11.0"], (await h.OneAsync()).Has);
    }

    [Fact]
    public async Task ALaterHeartbeat_OverwritesTheDependenciesAPriorOneReported()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: ["dotnet@10.0.100", "node@22.11.0"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: ["make"]));

        Assert.Equal(["make"], (await h.OneAsync()).Has);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: []));
        Assert.Empty((await h.OneAsync()).Has);
    }

    [Fact]
    public async Task ABeatWithNoHas_LeavesAPreviouslyReportedRowUntouched()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: ["dotnet@10.0.100", "node@22.11.0"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Equal(["dotnet@10.0.100", "node@22.11.0"], (await h.OneAsync()).Has);
    }

    [Fact]
    public async Task ARowThatNeverReportedHas_ReadsThemEmpty()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Empty((await h.OneAsync()).Has);
    }

    [Fact]
    public async Task ADependencyInAnyCase_IsStoredLowercased()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: ["Dotnet@10.0.100"]));

        Assert.Equal(["dotnet@10.0.100"], (await h.OneAsync()).Has);
        Assert.Equal("dotnet@10.0.100", (await h.RowAsync()).Has);
    }

    [Theory]
    [InlineData("cobol")]
    [InlineData("dotnet@x")]
    public async Task ABadDependency_IsRefusedWithTheRulesSentenceAndLeavesTheRowAlone(string entry)
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Has: ["make"]));
        var seenAt = (await h.RowAsync()).LastSeenAt;
        h.Time.Advance(TimeSpan.FromSeconds(10));

        // Not BeatAsync: it throws on anything but a value, and this refusal
        // is the assertion.
        var refusal = await h.Runners.Heartbeat(Runner, new RunnerHeartbeatRequest(Has: [entry]), default);

        var sentence = DependencySet.Normalise([entry], out _);
        Assert.NotNull(sentence);
        Assert.Equal(sentence, Reason(refusal.Result));
        var row = await h.RowAsync();
        Assert.Equal("make", row.Has);
        Assert.Equal(seenAt, row.LastSeenAt);
    }

    [Fact]
    public async Task ARefusedFirstBeatNamingDependencies_CreatesNoRow()
    {
        var h = await NewAsync();

        var refusal = await h.Runners.Heartbeat(Runner, new RunnerHeartbeatRequest(Has: ["cobol"]), default);

        Assert.Equal(DependencySet.Normalise(["cobol"], out _), Reason(refusal.Result));
        Assert.Empty(await h.Db.LegacyRunners.ToListAsync());
    }

    // ---- Tags ----

    [Fact]
    public async Task ABeatCarryingTags_NeverWritesThem()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: [" Fast ", "GPU"]));
        Assert.Null((await h.RowAsync()).Tags);

        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: ["gpu"]));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["fast"]));

        Assert.Equal(["gpu"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task ABeatWithAMalformedTag_IsNotRefused()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["has space"]));

        Assert.Null((await h.RowAsync()).Tags);
    }

    [Fact]
    public async Task GetRunners_AnswersTagsSortedRegardlessOfTheOrderAPatchNamedThemIn()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: ["gpu", "fast"]));

        Assert.Equal(["fast", "gpu"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task APatchChangingAnUnrelatedField_LeavesTagsExactlyAsAPriorPatchLeftThem()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: ["fast"]));

        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(["fast"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task APatchNamingTags_StoresThemTrimmedAndLowercased()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: ["GPU ", " linux"]));

        Assert.Equal(["gpu", "linux"], (await h.OneAsync()).Tags);
    }

    [Fact]
    public async Task APatchTagThatBreaksTheRule_IsRefused()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Tags: ["a b"]), default);

        Assert.Contains("a tag has no space or comma inside", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APatchWithNineTags_IsRefused()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var nine = Enumerable.Range(1, 9).Select(i => $"tag{i}").ToArray();
        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Tags: nine), default);

        Assert.Contains("a runner carries at most 8 tags", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APatchClearingTags_StoresEmptyAndAFollowingHeartbeatDoesNotReseedIt()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: ["fast"]));

        await h.PatchAsync(Runner, new RunnerPatchRequest(Tags: []));
        Assert.Equal("", (await h.RowAsync()).Tags);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Tags: ["gpu"]));
        Assert.Equal("", (await h.RowAsync()).Tags);
    }

    // ---- RefusesDangerous ----

    [Fact]
    public async Task ASecondBeatSayingFalseAfterTrue_ChangesRefusesDangerous()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(RefusesDangerous: true));
        Assert.True((await h.OneAsync()).RefusesDangerous);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(RefusesDangerous: false));
        Assert.False((await h.OneAsync()).RefusesDangerous);
    }

    [Fact]
    public async Task ABeatThatSaysNothingAboutRefusesDangerous_LeavesASetRowAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(RefusesDangerous: true));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.True((await h.OneAsync()).RefusesDangerous);
    }

    [Fact]
    public async Task ARowNeverToldRefusesDangerous_ReadsFalseOnTheDto()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Null((await h.RowAsync()).RefusesDangerous);
        Assert.False((await h.OneAsync()).RefusesDangerous);
    }

    [Fact]
    public async Task APatchBodyNamingRefusesDangerous_IsUnread()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(RefusesDangerous: true));

        var request = JsonSerializer.Deserialize<RunnerPatchRequest>(
            """{"refusesDangerous":false,"state":"paused"}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var patched = await h.PatchAsync(Runner, request);

        Assert.Equal(EfHatchRunner.Paused, patched.State);
        Assert.True(patched.RefusesDangerous);
    }

    [Fact]
    public async Task APatchChangingAnUnrelatedField_LeavesRefusesDangerousAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(RefusesDangerous: true));

        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.True((await h.OneAsync()).RefusesDangerous);
    }

    // ---- Projects ----

    [Fact]
    public async Task AFreshLoopBeat_StoresTheNormalisedProjectsInTheOrderGiven()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Projects: ["fil", " AER", "FIL"], Fresh: true));

        Assert.Equal("FIL\nAER", (await h.RowAsync()).Projects);
        Assert.Equal(["FIL", "AER"], (await h.OneAsync()).Projects);
    }

    [Fact]
    public async Task ANewRow_StoresTheProjectsItsFirstBeatNamed()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: ["aer"]));

        Assert.Equal(["AER"], (await h.OneAsync()).Projects);
    }

    [Fact]
    public async Task ABeatWithAnEmptyProjectsList_ClearsThem()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: ["AER"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: []));

        Assert.Null((await h.RowAsync()).Projects);
        Assert.Empty((await h.OneAsync()).Projects);
    }

    [Fact]
    public async Task ABeatWithNoProjects_LeavesThemAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: ["AER"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal(["AER"], (await h.OneAsync()).Projects);
    }

    [Fact]
    public async Task ASecondLoopBeat_ThatIsNotFresh_StillChangesProjects()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: ["AER"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: ["FIL"]));

        Assert.Equal(["FIL"], (await h.OneAsync()).Projects);
    }

    [Fact]
    public async Task AOnceBeat_ChangesProjectsToo()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Projects: ["AER"]));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Projects: ["FIL"]));

        Assert.Equal(["FIL"], (await h.OneAsync()).Projects);
    }

    // ---- Out of Claude usage ----

    [Fact]
    public async Task ExhaustedUntil_IsSetClearedAndLeftAloneTheSameWayMineIs()
    {
        var h = await NewAsync();
        var resetAt = Now.AddHours(2);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: true, ExhaustedUntil: resetAt));
        Assert.Equal(resetAt, (await h.OneAsync()).ExhaustedUntil);

        // A loop heartbeat that says it is not out clears it at once.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: false));
        Assert.Null((await h.OneAsync()).ExhaustedUntil);

        // And absent - an older CLI, or hatch work's single beat - leaves
        // whatever the row already said alone.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: true, ExhaustedUntil: resetAt));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal(resetAt, (await h.OneAsync()).ExhaustedUntil);
    }

    [Fact]
    public async Task ExhaustedUntil_IsSeededOnANewRow()
    {
        var h = await NewAsync();
        var resetAt = Now.AddHours(1);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Exhausted: true, ExhaustedUntil: resetAt));

        Assert.Equal(resetAt, (await h.OneAsync()).ExhaustedUntil);
    }

    [Fact]
    public async Task ExhaustedUntil_IsNotSomethingAPersonPatches()
    {
        // No field on the request at all - it is a fact a runner reports about
        // itself, never a person's to set from the page.
        Assert.DoesNotContain(
            typeof(RunnerPatchRequest).GetProperties(), p => p.Name == "ExhaustedUntil" || p.Name == "Exhausted");
    }

    // ---- The account's usage reading, and who it works for ----

    [Fact]
    public async Task ABeatCarryingAReading_StoresItWithTheCallersOwnerId()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        var readAt = Now.AddMinutes(-1);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 63, null)], UsageReadAt: readAt));

        var row = await h.RowAsync();
        Assert.Equal(nathan.Id, row.ForPersonId);
        Assert.Equal(readAt, row.UsageReadAt);
        Assert.Contains("63", row.Usage);
    }

    /// <summary>
    /// HA-173: a fourth window the session stream never carries, and a scoped
    /// weekly one named by the account - both kept exactly as the heartbeat
    /// sent them, the same as every window Hatch has never seen before.
    /// </summary>
    [Fact]
    public async Task ABeatCarryingExtraUsageAndANamedScopedWindow_KeepsBothAsSent()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage:
            [
                new RunnerUsageWindowDto("extra", "Extra usage", 12, null),
                new RunnerUsageWindowDto("weeklyModel", "Weekly (Opus 5)", 88, null),
            ],
            UsageReadAt: Now));

        var row = await h.RowAsync();
        Assert.Contains("\"extra\"", row.Usage);
        Assert.Contains("\"Extra usage\"", row.Usage);
        Assert.Contains("\"weeklyModel\"", row.Usage);
        Assert.Contains("\"Weekly (Opus 5)\"", row.Usage);
    }

    [Fact]
    public async Task ASecondBeatCarryingNoReading_LeavesTheStoredOneWhereItWas()
    {
        var h = await NewAsync();
        var readAt = Now.AddMinutes(-1);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 63, null)], UsageReadAt: readAt));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var row = await h.RowAsync();
        Assert.Equal(readAt, row.UsageReadAt);
        Assert.Contains("63", row.Usage);
    }

    [Fact]
    public async Task APercentPast100_IsClamped()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Usage: [new RunnerUsageWindowDto("session", "Session", 140, null)], UsageReadAt: Now));

        Assert.Contains("100", (await h.RowAsync()).Usage);
        Assert.DoesNotContain("140", (await h.RowAsync()).Usage);
    }

    [Fact]
    public async Task ForPersonId_IsWrittenOnEveryBeatLikeWhereAndRemotes()
    {
        var h = await NewAsync();
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        h.Actors.Principal = null;
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Null((await h.RowAsync()).ForPersonId);
    }

    // ---- What it reports of itself: runs, spent, host ----

    [Fact]
    public async Task RunsSpentAndHost_AreStoredAndReadBack()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Runs: 3, Spent: 1.25m, Host: "Fry", TokensUsed: 500_000, TokensLastHour: 50_000));

        var row = await h.OneAsync();
        Assert.Equal(3, row.Runs);
        Assert.Equal(1.25m, row.Spent);
        Assert.Equal("Fry", row.Host);
        Assert.Equal(500_000, row.TokensUsed);
        Assert.Equal(50_000, row.TokensLastHour);
    }

    [Fact]
    public async Task RunsSpentAndHost_AbsentOnALaterBeat_LeaveTheRowAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Runs: 3, Spent: 1.25m, Host: "Fry", TokensUsed: 500_000, TokensLastHour: 50_000));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var row = await h.OneAsync();
        Assert.Equal(3, row.Runs);
        Assert.Equal(1.25m, row.Spent);
        Assert.Equal("Fry", row.Host);
        Assert.Equal(500_000, row.TokensUsed);
        Assert.Equal(50_000, row.TokensLastHour);
    }

    [Fact]
    public async Task RunsSpentAndHost_OnALaterBeat_OverwriteWithNoFresh()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Runs: 3, Spent: 1.25m, Host: "Fry", TokensUsed: 500_000, TokensLastHour: 50_000));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Runs: 4, Spent: 2.5m, Host: "Leela", TokensUsed: 900_000, TokensLastHour: 90_000));

        var row = await h.OneAsync();
        Assert.Equal(4, row.Runs);
        Assert.Equal(2.5m, row.Spent);
        Assert.Equal("Leela", row.Host);
        Assert.Equal(900_000, row.TokensUsed);
        Assert.Equal(90_000, row.TokensLastHour);
    }

    [Fact]
    public async Task ThrottledUntil_IsWrittenAndClearedByALoopBeat_AndLeftAloneByAOnceBeat()
    {
        var h = await NewAsync();
        var until = Now.AddMinutes(30);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", ThrottledUntil: until));
        Assert.Equal(until, (await h.RowAsync()).ThrottledUntil);

        // A `once` beat shares the loop's row name and must not clear the wait.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));
        Assert.Equal(until, (await h.RowAsync()).ThrottledUntil);

        // A loop beat that says it is not throttled clears it at once.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", ThrottledUntil: null));
        Assert.Null((await h.RowAsync()).ThrottledUntil);
    }

    [Fact]
    public async Task Host_PastItsLimit_IsTruncatedNotRefused()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Host: new string('h', EfHatchRunner.MaxHostLength + 20)));

        Assert.Equal(new string('h', EfHatchRunner.MaxHostLength), (await h.OneAsync()).Host);
    }

    // ---- Where it runs ----

    [Fact]
    public async Task Where_IsStoredAndReadBack()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        Assert.Equal("here:/checkouts/one", (await h.OneAsync()).Where);
    }

    [Fact]
    public async Task ALiveRowAtAnotherWhere_Is409AndLeftUnchanged()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        var refusal = await h.Runners.Heartbeat(
            Runner, new RunnerHeartbeatRequest(Where: "elsewhere:/checkouts/two"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("here:/checkouts/one", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Contains("hatch config", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Equal("here:/checkouts/one", (await h.OneAsync()).Where);
    }

    [Fact]
    public async Task AGoneRowAtAnotherWhere_IsTakenOver()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 1));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "elsewhere:/checkouts/two"));

        Assert.Equal("elsewhere:/checkouts/two", (await h.OneAsync()).Where);
    }

    [Fact]
    public async Task ABeatWithNoWhere_IsNeverRefusedOnThatAccount()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: "here:/checkouts/one"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.NotNull(instruction);
        Assert.Equal("here:/checkouts/one", (await h.OneAsync()).Where);
    }

    // ---- Agent ----

    [Fact]
    public async Task Agent_IsStoredAndReadBack()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Agent: "claude"));

        Assert.Equal("claude", (await h.OneAsync()).Agent);
    }

    [Fact]
    public async Task ABeatWithNoAgent_IsNeverRefusedOnThatAccount()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Agent: "claude"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.NotNull(instruction);
        Assert.Equal("claude", (await h.OneAsync()).Agent);
    }

    // ---- Revision ----

    [Fact]
    public async Task Revision_IsSeededOverwrittenAndLeftAloneTheSameWayAgentIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "abc123"));
        Assert.Equal("abc123", (await h.OneAsync()).Revision);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "def456"));
        Assert.Equal("def456", (await h.OneAsync()).Revision);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal("def456", (await h.OneAsync()).Revision);
    }

    [Fact]
    public async Task Sequence_IsSeededOverwrittenAndLeftAloneTheSameWayAgentIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Sequence: 10));
        Assert.Equal(10, (await h.OneAsync()).Sequence);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Sequence: 20));
        Assert.Equal(20, (await h.OneAsync()).Sequence);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal(20, (await h.OneAsync()).Sequence);
    }

    [Fact]
    public async Task BuiltAt_IsSeededOverwrittenAndLeftAloneTheSameWayAgentIs()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(BuiltAt: Now.AddDays(-1)));
        Assert.Equal(Now.AddDays(-1), (await h.OneAsync()).BuiltAt);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(BuiltAt: Now));
        Assert.Equal(Now, (await h.OneAsync()).BuiltAt);

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal(Now, (await h.OneAsync()).BuiltAt);
    }

    [Fact]
    public async Task BoardDrift_IsBehindWhenTheRunnersSequenceTrailsTheServers()
    {
        var h = await NewAsync(revision: new StubRevision("server-sha", 10));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "old-sha", Sequence: 5));

        Assert.Equal(RunnerBoardDrift.Behind, (await h.OneAsync()).BoardDrift);
    }

    [Fact]
    public async Task BoardDrift_IsCurrentWhenTheShasAgree()
    {
        var h = await NewAsync(revision: new StubRevision("server-sha", 10));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "server-sha", Sequence: 10));

        Assert.Equal(RunnerBoardDrift.Current, (await h.OneAsync()).BoardDrift);
    }

    [Fact]
    public async Task SourceDrift_IsBehindWhenTheRunnersBuildTrailsItsOwnCheckout()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "built-sha", Sequence: 5, SourceRevision: "newer-sha", SourceSequence: 8));

        Assert.Equal(RunnerBoardDrift.Behind, (await h.OneAsync()).SourceDrift);
    }

    [Fact]
    public async Task SourceDrift_IsCurrentWhenTheBuildMatchesItsOwnCheckout()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "built-sha", Sequence: 8, SourceRevision: "built-sha", SourceSequence: 8));

        Assert.Equal(RunnerBoardDrift.Current, (await h.OneAsync()).SourceDrift);
    }

    [Fact]
    public async Task SourceDrift_IsUnknownWhenTheRunnerNeverReportedASource()
    {
        var h = await NewAsync(revision: new StubRevision("server-sha", 10));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Revision: "old-sha", Sequence: 5));

        var runner = await h.OneAsync();
        Assert.Equal(RunnerBoardDrift.Behind, runner.BoardDrift);
        Assert.Equal(RunnerBoardDrift.Unknown, runner.SourceDrift);
    }

    // ---- What it is working ----

    [Fact]
    public async Task ARunnerHoldingATicket_SaysWhichOneAndWhatItIsDoing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: "claimed a ticket", Where: Runner));

        var runner = await h.OneAsync();

        // Read off the issue at request time rather than copied onto the row:
        // an operator clearing a claim clears this too, on the next poll.
        Assert.Equal(issue.Key, runner.ClaimKey);
        Assert.Equal("inbox", runner.ClaimStatus);
        Assert.Equal("make test-api", runner.Line);
    }

    [Fact]
    public async Task AClaimThatWasCleared_LeavesTheRunnerIdleWithNoSecondCopyToTidy()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Line: "claimed a ticket", Where: Runner));

        await h.ReleaseAsync(issue.Key);

        var runner = await h.OneAsync();
        Assert.Null(runner.ClaimKey);
        Assert.Null(runner.ClaimStatus);
        Assert.Equal("claimed a ticket", runner.Line);
    }

    [Fact]
    public async Task AnExpiredClaim_IsNotSomethingARunnerIsStillWorking()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: Runner));

        // Past the lease, and the same arithmetic the board does: a claim
        // nothing has refreshed is not a claim.
        h.Time.Advance(TimeSpan.FromSeconds(TestClaims.Ttl + 1));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: Runner));

        var runner = await h.OneAsync();
        Assert.Null(runner.ClaimKey);
        Assert.Null(runner.ClaimStatus);
    }

    [Fact]
    public async Task AnotherRunnersClaim_IsNotDrawnOnThisRow()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, "elsewhere:/tree", chatter: "make test-api");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Null((await h.OneAsync()).ClaimKey);
    }

    [Fact]
    public async Task ALiveClaim_NoLongerKeepsTheRunnersRowFreshByItself()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Where: Runner));
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api");

        // IssueClaimController's own heartbeat and release now touch the
        // holding runner's row directly, on every beat - see the remark on
        // Runners.Project and Preemption's rule 5, which is what needs
        // LastSeenAt to be that current the moment a runner frees itself.
        // This harness's ClaimAsync writes the claim columns straight onto
        // the row without going through that controller (the endpoint is
        // built out of conditional UPDATEs the in-memory provider refuses),
        // so nothing here simulates the touch - which is exactly why a live
        // claim, on its own, no longer keeps this row reading as here past
        // its own horizon.
        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 10));

        var runner = await h.OneAsync();
        Assert.Equal(issue.Key, runner.ClaimKey);
        Assert.Equal(Now, runner.LastSeenAt);
    }

    // ---- Aging ----

    [Fact]
    public async Task ARunnerThatStoppedAnswering_IsStillDrawnUntilTwoHoursHaveGone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        // Gone, but on the page: a loop that died at midnight is a fact
        // somebody wants to see in the morning, not a row that quietly vanished.
        h.Time.Advance(TimeSpan.FromSeconds(Horizon * 3));
        var gone = await h.OneAsync();
        Assert.Equal(Now, gone.LastSeenAt);
        Assert.Equal(Horizon, gone.GoneAfterSeconds);

        // And then not, dropped by the WHERE on the read with nothing swept.
        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1) - TimeSpan.FromSeconds(Horizon * 3));
        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task ADroppedRunnerThatComesBack_KeepsTheBoundsItHadRatherThanBeingSeededAfresh()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 5));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "40"));

        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        Assert.Empty(await h.ListAsync());

        // The row was never deleted, only aged out of the read - so a beat
        // that is not fresh (a supervisor restart) still finds it bounded the
        // way somebody set it. A fresh loop started by hand takes its flags.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 5));
        Assert.Equal(40, (await h.OneAsync()).MaxRuns);
    }

    [Fact]
    public async Task TheMostRecentlyHeardFrom_IsFirst()
    {
        var h = await NewAsync();
        await h.BeatAsync("first:/tree", new RunnerHeartbeatRequest());

        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync("second:/tree", new RunnerHeartbeatRequest());

        Assert.Equal(["second:/tree", "first:/tree"], (await h.ListAsync()).Select(r => r.Name));
    }

    // ---- Sessions, and liveness drawn from one ----

    [Fact]
    public async Task Live_ListsAKeyWithOnlyALiveSession()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("session-only");
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, Runner));
        await h.Db.SaveChangesAsync();

        Assert.Equal("session-only", (await h.OneAsync()).Name);
    }

    [Fact]
    public async Task Live_DropsAKeyWhoseOnlySessionIsPastTheHorizon()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("session-only");
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, Runner));
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 1));

        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task AKeyWithNoSessions_HasAnEmptySessionsList()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        Assert.Empty((await h.OneAsync()).Sessions);
    }

    [Fact]
    public async Task ASessionOnlyKey_DrivesTheProject()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var key = await h.AddKeyAsync("session-only");
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, "here:/checkouts/one", remotes: "git@example.com:o/r.git"));
        await h.Db.SaveChangesAsync();

        Assert.Equal(["AER"], (await h.OneAsync()).Drives);
    }

    [Fact]
    public async Task TwoLiveSessionsUnderOneKey_DriveAsOneNameNeverTwiceNorAThrow()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        var key = await h.AddKeyAsync("two-sessions");
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, "here:/checkouts/one", remotes: "git@example.com:o/r.git"));
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now.AddSeconds(5), "here:/checkouts/two", remotes: "git@example.com:o/r.git"));
        await h.Db.SaveChangesAsync();

        Assert.Equal(["AER"], (await h.OneAsync()).Drives);
    }

    [Fact]
    public async Task AnOlderBeat_OutranksANewerSessionUnderAnotherName()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.BeatAsync("here:/beat", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        h.Time.Advance(TimeSpan.FromSeconds(10));
        var key = await h.AddKeyAsync("session-runner");
        h.Db.RunnerSessions.Add(NewSession(key.Id, h.Time.GetUtcNow(), "here:/checkouts/one", remotes: "git@example.com:o/r.git"));
        await h.Db.SaveChangesAsync();

        var rows = await h.ListAsync();
        Assert.Equal(["AER"], rows.Single(r => r.Name == "here:/beat").Drives);
        Assert.Empty(rows.Single(r => r.Name == "session-runner").Drives!);
    }

    [Fact]
    public async Task WithTheWallUp_AnOwnersSession_DrivesAnOwnedProject_OverAStrangersOlderBeat()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var stranger = h.Actors.AddPerson("Stranger");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);

        await h.LoopBeatAsync("here:/stranger", stranger);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        var key = await h.AddKeyAsync("owner-session", owner.Id);
        h.Db.RunnerSessions.Add(NewSession(key.Id, h.Time.GetUtcNow(), "here:/checkouts/one", remotes: "git@example.com:o/r.git"));
        await h.Db.SaveChangesAsync();

        var rows = await h.ListAsync();
        Assert.Equal(["AER"], rows.Single(r => r.Name == "owner-session").Drives);
        Assert.Empty(rows.Single(r => r.Name == "here:/stranger").Drives!);
    }

    [Fact]
    public async Task GetRunner_ListsSessionsNewestFirst_TheLiveOneCarriesItsClaim_AnEndedOneDoesNot()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);

        var older = NewSession(key.Id, Now, Runner);
        older.EndedAt = Now;
        h.Db.RunnerSessions.Add(older);
        await h.Db.SaveChangesAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        var newer = NewSession(key.Id, h.Time.GetUtcNow(), Runner);
        h.Db.RunnerSessions.Add(newer);
        await h.Db.SaveChangesAsync();

        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "working it");

        var found = await h.GetAsync(Runner);

        Assert.Equal(2, found!.Sessions.Count);
        Assert.Equal(newer.Id, found.Sessions[0].Id);
        Assert.Equal(issue.Key, found.Sessions[0].ClaimKey);
        Assert.Equal(older.Id, found.Sessions[1].Id);
        Assert.Null(found.Sessions[1].ClaimKey);
    }

    [Fact]
    public async Task AKeyedRunnerWithALiveSessionHoldingAClaim_CarriesItOnTheRow()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, "here:/a"));
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "make test-api", where: "here:/a");

        var runner = await h.OneAsync();

        Assert.Equal(issue.Key, runner.ClaimKey);
        Assert.Equal("make test-api", runner.Line);
        Assert.Equal(Now, runner.LineAt);
        Assert.NotNull(runner.ClaimStatus);
        Assert.Equal(runner.ClaimStatus, runner.Sessions.Single().ClaimStatus);
    }

    [Fact]
    public async Task AKeyedRunnerWhoseLiveSessionsHoldNoClaim_ReturnsNullForAllFour()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, "here:/a"));
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "elsewhere", where: "here:/elsewhere");

        var runner = await h.OneAsync();

        Assert.Null(runner.ClaimKey);
        Assert.Null(runner.Line);
        Assert.Null(runner.LineAt);
        Assert.Null(runner.ClaimStatus);
    }

    [Fact]
    public async Task AKeyedRunnerWithTwoClaimingSessions_CarriesTheOneHeardFromMostRecently()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);
        var earlier = NewSession(key.Id, Now, "here:/a");
        h.Db.RunnerSessions.Add(earlier);
        var first = await h.FileAsync();
        await h.Db.SaveChangesAsync();
        await h.ClaimAsync(first.Key, Runner, chatter: "the first", where: "here:/a");

        h.Time.Advance(TimeSpan.FromSeconds(10));
        h.Db.RunnerSessions.Add(NewSession(key.Id, h.Time.GetUtcNow(), "here:/b"));
        var second = await h.FileAsync();
        await h.Db.SaveChangesAsync();
        await h.ClaimAsync(second.Key, Runner, chatter: "the second", where: "here:/b");

        var runner = await h.OneAsync();

        Assert.Equal(second.Key, runner.ClaimKey);
        Assert.Equal("the second", runner.Line);
    }

    [Fact]
    public async Task AnEndedSessionsClaim_NeverReachesTheRunnerRow()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);
        var ended = NewSession(key.Id, Now, "here:/a");
        ended.EndedAt = Now;
        h.Db.RunnerSessions.Add(ended);
        await h.Db.SaveChangesAsync();
        var issue = await h.FileAsync();
        await h.ClaimAsync(issue.Key, Runner, chatter: "gone", where: "here:/a");

        var runner = await h.GetAsync(Runner);

        Assert.Null(runner!.ClaimKey);
        Assert.Null(runner.Line);
        Assert.Null(runner.ClaimStatus);
    }

    [Fact]
    public async Task AKeyedRunnerWhoseLiveSessionIsOutOfUsage_CarriesTheLatestFutureTimeOnTheRow()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);
        var one = NewSession(key.Id, Now, "here:/a");
        one.ExhaustedUntil = Now.AddHours(1);
        one.ThrottledUntil = Now.AddHours(3);
        var two = NewSession(key.Id, Now, "here:/b");
        two.ExhaustedUntil = Now.AddHours(2);
        two.ThrottledUntil = Now.AddMinutes(-5);
        h.Db.RunnerSessions.AddRange(one, two);
        await h.Db.SaveChangesAsync();

        var runner = await h.OneAsync();

        Assert.Equal(Now.AddHours(2), runner.ExhaustedUntil);
        Assert.Equal(Now.AddHours(3), runner.ThrottledUntil);
    }

    [Fact]
    public async Task AKeyedRunnerWhoseSessionUsageTimesHavePassed_CarriesNoneOnTheRow()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync(Runner);
        var session = NewSession(key.Id, Now, "here:/a");
        session.ExhaustedUntil = Now.AddHours(-1);
        session.ThrottledUntil = Now.AddHours(-2);
        h.Db.RunnerSessions.Add(session);
        await h.Db.SaveChangesAsync();

        var runner = await h.OneAsync();

        Assert.Null(runner.ExhaustedUntil);
        Assert.Null(runner.ThrottledUntil);
    }

    private static EfHatchRunnerSession NewSession(Guid apiKeyId, DateTimeOffset at, string? where, string? remotes = null) => new()
    {
        ApiKeyId = apiKeyId,
        Kind = EfHatchRunner.LoopKind,
        State = EfHatchRunner.Running,
        StartedAt = at,
        LastSeenAt = at,
        Where = where,
        Remotes = remotes,
    };

    // ---- Driving a project ----

    [Fact]
    public async Task TheEarlierFirstSeenRunner_IsTheOnlyOneWhoseInstructionCarriesTheProject()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var first = await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Equal(["AER"], first.Drives);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Empty(second.Drives!);
    }

    [Fact]
    public async Task TheDrivingRunnerGoingQuiet_HandsTheProjectToTheOtherRunner()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        h.Time.Advance(TimeSpan.FromSeconds(Horizon));
        var second = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Equal(["AER"], second.Drives);
    }

    [Fact]
    public async Task ARunnerNamingARemoteNoProjectBinds_DrivesNothing()
    {
        var h = await NewAsync();

        var beat = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:unbound/repo.git"]));

        Assert.Empty(beat.Drives!);
    }

    [Fact]
    public async Task AOnceRunner_IsNeverElectedEvenAsTheOnlyCandidate()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var beat = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Remotes: ["git@example.com:o/r.git"]));

        Assert.Empty(beat.Drives!);
    }

    [Fact]
    public async Task IdenticalFirstSeen_IsBrokenByNameOrdinally_AndStaysThatWayAcrossRepeatedCalls()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        // Written directly, bypassing the heartbeat's own insert ordering, so
        // both rows carry exactly the same FirstSeenAt.
        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "b:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        });
        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "a:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        });
        await h.Db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
        {
            var rows = await h.ListAsync();
            Assert.Equal(["AER"], rows.Single(r => r.Name == "a:/tree").Drives);
            Assert.Empty(rows.Single(r => r.Name == "b:/tree").Drives!);
        }
    }

    [Fact]
    public async Task ARetiredRow_IsNotLive_AndIsNotElected()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "a:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now.AddHours(-1), LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r", RetiredAt = Now,
        });
        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "b:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        });
        await h.Db.SaveChangesAsync();

        var row = Assert.Single(await h.ListAsync());
        Assert.Equal("b:/tree", row.Name);
        Assert.Equal(["AER"], row.Drives);
    }

    // ---- Retiring the rows a heartbeat supersedes ----

    private static EfHatchRunner Quiet(string name, Guid? owner, string where = "w", string kind = EfHatchRunner.LoopKind,
        DateTimeOffset? first = null, DateTimeOffset? last = null) => new()
    {
        Name = name, Kind = kind, Where = where, ForPersonId = owner, State = EfHatchRunner.Running,
        FirstSeenAt = first ?? Now.AddHours(-1), LastSeenAt = last ?? Now.AddMinutes(-30),
    };

    private static EfHatchRunner Stored(Harness h, string name) => h.Db.LegacyRunners.AsNoTracking().Single(r => r.Name == name);

    [Fact]
    public async Task ANewRowBeating_RetiresAnOlderQuietRow_AndSoDoesAnExistingRow()
    {
        var h = await NewAsync(wall: true);
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        h.Db.LegacyRunners.Add(Quiet("old:/one", nathan.Id));
        h.Db.LegacyRunners.Add(Quiet("old:/two", nathan.Id));
        await h.Db.SaveChangesAsync();

        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        var one = Stored(h, "old:/one");
        Assert.Equal(Now, one.RetiredAt);
        Assert.Equal("new:/tree", one.RetiredBy);
        Assert.Null(Stored(h, "new:/tree").RetiredAt);

        // An existing row that beats retires one too: the second old row is
        // put back to quiet-and-unretired, and the next beat finds it.
        var two = await h.Db.LegacyRunners.SingleAsync(r => r.Name == "old:/two");
        two.RetiredAt = null;
        two.RetiredBy = null;
        await h.Db.SaveChangesAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));

        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        Assert.Equal(Now.AddMinutes(1), Stored(h, "old:/two").RetiredAt);
        Assert.Equal(Now, Stored(h, "old:/one").RetiredAt);
    }

    [Fact]
    public async Task AnOlderRowStillBeating_IsLeftAlone()
    {
        var h = await NewAsync(wall: true);
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        h.Db.LegacyRunners.Add(Quiet("old:/one", nathan.Id, last: Now));
        await h.Db.SaveChangesAsync();

        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        var old = Stored(h, "old:/one");
        Assert.Null(old.RetiredAt);
        Assert.Null(old.RetiredBy);
    }

    [Fact]
    public async Task ARowThatIsNotTheSameOwnerWhereKindOrOlder_IsLeftAlone()
    {
        var h = await NewAsync(wall: true);
        var nathan = h.Actors.AddPerson("Nathan");
        var other = h.Actors.AddPerson("Other");
        h.Actors.Principal = nathan;
        h.Db.LegacyRunners.Add(Quiet("other-owner", other.Id));
        h.Db.LegacyRunners.Add(Quiet("other-where", nathan.Id, where: "elsewhere"));
        h.Db.LegacyRunners.Add(Quiet("other-kind", nathan.Id, kind: EfHatchRunner.OnceKind));
        h.Db.LegacyRunners.Add(Quiet("later", nathan.Id, first: Now.AddMinutes(1)));
        h.Db.LegacyRunners.Add(Quiet("same-first", nathan.Id, first: Now));
        await h.Db.SaveChangesAsync();

        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        foreach (var name in new[] { "other-owner", "other-where", "other-kind", "later", "same-first" })
        {
            var row = Stored(h, name);
            Assert.Null(row.RetiredAt);
            Assert.Null(row.RetiredBy);
        }
    }

    [Fact]
    public async Task WithNoOwner_TheWallUpRetiresNothing_AndTheWallOffMatchesNullToNull()
    {
        var up = await NewAsync(wall: true);
        up.Db.LegacyRunners.Add(Quiet("old:/one", null));
        await up.Db.SaveChangesAsync();

        await up.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        Assert.Null(Stored(up, "old:/one").RetiredAt);

        var off = await NewAsync();
        off.Db.LegacyRunners.Add(Quiet("old:/one", null));
        await off.Db.SaveChangesAsync();

        await off.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        Assert.Equal(Now, Stored(off, "old:/one").RetiredAt);
        Assert.Equal("new:/tree", Stored(off, "old:/one").RetiredBy);
    }

    [Fact]
    public async Task ARetiredRowsOwnHeartbeat_ClearsTheRetirement()
    {
        var h = await NewAsync(wall: true);
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        var old = Quiet("old:/one", nathan.Id);
        old.RetiredAt = Now.AddMinutes(-5);
        old.RetiredBy = "new:/tree";
        h.Db.LegacyRunners.Add(old);
        await h.Db.SaveChangesAsync();

        await h.BeatAsync("old:/one", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        var row = Stored(h, "old:/one");
        Assert.Null(row.RetiredAt);
        Assert.Null(row.RetiredBy);
    }

    [Fact]
    public async Task ASecondHeartbeatFromTheSupersedingRow_DoesNotMoveTheRetirement()
    {
        var h = await NewAsync(wall: true);
        var nathan = h.Actors.AddPerson("Nathan");
        h.Actors.Principal = nathan;
        h.Db.LegacyRunners.Add(Quiet("old:/one", nathan.Id));
        await h.Db.SaveChangesAsync();

        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));
        h.Time.Advance(TimeSpan.FromMinutes(4));
        await h.BeatAsync("new:/tree", new RunnerHeartbeatRequest(Kind: "loop", Where: "w"));

        Assert.Equal(Now, Stored(h, "old:/one").RetiredAt);
    }

    [Fact]
    public async Task ARetiredRow_IsStillInListAll_WithTheMarkAndNoDrives()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "a:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now.AddHours(-1), LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r", RetiredAt = Now, RetiredBy = "b:/tree",
        });
        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "b:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        });
        await h.Db.SaveChangesAsync();

        var rows = await h.ListAllAsync();
        var retired = rows.Single(r => r.Name == "a:/tree");
        Assert.Equal(Now, retired.RetiredAt);
        Assert.Equal("b:/tree", retired.SupersededBy);
        Assert.Empty(retired.Drives!);

        var live = rows.Single(r => r.Name == "b:/tree");
        Assert.Null(live.RetiredAt);
        Assert.Null(live.SupersededBy);
    }

    [Fact]
    public async Task ARetiredRow_WithoutTheMark_IsLiveAndElected()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var row = new EfHatchRunner
        {
            Name = "a:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, Remotes = "example.com/o/r",
        };
        h.Db.LegacyRunners.Add(row);
        await h.Db.SaveChangesAsync();

        var listed = Assert.Single(await h.ListAsync());
        Assert.Equal(["AER"], listed.Drives);

        row.RetiredAt = Now;
        await h.Db.SaveChangesAsync();

        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task ARetiredBeat_WithALiveSession_StillReadsLive()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("here:/tree");
        h.Db.LegacyRunners.Add(new EfHatchRunner
        {
            Name = "here:/tree", Kind = EfHatchRunner.LoopKind, FirstSeenAt = Now, LastSeenAt = Now,
            State = EfHatchRunner.Running, RetiredAt = Now,
        });
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, "here:/checkouts/one"));
        await h.Db.SaveChangesAsync();

        var row = Assert.Single(await h.ListAsync());
        Assert.Equal(Now, row.RetiredAt);
    }

    [Fact]
    public async Task GetRunnersAndPatchRunner_AgreeWithTheRunnersOwnHeartbeat()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");

        var beat = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        var listed = await h.OneAsync();
        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(beat.Drives, listed.Drives);
        Assert.Equal(beat.Drives, patched.Drives);
    }

    // ---- A stalled driver, passed over (HA-2092) ----
    //
    // The gone horizon is widened well past the stall window in every test
    // here, so here:/one reads as live throughout without beating again -
    // what hands the project to here:/two is the train's own stall rule
    // alone, never the older rule that an ordinarily quiet driver loses its
    // project.

    [Fact]
    public async Task AHeadMergeableLongerThanTheStallWindow_PassesItsDriverOverOnTheNextHeartbeat()
    {
        var h = await NewAsync(runnerGoneAfterSeconds: 3600);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.SeedMergingHeadAsync();

        var first = await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Equal(["AER"], first.Drives);

        h.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Empty(second.Drives!);

        // Past the ten-minute default, counted from the first beat that ever
        // saw this head acting - not from here:/two's own first sight of it.
        h.Time.Advance(TimeSpan.FromMinutes(10));
        var passedOver = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Equal(["AER"], passedOver.Drives);
        Assert.Equal("here:/one", (await h.Db.Projects.AsNoTracking().SingleAsync()).TrainStalledDriver);
    }

    [Fact]
    public async Task ThePassOver_WritesOneDriverStalledEventNamingBoth()
    {
        var h = await NewAsync(runnerGoneAfterSeconds: 3600);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var head = await h.SeedMergingHeadAsync();

        await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        var key = IssueKey.Format("AER", head.Number);
        var @event = Assert.Single(await h.EventsAsync(key), e => e.Kind == EfHatchIssueEvent.DriverStalled);
        Assert.Equal("Hatch", @event.Actor);
        var payload = JsonDocument.Parse(@event.Payload!).RootElement;
        Assert.Equal("here:/one", payload.GetProperty("passedOver").GetString());
        Assert.Equal("here:/two", payload.GetProperty("elected").GetString());

        // A further beat inside the window the pass-over just opened writes
        // none: the clock it restarted has not run out again.
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Single(await h.EventsAsync(key), e => e.Kind == EfHatchIssueEvent.DriverStalled);
    }

    [Fact]
    public async Task ABeatUnderThePassedOverRunnersOwnName_ClearsItAndReElectsIt()
    {
        var h = await NewAsync(runnerGoneAfterSeconds: 3600);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.SeedMergingHeadAsync();

        await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Equal("here:/one", (await h.Db.Projects.AsNoTracking().SingleAsync()).TrainStalledDriver);

        var reinstated = await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Equal(["AER"], reinstated.Drives);
        Assert.Null((await h.Db.Projects.AsNoTracking().SingleAsync()).TrainStalledDriver);
    }

    [Fact]
    public async Task ATrainStallMinutesOfZeroOrNegative_FallsBackToTenMinutesRatherThanStallingAtOnce()
    {
        var h = await NewAsync(trainStallMinutes: 0, runnerGoneAfterSeconds: 3600);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.SeedMergingHeadAsync();

        await h.BeatAsync("here:/one", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        // Short of the ten-minute default a configured zero falls back to -
        // if zero instead meant "stall at once", this would already have
        // been passed over.
        h.Time.Advance(TimeSpan.FromMinutes(9));
        var stillWaiting = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Empty(stillWaiting.Drives!);

        h.Time.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        var passedOver = await h.BeatAsync("here:/two", new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        Assert.Equal(["AER"], passedOver.Drives);
    }

    // ---- Driving a project: whose runner may ----

    [Fact]
    public async Task AnOwnersRunnerStartedLater_StillDrivesAnOwnedProject_OverAStrangersOlderOne()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var stranger = h.Actors.AddPerson("Stranger");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);

        var first = await h.LoopBeatAsync("here:/stranger", stranger);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await h.LoopBeatAsync("here:/owner", owner);

        Assert.Empty(first.Drives!);
        Assert.Equal(["AER"], second.Drives);
        Assert.Empty((await h.ListAsync()).Single(r => r.Name == "here:/stranger").Drives!);
    }

    [Fact]
    public async Task AnApproversRunner_DrivesAnOwnedProject()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var approver = h.Actors.AddPerson("Approver");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);
        await h.AddMemberAsync(approver.Id, ProjectMemberRole.Approver);

        var beat = await h.LoopBeatAsync("here:/approver", approver);

        Assert.Equal(["AER"], beat.Drives);
    }

    [Fact]
    public async Task AKeyBelongingToNobody_DrivesNothingOnAnOwnedProject()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddMemberAsync(h.Actors.AddPerson("Owner").Id, ProjectMemberRole.Owner);

        var beat = await h.LoopBeatAsync("here:/orphan", null);

        Assert.Empty(beat.Drives!);
    }

    [Fact]
    public async Task WithOnlyAStrangersRunnerLive_NobodyDrivesTheOwnedProject()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddMemberAsync(h.Actors.AddPerson("Owner").Id, ProjectMemberRole.Owner);

        var beat = await h.LoopBeatAsync("here:/stranger", h.Actors.AddPerson("Stranger"));

        Assert.Empty(beat.Drives!);
        Assert.Empty((await h.OneAsync()).Drives!);
    }

    [Fact]
    public async Task WithTheWallOff_TheOldestRunnerDrivesAnOwnedProject_WhoeverItWorksFor()
    {
        var h = await NewAsync();
        await h.BindRepositoryAsync("https://example.com/o/r");
        await h.AddMemberAsync(h.Actors.AddPerson("Owner").Id, ProjectMemberRole.Owner);

        var beat = await h.LoopBeatAsync("here:/stranger", h.Actors.AddPerson("Stranger"));

        Assert.Equal(["AER"], beat.Drives);
    }

    [Fact]
    public async Task AProjectWithNoOwner_IsDrivenByTheOldestRunner_EvenWithTheWallUp()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var stranger = h.Actors.AddPerson("Stranger");

        var bare = await h.LoopBeatAsync("here:/stranger", stranger);
        Assert.Equal(["AER"], bare.Drives);

        // An approver without an owner is still an ownerless project.
        await h.AddMemberAsync(h.Actors.AddPerson("Approver").Id, ProjectMemberRole.Approver);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        var later = await h.LoopBeatAsync("here:/orphan", null);

        Assert.Empty(later.Drives!);
        Assert.Equal(["AER"], (await h.LoopBeatAsync("here:/stranger", stranger)).Drives);
    }

    [Fact]
    public async Task MembershipChanges_MoveTheProjectOnTheNextBeat()
    {
        var h = await NewAsync(wall: true);
        await h.BindRepositoryAsync("https://example.com/o/r");
        var owner = h.Actors.AddPerson("Owner");
        var stranger = h.Actors.AddPerson("Stranger");
        await h.AddMemberAsync(owner.Id, ProjectMemberRole.Owner);

        await h.LoopBeatAsync("here:/owner", owner);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty((await h.LoopBeatAsync("here:/stranger", stranger)).Drives!);

        // The stranger becomes an approver, but the owner's runner still holds it.
        await h.AddMemberAsync(stranger.Id, ProjectMemberRole.Approver);
        Assert.Empty((await h.LoopBeatAsync("here:/stranger", stranger)).Drives!);

        // The owner's runner goes quiet, and the approver's takes over.
        h.Time.Advance(TimeSpan.FromSeconds(Horizon));
        Assert.Equal(["AER"], (await h.LoopBeatAsync("here:/stranger", stranger)).Drives);

        // Removed again, they lose it on the very next beat.
        await h.RemoveMemberAsync(stranger.Id);
        Assert.Empty((await h.LoopBeatAsync("here:/stranger", stranger)).Drives!);
    }

    // ---- The seed ----

    [Fact]
    public async Task TheFirstHeartbeat_SeedsTheBoundsTheProcessStartedWith()
    {
        var h = await NewAsync();

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Under: "AER-930", MaxRuns: 12, MaxSpend: 40m, UntilAt: Now.AddHours(6),
            MaxTokens: 2_000_000, MaxTokensPerHour: 200_000));

        var runner = await h.OneAsync();
        Assert.Equal("AER-930", runner.Under);
        Assert.Equal(12, runner.MaxRuns);
        Assert.Equal(40m, runner.MaxSpend);
        Assert.Equal(Now.AddHours(6), runner.UntilAt);
        Assert.Equal(2_000_000, runner.MaxTokens);
        Assert.Equal(200_000, runner.MaxTokensPerHour);
    }

    [Fact]
    public async Task ALaterHeartbeat_NeverSeedsOverWhatAPersonSet()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 12, MaxSpend: 40m));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "3", MaxSpend: "5"));

        // A restart sends the flags it was started with all over again. If that
        // re-seeded, an edit made at midnight would be undone by the loop's own
        // half-hourly restart - which is the failure this rule exists for.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(MaxRuns: 12, MaxSpend: 40m));

        var runner = await h.OneAsync();
        Assert.Equal(3, runner.MaxRuns);
        Assert.Equal(5m, runner.MaxSpend);
    }

    [Fact]
    public async Task AFreshLoopBeat_WritesAllFourBoundsOntoARowThatHasNone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Under: "AER-930", MaxRuns: 3, MaxSpend: 20m, UntilAt: Now.AddHours(6), Fresh: true));

        var runner = await h.OneAsync();
        Assert.Equal("AER-930", runner.Under);
        Assert.Equal(3, runner.MaxRuns);
        Assert.Equal(20m, runner.MaxSpend);
        Assert.Equal(Now.AddHours(6), runner.UntilAt);
    }

    [Fact]
    public async Task AFreshLoopBeat_OverwritesWhatAPersonSet()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxSpend: "50"));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxSpend: 20m, Fresh: true));

        Assert.Equal(20m, (await h.OneAsync()).MaxSpend);
    }

    [Fact]
    public async Task AFreshLoopBeat_OverwritesWhatAPersonSet_TokenBounds()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500000", MaxTokensPerHour: "50000"));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", MaxTokens: 2_000_000, MaxTokensPerHour: 200_000, Fresh: true));

        var runner = await h.OneAsync();
        Assert.Equal(2_000_000, runner.MaxTokens);
        Assert.Equal(200_000, runner.MaxTokensPerHour);
    }

    [Fact]
    public async Task AFreshLoopBeat_WithNoBounds_ClearsAllFour()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", Under: "AER-930", MaxRuns: 12, MaxSpend: 40m, UntilAt: Now.AddHours(6)));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxRuns: 0, MaxSpend: -1m, Fresh: true));

        var runner = await h.OneAsync();
        Assert.Null(runner.Under);
        Assert.Null(runner.MaxRuns);
        Assert.Null(runner.MaxSpend);
        Assert.Null(runner.UntilAt);
    }

    [Fact]
    public async Task AFreshLoopBeat_WithNoBounds_ClearsTheTokenBoundsToo()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", MaxTokens: 2_000_000, MaxTokensPerHour: 200_000));

        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxTokens: 0, MaxTokensPerHour: -1, Fresh: true));

        var runner = await h.OneAsync();
        Assert.Null(runner.MaxTokens);
        Assert.Null(runner.MaxTokensPerHour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task ALoopBeat_ThatIsNotFresh_LeavesWhatAPersonSetAlone(bool? fresh)
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxRuns: 12, MaxSpend: 40m));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "3", MaxSpend: "5"));

        // A supervisor restart: the same flags again, and no word that it is new.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxRuns: 12, MaxSpend: 40m, Fresh: fresh));

        var runner = await h.OneAsync();
        Assert.Equal(3, runner.MaxRuns);
        Assert.Equal(5m, runner.MaxSpend);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task ALoopBeat_ThatIsNotFresh_LeavesWhatAPersonSetAlone_TokenBounds(bool? fresh)
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxTokens: 2_000_000, MaxTokensPerHour: 200_000));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500000", MaxTokensPerHour: "50000"));

        // A supervisor restart: the same flags again, and no word that it is new.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(
            Kind: "loop", MaxTokens: 2_000_000, MaxTokensPerHour: 200_000, Fresh: fresh));

        var runner = await h.OneAsync();
        Assert.Equal(500_000, runner.MaxTokens);
        Assert.Equal(50_000, runner.MaxTokensPerHour);
    }

    [Fact]
    public async Task AOnceBeat_EvenIfFresh_LeavesTheBoundsAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxRuns: 12, MaxSpend: 40m));

        // `hatch work` shares the loop's runner name and carries no flags.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Fresh: true));

        var runner = await h.OneAsync();
        Assert.Equal(12, runner.MaxRuns);
        Assert.Equal(40m, runner.MaxSpend);
    }

    [Fact]
    public async Task AOnceBeat_EvenIfFresh_LeavesTheTokenBoundsAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", MaxTokens: 2_000_000, MaxTokensPerHour: 200_000));

        // `hatch work` shares the loop's runner name and carries no flags.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Fresh: true));

        var runner = await h.OneAsync();
        Assert.Equal(2_000_000, runner.MaxTokens);
        Assert.Equal(200_000, runner.MaxTokensPerHour);
    }

    [Fact]
    public async Task AFreshStandbyLoopBeat_OnANewRow_WritesItPaused()
    {
        var h = await NewAsync();

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Standby: true));

        Assert.Equal("paused", instruction.State);
        Assert.Equal("paused", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task AFreshStandbyLoopBeat_PausesARunningRow_AndAnswersPaused()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal("running", (await h.OneAsync()).State);

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Standby: true));

        Assert.Equal("paused", instruction.State);
        Assert.Equal("paused", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task AFreshStandbyLoopBeat_PausesAStoppingRow()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "stopping"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Standby: true));

        Assert.Equal("paused", instruction.State);
        Assert.Equal("paused", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task AStandbyLoopBeat_ThatIsNotFresh_LeavesTheStateAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        // A supervisor restart of a loop that was started on standby long ago.
        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Standby: true));

        Assert.Equal("running", instruction.State);
        Assert.Equal("running", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task AFreshLoopBeat_WithoutStandby_LeavesAPausedRowPaused()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop", Fresh: true));

        Assert.Equal("paused", instruction.State);
        Assert.Equal("paused", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task AOnceBeat_EvenIfFreshAndStandby_LeavesTheStateAlone()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Fresh: true, Standby: true));

        Assert.Equal("running", instruction.State);
        Assert.Equal("running", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task AOnceBeat_OnANewRow_WritesItRunning_EvenIfStandby()
    {
        var h = await NewAsync();

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once", Fresh: true, Standby: true));

        Assert.Equal("running", instruction.State);
    }

    [Fact]
    public async Task AHeartbeat_NeverWritesTheState()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "stopping"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        // It reads the instruction back rather than resetting it: a runner that
        // could un-stop itself by saying hello is a runner nobody can stop.
        Assert.Equal("stopping", instruction.State);
        Assert.Equal("stopping", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task TheKind_FollowsWhicheverCommandIsRunningNow()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        // One checkout runs both commands. A row that remembered the loop would
        // offer buttons for a process that ended an hour ago.
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));

        Assert.Equal("once", (await h.OneAsync()).Kind);
    }

    // ---- What the board asks for ----

    [Fact]
    public async Task TheThreeStates_AreTheOnesTheLoopKnows()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        foreach (var state in EfHatchRunner.States)
        {
            Assert.Equal(state, (await h.PatchAsync(Runner, new RunnerPatchRequest(State: state))).State);
            Assert.Equal(state, (await h.BeatAsync(Runner, new RunnerHeartbeatRequest())).State);
        }
    }

    [Fact]
    public async Task AStateNobodyImplements_IsRefusedInWords()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(State: "halt"), default);

        Assert.Equal(
            "a runner is running, paused, stopping - not \"halt\"",
            Reason(refusal.Result));
    }

    [Fact]
    public async Task TheBounds_AreFoldedIntoWhatTheLoopReadsBack()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "aer-930", MaxRuns: "12", MaxSpend: "40.00", UntilAt: "2026-09-09T06:00:00Z"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal("AER-930", instruction.Under);
        Assert.Equal(12, instruction.MaxRuns);
        Assert.Equal(40m, instruction.MaxSpend);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.Zero), instruction.UntilAt);
    }

    [Fact]
    public async Task AnAbsentFieldLeavesOneAloneAndAnEmptyOneTakesItOff()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "AER-930", MaxRuns: "12", MaxSpend: "40", UntilAt: "2026-09-09T06:00:00Z"));

        var still = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));
        Assert.Equal("AER-930", still.Under);
        Assert.Equal(12, still.MaxRuns);

        var cleared = await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "", MaxRuns: "", MaxSpend: "", UntilAt: ""));

        // The tri-state, all the way through: a cap taken off is a loop with no
        // cap, which a nullable number could not have said.
        Assert.Null(cleared.Under);
        Assert.Null(cleared.MaxRuns);
        Assert.Null(cleared.MaxSpend);
        Assert.Null(cleared.UntilAt);
    }

    [Fact]
    public async Task TheTokenBounds_AreFoldedIntoWhatTheLoopReadsBack()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500k", MaxTokensPerHour: "2M"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        Assert.Equal(500_000, instruction.MaxTokens);
        Assert.Equal(2_000_000, instruction.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Equal(500_000, row.MaxTokens);
        Assert.Equal(2_000_000, row.MaxTokensPerHour);
        Assert.Equal("runner", row.MaxTokensSource);
        Assert.Equal("runner", row.MaxTokensPerHourSource);
    }

    // ---- The site defaults, read by every route that answers ----

    [Fact]
    public async Task ASiteDefault_ReachesARunnerWithNoCap_OnItsBeatAndItsRow()
    {
        var h = await NewAsync(defaultMaxTokens: 300_000, defaultMaxTokensPerHour: 1_000_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal(300_000, instruction.MaxTokens);
        Assert.Equal(1_000_000, instruction.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Equal(300_000, row.MaxTokens);
        Assert.Equal(1_000_000, row.MaxTokensPerHour);
        Assert.Equal("default", row.MaxTokensSource);
        Assert.Equal("default", row.MaxTokensPerHourSource);

        // Read, never written: the row still has no cap of its own.
        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task ARunnersOwnCap_BeatsTheSiteDefault()
    {
        var h = await NewAsync(defaultMaxTokens: 300_000, defaultMaxTokensPerHour: 1_000_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500k", MaxTokensPerHour: "2M"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal(500_000, instruction.MaxTokens);
        Assert.Equal(2_000_000, instruction.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Equal(500_000, row.MaxTokens);
        Assert.Equal(2_000_000, row.MaxTokensPerHour);
        Assert.Equal("runner", row.MaxTokensSource);
        Assert.Equal("runner", row.MaxTokensPerHourSource);
    }

    [Fact]
    public async Task NeitherADefaultNorACap_IsNoCap()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Null(instruction.MaxTokens);
        Assert.Null(instruction.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Null(row.MaxTokens);
        Assert.Null(row.MaxTokensPerHour);
        Assert.Equal("none", row.MaxTokensSource);
        Assert.Equal("none", row.MaxTokensPerHourSource);

        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task OneFieldOwnAndTheOtherDefaulted_AnswersMixedSources()
    {
        var h = await NewAsync(defaultMaxTokensPerHour: 1_000_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500k"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal(500_000, instruction.MaxTokens);
        Assert.Equal(1_000_000, instruction.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Equal(500_000, row.MaxTokens);
        Assert.Equal(1_000_000, row.MaxTokensPerHour);
        Assert.Equal("runner", row.MaxTokensSource);
        Assert.Equal("default", row.MaxTokensPerHourSource);

        var stored = await h.RowAsync();
        Assert.Equal(500_000, stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task OnlyAPerHourDefault_LeavesTheOtherFieldUncapped()
    {
        var h = await NewAsync(defaultMaxTokensPerHour: 1_000_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Null(instruction.MaxTokens);
        Assert.Equal(1_000_000, instruction.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Null(row.MaxTokens);
        Assert.Equal(1_000_000, row.MaxTokensPerHour);
        Assert.Equal("none", row.MaxTokensSource);
        Assert.Equal("default", row.MaxTokensPerHourSource);

        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task ClearingTheDefault_TakesTheCapOffAtTheNextBeat_WithTheRowsUnwritten()
    {
        var site = new MutableSiteSettings { MaxTokens = 300_000, MaxTokensPerHour = 1_000_000 };
        var h = await NewAsync(siteSettings: site);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var under = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal(300_000, under.MaxTokens);
        Assert.Equal(1_000_000, under.MaxTokensPerHour);

        site.MaxTokens = null;
        site.MaxTokensPerHour = null;

        var after = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Null(after.MaxTokens);
        Assert.Null(after.MaxTokensPerHour);

        var row = await h.OneAsync();
        Assert.Null(row.MaxTokens);
        Assert.Null(row.MaxTokensPerHour);
        Assert.Equal("none", row.MaxTokensSource);
        Assert.Equal("none", row.MaxTokensPerHourSource);

        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task ClearingARunnersOwnCap_HandsItBackToTheDefault_ItDoesNotUncapIt()
    {
        var h = await NewAsync(defaultMaxTokens: 300_000, defaultMaxTokensPerHour: 1_000_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500k", MaxTokensPerHour: "2M"));

        var cleared = await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "", MaxTokensPerHour: ""));

        Assert.Equal(300_000, cleared.MaxTokens);
        Assert.Equal(1_000_000, cleared.MaxTokensPerHour);
        Assert.Equal("default", cleared.MaxTokensSource);
        Assert.Equal("default", cleared.MaxTokensPerHourSource);

        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task PatchRunner_AnswersTheDefaultOnARunnerWithNoCap()
    {
        var h = await NewAsync(defaultMaxTokens: 300_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(300_000, patched.MaxTokens);
        Assert.Equal("default", patched.MaxTokensSource);
        Assert.Null(patched.MaxTokensPerHour);
        Assert.Equal("none", patched.MaxTokensPerHourSource);
    }

    [Fact]
    public async Task PostRunnerOrder_AnswersTheDefaultOnItsRow()
    {
        var h = await NewAsync(defaultMaxTokens: 300_000, defaultMaxTokensPerHour: 1_000_000);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        Assert.Equal(300_000, ordered.MaxTokens);
        Assert.Equal(1_000_000, ordered.MaxTokensPerHour);
        Assert.Equal("default", ordered.MaxTokensSource);
        Assert.Equal("default", ordered.MaxTokensPerHourSource);

        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task TheFirstEverBeat_AnswersTheDefault()
    {
        var h = await NewAsync(defaultMaxTokens: 300_000, defaultMaxTokensPerHour: 1_000_000);

        var instruction = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal(300_000, instruction.MaxTokens);
        Assert.Equal(1_000_000, instruction.MaxTokensPerHour);

        var stored = await h.RowAsync();
        Assert.Null(stored.MaxTokens);
        Assert.Null(stored.MaxTokensPerHour);
    }

    [Fact]
    public async Task AnAbsentTokenFieldLeavesOneAloneAndAnEmptyOneTakesItOff()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "500k", MaxTokensPerHour: "2M"));

        var still = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));
        Assert.Equal(500_000, still.MaxTokens);
        Assert.Equal(2_000_000, still.MaxTokensPerHour);

        var cleared = await h.PatchAsync(Runner, new RunnerPatchRequest(MaxTokens: "", MaxTokensPerHour: ""));
        Assert.Null(cleared.MaxTokens);
        Assert.Null(cleared.MaxTokensPerHour);
        Assert.Equal("none", cleared.MaxTokensSource);
        Assert.Equal("none", cleared.MaxTokensPerHourSource);
    }

    [Fact]
    public async Task ARefusedFieldWritesNothingAtAll()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(
            Runner, new RunnerPatchRequest(State: "paused", MaxSpend: "lots"), default);

        Assert.Contains("--max-spend takes an amount", Reason(refusal.Result), StringComparison.Ordinal);

        // The good half of a bad request is not half-applied.
        Assert.Equal("running", (await h.OneAsync()).State);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("lots")]
    public async Task ARefusedMaxTokens_WritesNothingAtAll(string sent)
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(
            Runner, new RunnerPatchRequest(State: "paused", MaxTokens: sent), default);

        Assert.Contains(
            $"--max-tokens takes a token count, like 500k or 2M - not \"{sent}\"",
            Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Equal("running", (await h.OneAsync()).State);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("lots")]
    public async Task ARefusedMaxTokensPerHour_WritesNothingAtAll(string sent)
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(
            Runner, new RunnerPatchRequest(State: "paused", MaxTokensPerHour: sent), default);

        Assert.Contains(
            $"--max-tokens-per-hour takes a token count, like 500k or 2M - not \"{sent}\"",
            Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Equal("running", (await h.OneAsync()).State);
    }

    [Fact]
    public async Task ABareDate_IsRefusedBecauseAStopHourIsAnInstant()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(UntilAt: "2026-09-09"), default);

        Assert.Contains("--until takes an instant", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SomethingThatIsNotAnIssueKey_IsNotAScope()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Under: "the epic"), default);

        Assert.Contains("--under names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunnerNobodyHasEverSeen_IsNotSomethingToPatch()
    {
        var h = await NewAsync();

        var refusal = await h.Runners.PatchRunner("nowhere:/tree", new RunnerPatchRequest(State: "paused"), default);

        // The table records what has spoken to this Hatch. Typing a hostname
        // into a URL does not make a runner.
        Assert.IsType<NotFoundResult>(refusal.Result);
    }

    // ---- The order ----

    [Fact]
    public async Task AnOrder_RoundTripsThroughOneLoopHeartbeatAndIsGoneByTheNext()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();

        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));
        Assert.Equal("work", ordered.Order!.Kind);
        Assert.Equal(issue.Key, ordered.Order.Key);
        Assert.Equal("Nathan", ordered.Order.By);

        var first = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal("work", first.Order!.Kind);
        Assert.Equal(issue.Key, first.Order.Key);
        Assert.Equal("Nathan", first.Order.By);

        var second = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Null(second.Order);
        Assert.Null((await h.OneAsync()).Order);
    }

    [Fact]
    public async Task AOnceBeat_LeavesAPendingOrderInPlace()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        // Touch overwrites row.Kind, and a `hatch work` beat beside a loop
        // shares this runner's name - it must not swallow the order meant
        // for the loop's own next pass.
        var once = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));
        Assert.Null(once.Order);

        Assert.Equal("pass", (await h.OneAsync()).Order!.Kind);

        var loop = await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        Assert.Equal("pass", loop.Order!.Kind);
    }

    [Fact]
    public async Task OrderingWork_WritesTheOrderedEventWithTheActorAndPayload()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();

        await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));

        var last = Assert.Single(await h.EventsAsync(issue.Key), e => e.Kind == EfHatchIssueEvent.Ordered);
        Assert.Equal("Nathan", last.Actor);
        Assert.Contains(Runner, last.Payload);
        Assert.Contains("Nathan", last.Payload);
    }

    [Fact]
    public async Task OrderingAPass_WritesNoIssueEvent_ThereIsNoIssueToWriteItOn()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        Assert.Equal("pass", ordered.Order!.Kind);
        Assert.Null(ordered.Order.Key);
    }

    [Fact]
    public async Task AKindNobodyImplements_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("sleep"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("an order is work, pass", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkWithNoKey_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("work"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("work names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkWithAKeyThatIsNotAnIssueKey_Is400WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("work", "the epic"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("work names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkNamingAnIssueThatDoesNotExist_Is404WithTheSentence()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("work", "AER-999"), default);

        Assert.Equal(404, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("AER-999", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AOnceRunner_RefusesAnOrderItWillNeverReadBack()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "once"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("single increment", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoppingRunner_RefusesAnotherOrder()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "stopping"));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("stopping", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStandingOrder_RefusesASecondUntilItIsWithdrawn()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();
        await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));

        var refusal = await h.Runners.PostRunnerOrder(Runner, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        var reason = Reason(refusal.Result);
        Assert.Contains("work", reason, StringComparison.Ordinal);
        Assert.Contains(issue.Key, reason, StringComparison.Ordinal);
        Assert.Contains("withdraw it first", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteOnAnUnorderedRow_Is204AndWritesNothing()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var seenAt = (await h.RowAsync()).LastSeenAt;

        var result = await h.WithdrawAsync(Runner);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(seenAt, (await h.RowAsync()).LastSeenAt);
    }

    [Fact]
    public async Task DeleteOnAnOrderedRow_Is204AndClearsIt()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        var result = await h.WithdrawAsync(Runner);

        Assert.IsType<NoContentResult>(result);
        Assert.Null((await h.OneAsync()).Order);
        Assert.Null((await h.RowAsync()).OrderKind);
    }

    [Fact]
    public async Task GetRunnersAndPatchRunner_AgreeWithTheRunnersOwnOrder()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var issue = await h.FileAsync();
        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("work", issue.Key));

        var listed = await h.OneAsync();
        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal(ordered.Order, listed.Order);
        Assert.Equal(ordered.Order, patched.Order);
    }

    // ---- Who may press ----

    [Fact]
    public async Task AnOwnersRunner_IsControllableByItsOwner()
    {
        var h = await NewAsync(wall: true);
        await h.LoopBeatAsync(Runner, new Actor(ActorKind.Person, h.PersonId, "Nathan"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task ADifferentUsersRunner_Is403WithTheSentence()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(State: "paused"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("Alice", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Contains(Runner, Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdmin_ControlsAnyonesRunner()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task ARunnerThatBelongsToNobody_IsOpenToAnyUser()
    {
        var h = await NewAsync(wall: true);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task WithTheWallOff_AMismatchedOwner_StillControlsIt()
    {
        var h = await NewAsync();
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Equal("paused", patched.State);
    }

    [Fact]
    public async Task GetRunners_AnswersForAndCanControl()
    {
        var h = await NewAsync(wall: true);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);
        await h.LoopBeatAsync("here:/checkouts/two", null);

        var rows = await h.ListAsync();

        var owned = Assert.Single(rows, r => r.Name == Runner);
        Assert.Equal("Alice", owned.For);
        Assert.False(owned.CanControl);

        var open = Assert.Single(rows, r => r.Name == "here:/checkouts/two");
        Assert.Null(open.For);
        Assert.True(open.CanControl);
    }

    [Fact]
    public async Task GetRunners_AnswersYours()
    {
        var h = await NewAsync(wall: true);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, new Actor(ActorKind.Person, h.PersonId, "Nathan"));
        await h.LoopBeatAsync("here:/checkouts/alice", alice);
        await h.LoopBeatAsync("here:/checkouts/nobody", null);

        var rows = await h.ListAsync();

        Assert.True(Assert.Single(rows, r => r.Name == Runner).Yours);
        Assert.False(Assert.Single(rows, r => r.Name == "here:/checkouts/alice").Yours);
        Assert.False(Assert.Single(rows, r => r.Name == "here:/checkouts/nobody").Yours);
    }

    [Fact]
    public async Task GetRunners_WithTheWallOff_EveryRowIsYours()
    {
        var h = await NewAsync();
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);
        await h.LoopBeatAsync("here:/checkouts/nobody", null);

        var rows = await h.ListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Yours));
    }

    [Fact]
    public async Task GetRunners_AnAdminOverSomeoneElsesRow_CanControlButIsNotYours()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var alice = h.Actors.AddPerson("Alice");
        await h.LoopBeatAsync(Runner, alice);

        var row = Assert.Single(await h.ListAsync());

        Assert.True(row.CanControl);
        Assert.False(row.Yours);
    }

    [Fact]
    public async Task GetRunners_AKeyWithTheWallOn_OwnsNothing()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");

        // Seeded directly rather than beaten: a key-bearing beat now lands as
        // a session of the caller's own key (HA-2166), not a LegacyRunners row
        // under either of these names.
        h.Db.LegacyRunners.Add(Renamed(Runner, alice.Id, where: Runner));
        h.Db.LegacyRunners.Add(Renamed("here:/checkouts/nobody", null, where: "here:/checkouts/nobody"));
        await h.Db.SaveChangesAsync();

        var rows = await h.ListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.False(r.Yours));
    }

    [Fact]
    public async Task PatchRunner_AnswersYours()
    {
        var h = await NewAsync(wall: true);
        await h.LoopBeatAsync(Runner, new Actor(ActorKind.Person, h.PersonId, "Nathan"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.True(patched.Yours);
    }

    [Fact]
    public async Task PostRunnerOrder_AnswersYours()
    {
        var h = await NewAsync(wall: true);
        await h.LoopBeatAsync(Runner, new Actor(ActorKind.Person, h.PersonId, "Nathan"));

        var ordered = await h.OrderAsync(Runner, new RunnerOrderRequest("pass"));

        Assert.True(ordered.Yours);
    }

    // ---- Who sees which runner ----

    /// <summary>
    /// Three rows: Alice's, Bob's, and nobody's, seeded directly rather than
    /// beaten - a key-bearing beat now lands as a session rather than a
    /// LegacyRunners row under the name it was given (HA-2166), so a caller
    /// that is a key can no longer produce three differently-named rows this
    /// way. <c>LastSeenAt</c> still steps 10 seconds apart, the order
    /// <see cref="WithTheWallOff_EveryRunnerIsVisible"/> and its siblings read.
    /// </summary>
    private static async Task SeedThreeAsync(Harness h)
    {
        var alice = h.Actors.AddPerson("Alice");
        var bob = h.Actors.AddPerson("Bob");

        var aliceRow = Renamed("here:/checkouts/alice", alice.Id, where: "here:/checkouts/alice");
        aliceRow.LastSeenAt = Now;
        var bobRow = Renamed("here:/checkouts/bob", bob.Id, where: "here:/checkouts/bob");
        bobRow.LastSeenAt = Now.AddSeconds(10);
        var nobodyRow = Renamed("here:/checkouts/nobody", null, where: "here:/checkouts/nobody");
        nobodyRow.LastSeenAt = Now.AddSeconds(20);

        h.Db.LegacyRunners.AddRange(aliceRow, bobRow, nobodyRow);
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task WithTheWallOff_EveryRunnerIsVisible()
    {
        var h = await NewAsync(program: true, keyOwner: PersonRole.User);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody", "here:/checkouts/bob", "here:/checkouts/alice"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task APersonCalling_SeesEveryRunner()
    {
        var h = await NewAsync(wall: true);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody", "here:/checkouts/bob", "here:/checkouts/alice"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task AnAdminsKey_SeesEveryRunner()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.Admin);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody", "here:/checkouts/bob", "here:/checkouts/alice"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task AUsersKey_SeesOnlyItsOwnersRunner()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.User);
        await SeedThreeAsync(h);

        h.Actors.AddPerson("Owner", h.PersonId);
        h.Db.LegacyRunners.Add(Renamed("here:/checkouts/owner", h.PersonId, where: "here:/checkouts/owner"));
        await h.Db.SaveChangesAsync();
        h.Actors.Principal = new Actor(ActorKind.Person, h.PersonId, "Owner");

        var rows = await h.ListAsync();

        var owned = Assert.Single(rows);
        Assert.Equal("here:/checkouts/owner", owned.Name);
        Assert.Equal("Owner", owned.For);
    }

    [Fact]
    public async Task AUsersKey_WithNoPrincipal_SeesOnlyTheOwnerlessRunner()
    {
        var h = await NewAsync(wall: true, program: true);
        await SeedThreeAsync(h);
        h.Actors.Principal = null;

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody"], rows.Select(r => r.Name));
    }

    [Fact]
    public async Task NoIdentityAtAll_SeesOnlyTheOwnerlessRunner()
    {
        var h = await NewAsync(wall: true, nobody: true);
        await SeedThreeAsync(h);

        var rows = await h.ListAsync();

        Assert.Equal(["here:/checkouts/nobody"], rows.Select(r => r.Name));
    }

    // ---- The edge that is cut ----

    [Fact]
    public async Task SettingWhatARunnerMaySpend_IsClosedToAKey()
    {
        var h = await NewAsync(program: true);

        // Seeded directly rather than beaten: a key-bearing beat now lands as
        // a session, not a LegacyRunners row under this name (HA-2166) - the
        // refusal under test is NotAPerson's, read before any row lookup, so
        // the row just needs to exist for OneAsync to find.
        h.Db.LegacyRunners.Add(Renamed(Runner, null));
        await h.Db.SaveChangesAsync();

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxSpend: "1000"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Null((await h.OneAsync()).MaxSpend);
    }

    [Fact]
    public async Task SettingWhatARunnerMayTokenSpend_IsClosedToAKey()
    {
        var h = await NewAsync(program: true);
        h.Db.LegacyRunners.Add(Renamed(Runner, null));
        await h.Db.SaveChangesAsync();

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxTokens: "2M"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Null((await h.OneAsync()).MaxTokens);
    }

    [Fact]
    public async Task SettingARunnersTags_IsClosedToAKey()
    {
        var h = await NewAsync(program: true);
        h.Db.LegacyRunners.Add(Renamed(Runner, null));
        await h.Db.SaveChangesAsync();

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(Tags: ["gpu"]), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Null((await h.RowAsync()).Tags);
    }

    [Fact]
    public void TheThreeRoutes_AcceptKeysTheWayTheClaimAndThePlaybookDo()
    {
        // The heartbeat and the read are a dispatcher's - a runner that could
        // not say it was alive would leave a page that could only be empty. The
        // write is a person's, because it is the budget.
        Assert.True(Guard(nameof(RunnersController.Heartbeat))!.AcceptKey);
        Assert.True(Guard(nameof(RunnersController.GetRunners))!.AcceptKey);
        Assert.True(Guard(nameof(RunnersController.GetRunner))!.AcceptKey);

        var patch = Guard(nameof(RunnersController.PatchRunner));
        Assert.NotNull(patch);
        Assert.False(patch.AcceptKey);

        // And no class-level attribute for the PATCH to inherit a key acceptance from.
        Assert.Empty(typeof(RunnersController).GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    private static RequireRoleAttribute? Guard(string method) =>
        typeof(RunnersController).GetMethod(method)!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

    // ---- The key is the runner ----

    [Fact]
    public async Task AKeyWithNoBeat_IsListed_AsThoughItHadJustStarted_ButNotInLive()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("here:/checkouts/fresh");

        var row = Assert.Single(await h.ListAllAsync());
        Assert.Equal("here:/checkouts/fresh", row.Name);
        Assert.Equal(RunnerKinds.Loop, row.Kind);
        Assert.Equal(RunnerStates.Running, row.State);
        Assert.Equal(key.Id, row.ApiKeyId);
        Assert.Equal(key.Prefix, row.Prefix);
        Assert.Equal(key.CreatedAt, row.CreatedAt);
        Assert.Null(row.RevokedAt);

        Assert.Empty(await h.ListAsync());
    }

    [Fact]
    public async Task ARevokedKey_IsStillListed_WithRevokedAt()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("here:/checkouts/gone");
        await h.RevokeKeyAsync(key.Id);

        var row = Assert.Single(await h.ListAllAsync());
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public async Task ABeatWithNoKey_IsListed_WithEveryKeyFieldNull()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var row = await h.OneAsync();
        Assert.Null(row.ApiKeyId);
        Assert.Null(row.Prefix);
        Assert.Null(row.CreatedAt);
        Assert.Null(row.LastUsedAt);
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task Live_DropsAKeyOnlyRunnerAndAnAgedBeat()
    {
        var h = await NewAsync();
        await h.AddKeyAsync("here:/checkouts/keyonly");
        await h.BeatAsync("here:/checkouts/aged", new RunnerHeartbeatRequest());

        h.Time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
        await h.BeatAsync("here:/checkouts/fresh", new RunnerHeartbeatRequest());

        Assert.Equal(["here:/checkouts/fresh"], (await h.ListAsync()).Select(r => r.Name));
        Assert.Equal(3, (await h.ListAllAsync()).Count);
    }

    [Fact]
    public async Task AConfigRow_OverridesTheBeatsOwnBoundsAndTags()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Under: "AER-1", MaxRuns: 5, Tags: ["from-beat"]));
        var key = await h.AddKeyAsync(Runner);

        h.Db.RunnerConfigs.Add(new EfHatchRunnerConfig
        {
            ApiKeyId = key.Id,
            Tags = RunnerTagColumn.Write(["from-config"]),
            Under = "AER-2",
            MaxRuns = 40,
            MaxSpend = 12.5m,
            MaxTokens = 1000,
            MaxTokensPerHour = 100,
            UntilAt = Now.AddDays(1),
            UpdatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var row = await h.OneAsync();
        Assert.Equal(["from-config"], row.Tags);
        Assert.Equal("AER-2", row.Under);
        Assert.Equal(40, row.MaxRuns);
        Assert.Equal(12.5m, row.MaxSpend);
        Assert.Equal(1000, row.MaxTokens);
        Assert.Equal(100, row.MaxTokensPerHour);
        Assert.Equal(Now.AddDays(1), row.UntilAt);
    }

    [Fact]
    public async Task APersonCalling_SeesEveryKeyRunnerToo()
    {
        var h = await NewAsync(wall: true);
        await h.AddKeyAsync("here:/checkouts/owned-key", h.PersonId);
        await h.AddKeyAsync("here:/checkouts/ownerless-key");

        var rows = await h.ListAllAsync();
        Assert.Equal(["here:/checkouts/owned-key", "here:/checkouts/ownerless-key"], rows.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnAdminsKey_SeesEveryKeyRunnerToo()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.Admin);
        await h.AddKeyAsync("here:/checkouts/owned-key", Guid.NewGuid());
        await h.AddKeyAsync("here:/checkouts/ownerless-key");

        Assert.Equal(2, (await h.ListAllAsync()).Count);
    }

    [Fact]
    public async Task AUsersKey_SeesOnlyItsOwnersKeyRunner()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.User);
        h.Actors.Principal = new Actor(ActorKind.Person, h.PersonId, "Owner");
        await h.AddKeyAsync("here:/checkouts/owned-key", h.PersonId);
        await h.AddKeyAsync("here:/checkouts/someone-elses-key", Guid.NewGuid());

        var owned = Assert.Single(await h.ListAllAsync());
        Assert.Equal("here:/checkouts/owned-key", owned.Name);
    }

    [Fact]
    public async Task AUsersKey_WithNoPrincipal_SeesOnlyTheOwnerlessKeyRunner()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.User);
        h.Actors.Principal = null;
        await h.AddKeyAsync("here:/checkouts/owned-key", Guid.NewGuid());
        await h.AddKeyAsync("here:/checkouts/ownerless-key");

        var visible = Assert.Single(await h.ListAllAsync());
        Assert.Equal("here:/checkouts/ownerless-key", visible.Name);
    }

    // ---- Reading one ----

    [Fact]
    public async Task GetRunner_AnswersTheVisibleRowByName()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var row = await h.GetAsync(Runner);
        Assert.Equal(Runner, row?.Name);
    }

    [Fact]
    public async Task GetRunner_404sForAnUnknownName()
    {
        var h = await NewAsync();

        Assert.Null(await h.GetAsync("nobody:/here"));
    }

    [Fact]
    public async Task GetRunner_404sForAnInvisibleName()
    {
        var h = await NewAsync(wall: true, program: true, keyOwner: PersonRole.User);
        h.Actors.Principal = null;
        await h.AddKeyAsync("here:/checkouts/owned-key", Guid.NewGuid());

        Assert.Null(await h.GetAsync("here:/checkouts/owned-key"));
    }

    [Fact]
    public async Task GetRunner_ResolvesAnEscapedName()
    {
        var h = await NewAsync();
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest());

        var row = await h.GetAsync("here:%2Fcheckouts%2Fone");
        Assert.Equal(Runner, row?.Name);
    }

    // ---- Minting one ----

    [Fact]
    public async Task Create_MintsAKeyAndItsConfigAndAnswersTheSecretOnceUncached()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        var minted = Value(await h.CreateAsync(new CreateRunnerRequest("Abed Nadir", Tags: [" Docker ", "docker", "GPU"])));

        Assert.StartsWith("hatch_ak_", minted.Secret, StringComparison.Ordinal);
        Assert.Equal("Abed Nadir", minted.Runner.Name);
        Assert.NotNull(minted.Runner.ApiKeyId);
        Assert.Equal(["docker", "gpu"], minted.Runner.Tags);

        var key = Assert.Single(await h.Keys.ListApiKeysAsync(default));
        Assert.Equal(minted.Runner.ApiKeyId, key.Id);

        var config = Assert.Single(await h.Db.RunnerConfigs.ToListAsync());
        Assert.Equal(key.Id, config.ApiKeyId);
        Assert.Equal(["docker", "gpu"], RunnerTagColumn.Read(config.Tags).Order(StringComparer.Ordinal));

        Assert.Equal("no-store", h.Runners.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Create_WritesAConfigRowEvenWithNoTags()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        Value(await h.CreateAsync(new CreateRunnerRequest("Abed Nadir")));

        var config = Assert.Single(await h.Db.RunnerConfigs.ToListAsync());
        Assert.Empty(RunnerTagColumn.Read(config.Tags));
    }

    [Fact]
    public async Task Create_ARunnerAlreadyCalledThat_IsAConflictAndStaysOneKey()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        Value(await h.CreateAsync(new CreateRunnerRequest("Abed Nadir")));

        var again = await h.CreateAsync(new CreateRunnerRequest("Abed Nadir"));

        Assert.IsType<ConflictObjectResult>(again.Result);
        Assert.Single(await h.Keys.ListApiKeysAsync(default));
        Assert.Single(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Create_AKeyCaller_IsRefusedAndNothingIsWritten()
    {
        var h = await NewAsync(program: true);

        var refused = await h.CreateAsync(new CreateRunnerRequest("Abed Nadir"));

        var result = Assert.IsAssignableFrom<ObjectResult>(refused.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Empty(await h.Keys.ListApiKeysAsync(default));
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Create_AnOwnerWhoDoesNotExist_IsRefusedAndNothingIsWritten()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        var refused = await h.CreateAsync(new CreateRunnerRequest("Abed Nadir", OwnerPersonId: Guid.NewGuid()));

        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Equal("that person does not exist", Reason(refused.Result));
        Assert.Empty(await h.Keys.ListApiKeysAsync(default));
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Create_ANamedOwner_OwnsTheRunner()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var owner = h.Actors.AddPerson("Annie");

        var minted = Value(await h.CreateAsync(new CreateRunnerRequest("Abed Nadir", OwnerPersonId: owner.Id)));

        Assert.Equal(owner.Id, minted.Runner.OwnerPersonId);
        Assert.Equal(owner.Id, Assert.Single(await h.Keys.ListApiKeysAsync(default)).OwnerPersonId);
    }

    [Fact]
    public async Task Create_NoOwnerNamed_DefaultsToTheCallingPerson()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        var minted = Value(await h.CreateAsync(new CreateRunnerRequest("Abed Nadir")));

        Assert.Equal(h.PersonId, minted.Runner.OwnerPersonId);
    }

    [Fact]
    public async Task Create_NoOwnerNamed_AndNobodyIsCalling_BelongsToNobody()
    {
        var h = await NewAsync(nobody: true);

        var minted = Value(await h.CreateAsync(new CreateRunnerRequest("Abed Nadir")));

        Assert.Null(minted.Runner.OwnerPersonId);
        Assert.Null(Assert.Single(await h.Keys.ListApiKeysAsync(default)).OwnerPersonId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SUGGEST-NAME")]
    [InlineData("TOO-LONG")]
    public async Task Create_ANameThatCannotBeUsed_IsRefusedAndNothingIsWritten(string? name)
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        if (name == "TOO-LONG") name = new string('x', EfApiKey.MaxNameLength + 1);

        var refused = await h.CreateAsync(new CreateRunnerRequest(name));

        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Empty(await h.Keys.ListApiKeysAsync(default));
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Create_ANameOfNames_IsAcceptedAndReachableByItsOwnUrl()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        // What GET /runners/names reaches now is GetRunner: no such runner.
        Assert.Null(await h.GetAsync("names"));

        var minted = Value(await h.CreateAsync(new CreateRunnerRequest("names")));

        Assert.Equal("names", minted.Runner.Name);
        Assert.NotNull(await h.GetAsync("names"));
    }

    [Fact]
    public async Task Create_ABadTag_IsRefusedAndNothingIsWritten()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        var refused = await h.CreateAsync(new CreateRunnerRequest("Abed Nadir", Tags: ["a b"]));

        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Empty(await h.Keys.ListApiKeysAsync(default));
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    // ---- Suggesting a name ----

    [Fact]
    public async Task Suggest_OnAnEmptyBoard_IsTheFirstNameOnTheCastList()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        Assert.Equal(RunnerCast.All[0], await h.SuggestAsync());
    }

    [Fact]
    public async Task Suggest_AfterMintingThatName_IsTheNext()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        Value(await h.CreateAsync(new CreateRunnerRequest(await h.SuggestAsync())));

        Assert.Equal(RunnerCast.All[1], await h.SuggestAsync());
    }

    [Fact]
    public async Task Suggest_SkipsANameOnlyAHeartbeatHolds()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.BeatAsync(RunnerCast.All[0], new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.Equal(RunnerCast.All[1], await h.SuggestAsync());
    }

    [Fact]
    public async Task Suggest_SkipsAKeyNameWhateverItsCase()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var lowered = RunnerCast.All[0].ToLowerInvariant();
        Assert.NotEqual(RunnerCast.All[0], lowered);
        await h.AddKeyAsync(lowered);

        Assert.Equal(RunnerCast.All[1], await h.SuggestAsync());
    }

    [Fact]
    public async Task Suggest_SkipsARevokedKeysName()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(RunnerCast.All[0]);
        await h.RevokeKeyAsync(key.Id);

        Assert.Equal(RunnerCast.All[1], await h.SuggestAsync());
    }

    [Fact]
    public async Task Suggest_WithEveryCastNameTaken_NumbersTheFirst()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        foreach (var name in RunnerCast.All) await h.AddKeyAsync(name);

        Assert.Equal($"{RunnerCast.All[0]} 2", await h.SuggestAsync());
    }

    // ---- Editing a runner that has a key ----

    [Fact]
    public async Task Patch_WithAKey_WritesTheConfigRowAndMirrorsItOntoTheBeat()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(
            Under: "aer-1", MaxRuns: "3", MaxSpend: "4.5", MaxTokens: "2m", MaxTokensPerHour: "500k",
            UntilAt: "2026-09-12T17:00:00Z", Tags: ["GPU ", " linux"], State: "paused"));

        var config = await h.Db.RunnerConfigs.AsNoTracking().SingleAsync();
        Assert.Equal(key.Id, config.ApiKeyId);
        Assert.Equal("AER-1", config.Under);
        Assert.Equal(3, config.MaxRuns);
        Assert.Equal(4.5m, config.MaxSpend);
        Assert.Equal(2_000_000, config.MaxTokens);
        Assert.Equal(500_000, config.MaxTokensPerHour);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 17, 0, 0, TimeSpan.Zero), config.UntilAt);
        Assert.Equal(["gpu", "linux"], RunnerTagColumn.Read(config.Tags));

        var row = await h.RowAsync();
        Assert.Equal("AER-1", row.Under);
        Assert.Equal(3, row.MaxRuns);
        Assert.Equal(4.5m, row.MaxSpend);
        Assert.Equal(2_000_000, row.MaxTokens);
        Assert.Equal(500_000, row.MaxTokensPerHour);
        Assert.Equal(config.UntilAt, row.UntilAt);
        Assert.Equal("gpu\nlinux", row.Tags);
        Assert.Equal("paused", row.State);

        Assert.Equal("paused", patched.State);
        Assert.Equal("AER-1", patched.Under);
        Assert.Equivalent(await h.GetAsync(Runner), patched);
    }

    [Fact]
    public async Task Patch_WithAKeyAndNoBeat_WritesOnlyTheConfig()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(
            MaxRuns: "7", Tags: ["gpu"], State: "paused"));

        var config = await h.Db.RunnerConfigs.AsNoTracking().SingleAsync();
        Assert.Equal(key.Id, config.ApiKeyId);
        Assert.Equal(7, config.MaxRuns);
        Assert.Empty(await h.Db.LegacyRunners.ToListAsync());
        Assert.Equal(7, patched.MaxRuns);
        Assert.Equal(["gpu"], patched.Tags);
        Assert.Equivalent(await h.GetAsync(Runner), patched);
    }

    [Fact]
    public async Task Patch_WithAKey_ButOnlyBeatFields_LeavesTheConfigAlone()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.AddKeyAsync(Runner);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        await h.PatchAsync(Runner, new RunnerPatchRequest(State: "paused"));

        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
        Assert.Equal("paused", (await h.RowAsync()).State);
    }

    [Fact]
    public async Task Patch_WithAKey_UpdatesAnExistingConfigRow()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.AddKeyAsync(Runner);
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "3", Tags: ["a"]));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: ""));

        var config = await h.Db.RunnerConfigs.AsNoTracking().SingleAsync();
        Assert.Null(config.MaxRuns);
        Assert.Equal(["a"], RunnerTagColumn.Read(config.Tags));
        Assert.Equal(h.Time.GetUtcNow(), config.UpdatedAt);
    }

    [Fact]
    public async Task Patch_WithNoKey_WritesTheBeatAndNeverMakesAConfigRow()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "2", Tags: ["gpu"], State: "paused"));

        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
        var row = await h.RowAsync();
        Assert.Equal(2, row.MaxRuns);
        Assert.Equal("gpu", row.Tags);
        Assert.Equal("paused", row.State);
        Assert.Equal(2, patched.MaxRuns);
    }

    [Fact]
    public async Task Patch_ByTheOwnerOfAKey_IsAllowed()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        await h.AddKeyAsync(Runner, h.PersonId);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "1"));

        Assert.Equal(1, patched.MaxRuns);
    }

    [Fact]
    public async Task Patch_ByAnotherPerson_OfAKeysRunner_Is403AndWritesNothing()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");
        await h.AddKeyAsync(Runner, alice.Id);

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxRuns: "1"), default);

        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("Alice", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Patch_ByAnAdmin_OfAnotherPersonsKey_IsAllowed()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var alice = h.Actors.AddPerson("Alice");
        await h.AddKeyAsync(Runner, alice.Id);

        var patched = await h.PatchAsync(Runner, new RunnerPatchRequest(MaxRuns: "1"));

        Assert.Equal(1, patched.MaxRuns);
    }

    [Fact]
    public async Task Patch_ByAKey_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync(program: true, wall: true);
        await h.AddKeyAsync(Runner);

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxRuns: "1"), default);

        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Patch_OfARevokedKeysRunner_IsAConflict_EvenForANonOwner()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");
        var key = await h.AddKeyAsync(Runner, alice.Id);
        await h.RevokeKeyAsync(key.Id);

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxRuns: "1"), default);

        Assert.IsType<ConflictObjectResult>(refusal.Result);
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    [Fact]
    public async Task Patch_OfANameNeitherAKeyNorABeatHolds_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        var refusal = await h.Runners.PatchRunner(Runner, new RunnerPatchRequest(MaxRuns: "1"), default);

        Assert.IsType<NotFoundResult>(refusal.Result);
        Assert.Empty(await h.Db.RunnerConfigs.ToListAsync());
    }

    // ---- Revoking and handing over a runner ----

    [Fact]
    public async Task Revoke_StopsTheKey_AndTheRunnerStaysListed()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.AddKeyAsync(Runner);

        Assert.IsType<NoContentResult>(await h.RevokeAsync(Runner));

        var key = Assert.Single(await h.Keys.ListApiKeysAsync(default));
        Assert.NotNull(key.RevokedAt);
        Assert.Equal(Runner, Assert.Single(await h.ListAllAsync()).Name);
    }

    [Fact]
    public async Task Revoke_OfANameNoKeyAnswersTo_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.IsType<NotFoundResult>(await h.RevokeAsync(Runner));
        Assert.IsType<NotFoundResult>(await h.RevokeAsync("nobody:/at/all"));
    }

    // ---- Rotating a runner's secret ----

    [Fact]
    public async Task Rotate_ANewSecret_TheOldOneStopsVerifying_AndTheNewOneIsTheSameKey()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var minted = Value(await h.CreateAsync(new CreateRunnerRequest(Runner)));

        var rotated = Value(await h.RotateAsync(Runner));

        Assert.Null(await h.Keys.VerifyApiKeyAsync(minted.Secret, default));
        var verified = await h.Keys.VerifyApiKeyAsync(rotated.Secret, default);
        Assert.NotNull(verified);
        Assert.Equal(minted.Runner.ApiKeyId, verified!.Id);
        Assert.Equal(Runner, verified.Name);
    }

    [Fact]
    public async Task Rotate_UpdatesPrefixAndRotatedAt_OnTheAnswerAndOnGet()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var minted = Value(await h.CreateAsync(new CreateRunnerRequest(Runner)));
        Assert.Null(minted.Runner.RotatedAt);

        var rotated = Value(await h.RotateAsync(Runner));

        Assert.NotNull(rotated.Runner.RotatedAt);
        Assert.NotEqual(minted.Runner.Prefix, rotated.Runner.Prefix);

        var fetched = await h.GetAsync(Runner);
        Assert.Equal(rotated.Runner.Prefix, fetched?.Prefix);
        Assert.Equal(rotated.Runner.RotatedAt, fetched?.RotatedAt);
    }

    [Fact]
    public async Task Rotate_KeepsOwnerTagsAndSessions()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var owner = h.Actors.AddPerson("Ada");
        Value(await h.CreateAsync(new CreateRunnerRequest(Runner, OwnerPersonId: owner.Id, Tags: ["gpu"])));
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        var before = await h.GetAsync(Runner);

        Value(await h.RotateAsync(Runner));

        var after = await h.GetAsync(Runner);
        Assert.Equal(before?.OwnerPersonId, after?.OwnerPersonId);
        Assert.Equal(before?.Tags, after?.Tags);
        Assert.Equal(before?.Sessions, after?.Sessions);
        Assert.Equal(Runner, Assert.Single(await h.ListAllAsync()).Name);
    }

    [Fact]
    public async Task Rotate_IsSentNoStore()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        Value(await h.CreateAsync(new CreateRunnerRequest(Runner)));

        Value(await h.RotateAsync(Runner));

        Assert.Equal("no-store", h.Runners.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Rotate_OfANameNoKeyAnswersTo_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.IsType<NotFoundResult>((await h.RotateAsync(Runner)).Result);
        Assert.IsType<NotFoundResult>((await h.RotateAsync("nobody:/at/all")).Result);
    }

    [Fact]
    public async Task Rotate_OfARevokedKey_Is409_AndWritesNothing()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        await h.RevokeKeyAsync(key.Id);
        var hashBefore = (await h.Keys.ListApiKeysAsync(default)).Single().Hash;

        var refused = await h.RotateAsync(Runner);

        Assert.IsType<ConflictObjectResult>(refused.Result);
        var after = Assert.Single(await h.Keys.ListApiKeysAsync(default));
        Assert.Equal(hashBefore, after.Hash);
        Assert.Null(after.RotatedAt);
    }

    [Fact]
    public async Task Rotate_AKeyCaller_Is403()
    {
        var h = await NewAsync(program: true);
        var key = await h.AddKeyAsync(Runner);
        var hashBefore = key.Hash.ToArray();

        var refused = await h.RotateAsync(Runner);

        var result = Assert.IsAssignableFrom<ObjectResult>(refused.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(hashBefore, Assert.Single(await h.Keys.ListApiKeysAsync(default)).Hash);
    }

    [Fact]
    public async Task Rotate_ANameWithSlashes_ReachedByItsEscapedSegment()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        Value(await h.CreateAsync(new CreateRunnerRequest(Runner)));

        var rotated = Value(await h.RotateAsync(Uri.EscapeDataString(Runner)));

        Assert.Equal(Runner, rotated.Runner.Name);
    }

    [Fact]
    public async Task SetOwner_SetsAndClearsTheKeysOwner()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var ada = h.Actors.AddPerson("Ada");
        await h.AddKeyAsync(Runner);

        Assert.IsType<NoContentResult>(await h.SetOwnerAsync(Runner, ada.Id));
        Assert.Equal(ada.Id, Assert.Single(await h.Keys.ListApiKeysAsync(default)).OwnerPersonId);
        Assert.Equal(ada.Id, (await h.GetAsync(Runner))!.OwnerPersonId);

        Assert.IsType<NoContentResult>(await h.SetOwnerAsync(Runner, null));
        Assert.Null(Assert.Single(await h.Keys.ListApiKeysAsync(default)).OwnerPersonId);
    }

    [Fact]
    public async Task SetOwner_ToAPersonWhoDoesNotExist_Is400AndChangesNothing()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.AddKeyAsync(Runner, h.PersonId);

        Assert.IsType<BadRequestObjectResult>(await h.SetOwnerAsync(Runner, Guid.NewGuid()));

        Assert.Equal(h.PersonId, Assert.Single(await h.Keys.ListApiKeysAsync(default)).OwnerPersonId);
    }

    [Fact]
    public async Task SetOwner_OfANameNoKeyAnswersTo_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var ada = h.Actors.AddPerson("Ada");
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));

        Assert.IsType<NotFoundResult>(await h.SetOwnerAsync(Runner, ada.Id));
    }

    // ---- The session heartbeat ----

    [Fact]
    public async Task SessionHeartbeat_WithNoKey_Is403BeforeAnyDatabaseCall()
    {
        var h = await NewAsync(nobody: true);

        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop"));

        var refusal = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, refusal.StatusCode);
    }

    // ---- The election, answered on the beat itself (HA-2129) ----

    [SkippableFact]
    public async Task ASessionBeat_FromTheElectedKey_AnswersDrivesOnItsOwnResponse()
    {
        var h = await NewAsync(connectionString: await ConnectAsync());
        await h.BindRepositoryAsync("https://example.com/o/r");
        var one = await h.AddKeyAsync("here:/one-session");
        h.Caller.Key = one;

        var elected = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Equal(["AER"], elected.Value!.Drives);
    }

    [SkippableFact]
    public async Task ASessionBeat_FromAKeyTheElectionPassesOver_AnswersNoDrives()
    {
        var h = await NewAsync(connectionString: await ConnectAsync());
        await h.BindRepositoryAsync("https://example.com/o/r");
        var one = await h.AddKeyAsync("here:/one-session");
        h.Caller.Key = one;
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        var two = await h.AddKeyAsync("here:/two-session");
        h.Caller.Key = two;
        var notElected = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Empty(notElected.Value!.Drives!);
    }

    // ---- What the loop was told, kept beside the election (AR-80) ----

    [SkippableFact]
    public async Task ASessionBeat_StoresWhatItWasTold_AndEmptyWhenItWasToldNothing()
    {
        var h = await NewAsync(connectionString: await ConnectAsync());
        await h.BindRepositoryAsync("https://example.com/o/r");
        var one = await h.AddKeyAsync("here:/one-session");
        h.Caller.Key = one;
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        var two = await h.AddKeyAsync("here:/two-session");
        h.Caller.Key = two;
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        var stored = await h.Db.RunnerSessions.AsNoTracking().ToListAsync();
        Assert.Equal("AER", stored.Single(s => s.ApiKeyId == one.Id).DrivesTold);
        Assert.Equal("", stored.Single(s => s.ApiKeyId == two.Id).DrivesTold);
    }

    [SkippableFact]
    public async Task TheRunnersList_CarriesWhatTheLiveSessionWasTold_NotWhatTheElectionSaysNow()
    {
        var h = await NewAsync(connectionString: await ConnectAsync());
        await h.BindRepositoryAsync("https://example.com/o/r");
        var one = await h.AddKeyAsync("here:/one-session");
        h.Caller.Key = one;
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        // The loop was told it drives the project; blank what it was told, as a
        // server answering every beat with nothing would have, and the election
        // run again on the read still elects it.
        var session = await h.Db.RunnerSessions.SingleAsync();
        session.DrivesTold = "";
        await h.Db.SaveChangesAsync();

        var row = await h.OneAsync();
        Assert.Equal(["AER"], row.Drives);
        Assert.NotNull(row.DrivesTold);
        Assert.Empty(row.DrivesTold!);
    }

    [Fact]
    public async Task TheRunnersList_HasNoDrivesTold_ForASessionNeverAnswered()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("session-only");
        h.Db.RunnerSessions.Add(NewSession(key.Id, Now, Runner));
        await h.Db.SaveChangesAsync();

        Assert.Null((await h.OneAsync()).DrivesTold);
    }

    // ---- The stall watch, run off a session beat (HA-2129) ----

    [SkippableFact]
    public async Task ASessionBeat_PastTheStallWindow_PassesTheDriverOverAndWritesOneDriverStalledEvent()
    {
        var h = await NewAsync(runnerGoneAfterSeconds: 3600, connectionString: await ConnectAsync());
        await h.BindRepositoryAsync("https://example.com/o/r");
        var head = await h.SeedMergingHeadAsync();

        var one = await h.AddKeyAsync("here:/one-session");
        h.Caller.Key = one;
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        var two = await h.AddKeyAsync("here:/two-session");
        h.Caller.Key = two;
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        h.Time.Advance(TimeSpan.FromMinutes(10));
        var passedOver = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));

        Assert.Equal(["AER"], passedOver.Value!.Drives);
        Assert.Equal("here:/one-session", (await h.Db.Projects.AsNoTracking().SingleAsync()).TrainStalledDriver);

        var key = IssueKey.Format("AER", head.Number);
        var @event = Assert.Single(await h.EventsAsync(key), e => e.Kind == EfHatchIssueEvent.DriverStalled);
        Assert.Equal("here:/one-session", JsonDocument.Parse(@event.Payload!).RootElement.GetProperty("passedOver").GetString());
        Assert.Equal("here:/two-session", JsonDocument.Parse(@event.Payload!).RootElement.GetProperty("elected").GetString());
    }

    // ---- A session beat retires the rows its checkout has renamed away from ----

    private const string RenamedWhere = "host:/checkouts/one";

    private static EfHatchRunner Renamed(string name, Guid? owner, string? where = RenamedWhere, string kind = EfHatchRunner.LoopKind) => new()
    {
        Name = name, Kind = kind, Where = where, ForPersonId = owner, State = EfHatchRunner.Running,
        FirstSeenAt = Now.AddHours(-1), LastSeenAt = Now, Remotes = "example.com/o/r",
    };

    /// <summary>Whether a row counts as live, and which projects it is elected to drive - the two things a retired row loses.</summary>
    private static async Task<(bool Live, IReadOnlyList<string> Drives)> StandingAsync(Harness h, string name, bool wall)
    {
        var runners = new Runners(
            Options.Create(new HatchOptions { RunnerGoneAfterSeconds = Horizon }),
            Options.Create(new AuthOptions { Enabled = wall }),
            new StubRevision("dev", 0));
        var joined = await global::Hatch.Api.Modules.Hatch.Runner.JoinAsync(h.Db, h.Keys, default);
        var live = joined.Single(r => r.Name == name).IsLive(runners, new RunnerSessions(runners), h.Time.GetUtcNow());
        var drivers = await runners.DriversAsync(h.Db, h.Keys, h.Time.GetUtcNow(), default);

        return (live, drivers.GetValueOrDefault(name, []));
    }

    [Fact]
    public async Task JoinAsync_SkipsAnAdoptedRow()
    {
        var h = await NewAsync(wall: true);
        var row = Tagged("old:/one", Guid.NewGuid(), "");
        row.AdoptedAt = Now;
        row.AdoptedInto = "worker";
        h.Db.LegacyRunners.Add(row);
        await h.Db.SaveChangesAsync();

        var joined = await global::Hatch.Api.Modules.Hatch.Runner.JoinAsync(h.Db, h.Keys, default);

        Assert.DoesNotContain(joined, r => r.Name == "old:/one");
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ALoopBeatUnderANewName_RetiresTheOldRowAtOnce_AndItIsNeitherLiveNorElected()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        await h.BindRepositoryAsync("https://example.com/o/r");
        h.Db.LegacyRunners.Add(Renamed("old:/one", h.PersonId));
        await h.Db.SaveChangesAsync();

        // Before the beat the old row is live and drives the project.
        var before = await StandingAsync(h, "old:/one", wall: true);
        Assert.True(before.Live);
        Assert.Equal(["AER"], before.Drives);

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        var old = await h.Db.LegacyRunners.AsNoTracking().SingleAsync(r => r.Name == "old:/one");
        Assert.Equal(Now, old.RetiredAt);
        Assert.Equal("hatch", old.RetiredBy);
        var after = await StandingAsync(h, "old:/one", wall: true);
        Assert.False(after.Live);
        Assert.Empty(after.Drives);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ARowFromAnotherCheckoutOrOwnerOrKind_IsNeverRetired()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Renamed("elsewhere", h.PersonId, where: "host:/checkouts/two"));
        h.Db.LegacyRunners.Add(Renamed("nowhere", h.PersonId, where: null));
        h.Db.LegacyRunners.Add(Renamed("someone-elses", Guid.NewGuid()));
        h.Db.LegacyRunners.Add(Renamed("unowned", null));
        h.Db.LegacyRunners.Add(Renamed("a-pass", h.PersonId, kind: EfHatchRunner.OnceKind));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        foreach (var row in await h.Db.LegacyRunners.AsNoTracking().ToListAsync())
        {
            Assert.Null(row.RetiredAt);
            Assert.Null(row.RetiredBy);
        }
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ABeatWithNoWhere_RetiresNothing()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Renamed("old:/one", h.PersonId));
        h.Db.LegacyRunners.Add(Renamed("nowhere", h.PersonId, where: null));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true));

        Assert.All(await h.Db.LegacyRunners.AsNoTracking().ToListAsync(), r => Assert.Null(r.RetiredAt));
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ARetiredRowWhoseNameIsTheKeys_IsUnretiredByTheBeat()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        var back = Renamed("hatch", h.PersonId);
        back.RetiredAt = Now.AddMinutes(-5);
        back.RetiredBy = "other:/one";
        h.Db.LegacyRunners.Add(back);
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        var row = await h.Db.LegacyRunners.AsNoTracking().SingleAsync();
        Assert.Null(row.RetiredAt);
        Assert.Null(row.RetiredBy);
        Assert.True((await StandingAsync(h, "hatch", wall: true)).Live);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AOnceBesideALoop_RetiresNothing_AndALaterLoopBeatDoesNotMoveTheStamp()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Renamed("old:/one", h.PersonId));
        await h.Db.SaveChangesAsync();

        // The pass shares the loop's name and its (key, where): it speaks for
        // no rename, so the old row is left exactly as it was.
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "once", Where: RenamedWhere));
        Assert.Null((await h.Db.LegacyRunners.AsNoTracking().SingleAsync()).RetiredAt);

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: RenamedWhere));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "once", Where: RenamedWhere));
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: RenamedWhere));

        Assert.Equal(Now, (await h.Db.LegacyRunners.AsNoTracking().SingleAsync()).RetiredAt);
    }

    private static EfHatchRunner Tagged(string name, Guid? owner, string tags, int minutesAgo = 0, string? retiredBy = null, string? where = RenamedWhere)
    {
        var row = Renamed(name, owner, where);
        row.Tags = tags;
        row.LastSeenAt = Now.AddMinutes(-minutesAgo);
        if (retiredBy is not null) { row.RetiredAt = Now.AddMinutes(-5); row.RetiredBy = retiredBy; }
        return row;
    }

    private static async Task<EfHatchRunnerConfig?> ConfigOfAsync(Harness h) =>
        await h.Db.RunnerConfigs.AsNoTracking().SingleOrDefaultAsync(c => c.ApiKeyId == h.Caller.Key!.Id);

    [SkippableFact]
    public async Task SessionHeartbeat_TaggedRowsItRetires_AndNoConfig_CarryTheirTagsOntoANewConfig()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Tagged("old:/one", h.PersonId, "gpu\nbackend"));
        await h.Db.SaveChangesAsync();

        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        var config = await ConfigOfAsync(h);
        Assert.NotNull(config);
        Assert.Equal(["gpu", "backend"], RunnerTagColumn.Read(config.Tags));
        Assert.Equal(["backend", "gpu"], Value(result).Tags!.Order());
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AnExistingConfigWithNoTags_IsLeftAlone()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        var earlier = Now.AddDays(-3);
        h.Db.RunnerConfigs.Add(new EfHatchRunnerConfig { ApiKeyId = h.Caller.Key!.Id, Tags = "[]", UpdatedAt = earlier });
        h.Db.LegacyRunners.Add(Tagged("old:/one", h.PersonId, "gpu"));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        var config = await ConfigOfAsync(h);
        Assert.Equal("[]", config!.Tags);
        Assert.Equal(earlier, config.UpdatedAt);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AnExistingTaggedConfig_IsLeftAlone()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.RunnerConfigs.Add(new EfHatchRunnerConfig { ApiKeyId = h.Caller.Key!.Id, Tags = RunnerTagColumn.Write(["mine"]), UpdatedAt = Now });
        h.Db.LegacyRunners.Add(Tagged("old:/one", h.PersonId, "gpu"));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        Assert.Equal(["mine"], RunnerTagColumn.Read((await ConfigOfAsync(h))!.Tags));
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AnAlreadyRetiredRowAndOneRetiredNow_GiveTheirUnion()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Tagged("earlier:/two", h.PersonId, "gpu\nshared", minutesAgo: 30, retiredBy: "hatch", where: "host:/checkouts/two"));
        h.Db.LegacyRunners.Add(Tagged("old:/one", h.PersonId, "shared\nbackend", minutesAgo: 1));
        h.Db.LegacyRunners.Add(Tagged("other-key:/three", h.PersonId, "not-mine", minutesAgo: 30, retiredBy: "someone-else", where: "host:/checkouts/three"));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        Assert.Equal(["shared", "backend", "gpu"], RunnerTagColumn.Read((await ConfigOfAsync(h))!.Tags));
    }

    [SkippableFact]
    public async Task SessionHeartbeat_RowsWithNoTags_CreateNoConfig()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Renamed("old:/one", h.PersonId));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        Assert.Null(await ConfigOfAsync(h));
    }

    [SkippableFact]
    public async Task SessionHeartbeat_MoreTagsThanTheLimit_CarryTheMostRecentlyHeardUpToTheLimit()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Tagged("older:/one", h.PersonId, "a1\na2\na3\na4\na5", minutesAgo: 10));
        h.Db.LegacyRunners.Add(Tagged("newer:/two", h.PersonId, "b1\nb2\nb3\nb4\nb5", minutesAgo: 1));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        var tags = RunnerTagColumn.Read((await ConfigOfAsync(h))!.Tags);
        Assert.Equal(RunnerTags.MaxCount, tags.Count);
        Assert.Equal(["b1", "b2", "b3", "b4", "b5", "a1", "a2", "a3"], tags);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AOnceBeat_CarriesNothing()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, wall: true, keyOwner: PersonRole.User, connectionString: connectionString);
        h.Db.LegacyRunners.Add(Tagged("old:/one", h.PersonId, "gpu"));
        await h.Db.SaveChangesAsync();

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "once", Where: RenamedWhere));

        Assert.Null(await ConfigOfAsync(h));
    }

    [SkippableFact]
    public async Task SessionHeartbeat_WithNoOwnerAndTheWallUp_RetiresNothing_AndWithTheWallOffMatchesNullToNull()
    {
        var connectionString = await ConnectAsync();
        var up = await NewAsync(program: true, wall: true, connectionString: connectionString);
        up.Db.LegacyRunners.Add(Renamed("old:/one", null));
        await up.Db.SaveChangesAsync();

        await up.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        Assert.Null((await up.Db.LegacyRunners.AsNoTracking().SingleAsync()).RetiredAt);

        var connectionString2 = await ConnectAsync();
        var off = await NewAsync(program: true, connectionString: connectionString2);
        off.Db.LegacyRunners.Add(Renamed("old:/one", null));
        await off.Db.SaveChangesAsync();

        await off.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: RenamedWhere));

        Assert.Equal("hatch", (await off.Db.LegacyRunners.AsNoTracking().SingleAsync()).RetiredBy);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AFreshBeatAtAFreeWhere_InsertsOneRowAndNarrowsItsBounds()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        h.Db.RunnerConfigs.Add(new EfHatchRunnerConfig { ApiKeyId = h.Caller.Key!.Id, MaxRuns = 5, UpdatedAt = Now });
        await h.Db.SaveChangesAsync();

        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner, MaxRuns: 10));

        var instruction = Value(result);
        Assert.NotNull(instruction.SessionId);
        Assert.Equal(5, instruction.MaxRuns);
        Assert.Equal(1, await h.Db.RunnerSessions.CountAsync());
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AFreshBeatAtAnOccupiedWhere_Is409AndWritesNothing()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));
        var before = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();

        h.Time.Advance(TimeSpan.FromSeconds(20));
        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        var held = Assert.IsType<RunnerSessionRefusalDto>(refusal.Value);
        Assert.Contains("hatch is already the runner on", held.Error, StringComparison.Ordinal);
        Assert.Equal(before.LastSeenAt, held.LastSeenAt);
        Assert.Equal(Horizon - 20, held.GoneInSeconds);
        Assert.Equal(1, await h.Db.RunnerSessions.CountAsync());
        var after = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal(before.LastSeenAt, after.LastSeenAt);
    }

    [SkippableFact]
    public async Task EndSession_EndsThisKeysOwnSessionAndIsIdempotent()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));
        var id = (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).Id;

        h.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.IsType<NoContentResult>(await h.Runners.EndSession(id, default));
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.IsType<NoContentResult>(await h.Runners.EndSession(id, default));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddSeconds(10), row.EndedAt);
    }

    [SkippableFact]
    public async Task EndSession_AnotherKeysSessionOrNoSuchId_Is404()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        var other = await h.AddKeyAsync("here:/other");
        var theirs = await h.SeedSessionAsync(other.Id, "loop", "here:/others");

        Assert.IsType<NotFoundResult>(await h.Runners.EndSession(theirs.Id, default));
        Assert.IsType<NotFoundResult>(await h.Runners.EndSession(int.MaxValue, default));
        Assert.Null((await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == theirs.Id)).EndedAt);
    }

    [Fact]
    public async Task EndSession_WithNoKey_Is403()
    {
        var h = await NewAsync(nobody: true);

        var result = await h.Runners.EndSession(1, default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AFreshBeatRightAfterEnd_SeedsANewSession()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));
        var first = (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).Id;
        await h.Runners.EndSession(first, default);

        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        Assert.IsType<RunnerInstructionDto>(result.Value);
        var rows = await h.Db.RunnerSessions.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].EndedAt);
        Assert.Null(rows[1].EndedAt);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AFreshBeatAfterTheGoneHorizon_SeedsANewSession()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 1));
        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        Assert.IsType<RunnerInstructionDto>(result.Value);
        Assert.Equal(2, await h.Db.RunnerSessions.CountAsync());
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ANonFreshBeat_JoinsTheLiveSessionRatherThanInsertingAnother()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner));

        Assert.Equal(1, await h.Db.RunnerSessions.CountAsync());
        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddMinutes(1), row.LastSeenAt);
        Assert.Equal(Now, row.StartedAt);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AOnceBeat_JoinsALoopSessionWithoutChangingItsKindAndElseInsertsItsOwn()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "once", Where: Runner));

        Assert.Equal(1, await h.Db.RunnerSessions.CountAsync());
        Assert.Equal(EfHatchRunner.LoopKind, (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).Kind);

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "once", Where: "here:/checkouts/two"));

        Assert.Equal(2, await h.Db.RunnerSessions.CountAsync());
        Assert.Equal(EfHatchRunner.OnceKind, (await h.Db.RunnerSessions.AsNoTracking()
            .SingleAsync(s => s.Where == "here:/checkouts/two")).Kind);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ASecondBeat_ChangesRefusesDangerousAndProjects()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Fresh: true, Where: Runner, RefusesDangerous: true, Projects: ["AER"]));

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Where: Runner, RefusesDangerous: false, Projects: ["FIL"]));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.False(row.RefusesDangerous);
        Assert.Equal("FIL", row.Projects);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ABeatThatSaysNothing_LeavesRefusesDangerousAndProjectsAlone()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Fresh: true, Where: Runner, RefusesDangerous: true, Projects: ["AER"]));

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.True(row.RefusesDangerous);
        Assert.Equal("AER", row.Projects);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_TagsOnABeat_AreIgnored()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner, Tags: ["fast"]));

        Value(result);
        Assert.Equal(1, await h.Db.RunnerSessions.CountAsync());
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AFreshBeatWithTheMachineFields_StoresAllSevenAndProjectsThem()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        h.CoreDb.ApiKeys.Add(new EfApiKey
        {
            Id = h.Caller.Key!.Id, Name = h.Caller.Key.Name, Hash = h.Caller.Key.Hash, Prefix = h.Caller.Key.Prefix, CreatedAt = Now,
        });
        await h.CoreDb.SaveChangesAsync();

        await h.SessionBeatAsync(
            new RunnerHeartbeatRequest(
                Kind: "loop", Fresh: true, Where: Runner, Os: "linux", Arch: "x64", AgentVersion: "1.2.3",
                Container: true, Username: "ada", ProcessStartedAt: Now.AddMinutes(-5)),
            ip: "203.0.113.9");

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("linux", row.Os);
        Assert.Equal("x64", row.Arch);
        Assert.Equal("1.2.3", row.AgentVersion);
        Assert.True(row.Container);
        Assert.Equal("ada", row.Username);
        Assert.Equal(Now.AddMinutes(-5), row.ProcessStartedAt);
        Assert.Equal("203.0.113.9", row.Ip);

        var session = Assert.Single((await h.OneAsync()).Sessions);
        Assert.Equal("linux", session.Os);
        Assert.Equal("x64", session.Arch);
        Assert.Equal("1.2.3", session.AgentVersion);
        Assert.True(session.Container);
        Assert.Equal("ada", session.Username);
        Assert.Equal(Now.AddMinutes(-5), session.ProcessStartedAt);
        Assert.Equal("203.0.113.9", session.Ip);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AnOlderClisBeatWithNoMachineFields_LeavesThemNullButStoresTheIp()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner), ip: "203.0.113.9");

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Null(row.Os);
        Assert.Null(row.Arch);
        Assert.Null(row.AgentVersion);
        Assert.Null(row.Username);
        Assert.Null(row.ProcessStartedAt);
        Assert.Null(row.Container);
        Assert.Equal("203.0.113.9", row.Ip);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ABodyNamingIp_NeverSetsIt()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        var request = JsonSerializer.Deserialize<RunnerHeartbeatRequest>(
            """{"kind":"loop","where":"here:/checkouts/one","fresh":true,"ip":"6.6.6.6"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        await h.SessionBeatAsync(request, ip: "203.0.113.9");

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("203.0.113.9", row.Ip);
        Assert.Null(typeof(RunnerHeartbeatRequest).GetProperty("Ip"));
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ALaterBeatOmittingTheMachineFields_LeavesThemAloneButMovesTheIp()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        await h.SessionBeatAsync(
            new RunnerHeartbeatRequest(
                Kind: "loop", Fresh: true, Where: Runner, Os: "linux", Arch: "x64", AgentVersion: "1.2.3",
                Container: true, Username: "ada", ProcessStartedAt: Now.AddMinutes(-5)),
            ip: "203.0.113.9");

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner), ip: "203.0.113.10");

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("linux", row.Os);
        Assert.Equal("x64", row.Arch);
        Assert.Equal("1.2.3", row.AgentVersion);
        Assert.True(row.Container);
        Assert.Equal("ada", row.Username);
        Assert.Equal(Now.AddMinutes(-5), row.ProcessStartedAt);
        Assert.Equal("203.0.113.10", row.Ip);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_AnOsLongerThanTheColumn_IsTruncated()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner, Os: new string('x', 200)));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal(64, row.Os!.Length);
    }

    [SkippableFact]
    public async Task GetRunners_StampsEndedAtOnASessionPastTheHorizon()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner));

        h.Time.Advance(TimeSpan.FromSeconds(Horizon + 1));
        await h.ListAllAsync();

        Assert.Equal(h.Time.GetUtcNow(), (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).EndedAt);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_NamingModelsAndHas_StoresThemNewlineJoined()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Fresh: true, Where: Runner, Agent: "claude", Models: ["haiku", "sonnet"], Has: ["dotnet", "node"]));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("haiku\nsonnet", row.Models);
        Assert.Equal("dotnet\nnode", row.Has);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ALaterBeatNamingNewOnes_Overwrites()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Fresh: true, Where: Runner, Agent: "claude", Models: ["haiku"], Has: ["dotnet"]));

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Where: Runner, Agent: "claude", Models: ["sonnet"], Has: ["node"]));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("sonnet", row.Models);
        Assert.Equal("node", row.Has);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ABeatOmittingThem_LeavesThemUntouched()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);
        await h.SessionBeatAsync(new RunnerHeartbeatRequest(
            Kind: "loop", Fresh: true, Where: Runner, Agent: "claude", Models: ["haiku"], Has: ["dotnet"]));

        await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Where: Runner));

        var row = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("haiku", row.Models);
        Assert.Equal("dotnet", row.Has);
    }

    [SkippableFact]
    public async Task SessionHeartbeat_ABadDependency_Is400AndWritesNoRow()
    {
        var connectionString = await ConnectAsync();
        var h = await NewAsync(program: true, connectionString: connectionString);

        var result = await h.SessionBeatAsync(new RunnerHeartbeatRequest(Kind: "loop", Fresh: true, Where: Runner, Has: ["not a dependency"]));

        Assert.Equal(DependencySet.Normalise(["not a dependency"], out _), Reason(result.Result));
        Assert.Equal(0, await h.Db.RunnerSessions.CountAsync());
    }

    [Fact]
    public async Task GetRunner_OfABeatLessKeyWithALiveSession_ShowsItsModelsAndHas()
    {
        var h = await NewAsync();
        var key = await h.AddKeyAsync("hatch");
        await h.SeedSessionAsync(key.Id, "loop", Runner, models: "haiku\nsonnet", has: "dotnet");

        var dto = await h.GetAsync("hatch");

        Assert.Equal(["haiku", "sonnet"], dto!.Models);
        Assert.Equal(["dotnet"], dto.Has);
    }

    // ---- Session controls ----

    [Fact]
    public async Task PatchSession_SetsOneSessionsStateAndLeavesItsSiblingAlone()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var one = await h.SeedSessionAsync(key.Id, "loop", "here:/one");
        var two = await h.SeedSessionAsync(key.Id, "loop", "here:/two");

        await h.PatchSessionAsync(Runner, one.Id, new RunnerSessionPatchRequest(State: "paused"));

        Assert.Equal("paused", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == one.Id)).State);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == two.Id)).State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("halt")]
    public async Task PatchSession_ABadOrAbsentState_Is400AndWritesNothing(string? sent)
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        var refusal = await h.Runners.PatchRunnerSession(Runner, session.Id, new RunnerSessionPatchRequest(State: sent), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("a runner is running, paused, stopping", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task PatchSession_UnknownName_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        var refusal = await h.Runners.PatchRunnerSession("nowhere:/tree", 1, new RunnerSessionPatchRequest(State: "paused"), default);

        Assert.IsType<NotFoundResult>(refusal.Result);
    }

    [Fact]
    public async Task PatchSession_IdUnderAnotherKey_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        await h.AddKeyAsync(Runner);
        var other = await h.AddKeyAsync("here:/other");
        var session = await h.SeedSessionAsync(other.Id, "loop");

        var refusal = await h.Runners.PatchRunnerSession(Runner, session.Id, new RunnerSessionPatchRequest(State: "paused"), default);

        Assert.IsType<NotFoundResult>(refusal.Result);
    }

    [Fact]
    public async Task PatchSession_ByAnotherPerson_Is403NamingTheOwnerAndWritesNothing()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");
        var key = await h.AddKeyAsync(Runner, alice.Id);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        var refusal = await h.Runners.PatchRunnerSession(Runner, session.Id, new RunnerSessionPatchRequest(State: "paused"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("Alice", Reason(refusal.Result), StringComparison.Ordinal);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task PatchSession_ByAnAdmin_IsAllowed()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var alice = h.Actors.AddPerson("Alice");
        var key = await h.AddKeyAsync(Runner, alice.Id);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        await h.PatchSessionAsync(Runner, session.Id, new RunnerSessionPatchRequest(State: "paused"));

        Assert.Equal("paused", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task PatchSession_ByAKey_Is403AndWritesNothing()
    {
        var h = await NewAsync(program: true, wall: true);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        var refusal = await h.Runners.PatchRunnerSession(Runner, session.Id, new RunnerSessionPatchRequest(State: "paused"), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task OrderSession_Work_LandsOnTheSessionRowNotTheBeatOrSibling()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        await h.BeatAsync(Runner, new RunnerHeartbeatRequest(Kind: "loop"));
        var one = await h.SeedSessionAsync(key.Id, "loop", "here:/one");
        var two = await h.SeedSessionAsync(key.Id, "loop", "here:/two");
        var issue = await h.FileAsync();

        await h.OrderSessionAsync(Runner, one.Id, new RunnerOrderRequest("work", issue.Key));

        var ordered = await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == one.Id);
        Assert.Equal("work", ordered.OrderKind);
        Assert.Equal(issue.Key, ordered.OrderKey);
        Assert.Equal("Nathan", ordered.OrderedBy);
        Assert.NotNull(ordered.OrderedAt);

        Assert.Null((await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == two.Id)).OrderKind);
        Assert.Null((await h.RowAsync()).OrderKind);
    }

    [Fact]
    public async Task OrderSession_Pass_LandsOnTheSessionRowWithNoKey()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        await h.OrderSessionAsync(Runner, session.Id, new RunnerOrderRequest("pass"));

        var ordered = await h.Db.RunnerSessions.AsNoTracking().SingleAsync();
        Assert.Equal("pass", ordered.OrderKind);
        Assert.Null(ordered.OrderKey);
    }

    [Fact]
    public async Task OrderSession_Work_WritesTheOrderedEvent()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");
        var issue = await h.FileAsync();

        await h.OrderSessionAsync(Runner, session.Id, new RunnerOrderRequest("work", issue.Key));

        var last = Assert.Single(await h.EventsAsync(issue.Key), e => e.Kind == EfHatchIssueEvent.Ordered);
        Assert.Equal("Nathan", last.Actor);
        Assert.Contains(Runner, last.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderSession_ABadKind_Is400()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        var refusal = await h.Runners.PostRunnerSessionOrder(Runner, session.Id, new RunnerOrderRequest("sleep"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("an order is work, pass", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderSession_WorkWithNoKey_Is400()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        var refusal = await h.Runners.PostRunnerSessionOrder(Runner, session.Id, new RunnerOrderRequest("work"), default);

        Assert.Equal(400, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("work names an issue", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderSession_WorkNamingAnIssueThatDoesNotExist_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");

        var refusal = await h.Runners.PostRunnerSessionOrder(Runner, session.Id, new RunnerOrderRequest("work", "AER-999"), default);

        Assert.Equal(404, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("AER-999", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderSession_AOnceSession_Is409()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "once");

        var refusal = await h.Runners.PostRunnerSessionOrder(Runner, session.Id, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("single increment", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderSession_AStoppingSession_Is409()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");
        session.State = EfHatchRunner.Stopping;
        await h.Db.SaveChangesAsync();

        var refusal = await h.Runners.PostRunnerSessionOrder(Runner, session.Id, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.Contains("stopping", Reason(refusal.Result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderSession_AHeldOrder_Is409UntilWithdrawn()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");
        var issue = await h.FileAsync();
        await h.OrderSessionAsync(Runner, session.Id, new RunnerOrderRequest("work", issue.Key));

        var refusal = await h.Runners.PostRunnerSessionOrder(Runner, session.Id, new RunnerOrderRequest("pass"), default);

        Assert.Equal(409, ((ObjectResult)refusal.Result!).StatusCode);
        var reason = Reason(refusal.Result);
        Assert.Contains("work", reason, StringComparison.Ordinal);
        Assert.Contains(issue.Key, reason, StringComparison.Ordinal);
        Assert.Contains("withdraw it first", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteSessionOrder_ClearsOnlyThatSessionsOrder()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var one = await h.SeedSessionAsync(key.Id, "loop", "here:/one");
        var two = await h.SeedSessionAsync(key.Id, "loop", "here:/two");
        var issue = await h.FileAsync();
        await h.OrderSessionAsync(Runner, one.Id, new RunnerOrderRequest("work", issue.Key));
        await h.OrderSessionAsync(Runner, two.Id, new RunnerOrderRequest("pass"));

        var result = await h.WithdrawSessionAsync(Runner, one.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null((await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == one.Id)).OrderKind);
        Assert.Equal("pass", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == two.Id)).OrderKind);
    }

    [Fact]
    public async Task DeleteSessionOrder_OnAnUnorderedSession_Is204AndWritesNothing()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var session = await h.SeedSessionAsync(key.Id, "loop");
        var seenAt = session.LastSeenAt;

        var result = await h.WithdrawSessionAsync(Runner, session.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(seenAt, (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).LastSeenAt);
    }

    [Fact]
    public async Task PauseAll_OnlyPausesLiveLoopSessionsUnderThisKey()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        var other = await h.AddKeyAsync("here:/other");

        var liveOne = await h.SeedSessionAsync(key.Id, "loop", "here:/one");
        var liveTwo = await h.SeedSessionAsync(key.Id, "loop", "here:/two");
        var once = await h.SeedSessionAsync(key.Id, "once", "here:/once");
        var ended = await h.SeedSessionAsync(key.Id, "loop", "here:/ended", endedAt: Now);
        var pastHorizon = await h.SeedSessionAsync(
            key.Id, "loop", "here:/past", lastSeenAt: Now - TimeSpan.FromSeconds(Horizon + 1));
        var othersSession = await h.SeedSessionAsync(other.Id, "loop", "here:/others");

        var result = await h.PauseAllAsync(Runner);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("paused", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == liveOne.Id)).State);
        Assert.Equal("paused", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == liveTwo.Id)).State);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == once.Id)).State);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == ended.Id)).State);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == pastHorizon.Id)).State);
        Assert.Equal("running", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync(s => s.Id == othersSession.Id)).State);
    }

    [Fact]
    public async Task StopAll_SetsStoppingOnEveryLiveLoopSession()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        await h.SeedSessionAsync(key.Id, "loop");

        await h.StopAllAsync(Runner);

        Assert.Equal("stopping", (await h.Db.RunnerSessions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task PauseAll_WithNoLiveSessions_IsStill204()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);
        var key = await h.AddKeyAsync(Runner);
        await h.SeedSessionAsync(key.Id, "once");

        Assert.IsType<NoContentResult>(await h.PauseAllAsync(Runner));
    }

    [Fact]
    public async Task PauseAll_UnknownName_Is404()
    {
        var h = await NewAsync(wall: true, role: PersonRole.Admin);

        Assert.IsType<NotFoundResult>(await h.PauseAllAsync("nowhere:/tree"));
    }

    [Fact]
    public async Task PauseAll_ByAnotherPerson_Is403NamingTheOwner()
    {
        var h = await NewAsync(wall: true, role: PersonRole.User);
        var alice = h.Actors.AddPerson("Alice");
        var key = await h.AddKeyAsync(Runner, alice.Id);
        await h.SeedSessionAsync(key.Id, "loop");

        var refusal = Assert.IsType<ObjectResult>(await h.PauseAllAsync(Runner));

        Assert.Equal(403, refusal.StatusCode);
        Assert.Contains("Alice", refusal.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAll_ByAKey_Is403()
    {
        var h = await NewAsync(program: true, wall: true);
        var key = await h.AddKeyAsync(Runner);
        await h.SeedSessionAsync(key.Id, "loop");

        var refusal = Assert.IsType<ObjectResult>(await h.StopAllAsync(Runner));

        Assert.Equal(403, refusal.StatusCode);
    }

    [Fact]
    public void TheSessionControlRoutes_ArePersonOnly()
    {
        Assert.False(Guard(nameof(RunnersController.PatchRunnerSession))!.AcceptKey);
        Assert.False(Guard(nameof(RunnersController.PostRunnerSessionOrder))!.AcceptKey);
        Assert.False(Guard(nameof(RunnersController.DeleteRunnerSessionOrder))!.AcceptKey);
        Assert.False(Guard(nameof(RunnersController.PauseAll))!.AcceptKey);
        Assert.False(Guard(nameof(RunnersController.StopAll))!.AcceptKey);
    }

    private static async Task<string> ConnectAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the session heartbeat calls MarkGoneAsync and " +
            "TrimHistoryAsync, which EF's in-memory provider cannot execute. Run `make test-api-db`, or " +
            "point the variable at a scratch database.");

        return await HatchDatabase.PrepareAsync();
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The horizon these tests judge against, short enough to step over inside one.</summary>
    private const int Horizon = 60;

    private sealed class Harness
    {
        public required RunnersController Runners { get; init; }
        public required IssuesController Issues { get; init; }
        public required HatchContext Db { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required Guid PersonId { get; init; }
        public required StubCaller Caller { get; init; }

        private int nextRepoOrder;

        public async Task<IssueDto> FileAsync(string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", title, null, null, null, null), default));

        /// <summary>A remote bound to the shared project, written straight to the table - WorkControllerTests's own pattern.</summary>
        public async Task<EfHatchProjectRepository> BindRepositoryAsync(string remote)
        {
            var (canonical, _) = RemoteIdentity.Canonical(remote);
            var repo = new EfHatchProjectRepository
            {
                ProjectId = ProjectId,
                Remote = remote,
                Canonical = canonical!,
                SortOrder = nextRepoOrder++,
                CreatedAt = Now,
            };

            Db.ProjectRepositories.Add(repo);
            await Db.SaveChangesAsync();
            return repo;
        }

        public async Task AddMemberAsync(Guid personId, string role)
        {
            Db.ProjectMembers.Add(new EfHatchProjectMember
            {
                ProjectId = ProjectId, PersonId = personId, Role = role, CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        public async Task RemoveMemberAsync(Guid personId)
        {
            Db.ProjectMembers.RemoveRange(Db.ProjectMembers.Where(m => m.ProjectId == ProjectId && m.PersonId == personId));
            await Db.SaveChangesAsync();
        }

        /// <summary>A loop beat for the shared project's remote, as the key of <paramref name="who"/> - or of nobody, for null.</summary>
        public async Task<RunnerInstructionDto> LoopBeatAsync(string name, Actor? who)
        {
            Actors.Principal = who;
            return await BeatAsync(name, new RunnerHeartbeatRequest(Kind: "loop", Remotes: ["git@example.com:o/r.git"]));
        }

        public async Task<RunnerInstructionDto> BeatAsync(string name, RunnerHeartbeatRequest request) =>
            Value(await Runners.Heartbeat(name, request, default));

        public async Task<ActionResult<RunnerInstructionDto>> SessionBeatAsync(RunnerHeartbeatRequest request, string? ip = null)
        {
            if (ip is not null)
                Runners.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = IPAddress.Parse(ip) } },
                };
            return await Runners.SessionHeartbeat(request, default);
        }

        public async Task<RunnerDto> PatchAsync(string name, RunnerPatchRequest request) =>
            Value(await Runners.PatchRunner(name, request, default));

        public async Task<IActionResult> RevokeAsync(string name) => await Runners.RevokeRunner(name, default);

        /// <summary>Rotates a runner's secret, with the response the controller writes its headers to.</summary>
        public async Task<ActionResult<RunnerMintedDto>> RotateAsync(string name)
        {
            Runners.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return await Runners.RotateRunner(name, default);
        }

        public async Task<IActionResult> SetOwnerAsync(string name, Guid? personId) =>
            await Runners.SetRunnerOwner(name, new RunnerOwnerRequest(personId), default);

        public async Task<RunnerDto> OrderAsync(string name, RunnerOrderRequest request) =>
            Value(await Runners.PostRunnerOrder(name, request, default));

        public async Task<ActionResult> WithdrawAsync(string name) => await Runners.DeleteRunnerOrder(name, default);

        public async Task<RunnerDto> PatchSessionAsync(string name, int id, RunnerSessionPatchRequest request) =>
            Value(await Runners.PatchRunnerSession(name, id, request, default));

        public async Task<RunnerDto> OrderSessionAsync(string name, int id, RunnerOrderRequest request) =>
            Value(await Runners.PostRunnerSessionOrder(name, id, request, default));

        public async Task<ActionResult> WithdrawSessionAsync(string name, int id) =>
            await Runners.DeleteRunnerSessionOrder(name, id, default);

        public async Task<ActionResult> PauseAllAsync(string name) => await Runners.PauseAll(name, default);

        public async Task<ActionResult> StopAllAsync(string name) => await Runners.StopAll(name, default);

        /// <summary>A session written straight to the table - no heartbeat, no Postgres - for the session control tests, which judge a row they placed themselves rather than one a beat produced.</summary>
        public async Task<EfHatchRunnerSession> SeedSessionAsync(
            Guid apiKeyId, string kind, string? where = null, DateTimeOffset? lastSeenAt = null, DateTimeOffset? endedAt = null,
            string? models = null, string? has = null)
        {
            var session = new EfHatchRunnerSession
            {
                ApiKeyId = apiKeyId,
                Kind = kind,
                Where = where,
                State = EfHatchRunner.Running,
                StartedAt = Now,
                LastSeenAt = lastSeenAt ?? Now,
                EndedAt = endedAt,
                Models = models,
                Has = has,
            };
            Db.RunnerSessions.Add(session);
            await Db.SaveChangesAsync();
            return session;
        }

        /// <summary>An issue's own events, oldest first.</summary>
        public async Task<IReadOnlyList<EfHatchIssueEvent>> EventsAsync(string key)
        {
            var issue = await Find(key);
            return await Db.IssueEvents.Where(e => e.IssueId == issue.Id).OrderBy(e => e.At).ToListAsync();
        }

        /// <summary>
        /// A merges-column row with a clean, trunk-holding branch and a passed
        /// build - the shared project's own train head, with nothing standing
        /// in front of it - copied from WorkControllerTests's own
        /// <c>VerdictAsync</c>/<c>BuildAsync</c> pattern, since this harness has
        /// no merges column or checks of its own.
        /// </summary>
        public async Task<EfHatchIssue> SeedMergingHeadAsync(string title = "a thing", string remote = "https://example.com/o/r")
        {
            var merges = new EfHatchStatus { Name = "merges", SortOrder = 40, IsProtected = true, MergesPullRequest = true };
            Db.Add(merges);
            await Db.SaveChangesAsync();

            var created = Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "story", title, null, null, null, null), default));
            var issue = await Find(created.Key);
            issue.StatusId = merges.Id;
            issue.PullRequestUrl = $"{remote}/pull/1";
            await Db.SaveChangesAsync();

            var (canonical, _) = RemoteIdentity.Canonical(remote);
            Db.MergeChecks.Add(new EfHatchMergeCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical!,
                Trunk = "main",
                TrunkSha = new string('a', 40),
                Verdict = MergeVerdicts.Clean,
                Branch = "aer-1-thing",
                BranchSha = new string('b', 40),
                HoldsTrunk = true,
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });
            Db.BuildChecks.Add(new EfHatchBuildCheck
            {
                IssueId = issue.Id,
                Remote = remote,
                Canonical = canonical!,
                Branch = "aer-1-thing",
                Sha = new string('b', 40),
                ShaSince = Now,
                Verdict = BuildVerdicts.Passed,
                CheckedAt = Now,
                Runner = "host:/checkout",
                CheckedBy = "runner",
            });
            await Db.SaveChangesAsync();

            return issue;
        }

        public required IAuthService Keys { get; init; }

        public required AppDbContext CoreDb { get; init; }

        public async Task<IReadOnlyList<RunnerDto>> ListAsync() => Value(await Runners.GetRunners(default, live: true));

        public async Task<IReadOnlyList<RunnerDto>> ListAllAsync() => Value(await Runners.GetRunners(default, live: false));

        public async Task<RunnerDto?> GetAsync(string name)
        {
            var result = await Runners.GetRunner(name, default);
            return result.Result is NotFoundResult or NotFoundObjectResult ? null : Value(result);
        }

        /// <summary>Mints a runner as the harness's caller, with the response the controller writes its headers to.</summary>
        public async Task<ActionResult<RunnerMintedDto>> CreateAsync(CreateRunnerRequest request)
        {
            Runners.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            return await Runners.CreateRunner(request, default);
        }

        public async Task<string> SuggestAsync() => Value(await Runners.SuggestRunnerName(default));

        public async Task<RunnerDto> OneAsync() => Assert.Single(await ListAsync());

        /// <summary>A key with no beat - a runner never heard from yet, revocable with <see cref="RevokeKeyAsync"/>.</summary>
        public async Task<EfApiKey> AddKeyAsync(string name, Guid? ownerPersonId = null)
        {
            var created = await Keys.CreateApiKeyAsync(name, ownerPersonId, default);
            return created!.Key;
        }

        public async Task RevokeKeyAsync(Guid id) => await Keys.RevokeApiKeyAsync(id, default);

        /// <summary>
        /// The stored row itself, for the fields no wire DTO carries -
        /// <see cref="EfHatchRunner.ForPersonId"/>, <see cref="EfHatchRunner.Usage"/>
        /// and <see cref="EfHatchRunner.UsageReadAt"/> - none of which
        /// <see cref="RunnerDto"/> answers; see HA-134's "out of scope".
        /// </summary>
        public async Task<EfHatchRunner> RowAsync() => await Db.LegacyRunners.AsNoTracking().SingleAsync();

        /// <summary>
        /// A lease, written straight onto the row. The endpoint that normally
        /// writes one is built out of conditional UPDATEs the in-memory
        /// provider refuses - see HatchDatabase - and nothing here is testing
        /// the claim.
        /// </summary>
        public async Task ClaimAsync(string key, string runner, string? chatter = null, string? where = null)
        {
            var issue = await Find(key);
            issue.ClaimToken = Guid.NewGuid();
            issue.ClaimedBy = runner;
            issue.ClaimRunner = where ?? runner;
            issue.ClaimedAt = Time.GetUtcNow();
            issue.ClaimHeartbeatAt = Time.GetUtcNow();
            issue.ClaimChatter = chatter;
            issue.ClaimChatterAt = chatter is null ? null : Time.GetUtcNow();
            await Db.SaveChangesAsync();
        }

        public async Task ReleaseAsync(string key)
        {
            var issue = await Find(key);
            issue.ClaimToken = null;
            issue.ClaimRunner = null;
            issue.ClaimHeartbeatAt = null;
            issue.ClaimChatter = null;
            await Db.SaveChangesAsync();
        }

        private async Task<EfHatchIssue> Find(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.Include(i => i.Project).WithKey(projectKey, number).FirstAsync();
        }
    }

    private static async Task<Harness> NewAsync(
        bool program = false, bool wall = false, PersonRole role = PersonRole.User, Guid? personId = null,
        PersonRole? keyOwner = null, bool nobody = false, IHatchRevision? revision = null,
        long? defaultMaxTokens = null, long? defaultMaxTokensPerHour = null, ISiteSettingsService? siteSettings = null,
        string? connectionString = null, int? trainStallMinutes = null, int? runnerGoneAfterSeconds = null)
    {
        var db = connectionString is null
            ? new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
            : new HatchContext(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var coreDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new AuthService(coreDb, Options.Create(new AuthOptions { Enabled = wall }), time, NullLogger<AuthService>.Instance);
        var claims = TestClaims.With();
        var runners = new Runners(
            Options.Create(new HatchOptions
            {
                RunnerGoneAfterSeconds = runnerGoneAfterSeconds ?? Horizon,
                TrainStallMinutes = trainStallMinutes ?? new HatchOptions().TrainStallMinutes,
            }),
            Options.Create(new AuthOptions { Enabled = wall }),
            revision ?? new StubRevision("dev", 0));
        var actors = new StubActorDirectory();
        var dispatch = new Dispatch(db, actors, claims, time);

        var id = personId ?? Guid.NewGuid();
        var caller = nobody
            ? new StubCaller()
            : program
            ? new StubCaller
            {
                Key = new EfApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "hatch",
                    Hash = [1],
                    Prefix = "hatch_ak_x",
                    CreatedAt = Now,
                    OwnerPersonId = keyOwner is null ? null : id,
                    Owner = keyOwner is null ? null : new EfPerson { Id = id, Name = "Owner", Role = keyOwner.Value, CreatedAt = Now, UpdatedAt = Now },
                },
            }
            : new StubCaller { Person = new EfPerson { Id = id, Name = "Nathan", Role = role, CreatedAt = Now, UpdatedAt = Now } };

        var sessions = new RunnerSessions(runners);

        return new Harness
        {
            Runners = new RunnersController(db, runners, claims, caller, actors, time, Options.Create(new AuthOptions { Enabled = wall }),
                siteSettings ?? new StubSiteSettings(runnerDefaultMaxTokens: defaultMaxTokens, runnerDefaultMaxTokensPerHour: defaultMaxTokensPerHour), keys,
                sessions, dispatch, NullLogger<RunnersController>.Instance),
            Issues = new IssuesController(db, new RankService(db), new StubActorDirectory(), claims, caller, time, new ProjectAccess(db, new StubActorDirectory())),
            Db = db,
            Time = time,
            ProjectId = hatch.Id,
            Actors = actors,
            PersonId = id,
            Keys = keys,
            Caller = caller,
            CoreDb = coreDb,
        };
    }

    /// <summary>Site defaults a test can take away between two answers - what the settings page does.</summary>
    private sealed class MutableSiteSettings : ISiteSettingsService
    {
        public long? MaxTokens { get; set; }

        public long? MaxTokensPerHour { get; set; }

        public Task<SiteSettingsSnapshot> GetAsync(CancellationToken ct) =>
            new StubSiteSettings(runnerDefaultMaxTokens: MaxTokens, runnerDefaultMaxTokensPerHour: MaxTokensPerHour).GetAsync(ct);

        public void Invalidate()
        {
        }
    }

    /// <summary>Whoever is holding the phone: a person, or the key an agent carries.</summary>
    private sealed class StubCaller : ICallerIdentity
    {
        public EfPerson? Person { get; init; }

        public EfApiKey? Key { get; set; }

        public Task<EfAuthGrant?> GrantAsync(CancellationToken ct) => Task.FromResult<EfAuthGrant?>(null);

        public Task<Guid?> PersonIdAsync(CancellationToken ct) => Task.FromResult(Person?.Id);

        public Task<EfPerson?> PersonAsync(CancellationToken ct) => Task.FromResult(Person);

        public Task<EfApiKey?> ApiKeyAsync(CancellationToken ct) => Task.FromResult(Key);

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult<Actor?>(null);

        public Task<bool> IsProgramAsync(CancellationToken ct) => Task.FromResult(Key is not null);

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? CallerIdentity.Unattributed);
    }

    private sealed class StubRevision(string revision, int sequence) : IHatchRevision
    {
        public string Revision { get; } = revision;
        public int Sequence { get; } = sequence;
        public DateTimeOffset? BuiltAt => null;
        public bool IsDevelopment => Revision == global::Hatch.Api.Services.HatchRevision.Development;
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
