using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>One board's record as found on disk, and when it was last written or touched.</summary>
/// <param name="Record">What it says, or null where it cannot be read.</param>
/// <param name="WrittenAt">Its write time: set when a build is recorded, and again at each child start.</param>
public sealed record StoredBoard(BoardRecord? Record, DateTimeOffset WrittenAt);

/// <summary>What one sweep did: folders it removed, and old builds it had to leave for next time.</summary>
public readonly record struct SweepResult(int Removed, int Failed);

/// <summary>
/// Removing the builds a machine can no longer need, and knowing which ones it
/// still can: the current and previous build of each board that has run lately,
/// and any build something is running.
/// </summary>
public sealed partial class BuildStore
{
    /// <summary>How long a board may go without a loop on this machine before its builds stop counting.</summary>
    public static readonly TimeSpan BoardIdle = TimeSpan.FromDays(30);

    /// <summary>How old a download that was never finished must be before it is removed.</summary>
    public static readonly TimeSpan StaleDownload = TimeSpan.FromDays(1);

    [GeneratedRegex("^(?<sha>[0-9a-f]{40})\\.in-use-[0-9a-f]{32}$")]
    private static partial Regex LeasePattern();

    // Taken from the path rather than compared to this store's own folder, so a
    // root reached through a link still protects what is in it.
    [GeneratedRegex("(?:^|[/\\\\])versions[/\\\\](?<sha>[0-9a-f]{40})[/\\\\]")]
    private static partial Regex BuildFolderInPath();

