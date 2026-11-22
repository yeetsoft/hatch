namespace Hatch.Cli;

/// <summary>
/// Reads the path given to <c>hatch api</c> back into a route.
/// </summary>
/// <remarks>
/// Git Bash on Windows rewrites an argument that starts with <c>/</c> into a
/// drive path before a Windows program sees it, so <c>/api/board</c> arrives as
/// <c>C:/Program Files/Git/api/board</c>. The root it prepends is not in the
/// environment, so the prefix is cut off by reading the path: a route starts at
/// the first <c>api</c> segment. No real route begins with a drive letter and a
/// colon, so nothing is gated on the platform and a path from any other shell
/// is read exactly as it always was.
/// </remarks>
internal static class RoutePath
{
    /// <summary>Returns null and sets <paramref name="path"/>, or returns the one-sentence refusal.</summary>
    public static string? Read(string arg, out string path)
    {
        if (!Rewritten(arg))
        {
            path = "/" + arg.TrimStart('/');
            return null;
        }

        var cut = arg.IndexOf('?');
        var head = cut < 0 ? arg : arg[..cut];
        var query = cut < 0 ? "" : arg[cut..];
        head = head.Replace('\\', '/');

        for (var at = head.IndexOf("/api", StringComparison.Ordinal); at >= 0;
             at = head.IndexOf("/api", at + 1, StringComparison.Ordinal))
        {
            var end = at + "/api".Length;
            if (end == head.Length || head[end] == '/')
            {
                path = head[at..] + query;
                return null;
            }
        }

        path = "";
        return $"hatch: api was given '{arg}', which Git Bash rewrote from a route - expected a path starting /api/. "
            + "Put MSYS_NO_PATHCONV=1 before the command, or leave off the leading slash.";
    }

    private static bool Rewritten(string arg) =>
        arg.Length >= 3 && char.IsAsciiLetter(arg[0]) && arg[1] == ':' && arg[2] is '/' or '\\';
}
