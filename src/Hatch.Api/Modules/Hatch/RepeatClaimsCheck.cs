using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose's check for one runner claiming one ticket again and again with
/// the ticket not moving - HA-2499. <see cref="LetGo.Count"/> skips several
/// ways a release can end, so a runner can take a ticket many times and nothing
/// on the board counts it; this check counts claims instead, whatever outcome
/// their release carried. It says what it saw and moves nothing: it does not
/// pause the runner, mark the ticket stalled or change what <see cref="LetGo"/>
/// counts.
/// </summary>
/// <remarks>
/// A new commit shows on an issue's trail only as a <c>pull_request_state_changed</c>
/// whose head sha changed or a <c>build_check_changed</c> whose sha changed. A
/// push made before a pull request or a build verdict exists leaves no event,
/// so <see cref="Streak.Read"/> cannot see it.
/// </remarks>
public sealed class RepeatClaimsCheck(HatchContext db, IOptions<HatchOptions> options, TimeProvider time, FindingMessages? messages = null)
    : FindingCheck(db, time, messages)
{
    /// <summary>How recent the newest claim of a streak must be. A constant, not an option.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromHours(2);

    public override string Name => "claim-repeating";

    /// <summary>The streaks this sweep found, by issue key - read once in <see cref="ReadAsync"/>, reused by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<string, (Streak Streak, EfHatchIssue Issue)> standing =
        new Dictionary<string, (Streak, EfHatchIssue)>();

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        standing = new Dictionary<string, (Streak, EfHatchIssue)>();

        var threshold = GooseSettings.RepeatClaims(options.Value);
        if (threshold == 0) return ("turned off", TimeSpan.Zero, []);

        var since = now - Recent;
        var issueIds = await Db.IssueEvents.AsNoTracking()
            .Where(e => e.Kind == EfHatchIssueEvent.ClaimTaken && e.At >= since
                && !e.Issue!.Status!.IsTerminal && !e.Issue.Status.IsDeferred)
            .Select(e => e.IssueId)
            .Distinct()
            .ToListAsync(ct);

        var looked = $"{issueIds.Count} issues with a claim in the last 2 hours";
        if (issueIds.Count == 0) return (looked, TimeSpan.Zero, []);

        var issues = await Db.Issues.AsNoTracking()
            .Include(i => i.Project).Include(i => i.Status)
            .Where(i => issueIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var trails = (await Db.IssueEvents.AsNoTracking()
                .Where(e => issueIds.Contains(e.IssueId))
                .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
                .Select(e => new { e.IssueId, e.Id, e.Kind, e.Payload, e.At })
                .ToListAsync(ct))
            .GroupBy(e => e.IssueId)
            .ToDictionary(g => g.Key, g => g.Select(e => new Streak.Event(e.Id, e.Kind, e.Payload, e.At)).ToList());

        var found = new Dictionary<string, (Streak, EfHatchIssue)>();
        var candidates = new List<FindingCandidate>();
        foreach (var (id, trail) in trails)
        {
            if (Streak.Read(trail) is not { } streak) continue;
            if (streak.Claims < threshold || now - streak.NewestAt >= Recent) continue;
            if (await GooseFindings.FiledFromAFindingAsync(Db, id, ct)) continue;

            var issue = issues[id];
            var key = IssueKey.Format(issue.Project!.Key, issue.Number);
            found[key] = (streak, issue);
            candidates.Add(new FindingCandidate(key, streak.OldestId.ToString(), issue.ProjectId, streak.OldestAt));
        }

        standing = found;

        // Zero: a finding opens on the sweep that sees the claim that makes the streak.
        return (looked, TimeSpan.Zero, candidates);
    }

    protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        // Always present: candidate.Subject is one of standing's own keys, set
        // by ReadAsync earlier in this same sweep.
        var (streak, issue) = standing[candidate.Subject];

        var releases = streak.Releases.Count == 0
            ? "none"
            : string.Join(", ", streak.Releases
                .OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => $"{r.Key} {r.Value}"));

        var evidence = new List<GooseFindingEvidence>
        {
            new("runner", streak.Runner),
            new("claims", streak.Claims.ToString()),
            new("first claim", streak.OldestAt.ToString("O")),
            new("newest claim", streak.NewestAt.ToString("O")),
            new("releases", releases),
            new("column", issue.Status!.Name),
            new("stall mark", string.IsNullOrWhiteSpace(issue.StalledWhy) ? "none" : issue.StalledWhy),
            new("hold", issue.Held ? "held" : "none"),
        };

        return Task.FromResult<(string, IReadOnlyList<GooseFindingEvidence>)>(
            ($"{streak.Runner} has claimed {candidate.Subject} {streak.Claims} times and it has not moved", evidence));
    }

    /// <summary>One runner's unbroken run of claims on one issue, read from its trail.</summary>
    /// <param name="Runner">The name shown: the part of the holder after its first <c> at </c>, or all of it where there is none.</param>
    /// <param name="Claims">How many <c>claim_taken</c> events the streak holds.</param>
    /// <param name="OldestId">The id of the oldest claim event - what a streak is told apart by.</param>
    /// <param name="Releases">How many releases, within the streak's span, carried each outcome; a release with none reads <c>no outcome</c>.</param>
    public sealed record Streak(
        string Runner, int Claims, long OldestId, DateTimeOffset OldestAt, DateTimeOffset NewestAt,
        IReadOnlyDictionary<string, int> Releases)
    {
        /// <summary>One event of an issue's trail, as the fold needs it.</summary>
        public sealed record Event(long Id, string Kind, string? Payload, DateTimeOffset At);

        /// <summary>
        /// The streak that ends at this trail's newest claim, or none. Walks
        /// <paramref name="eventsNewestFirst"/> and stops at the first event
        /// that shows movement: a status change, a pull request whose head sha
        /// changed, a build verdict whose sha changed, or a claim by another
        /// holder. Movement newer than the newest claim means there is no
        /// streak at all - the ticket has moved since. Releases are counted
        /// from the newest claim back to the oldest, whatever their outcome.
        /// </summary>
        public static Streak? Read(IEnumerable<Event> eventsNewestFirst)
        {
            string? holder = null;
            Event? newest = null, oldest = null;
            var claims = 0;
            var releases = new Dictionary<string, int>(StringComparer.Ordinal);
            var pending = new List<string>();

            foreach (var e in eventsNewestFirst)
            {
                if (e.Kind == EfHatchIssueEvent.ClaimTaken)
                {
                    var taker = Holder(e.Payload);
                    if (holder is null) { holder = taker; newest = e; }
                    else if (taker != holder) break;

                    claims++;
                    oldest = e;

                    // Every release met so far lies between this claim and the newest.
                    foreach (var outcome in pending) releases[outcome] = releases.GetValueOrDefault(outcome) + 1;
                    pending.Clear();
                    continue;
                }

                if (IsMovement(e)) break;

                if (e.Kind == EfHatchIssueEvent.ClaimReleased)
                    pending.Add(LetGo.ReleaseOutcome(e.Payload) ?? "no outcome");
            }

            return oldest is null ? null : new Streak(RunnerName(holder!), claims, oldest.Id, oldest.At, newest!.At, releases);
        }

        private static bool IsMovement(Event e) => e.Kind switch
        {
            EfHatchIssueEvent.StatusChanged => true,
            EfHatchIssueEvent.PullRequestStateChanged => ShaChanged(e.Payload, "headSha"),
            EfHatchIssueEvent.BuildCheckChanged => ShaChanged(e.Payload, "sha"),
            _ => false,
        };

        /// <summary>Whether <c>to.&lt;field&gt;</c> names a sha that <c>from.&lt;field&gt;</c> did not - a null or missing <c>from</c> is a first sight, so a change.</summary>
        private static bool ShaChanged(string? payload, string field)
        {
            if (string.IsNullOrWhiteSpace(payload)) return false;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var to = Sha(doc.RootElement, "to", field);
                return to is not null && to != Sha(doc.RootElement, "from", field);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string? Sha(JsonElement root, string side, string field) =>
            root.TryGetProperty(side, out var s) && s.ValueKind == JsonValueKind.Object
                && s.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } sha
                    ? sha
                    : null;

        /// <summary>The <c>to</c> of a <c>claim_taken</c> payload - "&lt;key&gt; at &lt;runner&gt;" - or empty where there is none.</summary>
        private static string Holder(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return "";

            try
            {
                using var doc = JsonDocument.Parse(payload);
                return doc.RootElement.TryGetProperty("to", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            }
            catch (JsonException)
            {
                return "";
            }
        }

        private static string RunnerName(string holder)
        {
            const string at = " at ";
            var i = holder.IndexOf(at, StringComparison.Ordinal);
            return i < 0 ? holder : holder[(i + at.Length)..];
        }
    }
}
