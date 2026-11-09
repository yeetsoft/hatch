using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a key's live loop sessions add to its own beat row, once
/// <see cref="RunnerDeclaration.OfAsync(Runner,IReadOnlyList{EfHatchRunnerSession},IActorDirectory,CancellationToken)"/>
/// folds the two together - a key with no sessions reads exactly as its beat
/// alone.
/// </summary>
public class RunnerDeclarationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EfApiKey Key(string name = "a-key") => new()
    {
        Id = Guid.NewGuid(), Name = name, Prefix = "hatch_ak_", Hash = [], CreatedAt = Now,
    };

    private static EfHatchRunner Beat(string name, Action<EfHatchRunner>? tweak = null)
    {
        var row = new EfHatchRunner
        {
            Name = name,
            Kind = EfHatchRunner.LoopKind,
            FirstSeenAt = Now,
            LastSeenAt = Now,
            State = EfHatchRunner.Running,
        };
        tweak?.Invoke(row);
        return row;
    }

    private static EfHatchRunnerSession Session(Guid apiKeyId, Action<EfHatchRunnerSession>? tweak = null)
    {
        var session = new EfHatchRunnerSession
        {
            ApiKeyId = apiKeyId,
            Kind = EfHatchRunner.LoopKind,
            State = EfHatchRunner.Running,
            StartedAt = Now,
            LastSeenAt = Now,
        };
        tweak?.Invoke(session);
        return session;
    }

    [Fact]
    public async Task NoSessions_ReadsExactlyAsTheBeatAlone()
    {
        var actors = new StubActorDirectory();
        var key = Key();
        var beat = Beat(key.Name, b => { b.Projects = "AER"; b.Tags = "gpu"; b.RefusesDangerous = true; });
        var runner = new Runner(key, null, beat);

        var fromSessionOverload = await RunnerDeclaration.OfAsync(runner, [], actors, default);
        var fromBeatOverload = await RunnerDeclaration.OfAsync(beat, actors, default);

        Assert.Equal(fromBeatOverload.Under, fromSessionOverload.Under);
        Assert.Equal(fromBeatOverload.Mine, fromSessionOverload.Mine);
        Assert.Equal(fromBeatOverload.Name, fromSessionOverload.Name);
        Assert.Equal(fromBeatOverload.Tags.Has, fromSessionOverload.Tags.Has);
        Assert.Equal(fromBeatOverload.Tags.RefusesDangerous, fromSessionOverload.Tags.RefusesDangerous);
        Assert.Equal(fromBeatOverload.Has.Judged, fromSessionOverload.Has.Judged);
        Assert.Equal(fromBeatOverload.Models.Judged, fromSessionOverload.Models.Judged);
        Assert.Equal(fromBeatOverload.Projects, fromSessionOverload.Projects);
        Assert.Equal(fromBeatOverload.Repos.IsDeclared, fromSessionOverload.Repos.IsDeclared);
    }

    [Fact]
    public async Task TwoBoundSessions_UnionTheirProjectsInFirstSeenOrder()
    {
        var actors = new StubActorDirectory();
        var key = Key();
        var sessionA = Session(key.Id, s => s.Projects = "FIL\nAER");
        var sessionB = Session(key.Id, s => s.Projects = "AER\nHA");
        var runner = new Runner(key, null, null);

        var declared = await RunnerDeclaration.OfAsync(runner, [sessionA, sessionB], actors, default);

        Assert.Equal(["FIL", "AER", "HA"], declared.Projects);
    }

    [Fact]
    public async Task OneUnboundSession_MakesTheWholeDeclarationUnbound()
    {
        var actors = new StubActorDirectory();
        var key = Key();
        var bound = Session(key.Id, s => s.Projects = "AER");
        var unbound = Session(key.Id, s => s.Projects = null);
        var runner = new Runner(key, null, null);

        var declared = await RunnerDeclaration.OfAsync(runner, [bound, unbound], actors, default);

        Assert.Empty(declared.Projects);
    }

    [Fact]
    public async Task EitherSessionRefusingDangerous_RefusesIt()
    {
        var actors = new StubActorDirectory();
        var key = Key();
        var calm = Session(key.Id, s => s.RefusesDangerous = false);
        var refuses = Session(key.Id, s => s.RefusesDangerous = true);
        var runner = new Runner(key, null, null);

        var declared = await RunnerDeclaration.OfAsync(runner, [calm, refuses], actors, default);

        Assert.True(declared.Tags.RefusesDangerous);
    }

    [Fact]
    public async Task SessionOnly_ReadsSessionRemotesAndConfigTags()
    {
        var actors = new StubActorDirectory();
        var key = Key();
        var session = Session(key.Id, s => s.Remotes = "git@example.com:o/r.git");
        var config = new EfHatchRunnerConfig { ApiKeyId = key.Id, Tags = RunnerTagColumn.Write(["gpu"]), UpdatedAt = Now };
        var runner = new Runner(key, config, null);

        var declared = await RunnerDeclaration.OfAsync(runner, [session], actors, default);

        Assert.Equal("git@example.com:o/r.git", declared.Repos.Match("example.com/o/r"));
        Assert.Contains("gpu", declared.Tags.Has);
    }
}
