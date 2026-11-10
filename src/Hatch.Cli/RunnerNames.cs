namespace Hatch.Cli;

/// <summary>
/// What an older Hatch recorded about runner names, read and never written.
/// </summary>
/// <remarks>
/// A runner's name is its key's now, so nothing here chooses or records one.
/// What is left is the <see cref="Record"/> a checkout's earlier name was kept
/// in, for the one bridge that reads it once on its way out.
/// </remarks>
public static class RunnerNames
{
    /// <summary>
    /// The per-checkout record: which name each checkout on this machine
    /// once chose. Read-only.
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

        /// <summary>
        /// The record key for a checkout under a profile: the bare canonical
        /// path when there is no profile, else <c>profile|path</c>. A profile
        /// name is <c>[A-Za-z0-9-]+</c> and <c>|</c> cannot appear in a Windows
        /// path segment, so neither side can hold the delimiter.
        /// </summary>
        public static string Key(string? profile, string canonicalRoot) =>
            string.IsNullOrEmpty(profile) ? canonicalRoot : $"{profile}|{canonicalRoot}";

        /// <summary><c>&lt;key&gt;=&lt;name&gt;</c>, one per line, the key being <see cref="Key"/>'s. Missing reads as empty.</summary>
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
    }
}
