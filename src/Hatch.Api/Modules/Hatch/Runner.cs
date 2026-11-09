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
public sealed record Runner(EfApiKey? Key, EfHatchRunnerConfig? Config, EfHatchRunner? Beat)
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
    /// none.
    /// </summary>
    public bool IsLive(Runners runners, DateTimeOffset now) => Beat is not null && !runners.IsDropped(Beat.LastSeenAt, now);

    /// <summary>
    /// Every key, config and heartbeat, joined by name - the key's own
    /// <see cref="EfApiKey.Name"/> against <see cref="EfHatchRunner.Name"/>,
    /// ordinal - and sorted <see cref="LastSeenAt"/> descending, then
    /// <see cref="Name"/> ordinal, the order the read has always answered in.
    /// Three whole reads and no filter, the way <see cref="Runners.HeldAsync"/>
    /// reads its own table whole: the scale here is tens of rows, never
    /// thousands.
    /// </summary>
    public static async Task<List<Runner>> JoinAsync(HatchContext db, IAuthService keys, CancellationToken ct)
    {
        var allKeys = await keys.ListApiKeysAsync(ct);
        var configs = await db.RunnerConfigs.AsNoTracking().ToListAsync(ct);
        var beats = await db.Runners.AsNoTracking().ToListAsync(ct);

        var configsById = configs.ToDictionary(c => c.ApiKeyId);
        var beatsByName = beats.ToDictionary(b => b.Name, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        var joined = new List<Runner>(allKeys.Count + beats.Count);
        foreach (var key in allKeys)
        {
            beatsByName.TryGetValue(key.Name, out var beat);
            if (beat is not null) claimed.Add(key.Name);
            configsById.TryGetValue(key.Id, out var config);
            joined.Add(new Runner(key, config, beat));
        }

        foreach (var beat in beats)
        {
            if (!claimed.Contains(beat.Name)) joined.Add(new Runner(null, null, beat));
        }

        return joined
            .OrderByDescending(r => r.LastSeenAt)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }
}
