using System.Globalization;

namespace Hatch.Api.Services.Chat;

/// <summary>Words that more than one chat message uses, so they are spelled one way.</summary>
public static class ChatText
{
    /// <summary>A moment as a person reads it in a message: <c>2026-11-23 12:00 UTC</c>.</summary>
    public static string When(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
}
