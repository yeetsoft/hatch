using Hatch.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which columns are in the WIP section, and how much of one slice of the board
/// they may hold at once - the one place that says which columns count, the way
/// <see cref="Columns"/> is the one place that says which columns the board
/// draws.
///
/// <see cref="EfHatchStatus.IsWip"/> is the stored flag; a column keeps it
/// whatever else is true of it. This is where the flag is narrowed to the
/// columns that actually count - the ones that are neither deferred nor
/// terminal - so a flag stranded on a column that has since become either does
/// not silently hold the limit down.
/// </summary>
public static class Wip
{
    /// <summary>
    /// The section as <c>WipController</c>'s read and the board (HA-88) will
    /// share it: the flagged columns that are neither deferred nor terminal, in
    /// board order, and every slice Hatch knows - <see cref="EfHatchWipLimit.Slices"/>,
    /// always both, in that order - with the limit if one is held.
    /// </summary>
    public static async Task<WipSectionDto> SectionAsync(HatchContext db, CancellationToken ct)
    {
        var limits = await RowsAsync(db, ct);

        var statusIds = await db.Statuses.AsNoTracking()
            .Where(s => s.IsWip && !s.IsDeferred && !s.IsTerminal)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var slices = EfHatchWipLimit.Slices
            .Select(types => new WipSliceDto(EfHatchPlaybook.SplitTypes(types), limits.GetValueOrDefault(types)))
            .ToList();

        return new WipSectionDto(statusIds, slices);
    }

    /// <summary>
    /// How full the WIP section is right now, or null only where the board has
    /// never turned WIP on at all: no column left flagged (and neither deferred
    /// nor terminal). A slice with no limit row still comes back, with its own
    /// real load and no limit to gate anything with. Every board caller -
    /// <c>BoardController</c> today, the move gate and the dispatcher's fold
    /// later - reads the answer from here rather than counting for itself.
    /// </summary>
    /// <param name="statuses">
    /// The board's own status list, already loaded by the caller: no second
    /// query, and the same entities <see cref="Columns.Target"/> is asked about.
    /// </param>
    /// <param name="now">
    /// The instant every claim on the board is judged against. Callers that also
    /// draw claims elsewhere in the same request should read it once and pass
    /// that same value here, so a card drawn as claimed and the load's
    /// claimed-inbound part agree.
    /// </param>
    public static async Task<WipSection?> LoadAsync(
        HatchContext db, IssueClaims claims, List<EfHatchStatus> statuses, DateTimeOffset now, CancellationToken ct)
    {
        var section = statuses
            .Where(s => s.IsWip && !s.IsDeferred && !s.IsTerminal)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToList();

        if (section.Count == 0) return null;

        var limits = await RowsAsync(db, ct);

        var sectionIds = section.ToHashSet();
        var feeders = statuses
            .Where(s => !sectionIds.Contains(s.Id) && Columns.Target(statuses, s) is { } target && sectionIds.Contains(target.Id))
            .Select(s => s.Id)
            .ToHashSet();

        var sliceTypes = EfHatchWipLimit.Slices
            .Select(EfHatchPlaybook.SplitTypes)
            .ToList();
        var everyType = sliceTypes.SelectMany(t => t).ToHashSet();

        var rows = await db.Issues.AsNoTracking()
            .Where(i => everyType.Contains(i.Type) && (sectionIds.Contains(i.StatusId) || feeders.Contains(i.StatusId)))
            .Select(i => new
            {
                i.Id,
                i.StatusId,
                i.Type,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
            })
            .ToListAsync(ct);

        var parents = await db.Issues.AsNoTracking()
            .Select(i => new { i.Id, i.ParentId })
            .ToDictionaryAsync(i => i.Id, i => i.ParentId, ct);

        var slices = new List<WipSlice>();
        foreach (var types in sliceTypes)
        {
            var typeSet = types.ToHashSet();
            var ofType = rows.Where(r => typeSet.Contains(r.Type)).ToList();

            var counted = ofType
                .Where(r => sectionIds.Contains(r.StatusId) || claims.IsLive(r.Claim, now))
                .Select(r => r.Id)
                .ToHashSet();

            // A line of the tree costs the section one slot: where an ancestor
            // and a descendant are both counted, only the family's root - the
            // one with no further counted ancestor - is charged against the
            // limit.
            var familyRoots = counted.Where(id => !HasAncestorIn(id, counted, parents)).ToHashSet();

            var claimedInbound = ofType.Count(r =>
                !sectionIds.Contains(r.StatusId) && familyRoots.Contains(r.Id));

            var limit = limits.GetValueOrDefault(string.Join(",", types));
            slices.Add(new WipSlice(types, limit, counted, familyRoots.Count, claimedInbound, parents));
        }

        return new WipSection(section, slices);
    }

