using System.Globalization;

namespace Hatch.Contracts;

/// <summary>
/// The one rule a token count answers to - 500000, 500k, 2M, 1.5M, always at
/// least one token - so the <c>--max-tokens</c> flag and the <c>PATCH</c>
/// both read the same refusal rather than inventing their own.
/// </summary>
public static class TokenCount
{
    public const string Refusal = "a token count, like 500k or 2M";

    /// <summary>
    /// ASCII digits only, no trimming, no sign, <see cref="CultureInfo.InvariantCulture"/>.
    /// Accepts a plain whole number, a <c>k</c>/<c>K</c> thousand, or an
    /// <c>m</c>/<c>M</c> million with at most one decimal digit. Never
    /// throws; overflow and anything below one token are refusals, not
    /// exceptions.
    /// </summary>
    public static bool TryParse(string? text, out long count)
    {
        count = 0;
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            var last = text[^1];
            if (last is 'k' or 'K')
            {
                var body = text[..^1];
                if (!IsAsciiDigits(body)) return false;
                if (!long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
                count = checked(n * 1_000);
            }
            else if (last is 'm' or 'M')
            {
                var body = text[..^1];
                var dot = body.IndexOf('.');
                string whole;
                var fractionDigit = 0;
                if (dot < 0)
                {
                    whole = body;
                }
                else
                {
                    whole = body[..dot];
                    var fraction = body[(dot + 1)..];
                    if (fraction.Length != 1 || !char.IsAsciiDigit(fraction[0])) return false;
                    fractionDigit = fraction[0] - '0';
                }

                if (!IsAsciiDigits(whole)) return false;
                if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var wholeValue)) return false;

                var tenths = checked((wholeValue * 10) + fractionDigit);
                count = checked(tenths * 100_000);
            }
            else
            {
                if (!IsAsciiDigits(text)) return false;
                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
                count = n;
            }
        }
        catch (OverflowException)
        {
            count = 0;
            return false;
        }

        if (count < 1)
        {
            count = 0;
            return false;
        }

        return true;
    }

    private static bool IsAsciiDigits(string text)
    {
        if (text.Length == 0) return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c)) return false;
        }

        return true;
    }
}
