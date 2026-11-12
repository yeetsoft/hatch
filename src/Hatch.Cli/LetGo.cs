namespace Hatch.Cli;

/// <summary>
/// The comments written when a ticket comes back to the board without having
/// moved - a runner that fell over partway through an increment (HA-115), or
/// an increment that ran to the end and simply did not move it (HA-118). Two
/// methods and not one, because the two say different things happened, but
/// both read as the same kind of sentence: what let go of the ticket, and why.
/// </summary>
public static class LetGo
{
    /// <summary>
    /// The runner itself fell over - picking, claiming, the tree, the session,
    /// or tidying up after it - rather than the increment running to an
    /// ordinary end. Best-effort: a ticket whose flag could not be written is
    /// still let go of, and the runner says why on its own output instead of
    /// failing a second time over the same comment.
    /// </summary>
    public static Task CommentAsync(
        Board board, Terminal say, IAgentAdapter adapter, string key, string why, string? sessionId,
        CancellationToken ct) =>
        WriteAsync(
            board, say, adapter, key, $"An unattended runner failed and let {key} go: {why}.",
            sessionId, "No session had started when it failed.", ct);

    /// <summary>
    /// The increment ran to the end and still left the ticket where it found
    /// it - the first time in a row that happens, which costs one quiet
    /// comment rather than a flag: see <see cref="Increment"/>'s stall guard
    /// for why a second one in a row is treated differently. A ticket the
    /// board shows moved or gained children is not a stall at all any more,
    /// whatever the session's own account said, so this is only ever reached
    /// for one genuinely left untouched.
    /// </summary>
    public static Task LeftAsync(
        Board board, Terminal say, IAgentAdapter adapter, string key, string why, string? sessionId,
        CancellationToken ct, string? kept = null) =>
        WriteAsync(
            board, say, adapter, key,
            $"An unattended increment left {key} where it found it and let it go: {why}.",
            sessionId, "There is no session to resume: the run ended before it said what its id was.", ct, kept);

    /// <summary>
    /// The session's agent never started on this runner - the CLI said
    /// something and exited before its first stream event - so the ticket was
    /// not touched at all. Not a let-go, and not a stall: the comment is the
    /// whole record, and what the CLI wrote is what a person needs to fix the
    /// runner. No resume line, since there is no session. Best-effort, as
    /// <see cref="WriteAsync"/>.
    /// </summary>
    public static async Task UnstartedAsync(
        Board board, Terminal say, string key, string kept, int exitCode, CancellationToken ct)
    {
        var body = $"An unattended increment could not start its agent on this runner and left {key} untouched.";
        if (kept.Length > 0) body += "\n\n" + Scrub.Fence(kept);
        body += $"\n\nIt exited {exitCode}.";

        try
        {
            await board.CommentAsync(key, body, ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: {key} could not be told its agent never started - {e.Message}");
        }
    }

    /// <summary>
    /// The session's agent is signed out of its account - an expired OAuth
    /// session, a rejected key, a prompt to log in again - recognised whether
    /// or not the agent ever started a stream, so this must not claim the
    /// ticket was left untouched the way <see cref="UnstartedAsync"/> does: the
    /// stream may have done some of it before the account dropped. Not a
    /// let-go, and not a stall: the comment is the whole record, and the
    /// runner pauses itself at once rather than waiting for a second run. No
    /// resume line - signing in again is what the next session needs, not a
    /// session id. Best-effort, as <see cref="WriteAsync"/>.
    /// </summary>
    public static async Task SignedOutAsync(
        Board board, Terminal say, string key, string sentence, CancellationToken ct)
    {
        var body = $"The agent on this runner is signed out and needs signing in again: \"{sentence}\".";

        try
        {
            await board.CommentAsync(key, body, ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: {key} could not be told its agent is signed out - {e.Message}");
        }
    }

    private static async Task WriteAsync(
        Board board, Terminal say, IAgentAdapter adapter, string key, string opening, string? sessionId,
        string noSession, CancellationToken ct, string? kept = null)
    {
        var body = opening;

        body += sessionId is { Length: > 0 } session
            ? $"\n\nThe session it ran in is still there, with everything it did in context:\n\n    {adapter.ResumeCommand(session)}"
            : $"\n\n{noSession}";

        if (kept is { Length: > 0 }) body += "\n\n" + Scrub.Fence(kept);

        try
        {
            await board.CommentAsync(key, body, ct);
        }
        catch (Exception e) when (e is HatchException or OperationCanceledException)
        {
            say.Complain($"hatch: {key} could not be told it was let go - {e.Message}");
        }
    }
}
