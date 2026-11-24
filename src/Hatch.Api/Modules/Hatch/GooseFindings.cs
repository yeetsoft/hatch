using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One condition a <see cref="FindingCheck"/> is watching, as it stands on this
/// sweep: what it is about (<see cref="Subject"/>), what it is of right now
/// (<see cref="Token"/>), and the project it names, or none. <see cref="Began"/>
/// is when the condition began where the check can read that from its own
/// evidence; none means it began on the sweep that first saw it.
/// </summary>
public sealed record FindingCandidate(string Subject, string Token, int? ProjectId, DateTimeOffset? Began = null);

/// <summary>
/// A <see cref="IGooseCheck"/> that watches a set of conditions and opens a
/// stored <see cref="EfHatchGooseFinding"/> for one that has stood longer than
/// its own bound - see HA-2496. A subclass says only what it is watching
/// (<see cref="ReadAsync"/>) and, once a condition has stood long enough, what
/// to say about it (<see cref="DescribeAsync"/>); this class owns the sighting
/// and the finding, so two checks can never disagree about when a clock starts
/// or when a row opens.
///
/// <see cref="RunAsync"/> reads everything it needs before it changes anything,
/// so a check that throws while reading touches neither table - <see
/// cref="GooseJob"/> already catches the throw and tries again next sweep. The
/// reconcile itself awaits nothing, so two sweeps running at once (which the
/// goose's own lock already rules out) could never interleave their writes.
/// What follows it - queueing each finding's chat message through
/// <see cref="FindingMessages"/> - does await, and is the one part a throw can
/// leave half done; see the comment where it starts.
/// </summary>
public abstract class FindingCheck(HatchContext db, TimeProvider time, FindingMessages? messages = null) : IGooseCheck
{
    /// <summary>
    /// How long after a finding closes that every sweep still tries to say it
    /// ended. <see cref="GooseJob"/> catches a check's throw and still saves, so a
    /// failure while queueing the ended message would otherwise save the close and
    /// lose the message for good: the next sweep no longer sees the close as its own.
    /// </summary>
    private static readonly TimeSpan EndedRetry = TimeSpan.FromHours(1);

    /// <summary>
    /// The room, or every room in the workspace, a finding must not be sent to -
    /// a finding about a room's own failing sends is not sent to it. Nothing by
    /// default.
    /// </summary>
    protected virtual (int? RoomId, int? WorkspaceId) KeepOut(EfHatchGooseFinding finding) => (null, null);

    /// <summary>The same context <see cref="RunAsync"/> reconciles with - a subclass's own reads join the same unit of work.</summary>
    protected HatchContext Db => db;

    /// <summary>The same clock <see cref="RunAsync"/> reads <c>now</c> from.</summary>
    protected TimeProvider Time => time;

    public abstract string Name { get; }

