using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// A terminal that says everything to the one it wraps, unchanged, and keeps a
/// plain-text copy of every line in a file.
/// </summary>
/// <remarks>
/// <para>The inner terminal is called first, so the screen never waits on the
/// disk, and never under <see cref="_gate"/>, so a slow terminal cannot stall
/// the other thread's write. The lock covers the file alone.</para>
///
/// <para>The file is opened with <see cref="FileShare.ReadWrite"/> so
/// <c>tail -f</c> and <c>Get-Content -Wait</c> can follow it, and flushed after
/// every line so a loop killed outright leaves everything printed so far. A
/// log that cannot be opened or written is one complaint, once, and from then on
/// this is only a pass-through: a night's work is not worth losing to a full disk.</para>
/// </remarks>
public sealed partial class LogTerminal : Terminal, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Terminal _inner;
    private readonly TimeProvider _clock;
    private StreamWriter? _writer;
    private bool _dead;

    public LogTerminal(Terminal inner, string path, TimeProvider clock)
    {
        _inner = inner;
        _clock = clock;
        Path = path;

        string? failure = null;
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
        }
        catch (Exception e) when (IsLogFailure(e))
        {
            _dead = true;
            failure = e.Message;
        }

        if (failure is not null) _inner.Complain($"hatch: cannot write the log at {path}: {failure}");
    }

    /// <summary>Where the copy is being written.</summary>
    public string Path { get; }

    public override void Line(string line)
    {
        _inner.Line(line);
        Append(line);
    }

    public override void Complain(string line)
    {
        _inner.Complain(line);
        Append(line);
    }

    public void Dispose()
    {
        lock (_gate) Kill();
    }

    private void Append(string text)
    {
        var plain = Osc().Replace(Csi().Replace(text, ""), "");
        var stamp = _clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        string? failure = null;
        lock (_gate)
        {
            if (_dead || _writer is null) return;

            try
            {
                foreach (var piece in plain.Split('\n'))
                {
                    _writer.Write(stamp);
                    _writer.Write("  ");
                    _writer.WriteLine(piece.EndsWith('\r') ? piece[..^1] : piece);
                }

                _writer.Flush();
            }
            catch (Exception e) when (IsLogFailure(e))
            {
                Kill();
                failure = e.Message;
            }
        }

        // Outside the lock, and to the inner terminal rather than to this one,
        // so the notice is neither held up by the disk nor written to the log
        // that just failed.
        if (failure is not null) _inner.Complain($"hatch: cannot write the log at {Path}: {failure}");
    }

    /// <summary>Closes the writer and marks the log dead. The caller holds <see cref="_gate"/>.</summary>
    private void Kill()
    {
        _dead = true;

        var writer = _writer;
        _writer = null;
        try
        {
            writer?.Dispose();
        }
        catch (Exception e) when (IsLogFailure(e))
        {
        }
    }

    /// <summary>
    /// What a path that cannot be written throws: a locked or full disk, a
    /// directory that refuses, an unusable name, or a path under a file.
    /// </summary>
    private static bool IsLogFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    [GeneratedRegex(@"\x1b\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex Csi();

    [GeneratedRegex(@"\x1b\][^\x07\x1b]*(\x07|\x1b\\)")]
    private static partial Regex Osc();
}
