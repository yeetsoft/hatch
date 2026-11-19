using System.Globalization;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// A person is told once a project has ready work and a live, idle runner
/// able to take some of it has stood that way for longer than
/// <see cref="GooseSettings.UnclaimedBound"/> - HA-2497's first check. The
/// clock is the project's, not the step's: any claim taken anywhere in the
/// project - even one taken and released again between two sweeps - restarts
/// it, since <see cref="Token"/> is the project's newest <c>claim_taken</c>
/// event.
/// </summary>
public sealed class ReadyUnclaimedCheck(HatchContext db, ReadyWork work, IOptions<HatchOptions> options, TimeProvider time)
    : FindingCheck(db, time)
{
    public override string Name => "ready-unclaimed";

    /// <summary>This sweep's own projects, by key - set in <see cref="ReadAsync"/>, read back by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<string, ReadyProject> byProject = new Dictionary<string, ReadyProject>();

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var bound = GooseSettings.UnclaimedBound(options.Value);
        var snapshot = await work.ReadAsync(ct);
        byProject = snapshot.ByProject;

        if (bound == TimeSpan.Zero) return (snapshot.Looked, bound, []);

        var candidates = snapshot.ByProject.Values
            .Where(p => p.Steps.Count > 0 && p.IdlePeers.Count > 0)
            .Select(p => new FindingCandidate(
                p.ProjectKey,
                p.NewestClaim is { } claim ? claim.Id.ToString(CultureInfo.InvariantCulture) : "none",
                p.ProjectId))
            .ToList();

        return (snapshot.Looked, bound, candidates);
    }

    protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        // Always present: candidate.Subject is one of byProject's own keys,
        // set by ReadAsync earlier in this same sweep.
        var project = byProject[candidate.Subject];
        var count = project.Steps.Count;

        var sentence = $"{project.ProjectKey} has {count} {Step(count)} ready and no runner has taken one";

        var evidence = new List<GooseFindingEvidence> { new("steps", StepsPhrase(project.Steps, count)) };
        foreach (var peer in project.IdlePeers)
            evidence.Add(new GooseFindingEvidence(peer.Name, peer.LastSeenAt.ToString("O")));
        evidence.Add(new GooseFindingEvidence(
            "newest claim", project.NewestClaim is { } claim ? $"{claim.Key} at {claim.At:O}" : "none"));

        return Task.FromResult<(string, IReadOnlyList<GooseFindingEvidence>)>((sentence, evidence));
    }

    internal static string Step(int count) => count == 1 ? "step" : "steps";

    internal static string StepsPhrase(IReadOnlyList<Dispatch.ReadyStep> steps, int count)
    {
        var keys = steps.Take(10).Select(s => s.Key);
        return $"{string.Join(", ", keys)} ({count})";
    }
}

/// <summary>
/// A person is told once a project has ready work that no live runner can
/// take at all has stood that way for longer than
/// <see cref="GooseSettings.NoRunnerBound"/> - HA-2497's second check, open
/// for exactly the projects <see cref="ReadyUnclaimedCheck"/> is not: one
/// with an idle taker is the first check's, one with none at all is this
/// one's. The token is a constant - the condition is "ready and taken by
/// nobody", not any one step or claim, so nothing about it restarts the
/// clock the way a claim does for <see cref="ReadyUnclaimedCheck"/>.
/// </summary>
public sealed class ReadyNoRunnerCheck(HatchContext db, ReadyWork work, IOptions<HatchOptions> options, TimeProvider time)
    : FindingCheck(db, time)
{
    public override string Name => "ready-no-runner";

    private const string NoTaker = "no-taker";

    /// <summary>This sweep's own projects and runner roster, set in <see cref="ReadAsync"/>, read back by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<string, ReadyProject> byProject = new Dictionary<string, ReadyProject>();
    private IReadOnlyList<Dispatch.KnownRunner> knownRunners = [];
    private int liveRunnerCount;

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var bound = GooseSettings.NoRunnerBound(options.Value);
        var snapshot = await work.ReadAsync(ct);
        byProject = snapshot.ByProject;
        knownRunners = snapshot.KnownRunners;
        liveRunnerCount = snapshot.LiveRunnerCount;

        if (bound == TimeSpan.Zero) return (snapshot.Looked, bound, []);

        var candidates = snapshot.ByProject.Values
            .Where(p => p.Steps.Count > 0 && p.Steps.All(s => s.Takers.Count == 0))
            .Select(p => new FindingCandidate(p.ProjectKey, NoTaker, p.ProjectId))
            .ToList();

        return (snapshot.Looked, bound, candidates);
    }

    protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        // Always present: candidate.Subject is one of byProject's own keys,
        // set by ReadAsync earlier in this same sweep.
        var project = byProject[candidate.Subject];
        var count = project.Steps.Count;
        var pronoun = count == 1 ? "it" : "them";

        var sentence = $"{project.ProjectKey} has {count} {ReadyUnclaimedCheck.Step(count)} ready and no live runner can take {pronoun}";

        var evidence = new List<GooseFindingEvidence> { new("steps", ReadyUnclaimedCheck.StepsPhrase(project.Steps, count)) };

        if (liveRunnerCount == 0)
        {
            evidence.Add(new GooseFindingEvidence("live runners", "no runner is live"));
            foreach (var runner in knownRunners.OrderBy(r => r.Name, StringComparer.Ordinal))
                evidence.Add(new GooseFindingEvidence(runner.Name, runner.LastSeenAt.ToString("O")));
        }
        else
        {
            var reasonsByRunner = project.Steps
                .SelectMany(s => s.Untaken)
                .GroupBy(u => u.Runner, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var group in reasonsByRunner)
                evidence.Add(new GooseFindingEvidence(group.Key, string.Join("; ", group.Select(u => u.Reason).Distinct(StringComparer.Ordinal))));
        }

        return Task.FromResult<(string, IReadOnlyList<GooseFindingEvidence>)>((sentence, evidence));
    }
}
