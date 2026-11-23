using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Hatch.Api.Common;

/// <summary>
/// Seals and opens a value under the secret key the operator supplied
/// (<c>HATCH_SECRET_KEY</c>), with AES-256-GCM.
///
/// <para>The sealed form is <c>&lt;key id&gt;.&lt;nonce&gt;.&lt;ciphertext and tag&gt;</c>.
/// The key id is <see cref="SecretKeys.KeyId"/>. The nonce is 12 random bytes
/// and the tag 16; both of the last two segments are base64url without
/// padding, an alphabet with no <c>.</c> in it.</para>
///
/// <para>Sealing takes the name of the row the value belongs to, such as
/// <c>site-setting/AnthropicApiKey</c>, as associated data. A sealed value
/// copied onto another row does not open there.</para>
///
/// <para>Only the current key seals. The previous key, if any, opens what it
/// sealed before a rotation and seals nothing. Opening never throws: a value
/// that is damaged, cut short or not in this form is "cannot open", as
/// <see cref="SecretObfuscator.TryReveal"/> answers null. No exception here
/// carries a key.</para>
/// </summary>
public sealed class SecretCipher
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const char Separator = '.';

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[]? current;
    private readonly byte[]? previous;
    private readonly string? currentId;
    private readonly string? previousId;

    /// <param name="current">The key that seals, or null when none is set.</param>
    /// <param name="previous">A key that only opens, or null.</param>
    public SecretCipher(byte[]? current, byte[]? previous)
    {
        this.current = current;
        this.previous = previous;
        currentId = current is null ? null : SecretKeys.KeyId(current);
        previousId = previous is null ? null : SecretKeys.KeyId(previous);
    }

    /// <summary>
    /// Built from the two configured values. A value that does not parse counts
    /// as not set here; <c>SecretKeyValidation</c> has already stopped the API
    /// at start for one, so this is the quiet half.
    /// </summary>
    public static SecretCipher From(string? currentKey, string? previousKey)
    {
        SecretKeys.TryParse(currentKey, out var currentBytes);
        SecretKeys.TryParse(previousKey, out var previousBytes);
        return new SecretCipher(currentBytes, previousBytes);
    }

    /// <summary>Whether there is a current key to seal with.</summary>
    public bool CanSeal => current is not null;

    /// <summary>The key id values are sealed under now, or null with no current key.</summary>
    public string? CurrentKeyId => currentId;

    /// <summary>
    /// The sealed form of <paramref name="plaintext"/> for the row named
    /// <paramref name="rowName"/>, or null when there is no current key.
    /// </summary>
    public string? Seal(string plaintext, string rowName)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(rowName);
        if (current is null) return null;

        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipherAndTag = new byte[data.Length + TagLength];

        using var aes = new AesGcm(current, TagLength);
        aes.Encrypt(nonce, data, cipherAndTag.AsSpan(0, data.Length), cipherAndTag.AsSpan(data.Length, TagLength),
            Encoding.UTF8.GetBytes(rowName));

        return string.Concat(currentId, ".", Base64Url.EncodeToString(nonce), ".", Base64Url.EncodeToString(cipherAndTag));
    }

    /// <summary>
    /// Opens <paramref name="sealedValue"/> for the row named
    /// <paramref name="rowName"/> under whichever of the current and previous
    /// keys its key id names. False, with a null plaintext, for anything that
    /// does not open. Never throws.
    /// </summary>
    public bool TryOpen(string? sealedValue, string rowName, out string? plaintext)
    {
        plaintext = null;
        try
        {
            if (string.IsNullOrEmpty(sealedValue) || rowName is null) return false;

            var parts = sealedValue.Split(Separator);
            if (parts.Length != 3) return false;

            var key = KeyFor(parts[0]);
            if (key is null) return false;

            var nonce = Base64Url.DecodeFromChars(parts[1]);
            var cipherAndTag = Base64Url.DecodeFromChars(parts[2]);
            if (nonce.Length != NonceLength || cipherAndTag.Length < TagLength) return false;

            var length = cipherAndTag.Length - TagLength;
            var data = new byte[length];

            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, cipherAndTag.AsSpan(0, length), cipherAndTag.AsSpan(length, TagLength), data,
                Encoding.UTF8.GetBytes(rowName));

            plaintext = StrictUtf8.GetString(data);
            return true;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException or OverflowException)
        {
            plaintext = null;
            return false;
        }
    }

    private byte[]? KeyFor(string keyId)
    {
        if (current is not null && keyId == currentId) return current;
        if (previous is not null && keyId == previousId) return previous;
        return null;
    }
}
