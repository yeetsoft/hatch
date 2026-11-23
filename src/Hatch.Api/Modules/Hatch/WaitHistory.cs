using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// When anything last waited on a person, per project, over the last thirty
/// days: the one fact the board health read needs that a window cannot give,
/// since the window never reaches back that far.
///
/// A class rather than a static because it keeps its answers, as
/// <see cref="ColumnNormals"/> does: reading thirty days of trail is too much
/// to do on every poll, and a poll only needs it when nothing waits now. An
/// answer is reused for <see cref="ColumnNormals.Lifetime"/>, and the read
/// takes the instant it is judging against and never reads a clock.
/// </summary>
/// <remarks>
/// <para><b>Per project, and the caller's own.</b> The read runs on the context it
/// is handed and never ignores its query filters, so a project a caller cannot see
/// is never read for them and never asked about. Answers are kept per project key
/// so a scope picks from them; a caller can only pick keys it can see.</para>
/// <para><b>Why a kept answer and the window always overlap.</b> Five minutes is far
/// under the shortest window (the 2h one starts at least 1h55 before now), so any wait that ended after a kept answer was made
/// ended inside the window the poll reads itself. The window's answer wins when it
/// is the later one (<see cref="BoardHealthReport.Settle"/>).</para>
/// </remarks>
public sealed class WaitHistory
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset? LastWaited, DateTimeOffset MadeAt)> _kept = new(StringComparer.Ordinal);
    private string? _fingerprint;

    /// <summary>
    /// The latest instant a wait ended in any of the projects in
    /// <paramref name="scope"/> (every project the caller can see when null), over
    /// the thirty days before <paramref name="now"/>; null when none did. A project
    /// whose answer is missing or older than <see cref="ColumnNormals.Lifetime"/>,
    /// or whose columns changed, is read again; concurrent callers wait for one
    /// read. Nothing is kept if the read throws or is cancelled.
    /// </summary>
    public async Task<DateTimeOffset?> ForAsync(
        HatchContext db, List<EfHatchStatus> statuses, HashSet<string>? scope, DateTimeOffset now, CancellationToken ct)
    {
        var visible = await db.Projects.AsNoTracking().Select(p => p.Key).ToListAsync(ct);
        var wanted = scope is null ? visible : visible.Where(scope.Contains).ToList();
        if (wanted.Count == 0) return null;

        var fingerprint = Fingerprint(statuses);

        await _one.WaitAsync(ct);
        try
        {
            if (_fingerprint != fingerprint)
            {
                _kept.Clear();
                _fingerprint = fingerprint;
            }

            var stale = wanted
                .Where(k => !_kept.TryGetValue(k, out var held) || now - held.MadeAt >= ColumnNormals.Lifetime)
                .ToHashSet(StringComparer.Ordinal);

            if (stale.Count > 0)
            {
                var since = now - BoardHealthReport.WaitLookback;
                var issues = await BoardHealthTrail.IssuesAsync(db, ct);
                var moves = await BoardHealthTrail.MovesAsync(db, since, stale, ct);
                var records = await BoardHealthTrail.RecordsAsync(db, since, stale, ct);
                var levels = await BoardHealthTrail.LevelsAsync(db, since, ct);
                var questions = await QuestionTrail.ReadAsync(db, since, now, ct);

                var fresh = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
                foreach (var key in stale)
                {
                    fresh[key] = BoardHealthReport.LastWaited(
                        issues, moves, records, levels, questions, statuses, since, now, new HashSet<string> { key });
                }

                foreach (var (key, at) in fresh) _kept[key] = (at, now);
            }

            DateTimeOffset? latest = null;
            foreach (var key in wanted)
            {
                if (_kept[key].LastWaited is { } at && (latest is not { } current || at > current)) latest = at;
            }

            return latest;
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>What an answer depends on besides the history: every column's id, place and flags.</summary>
    private static string Fingerprint(List<EfHatchStatus> statuses) =>
        string.Join(";", statuses
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => $"{s.Id},{s.SortOrder},{s.IsTerminal},{s.IsDeferred}"));
}
