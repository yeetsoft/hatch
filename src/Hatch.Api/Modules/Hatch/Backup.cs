using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The outcomes a backup attempt is recorded under - see
/// <see cref="BackupAttempt"/>.
/// </summary>
public static class BackupOutcome
{
    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string NotConfigured = "not-configured";
}

/// <summary>
/// One run of the backup process, whatever it did or did not do - see
/// <see cref="BackupStatus"/>.
/// </summary>
public record BackupAttempt(DateTimeOffset At, string Outcome, string? Error, string? File, long? Bytes);

/// <summary>
/// The last backup attempt, held for whoever asks - the status endpoint, and
/// nothing else. A reference swap is the whole of its thread safety: the
/// record it holds is immutable, so nothing ever reads it half-written.
/// </summary>
public class BackupStatus
{
    private BackupAttempt? _last;

    public BackupAttempt? Last => Volatile.Read(ref _last);

    public void Record(BackupAttempt attempt) => Volatile.Write(ref _last, attempt);
}

/// <summary>One finished backup on disk, as <see cref="BackupFolder.List"/> finds it.</summary>
public record BackupCopy(string Name, DateTimeOffset At, long Bytes);

/// <summary>
/// A folder's backups, newest first, or the reason there are none to list.
/// </summary>
public record BackupCopies(IReadOnlyList<BackupCopy> Items, string? Problem);

/// <summary>
/// The one place a backup file's name is built or read back - so a copy on
/// disk and a copy in a listing agree on what counts as Hatch's.
/// </summary>
public static partial class BackupNames
{
    private const string Format = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>The name a finished backup taken at <paramref name="at"/> is written under.</summary>
    public static string Final(DateTimeOffset at) =>
        $"hatch-{at.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture)}.sql";

    /// <summary>The name a backup taken at <paramref name="at"/> is written under while still in progress.</summary>
    public static string Partial(DateTimeOffset at) => Final(at) + ".partial";

    /// <summary>Whether <paramref name="name"/> is a finished backup's name, and the instant it was taken at.</summary>
    public static bool TryParseCopy(string name, out DateTimeOffset at) => TryParse(FinalPattern(), name, out at);

    /// <summary>Whether <paramref name="name"/> is an in-progress backup's name, and the instant it was taken at.</summary>
    public static bool TryParsePartial(string name, out DateTimeOffset at) => TryParse(PartialPattern(), name, out at);

    private static bool TryParse(Regex pattern, string name, out DateTimeOffset at)
    {
        var match = pattern.Match(name);
        if (match.Success &&
            DateTimeOffset.TryParseExact(
                match.Groups[1].Value, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at))
        {
            return true;
        }

        at = default;
        return false;
    }

    // [0-9] and \z rather than \d and $: \d matches non-ASCII digits and $
    // matches before a trailing newline, and the point of this pattern is
    // that only exact names are Hatch's.
    [GeneratedRegex(@"^hatch-([0-9]{8}T[0-9]{6}Z)\.sql\z")]
    private static partial Regex FinalPattern();

    [GeneratedRegex(@"^hatch-([0-9]{8}T[0-9]{6}Z)\.sql\.partial\z")]
    private static partial Regex PartialPattern();
}

/// <summary>
/// Reads what backups exist on disk - the one place that lists them, so the
/// status endpoint and the pruning pass agree on what is there.
/// </summary>
public static class BackupFolder
{
    /// <summary>
    /// The folder's finished backups, newest first by the instant in their
    /// name - never by mtime, which a restore or a copy can leave stale.
    /// Never throws: a folder that does not exist or cannot be read comes
    /// back as no copies and a problem naming why.
    /// </summary>
    public static BackupCopies List(string path)
    {
        if (!Directory.Exists(path))
        {
            return new BackupCopies([], $"{path} does not exist");
        }

        try
        {
            var copies = new List<BackupCopy>();
            foreach (var file in new DirectoryInfo(path).EnumerateFiles())
            {
                if (!BackupNames.TryParseCopy(file.Name, out var at))
                {
                    continue;
                }

                try
                {
                    copies.Add(new BackupCopy(file.Name, at, file.Length));
                }
                catch (IOException)
                {
                    // Vanished mid-listing - a pruning race, not a reason to
                    // fail the whole listing.
                }
            }

            return new BackupCopies(copies.OrderByDescending(c => c.At).ToList(), null);
        }
        catch (Exception ex)
        {
            return new BackupCopies([], $"{path} cannot be read: {ex.Message}");
        }
    }
}

/// <summary>
/// Starts the backup process and hands back its stdout and stderr - the one
/// seam <see cref="BackupService"/> runs <c>pg_dump</c> through, so a test can
/// swap it for a fake that writes no bytes anywhere.
/// </summary>
public interface IBackupProcess
{
    Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct);
}

