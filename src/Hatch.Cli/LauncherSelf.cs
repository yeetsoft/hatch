using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Hatch.Cli;

/// <summary>Takes over this process's place with another program, where the platform can.</summary>
public interface IExec
{
    /// <summary>
    /// Runs <paramref name="path"/> in this process's place, with these
    /// arguments and the current environment plus <paramref name="added"/>.
    /// A real one never returns true: the process that called it is gone. It
    /// returns false when it could not, with <paramref name="failure"/> saying why.
    /// A test stands in for the far side by returning true.
    /// </summary>
    bool Replace(string path, string[] args, IReadOnlyDictionary<string, string> added, out string? failure);
}

/// <summary>The real thing: <c>execve</c> on Linux and macOS, never on Windows.</summary>
public sealed class PosixExec : IExec
{
    public bool Replace(string path, string[] args, IReadOnlyDictionary<string, string> added, out string? failure)
    {
        failure = null;

        if (OperatingSystem.IsWindows())
        {
            failure = "this platform cannot replace a process";
            return false;
        }

        var environment = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string name && entry.Value is string value) environment[name] = value;
        foreach (var (name, value) in added) environment[name] = value;

        var argv = new List<string> { path };
        argv.AddRange(args);
        var envp = environment.Select(e => $"{e.Key}={e.Value}").ToList();

