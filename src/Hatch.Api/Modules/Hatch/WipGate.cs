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
/// slot, and (HA-112) kept per epic as well, so one epic's own limit does not
/// hold a batch's unrelated stories back. It computes nothing <see cref="Wip"/>
/// does not already answer - the load, the counted types, the section, an
/// epic's own limit and whether an issue is already counted all come from
/// <see cref="Wip.LoadAsync"/> and <see cref="Wip.EpicsAsync"/>. A move is
/// refused by whichever limit it is over - the section's where both are, said
/// first because it is the more general fact - and admitted only once both
/// have let it through, so a story an epic refuses spends no section slot at
/// all.
/// </remarks>
public sealed class WipGate(HatchContext db, IssueClaims claims, DateTimeOffset now)
{
    private WipSection? _section;
    private bool _loaded;
    private readonly Dictionary<WipSlice, HashSet<long>> _admitted = [];
    private readonly Dictionary<long, HashSet<long>> _admittedUnderEpic = [];
    private readonly Dictionary<long, EpicLimit?> _epics = [];

    /// <summary>
    /// Whether a move of <paramref name="issue"/> into <paramref name="toStatusId"/>
    /// is let through, and how.
    /// </summary>
    /// <param name="type">
    /// The type the issue will hold after the edit - a retype and a move in
    /// the same request are gated as the type the card is left with.
    /// </param>
    /// <param name="parentId">
    /// The parent the card is left with after the edit - its own
    /// <c>ParentId</c>, or a reparent in the same request. HA-112's own
    /// question: an epic here holds the move to its own limit, the same way
    /// <paramref name="type"/> holds it to the section-wide slice.
    /// </param>
    /// <param name="wipOverride">Whether the caller said <em>move anyway</em>.</param>
    public async Task<WipVerdict> AdmitAsync(
        EfHatchIssue issue, int toStatusId, string type, long? parentId, bool wipOverride, CancellationToken ct)
    {
        var section = await SectionAsync(ct);

        // No section, a move that does not enter it, a move that never left
        // it, or a type no slice counts - none of these can be refused,
        // whatever the load. A slice with no limit row still has to be asked:
        // an epic's own limit holds even where the section-wide one does not.
        if (section is null) return WipVerdict.Pass;
        if (!section.Inside(toStatusId)) return WipVerdict.Pass;
        if (section.Inside(issue.StatusId)) return WipVerdict.Pass;
        if (section.SliceFor(type) is not { } slice) return WipVerdict.Pass;

        var admitted = _admitted.TryGetValue(slice, out var set) ? set : _admitted[slice] = [];

        // An issue Wip already counts (inside the section, or outside it on a
        // live claim headed in, or standing behind a family member this same
        // batch has already admitted) cannot be refused either, whatever the
        // load.
        if (slice.Counted(issue, admitted) || admitted.Contains(issue.Id)) return WipVerdict.Pass;

        // The epic's own limit only ever holds the story-and-bug slice - an
        // epic moving itself is held by the section-wide epic slice above,
        // not by this rule.
        var epic = slice.Counts("story") && parentId is { } pid ? await EpicAsync(pid, ct) : null;
        var admittedUnderEpic = epic is not null
            ? _admittedUnderEpic.TryGetValue(epic.Id, out var u) ? u : _admittedUnderEpic[epic.Id] = []
            : null;

        var sectionLoad = slice.Load + admitted.Count;
        var sectionFull = slice.Limit is { } limit && sectionLoad >= limit;

        var epicLoad = epic is not null ? section.LoadUnder(epic.Id) + admittedUnderEpic!.Count : 0;
        var epicFull = epic is not null && epicLoad >= epic.Limit;

        if (!sectionFull && !epicFull)
        {
            admitted.Add(issue.Id);
            admittedUnderEpic?.Add(issue.Id);
            return WipVerdict.Room;
        }

        if (!wipOverride)
        {
            // The section's sentence wins where both are full: the more
            // general fact, said first - and the only one of the two a
            // caller holding neither limit would ever see.
            return sectionFull
                ? WipVerdict.Refused(new WipRefusalDto(Wip.Sentence(sectionLoad, slice.Limit!.Value, slice.Types), sectionLoad, slice.Limit.Value))
                : WipVerdict.Refused(new WipRefusalDto(Wip.EpicSentence(epic!.Key, epicLoad, epic.Limit, slice.Types), epicLoad, epic.Limit));
        }

        // An override past either limit counts the move on, the same as room
        // would have - so an override that stepped over only the epic's limit
        // still takes the section's one free slot, and the next card in the
        // same batch is judged against a section with one slot fewer.
        admitted.Add(issue.Id);
        admittedUnderEpic?.Add(issue.Id);

        var overrides = new List<WipOverride>();
        if (sectionFull) overrides.Add(new WipOverride(slice.Limit!.Value, sectionLoad + 1, null));
        if (epicFull) overrides.Add(new WipOverride(epic!.Limit, epicLoad + 1, epic.Key));
        return WipVerdict.Overridden(overrides);
    }

    /// <summary>
    /// This parent's epic limit, or null when it is not an epic - read once
    /// per parent for the whole gate, so a bulk batch of fifty stories under
    /// the same epic costs one query rather than fifty.
    /// </summary>
    private async Task<EpicLimit?> EpicAsync(long parentId, CancellationToken ct)
    {
        if (_epics.TryGetValue(parentId, out var cached)) return cached;

        var epics = await Wip.EpicsAsync(db, [parentId], ct);
        return _epics[parentId] = epics.GetValueOrDefault(parentId);
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
    public static readonly WipVerdict Pass = new(WipVerdictKind.Pass, null, []);
    public static readonly WipVerdict Room = new(WipVerdictKind.Room, null, []);

    public static WipVerdict Refused(WipRefusalDto refusal) =>
        new(WipVerdictKind.Refused, refusal, []);

    /// <param name="overrides">
    /// One entry per limit stepped over, section's first where both are - see
    /// <see cref="WipOverride"/>.
    /// </param>
    public static WipVerdict Overridden(IReadOnlyList<WipOverride> overrides) =>
        new(WipVerdictKind.Overridden, null, overrides);

    private WipVerdict(WipVerdictKind kind, WipRefusalDto? refusal, IReadOnlyList<WipOverride> overrides)
    {
        Kind = kind;
        Refusal = refusal;
        Overrides = overrides;
    }

    public WipVerdictKind Kind { get; }

    /// <summary>The <c>409</c> body, set only when <see cref="Kind"/> is <see cref="WipVerdictKind.Refused"/>.</summary>
    public WipRefusalDto? Refusal { get; }

    /// <summary>
    /// Set only when <see cref="Kind"/> is <see cref="WipVerdictKind.Overridden"/>: one
    /// entry per limit the move stepped over.
    /// </summary>
    public IReadOnlyList<WipOverride> Overrides { get; }
}

/// <summary>
/// One limit a move stepped over, as <see cref="WipVerdict.Overridden"/> carries
/// it - the numbers for one <c>wip_overridden</c> event. <see cref="Epic"/> is
/// null for the section-wide limit and the epic's own key for its limit, so
/// the written event carries <c>epic</c> only where the numbers are the
/// epic's.
/// </summary>
/// <param name="Load">The load the move leaves, with the card counted - "6 of 5".</param>
public sealed record WipOverride(int Limit, int Load, string? Epic);
