namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Whether a project's merge train has been stuck on the same head long
/// enough that its driver should be passed over - the data HA-2076's stall
/// rule is judged on. A pure static class for the reason
/// <see cref="BroughtCurrent"/> is one: it reads nothing and writes nothing
/// of its own, only mutating the two watch columns it is handed.
/// </summary>
public static class TrainWatch
{
    /// <summary>
    /// Folds one project's watch columns forward by one heartbeat and says
    /// whether its head has been judged <see cref="WorkKinds.Merge"/> or
    /// <see cref="WorkKinds.Update"/> continuously for at least
    /// <paramref name="bound"/>.
    /// </summary>
    /// <param name="head">
    /// This project's current head - see <see cref="Dispatch.TrainHeadsAsync"/> -
    /// or null while its merges column has none.
    /// </param>
    /// <param name="hasDriver">
    /// Whether some live, eligible runner currently drives this project -
    /// passing a head over with nobody to pass it to would strand it, not
    /// fix it.
    /// </param>
    /// <remarks>
    /// Not a stateless <c>since</c>: a head that has merely sat in the column
    /// for hours - the next row up after an earlier merge - has done nothing
    /// a driver could be blamed for, so the clock starts only once this head
    /// is first seen acting, and restarts whenever it changes, is cleared, or
    /// is given a fresh window by a pass-over of its own.
    /// </remarks>
    public static bool Observe(EfHatchProject project, Dispatch.TrainHead? head, bool hasDriver, DateTimeOffset now, TimeSpan bound)
    {
        if (head is null || head.Kind is not (WorkKinds.Merge or WorkKinds.Update) || !hasDriver)
        {
            project.TrainWatchedIssueId = null;
            project.TrainWatchedSince = null;
            return false;
        }

        if (head.IssueId != project.TrainWatchedIssueId || project.TrainWatchedSince is null)
        {
            project.TrainWatchedIssueId = head.IssueId;
            project.TrainWatchedSince = now;
            return false;
        }

        var since = project.TrainWatchedSince.Value;
        if (head.BroughtCurrentAt is { } broughtCurrentAt && broughtCurrentAt > since) since = broughtCurrentAt;

        return now - since >= bound;
    }
}