/// <summary>What to run - see <see cref="IBackupProcess.RunAsync"/>.</summary>
public record BackupProcessStart(string FileName, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env);

/// <summary>How it finished - see <see cref="IBackupProcess.RunAsync"/>.</summary>
public record BackupProcessResult(int ExitCode, string Stderr);

/// <summary>
/// The real <c>pg_dump</c>, run as a child process. Not unit-tested: it is a
/// thin wrapper over <see cref="Process"/>, and the container it runs in has
/// the binary.
/// </summary>
public sealed class SystemBackupProcess : IBackupProcess
{
    public async Task<BackupProcessResult> RunAsync(BackupProcessStart start, Stream stdout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = start.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in start.Args) psi.ArgumentList.Add(arg);
        foreach (var (key, value) in start.Env) psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        process.Start();

        // No token here, same as AgentSessionRunner's stderr reader: it keeps
        // draining to the closed pipe even after Kill below, so a full stderr
        // pipe never deadlocks the process exiting.
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(stdout, ct);
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new BackupProcessResult(process.ExitCode, await stderrTask);
    }
}

/// <summary>
/// Dumps the <c>hatch</c> database to <see cref="HatchOptions.BackupPath"/>,
/// atomically and without ever throwing - every failure becomes a <c>failed</c>
/// <see cref="BackupAttempt"/> recorded in <see cref="BackupStatus"/> instead.
/// After an <c>ok</c> run it prunes: a finished copy goes only when it is past
/// the <see cref="HatchOptions.BackupKeepDays"/> newest and older than that
/// many days, a <c>.partial</c> only when it is over an hour old, and nothing
/// that is not Hatch's own name is ever touched.
/// </summary>
/// <remarks>
/// A <see cref="SemaphoreSlim"/> around the whole body, so the job that runs
/// this now and a controller that may run it later never overlap - two
/// <c>pg_dump</c>s racing the same final name is exactly the collision the
/// same-second check below exists to refuse.
/// </remarks>
public sealed class BackupService
{
    private readonly HatchOptions _options;
    private readonly IConfiguration _configuration;
    private readonly IBackupProcess _process;
    private readonly BackupStatus _status;
    private readonly TimeProvider _clock;
    private readonly ILogger<BackupService> _logger;
    private readonly TimeSpan _timeout;
    private readonly int _keepDays;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(
        IOptions<HatchOptions> options, IConfiguration configuration, IBackupProcess process,
        BackupStatus status, TimeProvider clock, ILogger<BackupService> logger, TimeSpan? timeout = null)
    {
        _options = options.Value;
        _configuration = configuration;
        _process = process;
        _status = status;
        _clock = clock;
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromMinutes(30);

        // Zero is the operator turning pruning off, so it is kept; only a
        // negative value, which nothing could mean, falls back.
        _keepDays = options.Value.BackupKeepDays switch
        {
            0 => 0,
            > 0 => options.Value.BackupKeepDays,
            _ => new HatchOptions().BackupKeepDays,
        };
    }

    /// <summary>
    /// The folder's finished backups - see <see cref="BackupFolder.List"/>.
    /// An empty path answers directly, with no sentence naming a folder the
    /// operator never configured.
    /// </summary>
    public BackupCopies Copies() =>
        string.IsNullOrEmpty(_options.BackupPath) ? new BackupCopies([], null) : BackupFolder.List(_options.BackupPath);

    /// <summary>
    /// Runs one backup attempt, start to finish, and never throws: every
    /// failure, anticipated or not, comes back as a <c>failed</c>
    /// <see cref="BackupAttempt"/> rather than an exception the caller has to
    /// guard against.
    /// </summary>
    public async Task<BackupAttempt> RunAsync(CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return Record(Failed("cancelled"));
        }

