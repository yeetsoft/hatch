namespace Hatch.Contracts;

/// <summary>
/// The chain of issues a Hatch-made pull request carries inside its body, own
/// ticket first, root last.
///
/// Versioning: add a field within a version, never redefine or remove one -
/// a reader older than the writer must still make sense of what it finds.
/// Redefining or removing a field bumps <see cref="CurrentVersion"/> instead.
/// A reader accepts any version greater than or equal to 1; it does not
/// reject a version newer than its own, since an unknown field is simply one
/// it does not read.
/// </summary>
public record PullRequestEnvelope(int Version, string? Instance, string? Project, IReadOnlyList<PullRequestEnvelopeIssue> Chain)
{
    public const int CurrentVersion = 1;

    public IReadOnlyList<PullRequestEnvelopeIssue> Chain { get; init; } = Chain ?? [];
}

public record PullRequestEnvelopeIssue(
    string Key,
    string Title,
    string? Type = null,
    string? Description = null,
    string? Status = null,
    string? ParentKey = null,
    string? Url = null,
    bool Truncated = false);

/// <summary>The HTML comments that bound the envelope block in a pull request's body.</summary>
public static class PullRequestEnvelopeMarkers
{
    public const string Start = "<!-- hatch:pull-request-envelope:start -->";
    public const string End = "<!-- hatch:pull-request-envelope:end -->";
}