        var allocated = new List<nint>();
        try
        {
            var argvPointers = Pointers(argv, allocated);
            var envpPointers = Pointers(envp, allocated);

            // Only returns when it failed.
            execve(path, argvPointers, envpPointers);
            failure = $"execve said {new Win32Exception(Marshal.GetLastPInvokeError()).Message}";
            return false;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            failure = e.Message;
            return false;
        }
        finally
        {
            foreach (var pointer in allocated) Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>The strings as a null-terminated array of C strings.</summary>
    private static nint[] Pointers(List<string> strings, List<nint> allocated)
    {
        var pointers = new nint[strings.Count + 1];
        for (var i = 0; i < strings.Count; i++)
        {
            pointers[i] = Marshal.StringToCoTaskMemUTF8(strings[i]);
            allocated.Add(pointers[i]);
        }

        return pointers;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int execve(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint[] argv, nint[] envp);
}

/// <summary>
/// What a launcher leaves for the process that takes its place at a change of
/// build, so the night goes on exactly as it was: the program on trial, the
/// one to fall back to, the holds, and what has failed how often. The night's
/// state file and the state to put back stay where they are, in the directory.
/// </summary>
public sealed record LauncherResume(
    string Program, string? ProgramSha, string ConfirmedProgram, string? ConfirmedSha, string? Trial,
    string? TrialOrigin, string? Hold, string? Failed, bool HasAside)
{
    /// <summary>The file in the night's directory the record is written to.</summary>
    public const string FileName = "resume";

    private const string AsideDirectory = "aside";

    /// <summary>The counts of failed asks as <c>&lt;sha&gt;=&lt;times&gt;</c> pairs, comma separated, or null where there are none.</summary>
    public static string? FormatFailed(IReadOnlyDictionary<string, int> failed) =>
        failed.Count == 0
            ? null
            : string.Join(',', failed.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => f.Key + "=" + f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Reads what <see cref="FormatFailed"/> wrote; anything it cannot read is left out.</summary>
    public Dictionary<string, int> ParseFailed()
    {
        var failed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in (Failed ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = pair.IndexOf('=');
            if (at > 0 && int.TryParse(
                    pair[(at + 1)..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var times))
                failed[pair[..at]] = times;
        }

        return failed;
    }

    /// <summary>
    /// Writes the record, and the state to put back beside it. False where
    /// either could not be written, with <paramref name="failure"/> saying why;
    /// nothing is left behind then.
    /// </summary>
    /// <remarks>
    /// One <c>name=value</c> line a field, with no value allowed to hold a line
    /// break. Not JSON: this is the launcher's own note to itself, and nothing
    /// that serialises it has to survive being trimmed.
    /// </remarks>
    public static bool Write(
        string directory, LauncherResume resume, Dictionary<string, byte[]>? aside, out string? failure)
    {
        failure = null;

        try
        {
            var fields = new (string Name, string? Value)[]
            {
                ("program", resume.Program), ("programSha", resume.ProgramSha),
                ("confirmedProgram", resume.ConfirmedProgram), ("confirmedSha", resume.ConfirmedSha),
                ("trial", resume.Trial), ("trialOrigin", resume.TrialOrigin), ("hold", resume.Hold),
                ("failed", resume.Failed), ("aside", aside is null ? "no" : "yes"),
            };

            if (fields.Any(f => f.Value is { } v && v.AsSpan().ContainsAny("\r\n")))
            {
                failure = "the night's state could not be left for it: a name in it holds a line break";
                return false;
            }

            if (aside is not null)
            {
                var folder = Path.Combine(directory, AsideDirectory);
                Directory.CreateDirectory(folder);
                foreach (var (name, bytes) in aside) File.WriteAllBytes(Path.Combine(folder, name), bytes);
            }

            AtomicFile.WriteText(
                Path.Combine(directory, FileName),
                string.Concat(fields.Where(f => f.Value is not null).Select(f => $"{f.Name}={f.Value}\n")));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure = $"the night's state could not be left for it: {e.Message}";
            Remove(directory);
            return false;
        }
    }

    /// <summary>
    /// The record in <paramref name="directory"/>, or null where there is none,
    /// it is garbled, or the directory is not one a launcher made.
    /// </summary>
    public static LauncherResume? Read(string directory)
    {
        try
        {
            if (!Path.GetFileName(directory.TrimEnd('/', '\\')).StartsWith("hatch-night-", StringComparison.Ordinal))
                return null;

            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return null;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                var at = line.IndexOf('=');
                if (at > 0) fields[line[..at]] = line[(at + 1)..];
            }

            string? Get(string name) => fields.GetValueOrDefault(name) is { Length: > 0 } value ? value : null;

            return Get("program") is { } program && Get("confirmedProgram") is { } confirmed
                ? new LauncherResume(
                    program, Get("programSha"), confirmed, Get("confirmedSha"), Get("trial"), Get("trialOrigin"),
                    Get("hold"), Get("failed"), Get("aside") == "yes")
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The state that was set aside, or null where none was.</summary>
    public Dictionary<string, byte[]>? ReadAside(string directory)
    {
        if (!HasAside) return null;

        try
        {
            var kept = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(Path.Combine(directory, AsideDirectory)))
                kept[Path.GetFileName(file)] = File.ReadAllBytes(file);
            return kept;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the record and what was set aside beside it, so nothing reads them twice.</summary>
    public static void Remove(string directory)
    {
        try { File.Delete(Path.Combine(directory, FileName)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        try { Directory.Delete(Path.Combine(directory, AsideDirectory), recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// A launcher replacing its own file with a newer build it has already stored
/// and checked, so a person downloads the runner once.
/// </summary>
/// <remarks>
/// <para>Newer means a higher sequence, read by running the stored build's
/// <c>version</c>. A launcher only moves forward: a board that rolls back moves
/// the child and leaves this file alone.</para>
///
/// <para>The new file is written beside the old one, because a rename is atomic
/// only inside one filesystem, and run from there before the old file is given
/// up. A file that is a link is followed, and the file it points at replaced.
/// Where the platform will not overwrite a running file (<see cref="RenameAside"/>)
/// the old one is renamed aside first and put back if the swap then fails. A
/// start sweeps what an earlier one left aside.</para>
///
/// <para>Nothing in here throws at its caller and nothing ends a night: a
/// failure is one line, said once per start, and the launcher carries on as it
/// was.</para>
/// </remarks>
public sealed class SelfUpdate
{
    /// <summary>Set to 1 by the launcher on the process it starts or becomes in its own place, so that one does not look for a newer build again. Set by the launcher and by no person.</summary>
    public const string HandoffVariable = "HATCH_LAUNCHER_HANDOFF";

    /// <summary>The night's directory, set by a launcher that took over from another at a change of build. Set by the launcher and by no person.</summary>
    public const string ResumeVariable = "HATCH_LAUNCHER_RESUME";

    private const string TempPrefix = ".hatch-new-";
    private const string AsideInfix = ".old-";
    private static readonly TimeSpan StaleTemp = TimeSpan.FromHours(1);

    private readonly Action<string> say;
    private int refused;

    /// <param name="ownPath">The file this launcher runs from, with any link followed.</param>
    /// <param name="ownRevision">The sha this launcher was built from.</param>
    /// <param name="ownSequence">This launcher's own commit count.</param>
    /// <param name="store">Where builds are kept.</param>
    /// <param name="readVersion">Runs a build's <c>version</c>; null where it would not answer.</param>
    /// <param name="exec">Takes this process's place with the new file.</param>
    /// <param name="renameAside">Whether the old file must be renamed away before the new one is put down: where a running file cannot be overwritten, which is Windows.</param>
    /// <param name="say">The line the person sees.</param>
    /// <param name="handoff">This process was started by a launcher updating itself, so it does not look again.</param>
    /// <param name="resume">The directory of a night this process takes over, or null.</param>
    public SelfUpdate(
        string ownPath, string ownRevision, int ownSequence, BuildStore store,
        Func<string, Task<VersionReport?>> readVersion, IExec exec, bool renameAside, Action<string> say,
        bool handoff = false, string? resume = null)
    {
        OwnPath = ownPath;
        OwnRevision = ownRevision;
        OwnSequence = ownSequence;
        Store = store;
        ReadVersion = readVersion;
        Exec = exec;
        RenameAside = renameAside;
        this.say = say;
        Handoff = handoff;
        Resume = resume;
    }

    public string OwnPath { get; }

    public string OwnRevision { get; }

    public int OwnSequence { get; }

    public BuildStore Store { get; }

    public Func<string, Task<VersionReport?>> ReadVersion { get; }

    public IExec Exec { get; }

    public bool RenameAside { get; }

    public bool Handoff { get; }

    public string? Resume { get; }

    /// <summary>
    /// The real one, or null where a launcher must not replace itself: a build
    /// nobody stamped (which is also a development build, and the container
    /// image's), or one that does not know where its own file is.
    /// </summary>
    /// <remarks>
    /// <c>scripts/hatch.sh</c>, <c>scripts/hatch.ps1</c> and a file named by
    /// <c>HATCH_RUNNER_BIN</c> never get here at all: they set the night's
    /// state, which is what makes <see cref="Launcher.Applies"/> say no.
    /// </remarks>
    public static SelfUpdate? Real(ICliRevision revision, string? processPath, BuildStore store, Action<string> say, bool take = true)
    {
        if (revision.IsDevelopment || revision.Sequence <= 0 || string.IsNullOrEmpty(processPath)) return null;

        string? handoff = null, resume = null;
        if (take)
        {
            handoff = Environment.GetEnvironmentVariable(HandoffVariable);
            resume = Environment.GetEnvironmentVariable(ResumeVariable);

            // Never inherited by a child, which would take it for its own.
            Environment.SetEnvironmentVariable(HandoffVariable, null);
            Environment.SetEnvironmentVariable(ResumeVariable, null);
        }

        return new SelfUpdate(
            Resolve(processPath), revision.Revision, revision.Sequence, store,
            binary => BuildCheck.ReadVersionAsync(binary, TimeSpan.FromSeconds(60)), new PosixExec(),
            renameAside: OperatingSystem.IsWindows(), say,
            handoff: !string.IsNullOrWhiteSpace(handoff), resume: string.IsNullOrWhiteSpace(resume) ? null : resume);
    }

    /// <summary>The file a path really is: a link is followed to the end, so the link itself is left as it was.</summary>
    public static string Resolve(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return path;
        }
    }

    /// <summary>A candidate: where it is kept, the sha it is kept under, and what its <c>version</c> said.</summary>
    public readonly record struct Candidate(string Path, string Sha, VersionReport? Report);

    /// <summary>
    /// The newest of <paramref name="candidates"/>, when it is strictly newer
    /// than this launcher: a report that says another revision than it is kept
    /// under, or none at all, is skipped, and a tie goes to the lower sha.
    /// Null means do nothing, which is also what an equal sequence means.
    /// </summary>
    public static Candidate? Choose(int ownSequence, string ownRevision, IEnumerable<Candidate> candidates)
    {
        Candidate? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate.Report is not { } report) continue;
            if (!string.Equals(report.Revision, candidate.Sha, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(candidate.Sha, ownRevision, StringComparison.OrdinalIgnoreCase)) continue;
            if (report.Sequence <= ownSequence) continue;

            if (best is not { } current
                || report.Sequence > current.Report!.Sequence
                || (report.Sequence == current.Report.Sequence && string.CompareOrdinal(candidate.Sha, current.Sha) < 0))
                best = candidate;
        }

        return best;
    }

    /// <summary>The newest stored build any board's record names, when it is newer than this launcher. Never throws.</summary>
    public async Task<Candidate?> FindNewerAsync()
    {
        try
        {
            var candidates = new List<Candidate>();

            foreach (var sha in Store.Currents())
            {
                if (string.Equals(sha, OwnRevision, StringComparison.OrdinalIgnoreCase)) continue;
                if (Store.Find(sha) is not { } path) continue;

                candidates.Add(new Candidate(path, sha, await ReadVersion(path)));
            }

            return Choose(OwnSequence, OwnRevision, candidates);
        }
        catch (Exception e) when (IsExpected(e))
        {
            Refuse(e.Message);
            return null;
        }
    }

    /// <summary>
    /// <paramref name="path"/> - a build this change has just stored - when it is
    /// newer than this launcher. Never throws.
    /// </summary>
    public async Task<Candidate?> JustStoredAsync(string path, string sha)
    {
        try
        {
            return Choose(OwnSequence, OwnRevision, [new Candidate(path, sha, await ReadVersion(path))]);
        }
        catch (Exception e) when (IsExpected(e))
        {
            Refuse(e.Message);
            return null;
        }
    }

    /// <summary>
    /// Makes the file at this launcher's own path <paramref name="candidate"/>:
    /// written beside it, run from there and asked for its revision, and only
    /// then swapped in. True when the file is now the new build; false, with one
    /// line said (the first time only), when nothing was changed.
    /// </summary>
    public async Task<bool> SwapAsync(Candidate candidate)
    {
        var directory = Path.GetDirectoryName(OwnPath);
        if (string.IsNullOrEmpty(directory)) return Refuse("this launcher's own folder is unknown");

        var temp = Path.Combine(
            directory, $"{TempPrefix}{Environment.ProcessId}-{Guid.NewGuid():N}{Path.GetExtension(OwnPath)}");
        string? aside = null;

        try
        {
            File.Copy(candidate.Path, temp, overwrite: false);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    temp,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // From beside the old file, which is the folder and the mount it
            // will run from: the check that matters is the one that is true there.
            var report = await ReadVersion(temp);
            if (report is null || !string.Equals(report.Revision, candidate.Sha, StringComparison.OrdinalIgnoreCase))
                return Refuse($"the copy beside {OwnPath} did not report revision {Short(candidate.Sha)}");

            if (RenameAside)
            {
                aside = $"{OwnPath}{AsideInfix}{Environment.ProcessId}-{Guid.NewGuid():N}";
                File.Move(OwnPath, aside);

                try
                {
                    File.Move(temp, OwnPath);
                }
                catch
                {
                    // The old file goes back, whatever stopped the new one. Never over a
                    // file somebody else has put at the path since: that one is whole.
                    try { File.Move(aside, OwnPath); } catch (Exception e) when (IsExpected(e)) { }
                    throw;
                }
            }
            else
            {
                File.Move(temp, OwnPath, overwrite: true);
            }

            return true;
        }
        catch (Exception e) when (IsExpected(e))
        {
            return Refuse(e.Message);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (IsExpected(e)) { }
        }
    }

    /// <summary>The one line the person sees when the file was replaced.</summary>
    public void Updated(Candidate candidate) =>
        say($"hatch: updated itself to build {Short(candidate.Sha)}");

    /// <summary>
    /// What an earlier start left in this launcher's folder: the file it renamed
    /// aside, which nothing is running by now, and a half-written copy from a
    /// start that was killed. A file still in use, or still being written, is
    /// skipped without a word.
    /// </summary>
    public void Sweep()
    {
        try
        {
            var directory = Path.GetDirectoryName(OwnPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            foreach (var file in Directory.EnumerateFiles(directory, Path.GetFileName(OwnPath) + AsideInfix + "*"))
                Delete(file);

            foreach (var file in Directory.EnumerateFiles(directory, TempPrefix + "*"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > StaleTemp) Delete(file);
        }
        catch (Exception e) when (IsExpected(e)) { }
    }

    /// <summary>Says why the file was not replaced, once per process, and answers false.</summary>
    public bool Refuse(string why)
    {
        if (Interlocked.Increment(ref refused) == 1)
            say($"hatch: could not update itself - {why}; carrying on with the build it has");

        return false;
    }

    private static void Delete(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (IsExpected(e)) { }
    }

    private static bool IsExpected(Exception e) =>
        e is IOException or UnauthorizedAccessException or Win32Exception or NotSupportedException
            or InvalidOperationException;

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
}
