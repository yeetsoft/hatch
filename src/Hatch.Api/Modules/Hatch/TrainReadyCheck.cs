using System.Text.Json;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose's own clock on a stuck merge train - a second one beside
/// <see cref="TrainWatch"/>'s, which only ever passes a driver over. This one
/// opens a <see cref="EfHatchGooseFinding"/> a person can read, with the
/// evidence beside it, once a project's head has been judged
/// <see cref="WorkKinds.Merge"/> or <see cref="WorkKinds.Update"/> for longer
/// than <see cref="GooseSettings.TrainReadyBound"/> - HA-2496.
/// </summary>
public sealed class TrainReadyCheck(
    HatchContext db, Dispatch dispatch, Runners runners, IAuthService keys, IOptions<HatchOptions> options, TimeProvider time)
    : FindingCheck(db, time)
{
    public override string Name => "train-ready";

    /// <summary>The heads this sweep saw, by project id - read once in <see cref="ReadAsync"/>, reused by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<int, Dispatch.TrainHead> headsByProject = new Dictionary<int, Dispatch.TrainHead>();

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var bound = GooseSettings.TrainReadyBound(options.Value);

        var heads = await dispatch.TrainHeadsAsync(now, ct);
        headsByProject = heads.ToDictionary(h => h.ProjectId);

        if (heads.Count == 0) return ("no project has a merge train head", bound, []);

        var keysByProject = await Db.Projects.AsNoTracking()
            .Where(p => heads.Select(h => h.ProjectId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Key, ct);

        var looked = string.Join("; ", heads.Select(h => $"{keysByProject.GetValueOrDefault(h.ProjectId, "?")}: {h.Key} judged {h.Kind}"));

        // Zero turns the check off: every open finding it holds closes, the
        // base reconciler's own doing once it sees no candidate at all.
        if (bound == TimeSpan.Zero) return (looked, bound, []);

        var candidates = heads
            .Where(h => h.Kind is WorkKinds.Merge or WorkKinds.Update)
            .ToList();

        var filed = new List<Dispatch.TrainHead>();
        foreach (var head in candidates)
        {
            if (await GooseFindings.FiledFromAFindingAsync(Db, head.IssueId, ct)) filed.Add(head);
        }

        return (
            looked, bound,
            candidates
                .Except(filed)
                .Select(h => new FindingCandidate(keysByProject.GetValueOrDefault(h.ProjectId, "?"), h.Key, h.ProjectId))
                .ToList());
    }

    protected override async Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        var now = Time.GetUtcNow();

        // Always present: candidate.ProjectId is one of headsByProject's own
        // keys, set by ReadAsync earlier in this same sweep.
        var head = headsByProject[candidate.ProjectId!.Value];

        var drivers = await runners.ProjectDriversAsync(Db, keys, now, ct);
        var driver = drivers.GetValueOrDefault(head.ProjectId);

        var statuses = await dispatch.OrderedStatusesAsync(ct);
        var merges = Columns.Merges(statuses);

        DateTimeOffset? lastDriverEventAt = null;
        if (driver.Name is { } driverName && merges is not null)
        {
            lastDriverEventAt = await Db.IssueEvents.AsNoTracking()
                .Where(e => e.Actor == driverName && e.Issue!.ProjectId == head.ProjectId && e.Issue.StatusId == merges.Id)
                .OrderByDescending(e => e.At)
                .Select(e => (DateTimeOffset?)e.At)
                .FirstOrDefaultAsync(ct);
        }

        var stalls = await Db.IssueEvents.AsNoTracking()
            .Where(e => e.IssueId == head.IssueId && e.Kind == EfHatchIssueEvent.DriverStalled && e.At >= began)
            .Select(e => e.Payload)
            .ToListAsync(ct);
        var passedOverRunners = stalls
            .Select(PassedOver)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var evidence = new List<GooseFindingEvidence>
        {
            new("head", head.Key),
            new("judged", head.Kind),
            new("driver", driver.Name ?? "no live runner is eligible to drive"),
            new("last driver event", lastDriverEventAt is { } at ? at.ToString("O") : "never"),
            new("behind", head.Behind.ToString()),
            new("pass-overs", stalls.Count.ToString()),
            new("passed-over runners", passedOverRunners.Count == 0 ? "none" : string.Join(", ", passedOverRunners)),
        };

        return (head.Sentence, evidence);
    }

    /// <summary>The runner a <c>driver_stalled</c> event's payload names as passed over, or null where the payload does not say.</summary>
    private static string? PassedOver(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("passedOver", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
