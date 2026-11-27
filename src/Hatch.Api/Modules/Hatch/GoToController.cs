using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Go to: the issues a typed number, key prefix or whole key names, the first few,
/// quickly on a board of a million issues.
/// </summary>
/// <remarks>
/// What a query means is <see cref="GoToQuery"/>'s, which follows the console's
/// own rule. The caller's project scope applies through <see cref="HatchContext"/>'s
/// query filters, as it does on every read.
/// </remarks>
[ApiController]
[Route("api/go-to")]
[RequireRole(PersonRole.User, AcceptKey = true)]
public class GoToController(HatchContext db) : ControllerBase
{
    /// <summary>What a caller who named no limit gets.</summary>
    private const int DefaultLimit = 50;

    /// <summary>The most rows one answer may hold.</summary>
    public const int MaxLimit = 50;

    /// <summary>
    /// The first rows a query names. There is no total, because counting every match
    /// of a short number is the slow part and the console only says "keep typing".
    /// </summary>
    /// <param name="q">
    /// Digits alone are a number prefix in every project. Any other single word is a
    /// key prefix, letters and digits only, any case. Anything else finds no rows.
    /// </param>
    /// <param name="projectId">
    /// The projects the caller narrowed to; repeat it for several. A whole key naming
    /// an issue outside them is listed first, marked out of scope. None: every project.
    /// </param>
    /// <param name="limit">How many rows to list, 1 to <see cref="MaxLimit"/>.</param>
    [HttpGet]
    public async Task<ActionResult<GoToDto>> GetGoTo(
        [FromQuery] string? q,
        [FromQuery] List<int>? projectId,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > MaxLimit)
            return BadRequest($"a limit is between 1 and {MaxLimit} - not {limit}");

        var query = GoToQuery.Parse(q);
        if (query is GoToQuery.None) return new GoToDto([], false);

        var projectIds = (projectId ?? []).Distinct().ToList();
        var inScope = db.Issues.AsNoTracking().AsQueryable();
        if (projectIds.Count > 0) inScope = inScope.Where(i => projectIds.Contains(i.ProjectId));

        // One more than asked for says whether there were more, without counting.
        var take = limit + 1;

        var rows = query switch
        {
            GoToQuery.Number number => await ByNumberAsync(inScope, number.Digits, take, ct),
            GoToQuery.Key key => await ByKeyAsync(inScope, key.Wanted, take, ct),
            _ => [],
        };

        var outside = query is GoToQuery.Key whole && projectIds.Count > 0
            ? await OutOfScopeAsync(whole.Wanted, projectIds, ct)
            : [];

