namespace Hatch.Cli;

/// <summary>
/// What <c>--abort-merge</c> does once a reset has already answered
/// <see cref="Reset.Merging"/> - the one door <see cref="GoToWork"/> and
/// <see cref="WorkCommand"/> both go through, so the flag reads the same
/// either way it is reached.
/// </summary>
internal static class MergeAbort
{
    /// <summary>
    /// Forces the merge clear with <see cref="IWorkspace.AbortMerge"/>, says
    /// what it did, and asks <paramref name="prepare"/> again - so a tree that
    /// is still not ready (<see cref="Reset.Later"/>, say) is handled by the
    /// caller's own cases rather than assumed clean. Called only after
    /// <paramref name="prepare"/> has already answered <see cref="Reset.Merging"/>.
    /// </summary>
    /// <param name="refusal">
    /// Set when the forced abort itself refused - the caller complains with it
    /// and ends the run, the same as a tree that cannot be reset at all. Null
    /// on every other way out.
    /// </param>
    /// <param name="flag">
    /// Named in both lines where this was <c>--abort-merge</c> deciding in
    /// advance - null where it was a person's own <c>a</c>, which already read
    /// the banner naming the merge and needs no flag quoted back at it.
    /// </param>
    public static Reset Clear(IWorkspace ws, string path, Func<Reset> prepare, Action<string> say, out string? refusal, string? flag = null)
    {
        var tag = flag is { Length: > 0 } ? $"{flag}: " : "";
        var forced = ws.AbortMerge();
        if (forced.Outcome == AbortOutcome.Refused)
        {
            var why = forced.Why is { Length: > 0 } ? $": {forced.Why}" : "";
            refusal = $"hatch: {tag}could not clear the merge in {path} - {forced.Step} refused{why}";
            return Reset.Merging;
        }

        refusal = null;
        var kind = forced.OwnMarker ? "the loop started" : "hatch did not start";
        say($"hatch:   {tag}aborted a merge {kind} in {path}, {forced.Discarded} uncommitted file(s) discarded");
        return prepare();
    }

    /// <summary>
    /// What a reset stopping on a merge means with nothing having already said
    /// "abort it in advance" (<c>--abort-merge</c>): the banner <see
    /// cref="Banner.MergeBlocked"/> draws from <see cref="IWorkspace.MergeBlocked"/>,
    /// then up to 30 seconds on <c>a</c> before the run gives up. Where keys are
    /// off - a pipe, <c>--no-keys</c>, <c>TERM=dumb</c>, or redirected output -
    /// the banner still prints and this returns at once: nobody can press a key
    /// on a pipe, and the 30 seconds is for a person.
    /// </summary>
    public static async Task<(Reset Reset, string? Complaint)> AskAsync(
        Runtime runtime, IWorkspace ws, string path, Func<Reset> prepare, CancellationToken ct)
    {
        var block = ws.MergeBlocked ?? new MergeBlock(OwnMarker: false, Why: null, Uncommitted: 0);
        runtime.Say.Lines(Banner.MergeBlocked(block, path, runtime.Say.Colour));

        if (!runtime.Controls.KeysOn) return (Reset.Merging, Complaint);

        var pressed = new TaskCompletionSource();
        void OnPress() => pressed.TrySetResult();
        runtime.Controls.AbortMerge += OnPress;

        // Created before Arm(), so a caller that sees the armed state and then
        // drives the clock forward cannot race past a timer that does not exist
        // yet.
        var timer = Task.Delay(TimeSpan.FromSeconds(30), runtime.Clock, ct);
        runtime.Controls.Arm();

        try
        {
            var won = await Task.WhenAny(timer, pressed.Task);
            if (won == pressed.Task)
            {
                var cleared = Clear(ws, path, prepare, runtime.Say.Line, out var refusal);
                return (cleared, refusal);
            }

            await timer; // a cancelled ct throws out of here, the same as it would anywhere else in the wait
            return (Reset.Merging, Complaint);
        }
        finally
        {
            runtime.Controls.AbortMerge -= OnPress;
            runtime.Controls.Disarm();
        }
    }

    private const string Complaint = "hatch: no key in 30s - the merge is still there; --abort-merge says yes in advance";
}
