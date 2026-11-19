namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Records who started an issue - the one helper every writer of
/// <see cref="EfHatchIssue.StatusId"/> that can move an issue right calls, so
/// <see cref="EfHatchIssue.StartedBy"/> and <see cref="EfHatchIssue.StartedAt"/>
/// are never set two different ways. See HA-2481.
/// </summary>
public static class Starts
{
    /// <summary>
    /// Writes <paramref name="who"/> and <paramref name="now"/> onto
    /// <paramref name="issue"/> when <paramref name="crossed"/> is true - the
    /// latest crossing overwrites both, including back to nobody for a key's
    /// move. A leftward move, a move within the right side, and filing an
    /// issue pass <c>false</c> and leave the fields as they are.
    /// </summary>
    public static void Record(EfHatchIssue issue, bool crossed, Guid? who, DateTimeOffset now)
    {
        if (!crossed) return;

        issue.StartedBy = who;
        issue.StartedAt = now;
    }
}
