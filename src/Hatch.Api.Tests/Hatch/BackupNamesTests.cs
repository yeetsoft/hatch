using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="BackupNames"/> and <see cref="BackupFolder"/> - the file-name
/// rules a backup is written and read back under, and the folder listing
/// built on them. Pure and synchronous, so these touch only a scratch
/// directory and never the database.
/// </summary>
public class BackupNamesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hatch-backup-names-" + Guid.NewGuid().ToString("N"));

    public BackupNamesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Final_ThenTryParseCopy_RoundTripsToTheSecond()
    {
        var at = new DateTimeOffset(2026, 1, 1, 3, 0, 0, 123, TimeSpan.FromHours(3));

        var name = BackupNames.Final(at);

        Assert.Equal("hatch-20260101T000000Z.sql", name);
        Assert.True(BackupNames.TryParseCopy(name, out var parsed));
        Assert.Equal(at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond)), parsed);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }

    [Fact]
    public void Partial_IsFinalPlusPartial_AndTryParsePartialRoundTrips()
    {
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var final = BackupNames.Final(at);
        var partial = BackupNames.Partial(at);

        Assert.Equal(final + ".partial", partial);
        Assert.True(BackupNames.TryParsePartial(partial, out var parsed));
        Assert.Equal(at, parsed);

        Assert.False(BackupNames.TryParseCopy(partial, out _));
        Assert.False(BackupNames.TryParsePartial(final, out _));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("hatch-backup.sql")]
    [InlineData("hatch-20260101T000000Z.sql.partial")]
    [InlineData("Hatch-20260101T000000Z.sql")]
    [InlineData("hatch-99999999T999999Z.sql")]
    [InlineData("hatch-20260101T000000Z.sql\n")]
    [InlineData("hatch-20260101T000000Z.SQL")]
    [InlineData("x-hatch-20260101T000000Z.sql")]
    public void TryParseCopy_NamesThatAreNotHatchs_AreRejected(string name)
    {
        Assert.False(BackupNames.TryParseCopy(name, out _));
    }

    [Fact]
    public void List_NewestFirstByName_EvenWithReversedMtimes()
    {
        var oldest = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var middle = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var newest = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        WriteCopy(oldest, 10);
        WriteCopy(middle, 20);
        WriteCopy(newest, 30);

        File.SetLastWriteTimeUtc(Path.Combine(_dir, BackupNames.Final(oldest)), DateTime.UtcNow);
        File.SetLastWriteTimeUtc(Path.Combine(_dir, BackupNames.Final(middle)), DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, BackupNames.Final(newest)), DateTime.UtcNow.AddMinutes(-20));

        var result = BackupFolder.List(_dir);

        Assert.Null(result.Problem);
        Assert.Equal(
            [BackupNames.Final(newest), BackupNames.Final(middle), BackupNames.Final(oldest)],
            result.Items.Select(c => c.Name));
        Assert.Equal([30, 20, 10], result.Items.Select(c => c.Bytes));
    }

    [Fact]
    public void List_SkipsNonMatchingFiles()
    {
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WriteCopy(at, 5);
        File.WriteAllText(Path.Combine(_dir, BackupNames.Partial(at)), "x");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, BackupNames.Final(at.AddDays(1))));

        var result = BackupFolder.List(_dir);

        var copy = Assert.Single(result.Items);
        Assert.Equal(BackupNames.Final(at), copy.Name);
    }

    [Fact]
    public void List_MissingFolder_ReturnsProblemNamingThePath()
    {
        var missing = Path.Combine(_dir, "does-not-exist");

        var result = BackupFolder.List(missing);

        Assert.Empty(result.Items);
        Assert.Contains(missing, result.Problem);
        Assert.Contains("does not exist", result.Problem);
    }

    [Fact]
    public void List_EmptyFolder_HasNoProblem()
    {
        var result = BackupFolder.List(_dir);

        Assert.Empty(result.Items);
        Assert.Null(result.Problem);
    }

    private void WriteCopy(DateTimeOffset at, int bytes) =>
        File.WriteAllBytes(Path.Combine(_dir, BackupNames.Final(at)), new byte[bytes]);
}
