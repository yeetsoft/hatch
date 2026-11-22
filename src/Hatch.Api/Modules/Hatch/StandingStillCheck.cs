using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A person is told when a ticket has stood in one column for hours with no
/// runner on it and nobody it is waiting for, whatever the cause - HA-2664.
/// The goose's other checks each watch one named cause; this one is a clock on
/// standing still itself, so the next cause nobody has named is not missed.
/// </summary>
/// <remarks>
/// <para><b>What is watched.</b> Leaf issues in the columns from the
/// implementation column up to, but not including, the first terminal one - the
/// merge column left out, since <see cref="TrainReadyCheck"/> watches it, and a
/// deferred column left out, since it is no lane. A leaf is an issue with no
/// child the dispatcher counts as open work, so a task waiting on its story's
/// shared pull request is not watched, and its story is once every child of it
/// is done or waiting on it.</para>
///
/// <para><b>What keeps an issue out.</b> A live claim, a row folded for a
/// person (<see cref="BlockedKinds.Person"/>), a review row waiting for an owner
/// or approver (<see cref="ScanRow.WaitsOnApprover"/>), and an issue filed as the
/// bug for a finding. The check adds no rule of its own about what holds a row:
/// it reads the scan's kind and the review judge's mark, never the words of a
/// sentence, so a reworded sentence cannot change what is watched.</para>
///
/// <para><b>The clock</b> is the reconciler's. It starts on the first sweep that
/// sees the issue as a candidate. The token is the column and the issue's newest
/// <c>claim_taken</c> event, so a move or a claim - even one taken and released
/// between two sweeps - is a new condition and restarts it. A candidate that
/// drops out, for any of the reasons above, loses its sighting, so the clock
/// starts again when it comes back.</para>
///
/// <para>It says what it saw and moves nothing, claims nothing and files
/// nothing. It reads the scan <see cref="ReadyWork"/> already makes, so a sweep
/// does not scan the board again for it.</para>
/// </remarks>
public sealed class StandingStillCheck(HatchContext db, ReadyWork work, IOptions<HatchOptions> options, TimeProvider time)
    : FindingCheck(db, time)
{
    /// <summary>The check's name, which also marks a finding whose subject is an issue's key - see <see cref="AttentionController"/>.</summary>
    public const string CheckName = "standing-still";

    public override string Name => CheckName;

    /// <summary>The rows this sweep's candidates came from, by issue key - set in <see cref="ReadAsync"/>, read back by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<string, ScanRow> standing = new Dictionary<string, ScanRow>();

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        standing = new Dictionary<string, ScanRow>();

        var bound = GooseSettings.StandingBound(options.Value);
        if (bound == TimeSpan.Zero) return ("turned off", bound, []);

        var snapshot = await work.ReadAsync(ct);
        if (snapshot.Board is not { } scan) return ("the board could not be read", bound, []);

        var watched = WatchedColumns(scan.Statuses);
        var rows = scan.Rows.Where(r => watched.Contains(r.From.Id) && IsLeaf(scan, r)).ToList();

        var kept = new List<ScanRow>();
        foreach (var row in rows)
        {
            if (HeldBack(scan, row)) continue;
            if (await GooseFindings.FiledFromAFindingAsync(Db, row.Issue.Id, ct)) continue;
            kept.Add(row);
        }

        var looked = $"{rows.Count} leaf issues in the working columns, {kept.Count} with nothing set to move them";
        if (kept.Count == 0) return (looked, bound, []);

        var ids = kept.Select(r => r.Issue.Id).ToList();
        var newestClaims = (await Db.IssueEvents.AsNoTracking()
                .Where(e => e.Kind == EfHatchIssueEvent.ClaimTaken && ids.Contains(e.IssueId))
                .GroupBy(e => e.IssueId)
                .Select(g => new { IssueId = g.Key, Id = g.Max(e => e.Id) })
                .ToListAsync(ct))
            .ToDictionary(c => c.IssueId, c => c.Id);

        var found = new Dictionary<string, ScanRow>(StringComparer.Ordinal);
        var candidates = new List<FindingCandidate>();
        foreach (var row in kept)
        {
            var key = IssueKey.Format(row.Issue.Project!.Key, row.Issue.Number);
            if (found.ContainsKey(key)) continue;

            found[key] = row;
            var claim = newestClaims.TryGetValue(row.Issue.Id, out var id) ? id : 0;
            candidates.Add(new FindingCandidate(
                key, $"{row.From.Id.ToString(CultureInfo.InvariantCulture)}:{claim.ToString(CultureInfo.InvariantCulture)}",
                row.Issue.ProjectId));
        }

        standing = found;
        return (looked, bound, candidates);
    }

    /// <summary>
    /// The columns from implementation up to, not including, the first terminal
    /// one, less the merge column. None where the board has no implementation column.
    /// </summary>
    private static HashSet<int> WatchedColumns(List<EfHatchStatus> statuses)
    {
        if (Columns.Implementation(statuses) is not { } implementation) return [];

        var merges = Columns.Merges(statuses);
        var board = Columns.Board(statuses);
        var start = board.FindIndex(s => s.Id == implementation.Id);
        if (start < 0) return [];

        return board.Skip(start)
            .TakeWhile(s => !s.IsTerminal)
            .Where(s => s.Id != merges?.Id)
            .Select(s => s.Id)
            .ToHashSet();
    }

    /// <summary>
    /// Whether no child of this issue is work the dispatcher counts as open: an
    /// epic is read as <see cref="FamilyGate.EpicOpenChildren"/> reads it, anything
    /// else as <see cref="FamilyGate.OpenWorkChildren"/> does, which leaves out a
    /// task waiting on its story's pull request. A task that is itself waiting on
    /// its story's pull request moves when the story does, so it is not watched.
    /// </summary>
    private static bool IsLeaf(Scan scan, ScanRow row)
    {
        var id = row.Issue.Id;
        if (scan.Family.IsWaiting(id)) return false;

        return IssueTypeKinds.RunsChildren(row.Issue.Type)
            ? scan.Family.EpicOpenChildren(id, scan.Statuses).Count == 0
            : scan.Family.OpenWorkChildren(id).Count == 0;
    }

    /// <summary>Whether something is already set to move this row: a live claim, a person, or an approver.</summary>
    private static bool HeldBack(Scan scan, ScanRow row)
    {
        // The claim itself, not the row's kind: an earlier fold can say something else first.
        if (scan.Claims.Claims is { } claims && claims.IsLive(ClaimSnapshot.Of(row.Issue), scan.Claims.Now)) return true;

        return row.BlockedKind == BlockedKinds.Person || row.WaitsOnApprover;
    }

    protected override async Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        // Always present: candidate.Subject is one of standing's own keys, set
        // by ReadAsync earlier in this same sweep.
        var row = standing[candidate.Subject];
        var issue = row.Issue;

        var trail = await Db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == issue.Id
                && (e.Kind == EfHatchIssueEvent.StatusChanged || e.Kind == EfHatchIssueEvent.Created
                    || e.Kind == EfHatchIssueEvent.ClaimTaken || e.Kind == EfHatchIssueEvent.ClaimReleased
                    || e.Kind == EfHatchIssueEvent.ClaimCleared))
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.Kind, e.Payload, e.At })
            .ToListAsync(ct);

        var entered = trail.FirstOrDefault(e => e.Kind == EfHatchIssueEvent.StatusChanged)
            ?? trail.FirstOrDefault(e => e.Kind == EfHatchIssueEvent.Created);

        var evidence = new List<GooseFindingEvidence>
        {
            new("column", entered is null ? row.From.Name : $"{row.From.Name}, entered {entered.At:O}"),
            new("queue", row.Blocked ?? "clear"),
            new("last claim", LastClaim(trail.Select(e => (e.Id, e.Kind, e.Payload, e.At)).ToList())),
            new("pull request", string.IsNullOrWhiteSpace(issue.PullRequestUrl) ? "none recorded" : issue.PullRequestUrl),
        };

        return ($"{candidate.Subject} has stood in {row.From.Name} with nothing set to move it", evidence);
    }

    /// <summary>
    /// Who took the issue's newest claim and when, and when it was released, from
    /// its trail newest first; or that there was none.
    /// </summary>
    private static string LastClaim(IReadOnlyList<(long Id, string Kind, string? Payload, DateTimeOffset At)> trailNewestFirst)
    {
        var taken = trailNewestFirst.FirstOrDefault(e => e.Kind == EfHatchIssueEvent.ClaimTaken);
        if (taken.Kind is null) return "none";

        var holder = Field(taken.Payload, "to") is { Length: > 0 } to ? to : "a holder not named";

        var released = trailNewestFirst
            .Where(e => e.Kind is EfHatchIssueEvent.ClaimReleased or EfHatchIssueEvent.ClaimCleared && e.Id > taken.Id)
            .OrderBy(e => e.Id)
            .Select(e => ((long, string, string?, DateTimeOffset)?)e)
            .FirstOrDefault();

        return released is { } r
            ? $"{holder}, taken {taken.At:O}, released {r.Item4:O}"
            : $"{holder}, taken {taken.At:O}, not released";
    }

    private static string? Field(string? payload, string name)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
