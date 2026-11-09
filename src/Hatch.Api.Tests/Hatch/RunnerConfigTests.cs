using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The board's half of a runner: a row keyed by an API key's id, related to
/// nothing, that reads back what was written and an empty tag array when no
/// tag was.
/// </summary>
public class RunnerConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ARow_SavesAndReadsBack()
    {
        var keyId = Guid.NewGuid();
        var until = Now.AddHours(8);
        using var db = NewContext();
        db.RunnerConfigs.Add(new EfHatchRunnerConfig
        {
            ApiKeyId = keyId,
            Tags = "[\"gpu\",\"mac\"]",
            Under = "HA-1",
            MaxRuns = 5,
            MaxSpend = 12.34m,
            MaxTokens = 3_000_000_000L,
            MaxTokensPerHour = 400_000L,
            UntilAt = until,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = await db.RunnerConfigs.SingleAsync(c => c.ApiKeyId == keyId);

        Assert.Equal("[\"gpu\",\"mac\"]", row.Tags);
        Assert.Equal("HA-1", row.Under);
        Assert.Equal(5, row.MaxRuns);
        Assert.Equal(12.34m, row.MaxSpend);
        Assert.Equal(3_000_000_000L, row.MaxTokens);
        Assert.Equal(400_000L, row.MaxTokensPerHour);
        Assert.Equal(until, row.UntilAt);
        Assert.Equal(Now, row.UpdatedAt);
    }

    [Fact]
    public async Task ATagsLessRow_ReadsBackEmptyArray()
    {
        var keyId = Guid.NewGuid();
        using var db = NewContext();
        db.RunnerConfigs.Add(new EfHatchRunnerConfig { ApiKeyId = keyId, UpdatedAt = Now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = await db.RunnerConfigs.SingleAsync(c => c.ApiKeyId == keyId);

        Assert.Equal("[]", row.Tags);
        Assert.Null(row.Under);
        Assert.Null(row.MaxRuns);
        Assert.Null(row.MaxSpend);
        Assert.Null(row.MaxTokens);
        Assert.Null(row.MaxTokensPerHour);
        Assert.Null(row.UntilAt);
    }

    [Fact]
    public void TheKeyIsNotGeneratedAndPointsAtNothing()
    {
        using var db = NewContext();

        var type = db.Model.FindEntityType(typeof(EfHatchRunnerConfig))!;
        var tags = type.FindProperty(nameof(EfHatchRunnerConfig.Tags))!;

        Assert.Empty(type.GetForeignKeys());
        Assert.Equal(ValueGenerated.Never, type.FindProperty(nameof(EfHatchRunnerConfig.ApiKeyId))!.ValueGenerated);
        Assert.Equal("jsonb", tags.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value);
        Assert.Equal("[]", tags.GetDefaultValue());
    }

    private static HatchContext NewContext() =>
        new(new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
