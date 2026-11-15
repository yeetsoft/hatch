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
    public static Reset Clear(IWorkspace ws, string path, Func<Reset> prepare, Action<string> say, out string? refusal)
    {
        var forced = ws.AbortMerge();
        if (forced.Outcome == AbortOutcome.Refused)
        {
            var why = forced.Why is { Length: > 0 } ? $": {forced.Why}" : "";
            refusal = $"hatch: --abort-merge: could not clear the merge in {path} - {forced.Step} refused{why}";
            return Reset.Merging;
        }

        refusal = null;
        var kind = forced.OwnMarker ? "the loop started" : "hatch did not start";
        say($"hatch:   --abort-merge: aborted a merge {kind} in {path}, {forced.Discarded} uncommitted file(s) discarded");
        return prepare();
    }
}