    private static async Task<Dictionary<string, int?>> RowsAsync(HatchContext db, CancellationToken ct) =>
        await db.WipLimits.AsNoTracking().ToDictionaryAsync(w => w.Types, w => (int?)w.Limit, ct);

    /// <summary>
    /// Whether any ancestor of <paramref name="id"/> is in <paramref name="set"/> -
    /// the walk that makes a line of the tree one unit rather than one per
    /// counted node on it, the same seen-guarded climb
    /// <see cref="ClaimLineage.Holder"/> makes to find a live claim above.
    /// </summary>
    internal static bool HasAncestorIn(long id, IReadOnlySet<long> set, IReadOnlyDictionary<long, long?> parents)
    {
        var seen = new HashSet<long> { id };
        var at = parents.GetValueOrDefault(id);
        while (at is { } pid && seen.Add(pid))
        {
            if (set.Contains(pid)) return true;
            at = parents.GetValueOrDefault(pid);
        }

        return false;
    }

    /// <summary>
    /// The sentence a refusal or an override names - "the WIP section is full -
    /// 2 of 2 stories and bugs are in it" - built once here from a slice's own
    /// types, pluralised and joined, so no caller spells a type name. The
    /// dispatcher's fold (HA-90) shares it and adds its own tail.
    /// </summary>
    public static string Sentence(int load, int limit, IReadOnlyList<string> types) =>
        $"the WIP section is full - {load} of {limit} {TypeWords.Plural(types)} are in it";

    /// <summary>
    /// The sentence an epic's own limit refuses or overrides with - "E is at
    /// its limit - 1 of 1 of its stories and bugs is in the WIP section" -
    /// built once here the way <see cref="Sentence"/> is, so the gate's 409
    /// and the dispatcher's fold (HA-112) never spell the epic's numbers
    /// twice. The fold adds its own tail, as it does to <see cref="Sentence"/>.
    /// </summary>
    public static string EpicSentence(string epicKey, int load, int limit, IReadOnlyList<string> types) =>
        $"{epicKey} is at its limit - {load} of {limit} of its {TypeWords.Plural(types)} {(load == 1 ? "is" : "are")} in the WIP section";

    /// <summary>
    /// Every epic a move or a fold needs the limit of, read once over the
    /// distinct non-null parent ids a caller hands in - a memoizable, bulk-safe
    /// counterpart to <see cref="LoadAsync"/>'s own board-wide reads. A parent
    /// id that is not an epic is simply absent, so a story's story parent (a
    /// task's story) never looks like an epic with a limit.
    /// </summary>
    public static async Task<IReadOnlyDictionary<long, EpicLimit>> EpicsAsync(
        HatchContext db, IEnumerable<long?> parentIds, CancellationToken ct)
    {
        var ids = parentIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, EpicLimit>();

        var rows = await db.Issues.AsNoTracking()
            .Where(i => ids.Contains(i.Id) && i.Type == "epic")
            .Select(i => new { i.Id, i.Project!.Key, i.Number, i.WipLimit })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Id,
            r => new EpicLimit(r.Id, IssueKey.Format(r.Key, r.Number), r.WipLimit ?? EfHatchIssue.DefaultEpicWipLimit));
    }
}

/// <summary>An epic's own limit, as <see cref="Wip.EpicsAsync"/> reads it - the key for the sentence, the limit for the gate and the fold.</summary>
public sealed record EpicLimit(long Id, string Key, int Limit);

