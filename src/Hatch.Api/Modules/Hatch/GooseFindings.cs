using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One condition a <see cref="FindingCheck"/> is watching, as it stands on this
/// sweep: what it is about (<see cref="Subject"/>), what it is of right now
/// (<see cref="Token"/>), and the project it names, or none.
/// </summary>
public sealed record FindingCandidate(string Subject, string Token, int? ProjectId);

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
/// </summary>
public abstract class FindingCheck(HatchContext db, TimeProvider time) : IGooseCheck
{
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
            return sighting is null || sighting.Token != c.Token ? now : sighting.BeganAt;
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
                    ProjectId = candidate.ProjectId, BeganAt = now,
                });
            }
            else if (sighting.Token != candidate.Token)
            {
                sighting.Token = candidate.Token;
                sighting.ProjectId = candidate.ProjectId;
                sighting.BeganAt = now;
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
        foreach (var candidate in candidates)
        {
            if (!descriptions.TryGetValue(candidate.Subject, out var description)) continue;

            var existing = findings.FirstOrDefault(f => f.Subject == candidate.Subject && f.Token == candidate.Token);
            if (existing is null)
            {
                db.GooseFindings.Add(new EfHatchGooseFinding
                {
                    Check = Name, Subject = candidate.Subject, Token = candidate.Token, ProjectId = candidate.ProjectId,
                    BeganAt = began[candidate.Subject], OpenedAt = now, ClosedAt = null,
                    Sentence = description.Sentence, Evidence = GooseFindingEvidence.Write(description.Evidence),
                });
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

        var closed = findings.Count(f => f.ClosedAt == now);
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
    /// Whether <paramref name="issueId"/> was filed as the bug for some
    /// finding - the one helper every check that files a bug reads, so each
    /// agrees on what "filed from a finding" means. Nothing files one yet (HA-2496);
    /// this exists so a later check that does is never the first to ask.
    /// </summary>
    public static Task<bool> FiledFromAFindingAsync(HatchContext db, long issueId, CancellationToken ct) =>
        db.GooseFindings.IgnoreQueryFilters().AnyAsync(f => f.BugIssueId == issueId, ct);
}
