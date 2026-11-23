namespace Hatch.Api.Common;

/// <summary>
/// The one way a secret is written to Hatch's database, and the one way it is
/// read back.
///
/// Every protected value carries the scheme that produced it, as
/// <c>&lt;scheme&gt;:&lt;payload&gt;</c>.
/// <list type="bullet">
/// <item><c>v2</c> is sealed under the operator's secret key
/// (<see cref="SecretCipher"/>), for the row it is stored on. It is the only
/// scheme written.</item>
/// <item><c>v1</c> is <see cref="SecretObfuscator"/>'s XOR - obfuscation, not
/// encryption. It is read and never written, and reading it needs no key.</item>
/// <item>No tag is a value written before schemes existed. It is read as
/// <c>v1</c>.</item>
/// </list>
///
/// The discriminator is free rather than clever: a <c>v1</c> payload is base64
/// and a <c>v2</c> payload is base64url joined by dots, and neither alphabet has
/// a colon, so no value can be mistaken for a tagged one.
///
/// Every method that takes a plaintext also takes the name of the row it
/// belongs to (<see cref="SecretStores"/>), so there is no way to store a value
/// without saying where.
/// </summary>
public sealed class SecretProtector(SecretCipher cipher)
{
    /// <summary>The scheme new values are written with.</summary>
    public const string CurrentScheme = "v2";

    private const char SchemeSeparator = ':';

    /// <summary>Whether there is a key to seal with. Without one, <see cref="TrySeal"/> refuses.</summary>
    public bool CanSeal => cipher.CanSeal;

    /// <summary>
    /// Whether <paramref name="stored"/> is already as a save would write it
    /// now: <c>v2</c>, under the current key. False for everything else,
    /// including a value that cannot be opened - it says nothing of whether
    /// the value is readable. Never throws.
    /// </summary>
    internal bool IsCurrent(string? stored)
    {
        var current = cipher.CurrentKeyId;
        return current is not null && KeyIdOf(stored) == current;
    }

    /// <summary>
    /// The key id a <c>v2</c> value names: the text between <c>v2:</c> and the
    /// first <c>.</c>, if that is shaped like one (<see cref="SecretKeys.KeyId"/>:
    /// eight lower-case hex characters). Null for <c>v1</c>, an untagged value,
    /// or anything else, so a hand-edited value never comes back out of here
    /// as an "id" to be logged. Never throws.
    /// </summary>
    internal static string? KeyIdOf(string? stored)
    {
        var prefix = $"{CurrentScheme}{SchemeSeparator}";
        const int idLength = 8;
        if (stored is null || !stored.StartsWith(prefix, StringComparison.Ordinal)) return null;
        if (stored.Length <= prefix.Length + idLength || stored[prefix.Length + idLength] != '.') return null;

        var id = stored.Substring(prefix.Length, idLength);
        return id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')) ? id : null;
    }

    /// <summary>
    /// Seals <paramref name="plaintext"/> for the row named <paramref name="rowName"/>.
    /// An empty input gives <c>""</c> whether or not there is a key: "no secret"
    /// is a state callers already have (a cleared field, an unset setting), and
    /// clearing one needs no key. Anything else needs a key, and false means
    /// there is none - nothing is stored weakly in its place.
    /// </summary>
    public bool TrySeal(string? plaintext, string rowName, out string stored)
    {
        ArgumentNullException.ThrowIfNull(rowName);

        stored = string.Empty;
        if (string.IsNullOrEmpty(plaintext)) return true;

        var sealedValue = cipher.Seal(plaintext, rowName);
        if (sealedValue is null) return false;

        stored = $"{CurrentScheme}{SchemeSeparator}{sealedValue}";
        return true;
    }

    /// <summary>
    /// The secret behind a stored value, held as an <see cref="OpenedSecret"/> so
    /// it prints as <c>[secret]</c>, or null when there isn't one -
    /// absent, blank, sealed for another row, under a key this Hatch does not
    /// hold, or damaged past reading.
    ///
    /// Null rather than an exception for the reason <see cref="SecretObfuscator.TryReveal"/>
    /// gives: a value that has been hand-edited in the database should cost the
    /// one setting it belongs to a re-entry, not throw out of every operation
    /// that happens to touch the row.
    /// </summary>
    public OpenedSecret? Unprotect(string? stored, string rowName)
    {
        TryUnprotect(stored, rowName, out var secret);
        return secret;
    }

    /// <summary>
    /// <see cref="Unprotect"/>, and whether the stored value opened. False means
    /// something is stored that could not be opened. A blank stored value, or one
    /// that opens to a blank plaintext, is true with a null <paramref name="secret"/>:
    /// there is nothing to open, and nothing is wrong.
    /// </summary>
    public bool TryUnprotect(string? stored, string rowName, out OpenedSecret? secret)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            secret = null;
            return true;
        }

        var plaintext = UnprotectText(stored, rowName);
        secret = OpenedSecret.From(plaintext);
        return plaintext is not null;
    }

    private string? UnprotectText(string? stored, string rowName)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        var separator = stored.IndexOf(SchemeSeparator);
        if (separator < 0)
        {
            // Untagged: written before schemes existed, and v1's own bytes.
            return SecretObfuscator.TryReveal(stored);
        }

        var scheme = stored[..separator];
        var payload = stored[(separator + 1)..];

        return scheme switch
        {
            "v2" => cipher.TryOpen(payload, rowName, out var plaintext) ? plaintext : null,
            "v1" => SecretObfuscator.TryReveal(payload),
            // An unknown scheme is a value written by a newer Hatch than this
            // one. Unreadable is the honest answer, the same as damaged.
            _ => null,
        };
    }
}
