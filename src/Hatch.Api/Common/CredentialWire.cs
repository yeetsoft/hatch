using System.Text;

namespace Hatch.Api.Common;

/// <summary>
/// How a credential is wrapped for the container runner on the wire, and
/// nothing else.
///
/// The wire is <c>v1</c> on purpose, and it is not how Hatch stores anything.
/// It is a fixed XOR key both ends carry, read by a container that runs only
/// with the wall off; changing it would break every container image already
/// built. Storage is <see cref="SecretProtector"/>'s and may change freely,
/// which is why this type owns its own copy of the key, the XOR and the tag,
/// and calls neither <see cref="SecretProtector"/> nor
/// <see cref="SecretObfuscator"/>. Nothing that stores a value calls this.
///
/// The CLI's <c>Secret.Reveal</c> reads it. The two ship as separate binaries,
/// so their tests agree on one written-out string rather than on shared code.
/// </summary>
public static class CredentialWire
{
    private const string Tag = "v1";
    private const char Separator = ':';

    private static readonly byte[] Key = "Hatch.SiteSettings.Obfuscation.v1"u8.ToArray();

    /// <summary>The credential as the wire carries it: <c>v1:&lt;base64&gt;</c>.</summary>
    public static string Wrap(string credential)
    {
        var bytes = Encoding.UTF8.GetBytes(credential);
        Xor(bytes);
        return $"{Tag}{Separator}{Convert.ToBase64String(bytes)}";
    }

    /// <summary>
    /// The credential behind a wrapped value, or null for one that is blank,
    /// carries another tag, or is not base64. The reader for tests; the real
    /// reader is the CLI's.
    /// </summary>
    public static string? Unwrap(string? wrapped)
    {
        if (string.IsNullOrWhiteSpace(wrapped)) return null;

        var separator = wrapped.IndexOf(Separator);
        if (separator < 0 || wrapped[..separator] != Tag) return null;

        try
        {
            var bytes = Convert.FromBase64String(wrapped[(separator + 1)..]);
            Xor(bytes);
            return Encoding.UTF8.GetString(bytes);
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
