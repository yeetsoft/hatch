using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Hatch.Cli;

/// <summary>
/// The cast list a checkout's runner is named from, the choice a first run
/// makes once, and the record that makes every later run repeat it.
/// </summary>
/// <remarks>
/// A runner used to be named <c>host:/path/to/checkout</c> - unique by
/// construction, and unreadable at a glance on a board shared with anybody
/// else. A character is the opposite trade: readable, and not unique by
/// construction, which is the whole reason <see cref="Choose"/> and
/// <see cref="Record"/> exist.
/// </remarks>
public static class RunnerNames
{
    /// <summary>
    /// Every name in <c>runner-names.txt</c>, in file order - main and
    /// recurring characters from five shows, plus the one-offs worth having.
    /// See that file for the cast, and <c>docs/hatch.md</c> for why two names
    /// (Carl Weathers, Creed Bratton) are missing on purpose.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Load();

    private static IReadOnlyList<string> Load()
    {
        var assembly = typeof(RunnerNames).Assembly;
        using var stream = assembly.GetManifestResourceStream("runner-names.txt")
                            ?? throw new InvalidOperationException("runner-names.txt is not embedded in this build");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            // Kept rather than refused: a duplicate slipped into the file is a
            // typo to fix, not a reason every runner in the house fails to
            // start. The first spelling wins, which is the one that would
            // already have been chosen from.
            if (seen.Add(trimmed)) names.Add(trimmed);
        }

        return names;
    }

    /// <summary>
    /// The name this checkout gets: a deterministic starting slot, walked
    /// forward past whatever <paramref name="taken"/> already holds.
    /// </summary>
    /// <remarks>
    /// The slot is a hash of <paramref name="host"/> and
    /// <paramref name="canonicalRoot"/> rather than the root alone, so two
    /// checkouts of the same repository on two machines do not reach for the
    /// same name first - though only <paramref name="taken"/> is what actually
    /// keeps them apart when they do. Reordering, adding to or trimming the
    /// cast list changes the slot a hash lands on, which is exactly why a
    /// choice is <see cref="Record">recorded</see> rather than recomputed on
    /// every run.
    /// </remarks>
    /// <param name="taken">
    /// Every name already spoken for - case-insensitively, since a person
    /// typing one by hand in <c>hatch config</c> should not land beside the
    /// list's own spelling under a different case.
    /// </param>
    public static string Choose(string host, string canonicalRoot, IReadOnlySet<string> taken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{host}\n{canonicalRoot}"));
        var start = (int)(BitConverter.ToUInt32(digest, 0) % (uint)All.Count);

        for (var i = 0; i < All.Count; i++)
        {
            var name = All[(start + i) % All.Count];
            if (!taken.Contains(name)) return name;
        }

        // Every one of about a hundred names is somebody else's, on this
        // machine or on the board - rare, and worth a numbered suffix on the
        // name this checkout would have had rather than refusing to start.
        var suffix = 2;
        while (taken.Contains($"{All[start]} {suffix}")) suffix++;
        return $"{All[start]} {suffix}";
    }

    /// <summary>
    /// The per-checkout record: which name each checkout on this machine
    /// already chose, so a later run repeats it rather than choosing again.
    /// </summary>
    /// <remarks>
    /// A file beside <see cref="Settings.UserConfigPath"/> rather than a key
    /// inside it - <c>config</c> follows the person into every checkout, and a
    /// runner's name is a fact about one of them. Keyed on
    /// <see cref="Checkout.Canonical"/> so two spellings of one path share one
    /// entry.
    /// </remarks>
    public static class Record
    {
        /// <summary>Beside the per-user <c>config</c> file, in the same directory.</summary>
        public static string DefaultPath() =>
            Path.Combine(Path.GetDirectoryName(Settings.UserConfigPath())!, "runners");

        /// <summary><c>&lt;canonical checkout path&gt;=&lt;name&gt;</c>, one per line. Missing reads as empty.</summary>
        public static IReadOnlyDictionary<string, string> Read(string path)
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return entries;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var split = line.IndexOf('=');
                if (split <= 0) continue;

                entries[line[..split].Trim()] = line[(split + 1)..].Trim();
            }

            return entries;
        }

        /// <summary>
        /// Records this checkout's name, keeping every other checkout's entry
        /// untouched - a read, an update, and the same temp-file-then-rename
        /// write <c>ConfigCommand.Write</c> uses, mode 600 the same way.
        /// </summary>
        public static void Set(string path, string canonicalRoot, string name)
        {
            var entries = new Dictionary<string, string>(Read(path), StringComparer.Ordinal) { [canonicalRoot] = name };

            var directory = Path.GetDirectoryName(path);
            if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

            var temp = $"{path}.{Environment.ProcessId}";
            File.WriteAllLines(temp, entries.Select(e => $"{e.Key}={e.Value}"));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            File.Move(temp, path, overwrite: true);
        }
    }
}
