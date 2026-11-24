using System.Globalization;

namespace Hatch.Api.Services.Chat;

/// <summary>What a button on a question says: which question, and which answer.</summary>
/// <param name="QuestionId">The question comment's id.</param>
/// <param name="Place">
/// The zero-based place of the option in the question's stored list, or null for
/// <em>accept recommended</em>. Never a label: Hatch reads the label from the
/// stored question, so a press cannot put words in anyone's mouth.
/// </param>
public sealed record ChatPressTarget(long QuestionId, int? Place)
{
    /// <summary>True for the button that takes the recommended option, whichever place that is.</summary>
    public bool AcceptRecommended => Place is null;
}

/// <summary>
/// The one place that writes and reads what a question's button carries, so the
/// story that draws the button and the one that answers its press cannot
/// disagree. The text goes in the button's action id, which Slack requires to be
/// unique per button on a message; the button's value may repeat it, but the
/// press is read from the action id alone.
/// </summary>
/// <remarks>
/// Form: <c>hatch.answer:{questionId}:{place}</c>, where place is a
/// non-negative number or <c>r</c>. Anything else reads as null: a button of
/// another kind is not an error.
/// </remarks>
public static class ChatPressAction
{
    public const string Prefix = "hatch.answer:";

    /// <summary>The marker for <em>accept recommended</em>.</summary>
    public const string RecommendedPlace = "r";

    /// <summary>The text for the option at <paramref name="place"/>.</summary>
    public static string Write(long questionId, int place)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionId);
        ArgumentOutOfRangeException.ThrowIfNegative(place);
        return $"{Prefix}{questionId.ToString(CultureInfo.InvariantCulture)}:{place.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>The text for <em>accept recommended</em>.</summary>
    public static string WriteRecommended(long questionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionId);
        return $"{Prefix}{questionId.ToString(CultureInfo.InvariantCulture)}:{RecommendedPlace}";
    }

    /// <summary>What <paramref name="actionId"/> says, or null when it is not one of these.</summary>
    public static ChatPressTarget? Read(string? actionId)
    {
        if (actionId is null || !actionId.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        var parts = actionId[Prefix.Length..].Split(':');
        if (parts.Length != 2) return null;

        if (!IsDigits(parts[0]) || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var questionId))
            return null;

        if (parts[1] == RecommendedPlace) return new ChatPressTarget(questionId, null);

        return IsDigits(parts[1]) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var place)
            ? new ChatPressTarget(questionId, place)
            : null;
    }

    // NumberStyles.None still lets some non-ASCII digits through on some cultures' parsers; the form is ASCII only.
    private static bool IsDigits(string s) => s.Length > 0 && s.All(c => c is >= '0' and <= '9');
}
