namespace Hatch.Cli;

/// <summary>
/// The comment written when a runner fails partway through an increment and
/// lets the ticket go, so a person finds out why without reading a log nobody
/// kept - one method, because every way an increment ends without finishing
/// says it the same way.
/// </summary>
public static class LetGo
{
    /// <summary>
    /// Best-effort: a ticket whose flag could not be written is still let go
    /// of, and the runner says why on its own output instead of failing a
    /// second time over the same comment.
    /// </summary>
    public static async Task CommentAsync(
        Board board, Terminal say, string key, string why, string? sessionId, CancellationToken ct)
    {
        var body = $"An unattended runner failed and let {key} go: {why}.";

        body += sessionId is { Length: > 0 } session
            ? $"\n\nThe session it ran in is still there, with everything it did in context:\n\n    claude --resume {session}"
            : "\n\nNo session had started when it failed.";

        try
        {
            await board.CommentAsync(key, body, ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: {key} could not be told the runner failed - {e.Message}");
        }
    }
}