    /// <summary>
    /// Says a launcher runs, or is about to run, <paramref name="sha"/>, for as
    /// long as the result is held: an open file the platform will not let a
    /// second process open (a lock on Windows, a <c>flock</c> on macOS and
    /// Linux), so a sweep in another launcher can tell it is in use. A launcher
    /// that dies drops it with its handle; the sweep then deletes the file.
    /// Never throws: a lease that cannot be made is an empty one.
    /// </summary>
    public IDisposable Hold(string sha)
    {
        if (!ShaPattern().IsMatch(sha)) return Lease.None;

        try
        {
            Directory.CreateDirectory(VersionsDirectory);
            var path = Path.Combine(VersionsDirectory, $"{sha}.in-use-{Guid.NewGuid():N}");
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return new Lease(stream, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Lease.None;
        }
    }

    /// <summary>
    /// Sets the write time of this board's record, if it has one, so a board a
    /// loop ran against today does not read as idle. A record is otherwise
    /// written only on a change of build.
    /// </summary>
    public void Touch(string origin, DateTimeOffset? now = null)
    {
        try
        {
            var path = BoardPath(origin);
            if (File.Exists(path)) File.SetLastWriteTimeUtc(path, (now ?? DateTimeOffset.UtcNow).UtcDateTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Every board's record with its write time. Null where the folder cannot
    /// be listed at all; none where there is no folder.
    /// </summary>
    public IReadOnlyList<StoredBoard>? ReadAll()
    {
        try
        {
            if (!Directory.Exists(BoardsDirectory)) return [];

            var all = new List<StoredBoard>();
            foreach (var file in Directory.EnumerateFiles(BoardsDirectory, "*.json"))
            {
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                BoardRecord? record = null;

                try
                {
                    record = JsonSerializer.Deserialize(File.ReadAllText(file), HatchJson.Default.BoardRecord);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }

                all.Add(new StoredBoard(record, written));
            }

            return all;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes what this machine can no longer need from <c>versions/</c>, and
    /// nothing else. Never throws.
    /// </summary>
    /// <param name="now">What a record's and a download's age is measured against.</param>
    /// <param name="runningPaths">The files processes on this machine are running, or null where that could not be found out - then no build is removed, only counted as left.</param>
    /// <param name="ownPath">The launcher's own file, whose folder is never removed if it is in here.</param>
    /// <param name="remove">Deletes a folder; a test passes one that fails.</param>
    /// <remarks>
    /// A build goes by being renamed to <c>.old-&lt;guid&gt;</c> first, which is
    /// atomic - <see cref="Find"/> answers null at once - and then deleted.
    /// Windows would let the rename happen under a running file, so there each
    /// file is first opened for writing, which a running one refuses. Either
    /// step failing leaves the folder for the next sweep; a leftover
    /// <c>.old-*</c> is always tried again.
    /// </remarks>
    public SweepResult Sweep(
        DateTimeOffset now, IReadOnlyCollection<string>? runningPaths, string? ownPath = null,
        Action<string>? remove = null)
    {
        var removed = 0;
        var failed = 0;

        try
        {
            if (!Directory.Exists(VersionsDirectory)) return default;

            var delete = remove ?? (path => Directory.Delete(path, recursive: true));
            var keep = new HashSet<string>(StringComparer.Ordinal);

            // A record that cannot be read may be naming a build: say nothing
            // is safe to remove, rather than guess.
            var boards = ReadAll();
            var canTell = boards is not null && runningPaths is not null;

            foreach (var board in boards ?? [])
            {
                if (now - board.WrittenAt >= BoardIdle) continue;
                if (board.Record is null) { canTell = false; continue; }

                if (board.Record.Current is { } current) keep.Add(current);
                if (board.Record.Previous is { } previous) keep.Add(previous);
            }

            foreach (var path in (runningPaths ?? []).Append(ownPath ?? ""))
                if (BuildFolderInPath().Match(path) is { Success: true } m)
                    keep.Add(m.Groups["sha"].Value);

            var entries = new DirectoryInfo(VersionsDirectory).EnumerateFileSystemInfos().ToList();

            foreach (var entry in entries)
            {
                if (entry is FileInfo && LeasePattern().Match(entry.Name) is { Success: true } lease && !IsLink(entry))
                {
                    if (IsHeld(entry.FullName)) keep.Add(lease.Groups["sha"].Value);
                    else TryDeleteFile(entry.FullName);
                }
            }

            foreach (var entry in entries)
            {
                if (entry is not DirectoryInfo folder || IsLink(entry)) continue;

                var name = folder.Name;

                if (ShaPattern().IsMatch(name))
                {
                    if (keep.Contains(name)) continue;

                    if (!canTell)
                    {
                        failed++;
                        continue;
                    }

                    // Windows lets a folder be renamed under a running file, but then
                    // the next call to it finds nothing: ask the files themselves.
                    if (OperatingSystem.IsWindows() && IsOpen(folder))
                    {
                        failed++;
                        continue;
                    }

                    var aside = Path.Combine(VersionsDirectory, $".old-{Guid.NewGuid():N}");

                    try { Directory.Move(folder.FullName, aside); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        continue;
                    }

                    if (TryDelete(delete, aside)) removed++; else failed++;
                }
                else if (name.StartsWith(".old-", StringComparison.Ordinal))
                {
                    if (TryDelete(delete, folder.FullName)) removed++; else failed++;
                }
                else if (name.StartsWith(".tmp-", StringComparison.Ordinal)
                    && now - new DateTimeOffset(folder.LastWriteTimeUtc, TimeSpan.Zero) >= StaleDownload)
                {
                    if (TryDelete(delete, folder.FullName)) removed++; else failed++;
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Whatever it could not do is left for the next sweep.
        }

        return new SweepResult(removed, failed);
    }

    private static bool IsLink(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || entry.LinkTarget is not null;

    private static bool TryDelete(Action<string> delete, string path)
    {
        try
        {
            delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Whether anything in this folder is open or running, as Windows says it:
    /// a running image refuses a second open for writing. Counted as open where
    /// the folder cannot be read at all.
    /// </summary>
    private static bool IsOpen(DirectoryInfo folder)
    {
        try
        {
            foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    using var probe = new FileStream(file.FullName, FileMode.Open, FileAccess.Write, FileShare.None);
                }
                catch (FileNotFoundException) { }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Whether a live process holds this lease: a second open that shares nothing is refused.</summary>
    private static bool IsHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private sealed class Lease(FileStream? stream, string path) : IDisposable
    {
        public static readonly Lease None = new(null, "");

        private int disposed;

        public void Dispose()
        {
            if (stream is null || Interlocked.Exchange(ref disposed, 1) != 0) return;

            try { stream.Dispose(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            TryDeleteFile(path);
        }
    }
}
