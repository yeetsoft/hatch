using System.Globalization;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The goose's finding for a chat workspace or room whose messages keep failing
/// to send: one <see cref="EfHatchGooseFinding"/> per workspace and per room
/// that has been failing for <see cref="GooseSettings.ChatSendBound"/> or longer
/// - HA-2777.
/// </summary>
/// <remarks>
/// <para>It reads the state the sender keeps: <c>SendFailingSince</c> on a
/// workspace, where the account or the network is failing, and on a room, where
/// one channel is. That column is the condition's own start, so
/// <see cref="FindingCandidate.Began"/> is read from it and the clock does not
/// restart on a sweep. The sender clears it on a send that works, and removing a
/// workspace or room removes the candidate, so the base class closes the finding
/// with no code of its own here.</para>
///
/// <para>The bound is applied by the base class. A zero bound turns the check
/// off by returning no candidate at all, which closes what is open.</para>
///
/// <para>It names no project: a chat workspace or room belongs to the install,
/// not to a project, so the finding is about the board and is seen by everyone
/// who can see findings.</para>
/// </remarks>
public sealed class ChatSendsFailingCheck(HatchContext db, IOptions<HatchOptions> options, TimeProvider time, FindingMessages? messages = null)
    : FindingCheck(db, time, messages)
{
    /// <summary>The check's name.</summary>
    public const string CheckName = "chat-sends-failing";

    private const string FailingToken = "failing";
    private const string WorkspacePrefix = "workspace:";
    private const string RoomPrefix = "room:";

    public override string Name => CheckName;

    /// <summary>What this sweep read, by subject - set in <see cref="ReadAsync"/>, read back by <see cref="DescribeAsync"/> within the same sweep.</summary>
    private IReadOnlyDictionary<string, Failing> failing = new Dictionary<string, Failing>();

    private sealed record Failing(string Where, string? Said, int Waiting);

    protected override async Task<(string Looked, TimeSpan Bound, IReadOnlyList<FindingCandidate> Candidates)> ReadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        failing = new Dictionary<string, Failing>();

        var bound = GooseSettings.ChatSendBound(options.Value);
        if (bound == TimeSpan.Zero) return ("turned off", bound, []);

        var workspaces = await Db.ChatWorkspaces.AsNoTracking()
            .Where(w => w.SendFailingSince != null)
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Name, w.SendFailingSince, w.LastRefusal })
            .ToListAsync(ct);

        var rooms = await Db.ChatRooms.AsNoTracking()
            .Where(r => r.SendFailingSince != null)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name, r.SendFailingSince, r.LastRefusal, WorkspaceName = r.Workspace!.Name })
            .ToListAsync(ct);

        var workspaceIds = workspaces.Select(w => w.Id).ToList();
        var roomIds = rooms.Select(r => r.Id).ToList();

        // Waiting is the sender's own rule: not dropped, and the revision sent
        // is behind the revision queued.
        var waiting = await Db.ChatMessages.AsNoTracking()
            .Where(m => m.DroppedAt == null && m.SentRevision < m.Revision)
            .Where(m => workspaceIds.Contains(m.WorkspaceId) || m.RoomId != null && roomIds.Contains(m.RoomId.Value))
            .Select(m => new { m.WorkspaceId, m.RoomId })
            .ToListAsync(ct);

        var found = new Dictionary<string, Failing>(StringComparer.Ordinal);
        var candidates = new List<FindingCandidate>();

        foreach (var w in workspaces)
        {
            var subject = WorkspacePrefix + w.Id.ToString(CultureInfo.InvariantCulture);
            found[subject] = new Failing(
                $"workspace {w.Name}", w.LastRefusal, waiting.Count(m => m.WorkspaceId == w.Id));
            candidates.Add(new FindingCandidate(subject, FailingToken, null, w.SendFailingSince));
        }

        foreach (var r in rooms)
        {
            var subject = RoomPrefix + r.Id.ToString(CultureInfo.InvariantCulture);
            found[subject] = new Failing(
                $"room {r.Name} in workspace {r.WorkspaceName}", r.LastRefusal, waiting.Count(m => m.RoomId == r.Id));
            candidates.Add(new FindingCandidate(subject, FailingToken, null, r.SendFailingSince));
        }

        failing = found;

        return ($"{workspaces.Count} workspaces and {rooms.Count} rooms failing to send", bound, candidates);
    }

    protected override (int? RoomId, int? WorkspaceId) KeepOut(EfHatchGooseFinding finding)
    {
        // The subject is the room's or the workspace's own id: the finding is
        // not sent to the place it says cannot be sent to.
        if (finding.Subject.StartsWith(RoomPrefix, StringComparison.Ordinal)
            && int.TryParse(finding.Subject.AsSpan(RoomPrefix.Length), CultureInfo.InvariantCulture, out var roomId))
            return (roomId, null);

        if (finding.Subject.StartsWith(WorkspacePrefix, StringComparison.Ordinal)
            && int.TryParse(finding.Subject.AsSpan(WorkspacePrefix.Length), CultureInfo.InvariantCulture, out var workspaceId))
            return (null, workspaceId);

        return (null, null);
    }

    protected override Task<(string Sentence, IReadOnlyList<GooseFindingEvidence> Evidence)> DescribeAsync(
        FindingCandidate candidate, DateTimeOffset began, CancellationToken ct)
    {
        var seen = failing[candidate.Subject];

        var said = string.IsNullOrWhiteSpace(seen.Said) ? "" : $" Slack said: {seen.Said.Trim()}";
        var sentence = $"Messages to {seen.Where} have not got through since {ChatText.When(began)}.{said}";

        IReadOnlyList<GooseFindingEvidence> evidence =
        [
            new GooseFindingEvidence(candidate.Subject.StartsWith(RoomPrefix, StringComparison.Ordinal) ? "Room" : "Workspace", seen.Where),
            new GooseFindingEvidence("Messages waiting", seen.Waiting.ToString(CultureInfo.InvariantCulture)),
        ];

        return Task.FromResult((sentence, evidence));
    }
}