    /// <summary>
    /// What this check is watching right now: a sentence for a person reading
    /// the run, the bound a condition must stand before it opens a finding,
    /// and every condition currently true - one per <see cref="FindingCandidate.Subject"/>.
    /// An empty list - including because the check's own setting turns it off -
    /// closes every finding this check holds open and deletes every sighting.
    /// </summary>
    protected abstract Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// What to say about <paramref name="candidate"/>, now that its condition
    /// has stood since <paramref name="began"/> for at least this check's own
    /// bound. Called once to open a finding and again on every later sweep the
    /// condition is still true, to refresh it - never to decide whether to.
    /// </summary>
    protected abstract Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct);

    public async Task<GooseObservation> RunAsync(CancellationToken ct)
    {
        // ---- Read everything, with no mutation in between: a throw anywhere
        // in this half, including from DescribeAsync, leaves both tables
        // exactly as this sweep found them. ----

        var now = time.GetUtcNow();
        var (looked, bound, candidates) = await ReadAsync(now, ct);

        var sightings = await db.GooseSightings.Where(s => s.Check == Name).ToListAsync(ct);
        var findings = await db.GooseFindings.Where(f => f.Check == Name && f.ClosedAt == null).ToListAsync(ct);

        // When this condition began: the existing sighting's, unless it names
        // another token - a new condition, not a continuation of the old one -
        // in which case it begins now.
        var began = candidates.ToDictionary(c => c.Subject, c =>
        {
            var sighting = sightings.FirstOrDefault(s => s.Subject == c.Subject);
            return sighting is null || sighting.Token != c.Token ? c.Began ?? now : sighting.BeganAt;
        });

        // Described only once a condition has stood its bound - what makes
        // describing itself the only async work the open/refresh half below
        // would otherwise need.
        var descriptions = new Dictionary<string, (string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)>();
        foreach (var candidate in candidates)
        {
            if (now - began[candidate.Subject] < bound) continue;
            descriptions[candidate.Subject] = await DescribeAsync(candidate, began[candidate.Subject], ct);
        }

        // ---- Mutate, with no awaits in between: once this half starts, an
        // exception from a later statement can only ever come from code that
        // reads what is already in memory, never from the database again. ----

        var activeSubjects = candidates.Select(c => c.Subject).ToHashSet();
        foreach (var candidate in candidates)
        {
            var sighting = sightings.FirstOrDefault(s => s.Subject == candidate.Subject);
            if (sighting is null)
            {
                db.GooseSightings.Add(new EfHatchGooseSighting
                {
                    Check = Name, Subject = candidate.Subject, Token = candidate.Token,
                    ProjectId = candidate.ProjectId, BeganAt = candidate.Began ?? now,
                });
            }
            else if (sighting.Token != candidate.Token)
            {
                sighting.Token = candidate.Token;
                sighting.ProjectId = candidate.ProjectId;
                sighting.BeganAt = candidate.Began ?? now;
            }
            else
            {
                sighting.ProjectId = candidate.ProjectId;
            }
        }

        foreach (var stale in sightings.Where(s => !activeSubjects.Contains(s.Subject)))
            db.GooseSightings.Remove(stale);

        // A finding closes the moment nothing matches both its subject and its
        // token: either the subject dropped out entirely, or it is still a
        // candidate but of a different token - a new condition, not this one
        // still standing.
        foreach (var finding in findings)
        {
            if (!candidates.Any(c => c.Subject == finding.Subject && c.Token == finding.Token))
                finding.ClosedAt = now;
        }

        var opened = 0;
        var refreshed = 0;
        var standing = new List<EfHatchGooseFinding>(findings.Where(f => f.ClosedAt is null));
        foreach (var candidate in candidates)
        {
            if (!descriptions.TryGetValue(candidate.Subject, out var description)) continue;

            var existing = findings.FirstOrDefault(f => f.Subject == candidate.Subject && f.Token == candidate.Token);
            if (existing is null)
            {
                var added = new EfHatchGooseFinding
                {
                    Check = Name, Subject = candidate.Subject, Token = candidate.Token, ProjectId = candidate.ProjectId,
                    BeganAt = began[candidate.Subject], OpenedAt = now, ClosedAt = null,
                    Sentence = description.Sentence, Evidence = GooseFindingEvidence.Write(description.Evidence),
                };
                db.GooseFindings.Add(added);
                standing.Add(added);
                opened++;
            }
            else
            {
                existing.ProjectId = candidate.ProjectId;
                existing.Sentence = description.Sentence;
                existing.Evidence = GooseFindingEvidence.Write(description.Evidence);
                refreshed++;
            }
        }

        var justClosed = findings.Where(f => f.ClosedAt == now).ToList();
        var closed = justClosed.Count;

        // ---- Say it in chat. Unlike the half above, this awaits: it reads the
        // rooms. The goose saves once, after every check, so the rows queued here
        // land with the sweep's own writes - and a throw here is caught by the
        // job, which still saves what was changed above. That is why a finding
        // that closed is retried for EndedRetry, and why nothing here may be the
        // only thing that remembers what is left to say. ----

        if (messages is not null)
        {
            // Every sweep, new finding or old: equal content changes nothing, so
            // a sweep that changes nothing sends nothing.
            foreach (var finding in standing)
            {
                var (roomId, workspaceId) = KeepOut(finding);
                await messages.StandingAsync(finding, ct, roomId, workspaceId);
            }

            var cutoff = now - EndedRetry;
            var lostSweeps = await db.GooseFindings
                .Where(f => f.Check == Name && f.ClosedAt != null && f.ClosedAt >= cutoff)
                .ToListAsync(ct);

            foreach (var finding in justClosed.Concat(lostSweeps.Where(f => f.ClosedAt != now)))
                await messages.ClosedAsync(finding, ct);
        }

        var concluded = candidates.Count == 0
            ? "nothing standing"
            : $"{opened} opened, {refreshed} refreshed, {closed} closed of {candidates.Count} standing";

        return new GooseObservation(looked, concluded);
    }
}

/// <summary>
/// What every <see cref="FindingCheck"/> shares about a stored
/// <see cref="EfHatchGooseFinding"/>, read-only.
/// </summary>
public static class GooseFindings
{
    /// <summary>
    /// The issue a finding names, as its key, or null. The token is the subject's
    /// own handle. It names an issue only when it parses as a key in the finding's
    /// own project (<paramref name="projectKey"/>) - a token is never trusted to
    /// point across projects. A standing-still finding's subject is the issue's
    /// key and its token is a column and a claim, so it reads the subject, held to
    /// the same rule. The attention read and the chat message both ask this, so
    /// they agree on what a finding is about.
    /// </summary>
    public static string? IssueKey(EfHatchGooseFinding finding, string? projectKey)
    {
        var handle = finding.Check == StandingStillCheck.CheckName ? finding.Subject : finding.Token;

        return projectKey is not null
            && global::Hatch.Api.Modules.Hatch.IssueKey.TryParse(handle, out var tokenProject, out var number)
            && string.Equals(tokenProject, projectKey, StringComparison.OrdinalIgnoreCase)
            ? global::Hatch.Api.Modules.Hatch.IssueKey.Format(projectKey, number)
            : null;
    }

    /// <summary>
    /// Whether <paramref name="issueId"/> was filed as the bug for some
    /// finding - the one helper every check that files a bug reads, so each
    /// agrees on what "filed from a finding" means. A person files one, through
    /// <see cref="GooseFindingBugController"/> (HA-2516), and the goose never does;
    /// every check asks this so a finding does not watch its own fix.
    /// </summary>
    public static Task<bool> FiledFromAFindingAsync(HatchContext db, long issueId, CancellationToken ct) =>
        db.GooseFindings.IgnoreQueryFilters().AnyAsync(f => f.BugIssueId == issueId, ct);
}
