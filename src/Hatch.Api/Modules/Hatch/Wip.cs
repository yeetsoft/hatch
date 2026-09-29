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
    /// The stories-and-bugs slice, as both <c>WipController</c>'s read and the
    /// board (HA-88) will share it: the limit if one is held, the types it
    /// counts, and the flagged columns that are neither deferred nor terminal,
    /// in board order.
    /// </summary>
    public static async Task<WipSectionDto> SectionAsync(HatchContext db, CancellationToken ct)
    {
        var limit = await db.WipLimits.AsNoTracking()
            .Where(w => w.Types == EfHatchWipLimit.StoriesAndBugs)
            .Select(w => (int?)w.Limit)
            .SingleOrDefaultAsync(ct);

        var statusIds = await db.Statuses.AsNoTracking()
            .Where(s => s.IsWip && !s.IsDeferred && !s.IsTerminal)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        return new WipSectionDto(limit, EfHatchPlaybook.SplitTypes(EfHatchWipLimit.StoriesAndBugs), statusIds);
    }

    /// <summary>
    /// How full the WIP section is right now, or null where the board has never
    /// turned WIP on: no limit row, or no column left flagged (and neither
    /// deferred nor terminal). Every board caller - <c>BoardController</c>
    /// today, the move gate and the dispatcher's fold later - reads the answer
    /// from here rather than counting for itself.
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
        var limit = await db.WipLimits.AsNoTracking()
            .Where(w => w.Types == EfHatchWipLimit.StoriesAndBugs)
            .Select(w => (int?)w.Limit)
            .SingleOrDefaultAsync(ct);

        if (limit is not { } value) return null;

        var section = statuses
            .Where(s => s.IsWip && !s.IsDeferred && !s.IsTerminal)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToList();

        if (section.Count == 0) return null;

        var sectionIds = section.ToHashSet();
        var feeders = statuses
            .Where(s => !sectionIds.Contains(s.Id) && Columns.Target(statuses, s) is { } target && sectionIds.Contains(target.Id))
            .Select(s => s.Id)
            .ToHashSet();

        var types = EfHatchPlaybook.SplitTypes(EfHatchWipLimit.StoriesAndBugs);

        var rows = await db.Issues.AsNoTracking()
            .Where(i => types.Contains(i.Type) && (sectionIds.Contains(i.StatusId) || feeders.Contains(i.StatusId)))
            .Select(i => new
            {
                i.Id,
                i.StatusId,
                Claim = new ClaimSnapshot(
                    i.ClaimToken, i.ClaimedBy, i.ClaimRunner,
                    i.ClaimedAt, i.ClaimHeartbeatAt, i.ClaimChatter, i.ClaimChatterAt),
            })
            .ToListAsync(ct);

        var counted = rows
            .Where(r => sectionIds.Contains(r.StatusId) || claims.IsLive(r.Claim, now))
            .Select(r => r.Id)
            .ToHashSet();

        var claimedInbound = rows.Count(r => !sectionIds.Contains(r.StatusId) && counted.Contains(r.Id));

        return new WipSection(value, types, section, counted, claimedInbound);
    }

    /// <summary>
    /// The sentence a refusal or an override names - "the WIP section is full -
    /// 2 of 2 stories and bugs are in it" - built once here from the limit
    /// row's own types, pluralised and joined, so no caller spells a type name.
    /// The dispatcher's fold (HA-90) shares it and adds its own tail.
    /// </summary>
    public static string Sentence(int load, int limit, IReadOnlyList<string> types) =>
        $"the WIP section is full - {load} of {limit} {Pluralize(types)} are in it";

    private static string Pluralize(IReadOnlyList<string> types)
    {
        var plural = types.Select(PluralizeOne).ToList();
        return plural.Count switch
        {
            0 => "issues",
            1 => plural[0],
            2 => $"{plural[0]} and {plural[1]}",
            _ => $"{string.Join(", ", plural.Take(plural.Count - 1))}, and {plural[^1]}",
        };
    }

    private static string PluralizeOne(string type) =>
        type.EndsWith('y') ? $"{type[..^1]}ies" : $"{type}s";
}

/// <summary>
/// One reading of the WIP section, as <see cref="Wip.LoadAsync"/> takes it: the
/// limit, the counted types, the section's columns, and which issues are the
/// load right now. Callers ask it questions rather than re-deriving any of this
/// - <see cref="Inside"/> and <see cref="Counts"/> exist so the move gate
/// (HA-89) and the dispatcher's fold (HA-90) never work out the section or the
/// types for themselves.
/// </summary>
public sealed class WipSection
{
    private readonly IReadOnlySet<int> _section;
    private readonly IReadOnlySet<string> _types;
    private readonly IReadOnlySet<long> _counted;

    internal WipSection(
        int limit, IReadOnlyList<string> types, IReadOnlyList<int> statusIds, IReadOnlySet<long> counted, int claimedInbound)
    {
        Limit = limit;
        Types = types;
        StatusIds = statusIds;
        Load = counted.Count;
        ClaimedInbound = claimedInbound;
        _section = statusIds.ToHashSet();
        _types = types.ToHashSet();
        _counted = counted;
    }

    public int Limit { get; }
    public IReadOnlyList<string> Types { get; }
    public IReadOnlyList<int> StatusIds { get; }
    public int Load { get; }
    public int ClaimedInbound { get; }

    /// <summary>Whether a column counts towards the section - <see cref="StatusIds"/>, as a set.</summary>
    public bool Inside(int statusId) => _section.Contains(statusId);

    /// <summary>Whether an issue type counts towards this limit - <see cref="Types"/>, as a set.</summary>
    public bool Counts(string type) => _types.Contains(type);

    /// <summary>
    /// Whether this issue is one of the ones <see cref="Load"/> was summed from:
    /// in the section, or outside it and holding a live claim whose next column
    /// is in the section. <see cref="Load"/> is always exactly the count of
    /// issues this is true for.
    /// </summary>
    public bool Counted(EfHatchIssue issue) => _counted.Contains(issue.Id);

    public WipDto ToDto() => new(Limit, Types, StatusIds, Load, ClaimedInbound);
}
