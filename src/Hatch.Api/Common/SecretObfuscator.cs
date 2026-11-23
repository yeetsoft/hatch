using System.Text;

namespace Hatch.Api.Common;

/// <summary>
/// Reversible XOR obfuscation - not encryption, just enough that a token isn't
/// sitting in the DB as cleartext.
///
/// This is scheme <c>v1</c>, and it is an implementation detail of
/// <see cref="SecretProtector"/>: call that instead. Nothing writes it to the
/// database any more - new values are sealed under the operator's key as
/// <c>v2</c> - but the protector still reads it, and needs no key to. The one
/// other caller is the credential handoff to the container runner,
/// <see cref="CredentialWire"/>, which is <c>v1</c> on the wire on purpose and
/// keeps its own copy.
/// </summary>
public static class SecretObfuscator
{
    private static readonly byte[] Key = "Hatch.SiteSettings.Obfuscation.v1"u8.ToArray();

    public static string Obfuscate(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        Xor(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string Deobfuscate(string obfuscated)
    {
        var bytes = Convert.FromBase64String(obfuscated);
        Xor(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Deobfuscates a stored secret, treating one that won't decode - absent,
    /// blank, or hand-edited into something that isn't base64 - as absent
    /// rather than as an exception. Callers that hold obfuscated columns
    /// (an agent credential, a Google client secret) want a missing value to
    /// cost that one setting a re-entry, not to throw out of every operation
    /// that touches it.
    /// </summary>
    public static string? TryReveal(string? obfuscated)
    {
        if (string.IsNullOrWhiteSpace(obfuscated)) return null;
        try
        {
            var plaintext = Deobfuscate(obfuscated);
            return string.IsNullOrWhiteSpace(plaintext) ? null : plaintext;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void Xor(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
            data[i] ^= Key[i % Key.Length];
    }
}