        try
        {
            return await RunLockedAsync(ct);
        }
        catch (Exception ex)
        {
            // Nothing below should throw - each anticipated failure records
            // its own sentence - but a method that promises never to throw
            // needs one net under everything, not just under what it saw
            // coming.
            return Record(Failed(ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BackupAttempt> RunLockedAsync(CancellationToken ct)
    {
        var path = _options.BackupPath;
        if (string.IsNullOrEmpty(path))
        {
            return Record(new BackupAttempt(_clock.GetUtcNow(), BackupOutcome.NotConfigured, null, null, null));
        }

        if (!Directory.Exists(path))
        {
            return Record(Failed($"backup folder {path} does not exist"));
        }

        var builder = new NpgsqlConnectionStringBuilder(_configuration.GetConnectionString("Hatch") ?? "");
        if (string.IsNullOrEmpty(builder.Host)) return Record(Failed("backup connection string has no host"));
        if (string.IsNullOrEmpty(builder.Username)) return Record(Failed("backup connection string has no username"));
        if (string.IsNullOrEmpty(builder.Database)) return Record(Failed("backup connection string has no database"));

        var now = _clock.GetUtcNow();
        var finalName = BackupNames.Final(now);
        var finalPath = Path.Combine(path, finalName);
        if (File.Exists(finalPath))
        {
            return Record(Failed($"{finalPath} already exists"));
        }

        var partialPath = Path.Combine(path, BackupNames.Partial(now));

        FileStream file;
        try
        {
            file = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write);
        }
        catch (Exception ex)
        {
            return Record(Failed($"backup folder {path} cannot be written: {ex.Message}"));
        }

        var args = new List<string>
        {
            "--clean", "--if-exists", "--no-password",
            "-h", builder.Host!, "-p", builder.Port.ToString(CultureInfo.InvariantCulture),
            "-U", builder.Username!, builder.Database!,
        };
        var env = new Dictionary<string, string> { ["PGPASSWORD"] = builder.Password ?? "" };
        var start = new BackupProcessStart("pg_dump", args, env);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            BackupProcessResult result;
            try
            {
                result = await _process.RunAsync(start, file, cts.Token);
            }
            finally
            {
                // Closed before anything below touches the file - Windows
                // refuses both a move and a delete on an open handle.
                await file.DisposeAsync();
            }

            if (result.ExitCode != 0)
            {
                DeletePartial(partialPath);
                return Record(Failed(StderrError(result)));
            }

            File.Move(partialPath, finalPath);
            var attempt = Record(new BackupAttempt(now, BackupOutcome.Ok, null, finalName, new FileInfo(finalPath).Length));

            // After the record, and never throwing: a prune problem must not
            // turn a good dump into a failed one.
            Prune(now);
            return attempt;
        }
        catch (OperationCanceledException)
        {
            DeletePartial(partialPath);
            return Record(Failed(ct.IsCancellationRequested ? "cancelled" : $"pg_dump did not finish within {_timeout}"));
        }
        catch (Exception ex)
        {
            DeletePartial(partialPath);
            return Record(Failed(ex.Message));
        }
    }

    /// <summary>
    /// Deletes what an <c>ok</c> run at <paramref name="now"/> makes stale, and
    /// never throws. Candidates come only from <see cref="BackupNames"/>'s
    /// parsers, and age is read from the name's stamp, never the mtime.
    /// </summary>
    private void Prune(DateTimeOffset now)
    {
        var path = _options.BackupPath;

        // Partials go whatever the keep setting says: they are never a copy.
        try
        {
            var cutoff = now.AddHours(-1);
            foreach (var file in new DirectoryInfo(path).EnumerateFiles())
            {
                if (BackupNames.TryParsePartial(file.Name, out var at) && at < cutoff)
                {
                    Delete(path, file.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup pruning of stale partials failed in {Folder}", path);
        }

        if (_keepDays <= 0) return;

        try
        {
            var copies = BackupFolder.List(path);
            if (copies.Problem is not null)
            {
                _logger.LogWarning("Backup pruning skipped: {Problem}", copies.Problem);
                return;
            }

            // Newest first, so an index is the count of newer copies: the
            // floor is the first _keepDays, and age decides only past it.
            var cutoff = now.AddDays(-_keepDays);
            for (var i = _keepDays; i < copies.Items.Count; i++)
            {
                if (copies.Items[i].At < cutoff)
                {
                    Delete(path, copies.Items[i].Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup pruning of old copies failed in {Folder}", path);
        }
    }

    private void Delete(string folder, string name)
    {
        try
        {
            File.Delete(Path.Combine(folder, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Backup pruning could not delete {File} in {Folder}", name, folder);
        }
    }

    private BackupAttempt Record(BackupAttempt attempt)
    {
        _status.Record(attempt);
        return attempt;
    }

    private BackupAttempt Failed(string error) => new(_clock.GetUtcNow(), BackupOutcome.Failed, error, null, null);

    private static void DeletePartial(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort - a failure path touches nothing else, including
            // whether its own cleanup succeeded.
        }
    }

    /// <summary>
    /// The sentence a non-zero exit records: the last five non-empty lines of
    /// stderr, or - when pg_dump wrote nothing there - the bare exit code.
    /// </summary>
    private static string StderrError(BackupProcessResult result)
    {
        var lines = result.Stderr
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(line => line.Length > 0)
            .TakeLast(5)
            .ToList();

        var message = lines.Count > 0 ? string.Join(' ', lines) : $"pg_dump exited with code {result.ExitCode}";
        return message.Length > 500 ? message[..500] : message;
    }
}
