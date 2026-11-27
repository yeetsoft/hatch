namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What stage an ambition is at, and what it needs next: the one place the
/// Ambitions page's word and its sentence are made. Worked out on each read and
/// stored nowhere.
/// </summary>
/// <remarks>
/// Pure: everything it reads is handed to it, so it makes no database call. No
/// column is found by its name; a sentence names a column only after it was
/// found by what it is ticked or where it stands. The rules are the queue's -
/// <see cref="Columns"/> for where a column stands,
/// <see cref="Dispatch.MatchShapingPress"/> for the column an ambition is shaped
/// in, <see cref="FamilyGate.PassFold"/> for why a pass is not running - so the
/// page and the queue cannot disagree about what an ambition waits for.
/// </remarks>
public static class AmbitionStage
{
    public const string Done = "done";
    public const string Shelved = "shelved";
    public const string Shaping = "shaping";
    public const string Draft = "draft";
    public const string Ready = "ready";
    public const string BudgetSpent = "budget spent";
    public const string Complete = "complete";
    public const string Running = "running";
    public const string Stopped = "stopped";

    /// <summary>
    /// The order the groups are listed in. <see cref="Done"/> and
    /// <see cref="Shelved"/> are not here: they share the one group after every
    /// other.
    /// </summary>
    private static readonly string[] Order = [BudgetSpent, Running, Stopped, Complete, Ready, Shaping, Draft];

    /// <summary>Where a stage's group stands in the list: lower is nearer the top.</summary>
    public static int Group(string stage) =>
        Array.IndexOf(Order, stage) is var at && at >= 0 ? at : Order.Length;

    /// <summary>The first stage that is true of an ambition, and its sentence.</summary>
    /// <param name="status">The column the ambition stands in.</param>
    /// <param name="statuses">Every column, in board order.</param>
    /// <param name="capabilities">How many capabilities the ambition has.</param>
    /// <param name="holds">How many of those have <c>holds</c> as their newest verdict.</param>
    /// <param name="shapingStatusId">The from-column of the row that shapes ambitions for its project, or null where no row does.</param>
    /// <param name="budgetSpent">Whether its budget is spent. Read for the implementation column only.</param>
    /// <param name="passFold"><see cref="FamilyGate.PassFold"/>'s sentence, or null where a pass is not folded. Read for the implementation column only.</param>
    public static (string Stage, string Why) Of(
        EfHatchStatus status, List<EfHatchStatus> statuses, int capabilities, int holds,
        int? shapingStatusId, bool budgetSpent, string? passFold)
    {
        if (status.IsTerminal) return (Done, $"It stands in \"{status.Name}\", which closes work.");
        if (status.IsDeferred) return (Shelved, $"It stands in \"{status.Name}\" and is set aside until a person moves it.");
        if (status.Id == shapingStatusId) return (Shaping, "It is being shaped into a title, a description and capabilities.");

        if (Columns.Implementation(statuses) is not { } implementation)
            return (Stopped, $"It stands in \"{status.Name}\", and no column on this board is one a pass runs in.");

        if (!Columns.AtOrPastImplementation(statuses, status.Id))
            return capabilities == 0
                ? (Draft, "It has no capabilities yet.")
                : (Ready, $"Nothing runs until a person starts it - {Dispatch.MoveItInto(implementation.Name)}.");

        if (status.Id != implementation.Id)
            return (Stopped, $"It stands in \"{status.Name}\", and a pass runs only in \"{implementation.Name}\".");

        if (budgetSpent) return (BudgetSpent, "Its budget is spent - raise it or clear it before the next pass.");

        if (capabilities > 0 && holds == capabilities)
            return (Complete, "Every capability holds, so a pass has nothing left to do.");

        return (Running, passFold ?? "A pass runs as soon as a runner takes it.");
    }
}
