using System.Security.Cryptography;
using System.Text;

namespace Hatch.Api.Services.Chat;

/// <summary>
/// A chat subject for a thing that has no id yet when its message is queued: a
/// finding is named before a save gives it one, and a subject is at most
/// <see cref="Hatch.Api.Modules.Hatch.EfHatchChatSubject.MaxSubjectLength"/> characters.
/// </summary>
public static class ChatSubjectKey
{
    private const int HashLength = 22;

    /// <summary>
    /// <c>prefix:</c> and the first 22 characters of the base64url SHA-256 of
    /// <paramref name="parts"/> joined by <c>|</c>. The same parts always give the
    /// same subject; name the occurrence in the parts, not just the kind of thing.
    /// </summary>
    public static string Occurrence(string prefix, params object[] parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var text = string.Join('|', parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)));
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return $"{prefix}:{hash[..HashLength]}";
    }
}
