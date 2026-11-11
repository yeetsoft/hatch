namespace Hatch.Cli.Tests;

/// <summary>The file a dead loop leaves for the next one in the checkout.</summary>
public sealed class SessionRecordTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hatch-record-").FullName;

    private string Where => SessionRecord.PathFor(Path.Combine(_temp, "checkout"), _temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static SessionRecord Sample(string? sessionId = "abc") =>
        new(4242, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks, "AER-1",
            Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.UnixEpoch, "https://hatch.example", sessionId);

    [Fact]
    public void A_record_written_reads_back_as_it_was()
    {
        var record = Sample();

        record.Write(Where);

        Assert.Equal(record, SessionRecord.Read(Where));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), record.StartedAt);
    }

    [Fact]
    public void A_record_with_no_session_id_yet_reads_back_with_none()
    {
        Sample(sessionId: null).Write(Where);

        Assert.Null(SessionRecord.Read(Where)!.SessionId);
    }

    [Fact]
    public void A_record_is_deleted_and_deleting_it_again_is_quiet()
    {
        Sample().Write(Where);

        SessionRecord.Delete(Where);
        SessionRecord.Delete(Where);
        SessionRecord.Delete(null);

        Assert.False(File.Exists(Where));
        Assert.Null(SessionRecord.Read(Where));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"pid\": ")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void A_file_that_is_not_a_record_reads_as_none(string content)
    {
        File.WriteAllText(Where, content);

        Assert.Null(SessionRecord.Read(Where));
    }

    [Fact]
    public void A_missing_file_reads_as_none()
    {
        Assert.Null(SessionRecord.Read(Where));
    }

    [Fact]
    public void The_path_is_per_checkout_and_under_the_temp_directory()
    {
        var one = SessionRecord.PathFor("/one", _temp);
        var other = SessionRecord.PathFor("/other", _temp);

        Assert.NotEqual(one, other);
        Assert.Equal(_temp, Path.GetDirectoryName(one));
    }

    [Fact]
    public void A_record_for_another_board_survives_a_loops_own_clean_up_and_ours_does_not()
    {
        Sample().Write(Where);

        SessionRecord.DeleteOurs(Where, "https://elsewhere.example");
        Assert.NotNull(SessionRecord.Read(Where));

        SessionRecord.DeleteOurs(Where, "https://hatch.example/");
        Assert.Null(SessionRecord.Read(Where));
    }
}
