using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The one question both column-changing doors ask before anything is
/// written: is this move admitted, and if it is, was the limit overridden to
/// let it through? <see cref="IssuesController.MoveIssue"/> and
/// <see cref="IssuesController.StageEditAsync"/> each build one per request
/// (<see cref="IssuesController.BulkEdit"/> builds one for the whole batch)
/// and ask it once per issue whose column is changing - a request that
/// changes no column never reads it at all.
/// </summary>
/// <remarks>
/// It counts on from its own admissions the way <c>ColumnBottoms</c> counts on
/// from its own ranks: a bulk request aimed at one free slot must not admit two
/// stories into it, and nothing is saved between the issues a batch touches -
/// kept per slice, so one epic admitted in a batch does not use up a story's
/// slot. It computes nothing <see cref="Wip"/> does not already answer - the
/// load, the counted types, the section and whether an issue is already
/// counted all come from <see cref="Wip.LoadAsync"/>.
/// </remarks>
public sealed class WipGate(HatchContext db, IssueClaims claims, DateTimeOffset now)
{
    private WipSection? _section;
    private bool _loaded;
    private readonly Dictionary<WipSlice, HashSet<long>> _admitted = [];

    /// <summary>
    /// Whether a move of <paramref name="issue"/> into <paramref name="toStatusId"/>
    /// is let through, and how.
    /// </summary>
    /// <param name="type">
    /// The type the issue will hold after the edit - a retype and a move in
    /// the same request are gated as the type the card is left with.
    /// </param>
    /// <param name="wipOverride">Whether the caller said <em>move anyway</em>.</param>
    public async Task<WipVerdict> AdmitAsync(
        EfHatchIssue issue, int toStatusId, string type, bool wipOverride, CancellationToken ct)
    {
        var section = await SectionAsync(ct);

        // No section, a move that does not enter it, a move that never left
        // it, a type no slice counts, or a slice with no limit - none of
        // these can be refused, whatever the load.
        if (section is null) return WipVerdict.Pass;
        if (!section.Inside(toStatusId)) return WipVerdict.Pass;
        if (section.Inside(issue.StatusId)) return WipVerdict.Pass;
        if (section.SliceFor(type) is not { Limit: { } limit } slice) return WipVerdict.Pass;

        var admitted = _admitted.TryGetValue(slice, out var set) ? set : _admitted[slice] = [];

        // An issue Wip already counts (inside the section, or outside it on a
        // live claim headed in) cannot be refused either, whatever the load.
        if (slice.Counted(issue) || admitted.Contains(issue.Id)) return WipVerdict.Pass;

        var load = slice.Load + admitted.Count;
        if (load < limit)
        {
            admitted.Add(issue.Id);
            return WipVerdict.Room;
        }

        if (wipOverride)
        {
            admitted.Add(issue.Id);
            return WipVerdict.Overridden(limit, load + 1);
        }

        return WipVerdict.Refused(new WipRefusalDto(Wip.Sentence(load, limit, slice.Types), load, limit));
    }

    private async Task<WipSection?> SectionAsync(CancellationToken ct)
    {
        if (_loaded) return _section;

        var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync(ct);
        _section = await Wip.LoadAsync(db, claims, statuses, now, ct);
        _loaded = true;
        return _section;
    }
}

/// <summary>One of the four answers <see cref="WipGate.AdmitAsync"/> gives.</summary>
public enum WipVerdictKind
{
    /// <summary>Not gated at all - see <see cref="WipGate.AdmitAsync"/> for the reasons.</summary>
    Pass,

    /// <summary>Admitted: there was room.</summary>
    Room,

    /// <summary>Refused: the section is full and nobody said to move anyway.</summary>
    Refused,

    /// <summary>Admitted past a full section because a person said to.</summary>
    Overridden,
}

/// <summary>
/// What <see cref="WipGate.AdmitAsync"/> answered - the kind, and whichever of
/// the two payloads goes with it.
/// </summary>
public sealed class WipVerdict
{
    public static readonly WipVerdict Pass = new(WipVerdictKind.Pass, null, 0, 0);
    public static readonly WipVerdict Room = new(WipVerdictKind.Room, null, 0, 0);

    public static WipVerdict Refused(WipRefusalDto refusal) =>
        new(WipVerdictKind.Refused, refusal, refusal.Load, refusal.Limit);

    /// <param name="load">The load the move leaves, with the card counted - "6 of 5".</param>
    public static WipVerdict Overridden(int limit, int load) =>
        new(WipVerdictKind.Overridden, null, load, limit);

    private WipVerdict(WipVerdictKind kind, WipRefusalDto? refusal, int load, int limit)
    {
        Kind = kind;
        Refusal = refusal;
        Load = load;
        Limit = limit;
    }

    public WipVerdictKind Kind { get; }

    /// <summary>The <c>409</c> body, set only when <see cref="Kind"/> is <see cref="WipVerdictKind.Refused"/>.</summary>
    public WipRefusalDto? Refusal { get; }

    /// <summary>
    /// Set only when <see cref="Kind"/> is <see cref="WipVerdictKind.Overridden"/>: the
    /// load the move leaves, with the card counted.
    /// </summary>
    public int Load { get; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="WipVerdictKind.Overridden"/>.</summary>
    public int Limit { get; }
}
