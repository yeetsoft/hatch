using System.Diagnostics;

namespace Hatch.Cli;

/// <summary>One clone, made once - the seam <see cref="Clones"/> calls through.</summary>
public interface IClone
{
    /// <summary>Clones into the path. Null on success; git's own last line otherwise.</summary>
    string? Run();
}

/// <summary>The real thing: a plain <c>git clone --quiet</c>.</summary>
public sealed class GitClone(string remote, string path) : IClone
{
    public string? Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); // git clone will not make the parent
        var start = new ProcessStartInfo
        {
            FileName = "git", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "clone", "--quiet", remote, path }) start.ArgumentList.Add(a);

        using var process = Process.Start(start);
        if (process is null) return "could not start git";

        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0) return null;

        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1].Trim() : $"git clone exited {process.ExitCode}";
    }
}