/// <summary>
/// One reading of the WIP section, as <see cref="Wip.LoadAsync"/> takes it: the
/// section's columns, and every slice Hatch knows, each with its own limit,
/// load and claimed-inbound count. Callers ask it questions rather than
/// re-deriving any of this - <see cref="Inside"/> and <see cref="SliceFor"/>
/// exist so the move gate (HA-89) and the dispatcher's fold (HA-90) never work
/// out the section or the types for themselves.
/// </summary>
public sealed class WipSection
{
    private readonly IReadOnlySet<int> _section;

    internal WipSection(IReadOnlyList<int> statusIds, IReadOnlyList<WipSlice> slices)
    {
        StatusIds = statusIds;
        Slices = slices;
        _section = statusIds.ToHashSet();
    }

    public IReadOnlyList<int> StatusIds { get; }
    public IReadOnlyList<WipSlice> Slices { get; }

    /// <summary>Whether a column counts towards the section - <see cref="StatusIds"/>, as a set.</summary>
    public bool Inside(int statusId) => _section.Contains(statusId);

    /// <summary>The slice whose types cover this issue type, or null for a type no slice counts (a task).</summary>
    public WipSlice? SliceFor(string type) => Slices.FirstOrDefault(s => s.Counts(type));

    /// <summary>
    /// The load under one epic - its direct stories and bugs the section
    /// counts - asked of the one slice <see cref="EfHatchWipLimit.StoriesAndBugs"/>
    /// names, always the first of <see cref="EfHatchWipLimit.Slices"/>. HA-112's
    /// own question, answered from the same rows the section-wide slice already
    /// holds: no second query, and no new column.
    /// </summary>
    public int LoadUnder(long epicId) => Slices.First(s => s.Counts("story")).LoadUnder(epicId);

    public WipDto ToDto() => new(StatusIds, Slices.Select(s => s.ToDto()).ToList());
}

/// <summary>
/// One slice of the WIP section: the types it counts, its limit (or null for
/// none), and which issues are its load right now.
/// </summary>
public sealed class WipSlice
{
    private readonly IReadOnlySet<string> _types;
    private readonly IReadOnlySet<long> _counted;
    private readonly IReadOnlyDictionary<long, long?> _parents;

    internal WipSlice(
        IReadOnlyList<string> types, int? limit, IReadOnlySet<long> counted,
        int load, int claimedInbound, IReadOnlyDictionary<long, long?> parents)
    {
        Types = types;
        Limit = limit;
        Load = load;
        ClaimedInbound = claimedInbound;
        _types = types.ToHashSet();
        _counted = counted;
        _parents = parents;
    }

    public IReadOnlyList<string> Types { get; }
    public int? Limit { get; }
    public int Load { get; }
    public int ClaimedInbound { get; }

    /// <summary>Whether an issue type counts towards this slice - <see cref="Types"/>, as a set.</summary>
    public bool Counts(string type) => _types.Contains(type);

    /// <summary>
    /// Whether this issue is counted against <see cref="Load"/>: directly - in
    /// the section, or outside it and holding a live claim whose next column is
    /// in the section - or because a line of the tree costs the section one
    /// slot and an ancestor of this issue is counted, directly or (within one
    /// request) by <paramref name="alsoAdmitted"/>, the moves a batch has
    /// already let through but not yet saved.
    /// </summary>
    public bool Counted(EfHatchIssue issue, IReadOnlyCollection<long>? alsoAdmitted = null) =>
        _counted.Contains(issue.Id)
        || Wip.HasAncestorIn(issue.Id, _counted, _parents)
        || (alsoAdmitted is { Count: > 0 } && Wip.HasAncestorIn(issue.Id, alsoAdmitted.ToHashSet(), _parents));

    /// <summary>
    /// How many of this slice's counted issues are this epic's direct children -
    /// the load HA-112 holds an epic's own limit against. Direct children only,
    /// by construction: <see cref="_parents"/> holds one level, so a bug under
    /// a story under the epic carries the story's id, never the epic's.
    /// </summary>
    public int LoadUnder(long epicId) => _counted.Count(id => _parents.GetValueOrDefault(id) == epicId);

    public WipSliceLoadDto ToDto() => new(Types, Limit, Load, ClaimedInbound);
}
