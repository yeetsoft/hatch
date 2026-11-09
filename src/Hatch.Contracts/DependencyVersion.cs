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
    /// Whether <paramref name="detected"/> is at least <paramref name="declared"/>,
    /// read as a minimum. A null <paramref name="declared"/> asks for no version,
    /// so anything satisfies it. Otherwise <paramref name="detected"/> must have at
    /// least as many components - read up to its first non-integer component,
    /// however many there are - and the first component where the two differ, in
    /// order, must favour <paramref name="detected"/>; all equal also satisfies it.
    /// Components are compared as digit strings, so one longer than any integer
    /// type simply outranks a shorter one rather than overflowing.
    /// </summary>
    public static bool Satisfies(string? declared, string? detected)
    {
        if (declared is null) return true;
        if (detected is null || Problem(declared) is not null) return false;

        var want = declared.Split('.');
        var have = detected.Split('.').TakeWhile(IsInteger).ToArray();
        if (have.Length < want.Length) return false;

        return Compare(want, have) <= 0;
    }

    /// <summary>
    /// Whether <paramref name="detected"/> and <paramref name="declared"/> name the
    /// same fact - the exact-prefix test for comparing two facts, rather than a
    /// declared version against a detected one. A null <paramref name="declared"/>
    /// matches anything. Otherwise <paramref name="detected"/> must have at least as
    /// many components - read up to its first non-integer component - and every one
    /// of the first <paramref name="declared"/> components must be numerically equal.
    /// Components are compared as digit strings, so one longer than any integer type
    /// simply never matches.
    /// </summary>
    public static bool Matches(string? declared, string? detected)
    {
        if (declared is null) return true;
        if (detected is null || Problem(declared) is not null) return false;

        var want = declared.Split('.');
        var have = detected.Split('.').TakeWhile(IsInteger).ToArray();
        if (have.Length < want.Length) return false;

        return Compare(want, have) == 0;
    }

    /// <summary>
    /// Compares <paramref name="want"/> against the first <paramref name="want"/>.Length
    /// components of <paramref name="have"/>, component by component as digit strings
    /// via <see cref="Canonical"/>: a shorter canonical string is the smaller one, else
    /// <see cref="string.CompareOrdinal(string?, string?)"/> decides. No integer parsing,
    /// so nothing overflows. Negative when <paramref name="want"/> is smaller, positive
    /// when it is larger, zero when every compared component is equal.
    /// </summary>
    private static int Compare(string[] want, string[] have)
    {
        for (var i = 0; i < want.Length; i++)
        {
            var w = Canonical(want[i]);
            var h = Canonical(have[i]);
            var cmp = w.Length != h.Length ? w.Length.CompareTo(h.Length) : string.CompareOrdinal(w, h);
            if (cmp != 0) return cmp;
        }

        return 0;
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
