namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a typed query means to the go-to lookup, with no database in it. It follows
/// <c>goToRows</c> in the console's <c>lib/goTo.ts</c>, so the server and the console
/// agree.
/// </summary>
/// <remarks>
/// The meanings: digits alone are a number prefix; any other single word is a key
/// prefix, on letters and digits alone and ignoring case; several words are title
/// words, each the start of a word in the title and every one of them present;
/// blank, or nothing left once only letters and digits are kept, finds no rows
/// here.
/// </remarks>
public abstract record GoToQuery
{
    /// <summary>The longest an issue number is written: <see cref="int.MaxValue"/> is ten digits.</summary>
    public const int MaxDigits = 10;

    /// <summary>Blank, several words, or nothing left once only letters and digits are kept.</summary>
    public sealed record None : GoToQuery;

    /// <summary>Digits alone: a prefix of an issue number, in every project.</summary>
    public sealed record Number(string Digits) : GoToQuery;

    /// <summary>One word, squashed to lower-case letters and digits: a prefix of a key, hyphen ignored.</summary>
    public sealed record Key(string Wanted) : GoToQuery;

    /// <summary>Several words: a title search, as <see cref="SearchQuery.ToTsQuery"/> reads them.</summary>
    public sealed record Words(string Typed) : GoToQuery;

    /// <summary>Reads what was typed.</summary>
    public static GoToQuery Parse(string? q)
    {
        var typed = q?.Trim();
        if (string.IsNullOrEmpty(typed)) return new None();

        if (typed.Any(char.IsWhiteSpace))
            return SearchQuery.ToTsQuery(typed) is null ? new None() : new Words(typed);

        if (typed.All(IsDigit)) return new Number(typed);

        var wanted = Squash(typed);
        return wanted.Length == 0 ? new None() : new Key(wanted);
    }

    /// <summary>Letters and digits only, lower case: the form a key and a query are compared in.</summary>
    public static string Squash(string text) =>
        new([.. text
            .Where(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            .Select(char.ToLowerInvariant)]);

    /// <summary>
    /// Every issue number that starts with <paramref name="digits"/>, as one inclusive
    /// range per digit length: <c>45</c> is 45, 450-459, 4500-4599 and so on up to ten
    /// digits, the top of a range held at <see cref="int.MaxValue"/> and a range wholly
    /// past it dropped.
    /// </summary>
    /// <remarks>
    /// A number never starts with 0, and is never longer than ten digits, so such a
    /// prefix matches nothing. Ascending in length, which is ascending in number, so
    /// rows read range by range are already in number order.
    /// </remarks>
    public static IReadOnlyList<(int Lo, int Hi)> Ranges(string digits)
    {
        if (digits.Length == 0 || digits.Length > MaxDigits || digits[0] == '0' || !digits.All(IsDigit))
            return [];

        var ranges = new List<(int, int)>();
        for (var length = digits.Length; length <= MaxDigits; length++)
        {
            var spare = length - digits.Length;
            var lo = long.Parse(digits + new string('0', spare));
            if (lo > int.MaxValue) break;

            var hi = long.Parse(digits + new string('9', spare));
            ranges.Add(((int)lo, (int)Math.Min(hi, int.MaxValue)));
        }

        return ranges;
    }

    /// <summary>
    /// The ways a squashed word can be a project key followed by a number prefix:
    /// every split at the third character or later whose tail is all digits and does
    /// not start with 0. A project key may hold digits, so <c>ha34</c> is HA and 34,
    /// and is also HA3 and 4 wherever a project HA3 exists. Shortest head first.
    /// </summary>
    public static IReadOnlyList<(string Head, string Tail)> Splits(string wanted)
    {
        var splits = new List<(string, string)>();

        // A project key is at least two characters, so the first split is at 2.
        for (var at = 2; at < wanted.Length; at++)
        {
            var tail = wanted[at..];
            if (tail[0] == '0' || !tail.All(IsDigit)) continue;

            splits.Add((wanted[..at], tail));
        }

        return splits;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}
