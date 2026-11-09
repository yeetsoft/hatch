using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// One session a runner key has held - inert today, saving and reading back
/// every field on the terms <see cref="RunnerConfigTests"/> has.
/// </summary>
public class RunnerSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AFullRow_SavesAndReadsBack()
    {
        var keyId = Guid.NewGuid();
        var until = Now.AddHours(8);
        using var db = NewContext();
        db.RunnerSessions.Add(new EfHatchRunnerSession
        {
            ApiKeyId = keyId,
            Where = "host:/path/to/checkout",
            Kind = "loop",
            Host = "host",
            State = "running",
            StartedAt = Now,
            LastSeenAt = Now.AddMinutes(5),
            EndedAt = Now.AddHours(1),
            Line = "waiting for the next ticket",
            LineAt = Now.AddMinutes(4),
            Under = "HA-1",
            MaxRuns = 5,
            MaxSpend = 12.34m,
            MaxTokens = 3_000_000_000L,
            MaxTokensPerHour = 400_000L,
            UntilAt = until,
            Runs = 3,
            Spent = 1.23456789m,
            TokensUsed = 1_000L,
            TokensLastHour = 100L,
            ExhaustedUntil = Now.AddHours(2),
            ThrottledUntil = Now.AddHours(3),
            Usage = "[{\"window\":\"5h\"}]",
            UsageReadAt = Now.AddMinutes(1),
            Remotes = "origin",
            Clones = true,
            Mine = false,
            Standing = true,
            Agent = "claude",
            Revision = "deadbeef",
            Sequence = 42,
            BuiltAt = Now.AddDays(-1),
            SourceRevision = "cafef00d",
            SourceSequence = 41,
            Projects = "HA",
            RefusesDangerous = true,
            OrderKind = "work",
            OrderKey = "HA-2",
            OrderedBy = "Nathan",
            OrderedAt = Now.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = await db.RunnerSessions.SingleAsync(s => s.ApiKeyId == keyId);

        Assert.Equal("host:/path/to/checkout", row.Where);
        Assert.Equal("loop", row.Kind);
        Assert.Equal("host", row.Host);
        Assert.Equal("running", row.State);
        Assert.Equal(Now, row.StartedAt);
        Assert.Equal(Now.AddMinutes(5), row.LastSeenAt);
        Assert.Equal(Now.AddHours(1), row.EndedAt);
        Assert.Equal("waiting for the next ticket", row.Line);
        Assert.Equal(Now.AddMinutes(4), row.LineAt);
        Assert.Equal("HA-1", row.Under);
        Assert.Equal(5, row.MaxRuns);
        Assert.Equal(12.34m, row.MaxSpend);
        Assert.Equal(3_000_000_000L, row.MaxTokens);
        Assert.Equal(400_000L, row.MaxTokensPerHour);
        Assert.Equal(until, row.UntilAt);
        Assert.Equal(3, row.Runs);
        Assert.Equal(1.23456789m, row.Spent);
        Assert.Equal(1_000L, row.TokensUsed);
        Assert.Equal(100L, row.TokensLastHour);
        Assert.Equal(Now.AddHours(2), row.ExhaustedUntil);
        Assert.Equal(Now.AddHours(3), row.ThrottledUntil);
        Assert.Equal("[{\"window\":\"5h\"}]", row.Usage);
        Assert.Equal(Now.AddMinutes(1), row.UsageReadAt);
        Assert.Equal("origin", row.Remotes);
        Assert.True(row.Clones);
        Assert.False(row.Mine);
        Assert.True(row.Standing);
        Assert.Equal("claude", row.Agent);
        Assert.Equal("deadbeef", row.Revision);
        Assert.Equal(42, row.Sequence);
        Assert.Equal(Now.AddDays(-1), row.BuiltAt);
        Assert.Equal("cafef00d", row.SourceRevision);
        Assert.Equal(41, row.SourceSequence);
        Assert.Equal("HA", row.Projects);
        Assert.True(row.RefusesDangerous);
        Assert.Equal("work", row.OrderKind);
        Assert.Equal("HA-2", row.OrderKey);
        Assert.Equal("Nathan", row.OrderedBy);
        Assert.Equal(Now.AddMinutes(-1), row.OrderedAt);
    }

    [Fact]
    public async Task ABareRow_ReadsBackNulls()
    {
        var keyId = Guid.NewGuid();
        using var db = NewContext();
        db.RunnerSessions.Add(new EfHatchRunnerSession
        {
            ApiKeyId = keyId,
            Kind = "once",
            State = "running",
            StartedAt = Now,
            LastSeenAt = Now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = await db.RunnerSessions.SingleAsync(s => s.ApiKeyId == keyId);

        Assert.Null(row.Where);
        Assert.Null(row.Host);
        Assert.Null(row.EndedAt);
        Assert.Null(row.Line);
        Assert.Null(row.LineAt);
        Assert.Null(row.Under);
        Assert.Null(row.MaxRuns);
        Assert.Null(row.MaxSpend);
        Assert.Null(row.MaxTokens);
        Assert.Null(row.MaxTokensPerHour);
        Assert.Null(row.UntilAt);
        Assert.Null(row.Runs);
        Assert.Null(row.Spent);
        Assert.Null(row.TokensUsed);
        Assert.Null(row.TokensLastHour);
        Assert.Null(row.ExhaustedUntil);
        Assert.Null(row.ThrottledUntil);
        Assert.Null(row.Usage);
        Assert.Null(row.UsageReadAt);
        Assert.Null(row.Remotes);
        Assert.Null(row.Clones);
        Assert.Null(row.Mine);
        Assert.Null(row.Standing);
        Assert.Null(row.Agent);
        Assert.Null(row.Revision);
        Assert.Null(row.Sequence);
        Assert.Null(row.BuiltAt);
        Assert.Null(row.SourceRevision);
        Assert.Null(row.SourceSequence);
        Assert.Null(row.Projects);
        Assert.Null(row.RefusesDangerous);
        Assert.Null(row.OrderKind);
        Assert.Null(row.OrderKey);
        Assert.Null(row.OrderedBy);
        Assert.Null(row.OrderedAt);
    }

    private static HatchContext NewContext() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
