using System.Security.Cryptography;

namespace Hatch.Api.Common;

/// <summary>
/// Reads the secret key an operator hands Hatch: base64 of exactly 32 bytes,
/// the size AES-256 takes. The cipher, the startup check and the tests all go
/// through here, so there is one meaning of "a usable key".
/// </summary>
public static class SecretKeys
{
    /// <summary>The length of a key in bytes.</summary>
    public const int KeyLength = 32;

    /// <summary>
    /// True when <paramref name="value"/> is blank (<paramref name="key"/> is
    /// then null: nobody set one) or is base64 of exactly <see cref="KeyLength"/>
    /// bytes. False for anything else. Never throws, and never puts the value
    /// anywhere.
    /// </summary>
    public static bool TryParse(string? value, out byte[]? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            if (bytes.Length != KeyLength) return false;
            key = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The short name a key goes by in a sealed value and in a log: the first
    /// eight hex characters, lower case, of the SHA-256 of its bytes. It says
    /// which key without giving any of it away.
    /// </summary>
    public static string KeyId(byte[] key) => Convert.ToHexStringLower(SHA256.HashData(key))[..8];
}
