namespace Hatch.Cli.Tests;

/// <summary>
/// <c>BuildStore</c> - installs a downloaded build at <c>versions/&lt;sha&gt;</c>
/// so that only one that passed every check is ever at that path, however many
/// installers race to put the same sha down at once.
/// </summary>
public sealed class BuildStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hatch-buildstore-").FullName;
    private readonly string _sha = new('a', 40);
    private readonly string _otherSha = new('b', 40);

    private BuildStore Store() => new(_root);

    private static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static Func<string, Task<string?>> Writes(byte[] bytes) =>
        async path => { await File.WriteAllBytesAsync(path, bytes); return null; };

    private static readonly Func<string, Task<string?>> PassCheck = _ => Task.FromResult<string?>(null);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Install_puts_the_binary_at_versions_sha()
    {
        var result = await Store().InstallAsync(_sha, Writes(Bytes("a build")), PassCheck, default);

        Assert.Null(result.Failure);
        var expected = Path.Combine(_root, "versions", _sha, BuildStore.BinaryName);
        Assert.Equal(expected, result.Path);
        Assert.Equal("a build", File.ReadAllText(expected));
    }

    [Fact]
    public async Task A_second_install_finds_it_and_never_calls_fetch()
    {
        var store = Store();
        await store.InstallAsync(_sha, Writes(Bytes("a build")), PassCheck, default);

        var called = false;
        Func<string, Task<string?>> fetch = _ => { called = true; return Task.FromResult<string?>(null); };

        var result = await store.InstallAsync(_sha, fetch, PassCheck, default);

        Assert.False(called);
        Assert.Equal(Path.Combine(_root, "versions", _sha, BuildStore.BinaryName), result.Path);
    }

    [Fact]
    public async Task A_fetch_failure_leaves_nothing_under_versions()
    {
        Func<string, Task<string?>> fetch = _ => Task.FromResult<string?>("could not reach the board");

        var result = await Store().InstallAsync(_sha, fetch, PassCheck, default);

        Assert.Equal("could not reach the board", result.Failure);
        Assert.Null(result.Path);
        AssertNothingUnderVersions();
    }

    [Fact]
    public async Task A_check_failure_leaves_nothing_under_versions()
    {
        Func<string, Task<string?>> check = _ => Task.FromResult<string?>("the new build did not report its revision");

        var result = await Store().InstallAsync(_sha, Writes(Bytes("a build")), check, default);

        Assert.Equal("the new build did not report its revision", result.Failure);
        Assert.Null(result.Path);
        AssertNothingUnderVersions();
    }

    [Fact]
    public async Task A_fetch_that_throws_leaves_nothing_under_versions_and_is_never_found()
    {
        Func<string, Task<string?>> fetch = async path =>
        {
            await File.WriteAllBytesAsync(path, Bytes("half"));
            throw new InvalidOperationException("the connection dropped");
        };

        var result = await Store().InstallAsync(_sha, fetch, PassCheck, default);

        Assert.NotNull(result.Failure);
        Assert.Null(result.Path);
        Assert.Null(Store().Find(_sha));
        AssertNothingUnderVersions();
    }

    [Fact]
    public async Task A_folder_with_no_binary_is_not_found()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", _sha));

        Assert.Null(Store().Find(_sha));
    }

    [Fact]
    public async Task An_invalid_sha_is_refused_and_touches_nothing()
    {
        var result = await Store().InstallAsync("not-a-sha", Writes(Bytes("x")), PassCheck, default);

        Assert.NotNull(result.Failure);
        Assert.Null(result.Path);
        Assert.False(Directory.Exists(Path.Combine(_root, "versions")));
    }

    /// <summary>
    /// Two installs of one sha, both fetches held until both are inside, so
    /// the race is real rather than one finishing before the other starts.
    /// Both answer the same path, one <c>versions/&lt;sha&gt;</c> exists with
    /// the right bytes, and no <c>.tmp-*</c> is left behind.
    /// </summary>
    [Fact]
    public async Task Two_installs_of_one_sha_race_to_the_same_path()
    {
        var store = Store();
        var bytes = Bytes("a build");
        using var barrier = new Barrier(2);

        Func<string, Task<string?>> fetch = async path =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await File.WriteAllBytesAsync(path, bytes);
            return null;
        };

        var first = store.InstallAsync(_sha, fetch, PassCheck, default);
        var second = store.InstallAsync(_sha, fetch, PassCheck, default);

        var results = await Task.WhenAll(first, second);

        var expected = Path.Combine(_root, "versions", _sha, BuildStore.BinaryName);
        Assert.All(results, r => Assert.Equal(expected, r.Path));
        Assert.All(results, r => Assert.Null(r.Failure));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(expected));

        var leftovers = Directory.GetDirectories(Path.Combine(_root, "versions"))
            .Where(d => Path.GetFileName(d).StartsWith(".tmp-", StringComparison.Ordinal));
        Assert.Empty(leftovers);
    }

    [Fact]
    public void Two_boards_records_leave_each_file_as_written()
    {
        var store = Store();

        store.Record("https://hatch.example.com", _sha);
        store.Record("https://hatch.other.com", _otherSha);

        Assert.Equal(_sha, store.Read("https://hatch.example.com")!.Current);
        Assert.Equal(_otherSha, store.Read("https://hatch.other.com")!.Current);
    }

    [Fact]
    public void Record_moves_current_to_previous_only_when_the_sha_differs()
    {
        var store = Store();

        store.Record("https://hatch.example.com", _sha);
        store.Record("https://hatch.example.com", _sha);
        var same = store.Read("https://hatch.example.com")!;
        Assert.Equal(_sha, same.Current);
        Assert.Null(same.Previous);

        store.Record("https://hatch.example.com", _otherSha);
        var moved = store.Read("https://hatch.example.com")!;
        Assert.Equal(_otherSha, moved.Current);
        Assert.Equal(_sha, moved.Previous);
    }

    [Fact]
    public void A_garbled_record_reads_as_empty()
    {
        var store = Store();
        store.Record("https://hatch.example.com", _sha);

        var path = Directory.GetFiles(Path.Combine(_root, "boards")).Single();
        File.WriteAllText(path, "not json at all");

        Assert.Null(store.Read("https://hatch.example.com"));
    }

    [Fact]
    public void A_missing_record_reads_as_empty()
    {
        Assert.Null(Store().Read("https://hatch.example.com"));
    }

    private void AssertNothingUnderVersions()
    {
        var versions = Path.Combine(_root, "versions");
        if (!Directory.Exists(versions)) return;

        Assert.Empty(Directory.GetDirectories(versions));
    }

    // ---- Newest ----

    private string Keep(string name, bool binary = true, DateTime? written = null)
    {
        var directory = Path.Combine(_root, "versions", name);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, BuildStore.BinaryName);
        if (binary)
        {
            File.WriteAllText(file, name);
            if (written is { } at) File.SetLastWriteTimeUtc(file, at);
        }

        return file;
    }

    [Fact]
    public void Newest_is_null_where_nothing_is_kept_or_there_is_no_versions_folder()
    {
        Assert.Null(Store().Newest());

        Directory.CreateDirectory(Path.Combine(_root, "versions"));
        Assert.Null(Store().Newest());
    }

    [Fact]
    public void Newest_is_the_only_build_kept()
    {
        var kept = Keep(_sha);

        Assert.Equal(kept, Store().Newest());
    }

    [Fact]
    public void Newest_is_the_build_whose_binary_was_written_last_whatever_its_name()
    {
        var early = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        Keep(_otherSha, written: early);
        var later = Keep(_sha, written: early.AddDays(3));

        Assert.Equal(later, Store().Newest());
    }

    [Fact]
    public void A_tie_goes_to_the_lower_sha()
    {
        var at = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var lower = Keep(_sha, written: at);
        Keep(_otherSha, written: at);

        Assert.Equal(lower, Store().Newest());
    }

    [Fact]
    public void A_folder_with_no_binary_and_a_folder_that_is_not_a_sha_are_not_builds()
    {
        var at = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var kept = Keep(_sha, written: at);
        Keep(_otherSha, binary: false);
        Keep("not-a-sha", written: at.AddDays(9));
        Keep(".tmp-" + Guid.NewGuid(), written: at.AddDays(9));

        Assert.Equal(kept, Store().Newest());
    }
}
