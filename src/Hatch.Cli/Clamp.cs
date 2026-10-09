namespace Hatch.Cli;

/// <summary>
/// The comment written when a session crossed its playbook's budget mid-run
/// and was told to wrap up - so the ticket says why, without anybody having to
/// read the transcript to find out.
/// </summary>
public static class Clamp
{
    public static Task CommentAsync(Board board, Terminal say, string key, long tokens, int? requests, CancellationToken ct)
    {
        var body = $"The clamp fired at {Format.Compact(tokens)} tokens"
            + (requests is { } count ? $", after {count} request{(count == 1 ? "" : "s")}" : "")
            + " - the session was told to wrap up.";

        return WriteAsync(board, say, key, body, ct);
    }

    private static async Task WriteAsync(Board board, Terminal say, string key, string body, CancellationToken ct)
    {
        try
        {
            await board.CommentAsync(key, body, ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: {key} could not be told the clamp fired - {e.Message}");
        }
    }
}
