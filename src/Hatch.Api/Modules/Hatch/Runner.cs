using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One runner, as the three tables that can each say something about it join:
/// the key it was minted as, the board's own settings for that key, and
/// whatever it has last heartbeaten as. Any one of <see cref="Key"/> or
/// <see cref="Beat"/> may be missing - a key that has never run beats as
/// itself with <see cref="Beat"/> null, and a checkout heartbeating under a
/// name no key claims beats with <see cref="Key"/> and <see cref="Config"/>
/// both null - but never both, since a row with neither is not a runner at
/// all.
/// </summary>
public sealed record Runner(EfApiKey? Key, EfHatchRunnerConfig? Config, EfHatchRunner? Beat, IReadOnlyList<EfHatchRunnerSession>? Sessions = null)
{
    /// <summary>The key's own name where there is a key, else the beat's - the identity this entry is drawn under.</summary>
    public string Name => Key?.Name ?? Beat!.Name;

    /// <summary>
    /// Who this runner works for: the key's own <c>OwnerPersonId</c> when a
    /// key exists - even when that is null, an owned-by-nobody key - else the
    /// beat's <c>ForPersonId</c> for a heartbeat no key claims.
    /// </summary>
    public Guid? OwnerPersonId => Key is not null ? Key.OwnerPersonId : Beat?.ForPersonId;

    /// <summary>The beat's own last heartbeat where there is one, else the key's own activity - minted, or last used.</summary>
    public DateTimeOffset LastSeenAt => Beat?.LastSeenAt ?? Key!.LastUsedAt ?? Key!.CreatedAt;

    /// <summary>
    /// Whether this entry counts as <c>live</c>: a key with no beat has never
    /// run, and a beat past <see cref="Runners.DropBefore"/> is as good as
    /// none - unless one of <see cref="Sessions"/> is still live on its own
    /// terms, additive to the beat's. A key that has never held a session is
    /// exactly as live as before this existed. A retired beat never counts,
    /// however recent its heartbeat - a session is its own fact and stays
    /// additive regardless.
    /// </summary>
    public bool IsLive(Runners runners, RunnerSessions sessions, DateTimeOffset now) =>
        (Beat is not null && Beat.RetiredAt is null && !runners.IsDropped(Beat.LastSeenAt, now)) ||
        Sessions?.Any(s => sessions.IsLive(s, now)) == true;

    /// <summary>
    /// Every key, config, heartbeat and session, joined by name or by id - the
    /// key's own <see cref="EfApiKey.Name"/> against <see cref="EfHatchRunner.Name"/>,
    /// ordinal, and every session grouped by its own <see cref="EfHatchRunnerSession.ApiKeyId"/>
    /// - and sorted <see cref="LastSeenAt"/> descending, then <see cref="Name"/>
    /// ordinal, the order the read has always answered in. Four whole reads
    /// and no filter, the way <see cref="Runners.HeldAsync"/> reads its own
    /// table whole: the scale here is tens of rows, never thousands.
    ///
    /// A beat that no key claims and that <see cref="Runners.AdoptByOwnerAsync"/>
    /// has already folded onto a key's config is history, not a runner: it is
    /// left out of the unclaimed half below rather than listed a second time
    /// under its own old name.
    /// </summary>
    public static async Task<List<Runner>> JoinAsync(HatchContext db, IAuthService keys, CancellationToken ct)
    {
        var allKeys = await keys.ListApiKeysAsync(ct);
        var configs = await db.RunnerConfigs.AsNoTracking().ToListAsync(ct);
        var beats = await db.LegacyRunners.AsNoTracking().ToListAsync(ct);
        var sessions = await db.RunnerSessions.AsNoTracking().ToListAsync(ct);

        var configsById = configs.ToDictionary(c => c.ApiKeyId);
        var sessionsByKeyId = sessions.GroupBy(s => s.ApiKeyId).ToDictionary(g => g.Key, g => (IReadOnlyList<EfHatchRunnerSession>)g.ToList());
        var beatsByName = beats.ToDictionary(b => b.Name, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        var joined = new List<Runner>(allKeys.Count + beats.Count);
        foreach (var key in allKeys)
        {
            beatsByName.TryGetValue(key.Name, out var beat);
            if (beat is not null) claimed.Add(key.Name);
            configsById.TryGetValue(key.Id, out var config);
            sessionsByKeyId.TryGetValue(key.Id, out var ownSessions);
            joined.Add(new Runner(key, config, beat, ownSessions));
        }

        foreach (var beat in beats)
        {
            if (!claimed.Contains(beat.Name) && beat.AdoptedAt is null) joined.Add(new Runner(null, null, beat));
        }

        return joined
            .OrderByDescending(r => r.LastSeenAt)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }
}