        var listed = outside.Concat(rows).ToList();
        return new GoToDto(
            [.. listed.Take(limit).Select(r => r.ToDto())],
            listed.Count > limit);
    }

    /// <summary>
    /// Issues whose number starts with <paramref name="digits"/>, by number then project
    /// key, at most <paramref name="take"/>.
    /// </summary>
    /// <remarks>
    /// One read per digit length, each with its own limit, joined with UNION ALL. A
    /// single OR over the ranges would let Postgres walk the number index from the
    /// first number to the last one wanted: for 99999 that is every number between.
    /// Ranges ascend with digit length, so number order holds across them.
    /// </remarks>
    private static async Task<List<Row>> ByNumberAsync(
        IQueryable<EfHatchIssue> issues, string digits, int take, CancellationToken ct)
    {
        var union = NumberRanges(issues, GoToQuery.Ranges(digits), take);
        return union is null ? [] : await union.ToListAsync(ct);
    }

    private static IQueryable<Row>? NumberRanges(
        IQueryable<EfHatchIssue> issues, IReadOnlyList<(int Lo, int Hi)> ranges, int take)
    {
        IQueryable<Row>? union = null;
        foreach (var (lo, hi) in ranges)
        {
            var branch = First(Read(issues.Where(i => i.Number >= lo && i.Number <= hi)), take);
            union = union is null ? branch : union.Concat(branch);
        }

        return union is null || ranges.Count == 1 ? union : First(union, take);
    }

    /// <summary>The SQL for a number prefix, as the database is asked it.</summary>
    public static string NumberQueryString(IQueryable<EfHatchIssue> issues, string digits, int take) =>
        NumberRanges(issues, GoToQuery.Ranges(digits), take)?.ToQueryString() ?? "";

    /// <summary>
    /// Issues of the projects whose key starts with <paramref name="wanted"/>, and of a
    /// project named by the head of a split, in the range its tail is a number prefix of.
    /// The two kinds cannot overlap: the first reads projects whose key is at least as
    /// long as the word, the second projects whose key is shorter.
    /// </summary>
    private async Task<List<Row>> ByKeyAsync(
        IQueryable<EfHatchIssue> issues, string wanted, int take, CancellationToken ct)
    {
        var upper = wanted.ToUpperInvariant();
        var splits = GoToQuery.Splits(wanted);
        var heads = splits.Select(s => s.Head.ToUpperInvariant()).ToList();

        // Projects are few, and read through the scope filter.
        var projects = await db.Projects.AsNoTracking()
            .Where(p => p.Key.StartsWith(upper) || heads.Contains(p.Key))
            .Select(p => new { p.Id, p.Key })
            .ToListAsync(ct);

        var found = new List<Row>();

        var byPrefix = projects.Where(p => p.Key.StartsWith(upper, StringComparison.Ordinal)).Select(p => p.Id).ToList();
        if (byPrefix.Count > 0)
        {
            found.AddRange(await First(Read(issues.Where(i => byPrefix.Contains(i.ProjectId))), take)
                .ToListAsync(ct));
        }

        foreach (var (head, tail) in splits)
        {
            var project = projects.FirstOrDefault(p => p.Key == head.ToUpperInvariant());
            if (project is null) continue;

            var id = project.Id;
            var union = NumberRanges(issues.Where(i => i.ProjectId == id), GoToQuery.Ranges(tail), take);
            if (union is not null) found.AddRange(await union.ToListAsync(ct));
        }

        return Ordered(found).Take(take).ToList();
    }

    /// <summary>
    /// The issues <paramref name="wanted"/> names whole - <c>ha4</c> is HA-4, and
    /// <c>ha34</c> is HA-34 and HA3-4 - that are not in a project of
    /// <paramref name="projectIds"/>, marked out of scope.
    /// </summary>
    private async Task<List<Row>> OutOfScopeAsync(string wanted, List<int> projectIds, CancellationToken ct)
    {
        var wholes = GoToQuery.Splits(wanted)
            .Select(s => (Head: s.Head.ToUpperInvariant(), Parsed: int.TryParse(s.Tail, out var n) ? n : 0))
            .Where(s => s.Parsed > 0)
            .ToList();

        var found = new List<Row>();
        foreach (var (head, number) in wholes)
        {
            var hit = await Read(db.Issues.AsNoTracking()
                    .Where(i => !projectIds.Contains(i.ProjectId) && i.Project!.Key == head && i.Number == number))
                .FirstOrDefaultAsync(ct);

            if (hit is not null) found.Add(hit with { OutOfScope = true });
        }

        return Ordered(found).ToList();
    }

    private static IEnumerable<Row> Ordered(IEnumerable<Row> rows) =>
        rows.OrderBy(r => r.Number).ThenBy(r => r.ProjectKey, StringComparer.Ordinal);

    /// <summary>What a row needs from the issue, its project and its column, in one join.</summary>
    private static IQueryable<Row> Read(IQueryable<EfHatchIssue> issues) =>
        issues.Select(i => new Row
        {
            ProjectKey = i.Project!.Key,
            Number = i.Number,
            Type = i.Type,
            Title = i.Title,
            StatusId = i.StatusId,
            StatusName = i.Status!.Name,
            StatusColor = i.Status.Color,
        });

    /// <summary>The first rows, in the order every reading of a query is: by number, then project key.</summary>
    private static IQueryable<Row> First(IQueryable<Row> rows, int take) =>
        rows.OrderBy(r => r.Number).ThenBy(r => r.ProjectKey).Take(take);

    /// <remarks>Set by member, not by constructor: EF orders a set of these by a member only when it can see how each was built.</remarks>
    private sealed record Row
    {
        public required string ProjectKey { get; init; }
        public required int Number { get; init; }
        public required string Type { get; init; }
        public required string Title { get; init; }
        public required int StatusId { get; init; }
        public required string StatusName { get; init; }
        public required string StatusColor { get; init; }
        public bool OutOfScope { get; init; }

        public GoToRowDto ToDto() =>
            new(IssueKey.Format(ProjectKey, Number), ProjectKey, Number, Type, Title, OutOfScope,
                StatusId, StatusName, StatusColor);
    }
}
