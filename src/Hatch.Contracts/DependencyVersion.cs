namespace Hatch.Contracts;

/// <summary>
/// The one rule a dependency's version answers to - one to three dotted whole
/// numbers - and the one test of whether a detected version meets a declared one.
/// </summary>
public static class DependencyVersion
{
    private const int MaxComponents = 3;
    private const int MaxComponentLength = 9;

    /// <summary>Null when <paramref name="version"/> is valid (or absent), else the sentence refusing it.</summary>
    public static string? Problem(string? version)
    {
        if (version is null) return null;

        var parts = version.Split('.');
        if (parts.Length <= MaxComponents
            && parts.All(part => part.Length <= MaxComponentLength && IsInteger(part)))
            return null;

        return $"a version is one to three dotted whole numbers, like 10 or 10.0.100 - not \"{version}\"";
    }

    /// <summary>
    /// Whether <paramref name="detected"/> meets <paramref name="declared"/>.
    /// A null <paramref name="declared"/> asks for no version, so anything
    /// satisfies it. Otherwise every declared component must exist in
    /// <paramref name="detected"/> - read up to its first non-integer component,
    /// however many there are - and be numerically equal. Components are compared
    /// as digit strings, so one longer than any integer type simply never matches.
    /// </summary>
    public static bool Satisfies(string? declared, string? detected)
    {
        if (declared is null) return true;
        if (detected is null || Problem(declared) is not null) return false;

        var want = declared.Split('.');
        var have = detected.Split('.').TakeWhile(IsInteger).ToArray();
        if (have.Length < want.Length) return false;

        for (var i = 0; i < want.Length; i++)
        {
            if (Canonical(want[i]) != Canonical(have[i])) return false;
        }

        return true;
    }

    // Non-empty and ASCII digits only: char.IsDigit would accept other scripts.
    private static bool IsInteger(string part)
    {
        if (part.Length == 0) return false;

        foreach (var c in part)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }

    private static string Canonical(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }
}
