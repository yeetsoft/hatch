using System.Globalization;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose's finding for a chat workspace whose connection keeps failing to
/// open: one <see cref="EfHatchGooseFinding"/> per workspace that has been
/// failing for <see cref="GooseSettings.ChatListenBound"/> or longer - HA-2807.
/// </summary>
/// <remarks>
/// <para>It reads the state the listener keeps: <c>ListenFailingSince</c> on a
/// workspace. That column is the run's own start, so
/// <see cref="FindingCandidate.Began"/> is read from it and the clock does not
/// restart on a sweep. The listener clears it on a connection that opens, and
/// removing a workspace or its app-level token removes the candidate, so the
/// base class closes the finding with no code of its own here.</para>
///
/// <para>The bound is applied by the base class. A zero bound turns the check
/// off by returning no candidate at all, which closes what is open.</para>
///
/// <para>It names no project: a chat workspace belongs to the install, not to
/// a project, so the finding is about the board and is seen by everyone who
/// can see findings. Unlike <see cref="ChatSendsFailingCheck"/>, it keeps no
/// room out - sending still works when listening does not, so the workspace's
/// own room can hear it.</para>
/// </remarks>
public sealed class ChatListenFailingCheck(HatchContext db, IOptions<HatchOptions> options, TimeProvider time, FindingMessages? messages = null)
    : FindingCheck(db, time, messages)
{
    /// <summary>The check's name.</summary>
    public const string CheckName = "chat-listen-failing";

    private const string FailingToken = "failing";
    private const string WorkspacePrefix = "workspace:";

    public override string Name => CheckName;

    /// <summary>What this sweep read, by subject - set in <see cref="ReadAsync"/>, read back by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<string, Failing> failing = new Dictionary<string, Failing>();

    private sealed record Failing(string Name, string? Said);

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        failing = new Dictionary<string, Failing>();

        var bound = GooseSettings.ChatListenBound(options.Value);
        if (bound == TimeSpan.Zero) return ("turned off", bound, []);

        var workspaces = await Db.ChatWorkspaces.AsNoTracking()
            .Where(w => w.ListenFailingSince != null && w.AppToken != null)
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Name, w.ListenFailingSince, w.ListenLastRefusal })
            .ToListAsync(ct);

        var found = new Dictionary<string, Failing>(StringComparer.Ordinal);
        var candidates = new List<FindingCandidate>();

        foreach (var w in workspaces)
        {
            var subject = WorkspacePrefix + w.Id.ToString(CultureInfo.InvariantCulture);
            found[subject] = new Failing(w.Name, w.ListenLastRefusal);
            candidates.Add(new FindingCandidate(subject, FailingToken, null, w.ListenFailingSince));
        }

        failing = found;

        return ($"{workspaces.Count} workspaces not listening", bound, candidates);
    }

    protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        var seen = failing[candidate.Subject];

        var said = string.IsNullOrWhiteSpace(seen.Said) ? "" : $" Slack said: {seen.Said.Trim()}";
        var sentence = $"Hatch has not been able to listen to workspace {seen.Name} since {ChatText.When(began)}.{said}";

        IReadOnlyList<GooseFindingEvidence> evidence = [new GooseFindingEvidence("Workspace", $"workspace {seen.Name}")];

        return Task.FromResult((sentence, evidence));
    }
}
