using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which Slack links still belong to somebody. People live in the core schema
/// and links in this module's, and a module may not point a constraint across
/// (<c>Modules/README.md</c>), so removing a person cannot cascade to their
/// links. Instead a link whose person is gone counts for nothing wherever it is
/// read, and its row is deleted the next time its workspace's links are read
/// or written - the rule <see cref="IActorDirectory"/> gives an assignee.
/// </summary>
/// <remarks>
/// <para>Only a <em>confirmed</em> link blocks anything: the unique index on a
/// confirmed member is filtered to confirmed rows. So the delete has to happen
/// before a confirm is saved, not after the index refuses it.</para>
///
/// <para>Not <see cref="IActorDirectory.LiveAsync"/>, which lists the local
/// person only while a request is in flight. A sender running in a background
/// job has no request, and would read the local person's link as dead and
/// delete it. The rule here is the same one without the request: the local
/// person is live where the wall is off and no sign-in could name anybody else
/// (<see cref="LocalCaller.PersonSpeaks"/>).</para>
/// </remarks>
public sealed class ChatLinkRoster(
    HatchContext db,
    AppDbContext appDb,
    IOptions<AuthOptions> options,
    IAuthService auth,
    ISiteSettingsService settings,
    ILogger<ChatLinkRoster> logger)
{
    /// <summary>
    /// Deletes every link in scope whose person is gone: one workspace's, or
    /// every workspace's for a null id. Saves the context, so call it before
    /// the caller has changed anything it tracks. Another request deleting the
    /// same row first is not an error. Returns how many rows this call removed.
    /// </summary>
    public async Task<int> PruneAsync(int? workspaceId, CancellationToken ct)
    {
        var scope = workspaceId is { } w ? db.ChatLinks.Where(l => l.WorkspaceId == w) : db.ChatLinks;

        var people = await scope.Select(l => l.PersonId).Distinct().ToListAsync(ct);
        if (people.Count == 0) return 0;

        var live = await LiveAsync(people, ct);
        var gone = people.Where(p => !live.Contains(p)).ToList();
        if (gone.Count == 0) return 0;

        var rows = await scope.Where(l => gone.Contains(l.PersonId)).ToListAsync(ct);
        db.ChatLinks.RemoveRange(rows);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request deleted them first. Only these rows are let go of:
            // what else the caller has tracked is left as it was.
            foreach (var row in rows)
                if (db.Entry(row).State != EntityState.Detached) db.Entry(row).State = EntityState.Detached;

            return 0;
        }

        logger.LogInformation("{Count} Slack links dropped for people who are gone", rows.Count);

        return rows.Count;
    }

    /// <summary>Whether the person is somebody a link may belong to.</summary>
    public async Task<bool> IsLiveAsync(Guid personId, CancellationToken ct) =>
        (await LiveAsync([personId], ct)).Contains(personId);

    /// <summary>
    /// The ones among <paramref name="personIds"/> that are live: in the people
    /// table, or the local person where the wall is off and nobody is enrolled.
    /// The wall and enrolment are read once, however many ids are asked about.
    /// </summary>
    public async Task<HashSet<Guid>> LiveAsync(IReadOnlyCollection<Guid> personIds, CancellationToken ct)
    {
        var ids = personIds.Distinct().ToList();
        var live = (await appDb.People.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        if (ids.Contains(LocalCaller.PersonId) && !live.Contains(LocalCaller.PersonId) && await LocalSpeaksAsync(ct))
            live.Add(LocalCaller.PersonId);

        return live;
    }

    /// <summary>
    /// The names of the live people among <paramref name="personIds"/>. The
    /// local person is named as the rest of the app names them.
    /// </summary>
    public async Task<Dictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> personIds, CancellationToken ct)
    {
        var ids = personIds.Distinct().ToList();
        var names = await appDb.People.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        if (ids.Contains(LocalCaller.PersonId) && !names.ContainsKey(LocalCaller.PersonId) && await LocalSpeaksAsync(ct))
            names[LocalCaller.PersonId] = LocalCaller.PersonNameOf((await settings.GetAsync(ct)).LocalPersonName, options.Value.LocalPerson.Name);

        return names;
    }

    /// <summary>
    /// A workspace's confirmed links, after dropping those of people who are
    /// gone. <b>This is the one read to use to choose who is sent a message</b>:
    /// a link it does not return is not to be written to, and a read made any
    /// other way may name a person who has been removed.
    /// </summary>
    public async Task<List<EfHatchChatLink>> ConfirmedAsync(int workspaceId, CancellationToken ct)
    {
        await PruneAsync(workspaceId, ct);

        return await db.ChatLinks.AsNoTracking().Confirmed()
            .Where(l => l.WorkspaceId == workspaceId)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);
    }

    private async Task<bool> LocalSpeaksAsync(CancellationToken ct) =>
        !options.Value.Enabled
        && LocalCaller.PersonSpeaks(options.Value.Google.Configured, await auth.HasAnyPersonAsync(ct));
}
