using System.Text.RegularExpressions;

namespace Hatch.Cli;

/// <summary>
/// The <c>--project</c> flags of <c>go-to-work</c> and <c>work</c>, checked for
/// shape and normalised the way the server normalises the same list, so that a
/// loop's own flags and the list the board echoes back compare equal.
/// </summary>
public static class ProjectFlags
{
    // The same shape as EfHatchProject.KeyPattern (Hatch.Api, which the CLI does
    // not reference). Shape only: whether the project exists is the server's to say.
    private static readonly Regex Shape = new("^[A-Z][A-Z0-9]{1,5}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Trims, upper-cases and drops repeats (first seen kept, in order). False,
    /// with <paramref name="bad"/> set to the first value that is not a project
    /// key's shape, where one is not - a flag given nothing is one of those.
    /// </summary>
    public static bool Parse(IEnumerable<string> raw, out IReadOnlyList<string> keys, out string? bad)
    {
        keys = [];
        bad = null;
        var seen = new List<string>();
        foreach (var value in raw)
        {
            var key = value.Trim().ToUpperInvariant();
            if (!Shape.IsMatch(key))
            {
                bad = value;
                return false;
            }
            if (!seen.Contains(key)) seen.Add(key);
        }
        keys = seen;
        return true;
    }
}
