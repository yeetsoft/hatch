using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>What one install answered with: the binary's own path, or a failure sentence.</summary>
public sealed record InstallResult(string? Path, string? Failure);

/// <summary>
/// One board's own pointer: the current build this installation is on, and the
/// one before it.
/// </summary>
public sealed record BoardRecord(string Origin, string? Current, string? Previous);

/// <summary>
/// Builds a launcher has downloaded, kept side by side so a restart can fall
/// back to the one before, and never half-installed - however many installers
/// race to put the same sha down at once.
/// </summary>
/// <remarks>
/// <paramref name="root"/> is a parameter rather than always
/// <see cref="Settings.UserFolder"/> so a test can point it at a temp
/// directory - the real one is the only caller that reaches for that default.
/// </remarks>
public sealed partial class BuildStore(string root)
{
    /// <summary>What a build is named on disk, platform to platform.</summary>
    public static string BinaryName => OperatingSystem.IsWindows() ? "hatch.exe" : "hatch";

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex ShaPattern();

    private string VersionsDirectory => Path.Combine(root, "versions");

    private string BoardsDirectory => Path.Combine(root, "boards");

    /// <summary>The installed binary's path, or null - an empty or missing folder is not a build.</summary>
    public string? Find(string sha)
    {
        if (!ShaPattern().IsMatch(sha)) return null;

        var path = Path.Combine(VersionsDirectory, sha, BinaryName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The kept build whose binary was written last, or null where none is kept.
    /// Ties go to the lower sha, so the answer never depends on the order a
    /// directory is listed in. Reads only, and never throws on an IO error.
    /// </summary>
    public string? Newest()
    {
        try
        {
            if (!Directory.Exists(VersionsDirectory)) return null;

            string? best = null;
            var bestAt = DateTime.MinValue;

            foreach (var directory in Directory.EnumerateDirectories(VersionsDirectory))
            {
                var sha = Path.GetFileName(directory);
                if (Find(sha) is not { } binary) continue;

                var at = File.GetLastWriteTimeUtc(binary);
                if (best is null || at > bestAt || (at == bestAt && string.CompareOrdinal(sha, best) < 0))
                {
                    best = sha;
                    bestAt = at;
                }
            }

            return best is null ? null : Find(best);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The sha every board's record names as current, once each, in order. A
    /// record that cannot be read is left out. Reads only, and never throws on
    /// an IO error.
    /// </summary>
    public IReadOnlyList<string> Currents()
    {
        try
        {
            if (!Directory.Exists(BoardsDirectory)) return [];

            var shas = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(BoardsDirectory, "*.json"))
            {
                try
                {
                    var record = JsonSerializer.Deserialize(File.ReadAllText(file), HatchJson.Default.BoardRecord);
                    if (record?.Current is { } sha && ShaPattern().IsMatch(sha)) shas.Add(sha);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            }

            return [.. shas];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Already there, or fetched, checked and moved into place - the move is
    /// what makes the result atomic: nothing ever sees a directory under
    /// <c>versions/&lt;sha&gt;</c> that was not fetched and checked whole.
    /// </summary>
    /// <remarks>
    /// <paramref name="fetch"/> and <paramref name="check"/> each answer null
    /// on success or a failure sentence - never by throwing, though a throw
    /// from either is still caught and turned into one, same as a throw from
    /// the move itself. Only the caller's own <paramref name="ct"/> propagates,
    /// and only after the temp folder this call made is removed.
    /// </remarks>
    public async Task<InstallResult> InstallAsync(
        string sha, Func<string, Task<string?>> fetch, Func<string, Task<string?>> check, CancellationToken ct)
    {
        if (!ShaPattern().IsMatch(sha))
            return new InstallResult(null, $"""hatch: "{sha}" is not a sha - refusing to install""");

        if (Find(sha) is { } already) return new InstallResult(already, null);

        var temp = Path.Combine(VersionsDirectory, $".tmp-{Guid.NewGuid()}");
        var final = Path.Combine(VersionsDirectory, sha);

        try
        {
            Directory.CreateDirectory(temp);
            var tempBinary = Path.Combine(temp, BinaryName);

            if (await fetch(tempBinary) is { } fetchFailure) return new InstallResult(null, fetchFailure);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    tempBinary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            if (await check(tempBinary) is { } checkFailure) return new InstallResult(null, checkFailure);

            return await MoveIntoPlaceAsync(sha, temp, final, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return new InstallResult(null, $"hatch: installing {sha[..8]} - {e.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Renames the temp folder onto <paramref name="final"/>, which is what
    /// makes the whole install atomic. However many installers reach here for
    /// the same sha at once, exactly one rename succeeds; every other one
    /// finds <see cref="Find"/> now answers the winner's path, and uses that
    /// instead of its own. A folder in the way with no binary in it - a prior
    /// install that lost a lock mid-move - is left exactly as it was: there is
    /// no safe way to tell that apart from one still being written.
    /// </summary>
    /// <remarks>
    /// Retried five times, 200ms apart, because on Windows a scanner can hold
    /// the folder open for a moment right after a binary is written to it,
    /// which is a lock that clears on its own rather than a real collision.
    /// </remarks>
    private async Task<InstallResult> MoveIntoPlaceAsync(string sha, string temp, string final, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(temp, final);
                return new InstallResult(Path.Combine(final, BinaryName), null);
            }
            catch (IOException e)
            {
                if (Find(sha) is { } found) return new InstallResult(found, null);
                if (attempt >= 5)
                    return new InstallResult(null, $"hatch: installing {sha[..8]} - {e.Message}");

                await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
            }
        }
    }

    /// <summary>This board's own record, or null where none was ever written or it cannot be read back.</summary>
    public BoardRecord? Read(string origin)
    {
        try
        {
            var path = BoardPath(origin);
            if (!File.Exists(path)) return null;

            return JsonSerializer.Deserialize(File.ReadAllText(path), HatchJson.Default.BoardRecord);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="sha"/> as this board's current build, moving
    /// whatever was current into <c>previous</c> - only when it is a
    /// different sha, so installing the one already current does not push a
    /// real previous build out of the record. False on an IO error; nothing
    /// else about the record changes then.
    /// </summary>
    public bool Record(string origin, string sha)
    {
        try
        {
            var existing = Read(origin);
            var next = existing?.Current == sha
                ? existing
                : new BoardRecord(origin, sha, existing?.Current);

            AtomicFile.WriteText(BoardPath(origin), JsonSerializer.Serialize(next, HatchJson.Default.BoardRecord));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string BoardPath(string origin) => Path.Combine(BoardsDirectory, $"{BoardId(origin)}.json");

    /// <summary>The first 16 hex of a digest over the origin, lower-cased with no trailing slash.</summary>
    private static string BoardId(string origin)
    {
        var canonical = origin.Trim().TrimEnd('/').ToLowerInvariant();
        var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(digest)[..16];
    }
}
