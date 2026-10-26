using Hatch.Api.Services.Auth;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// What a runner's own row says a pass should be judged on: the same
/// repositories, scope, <c>mine</c>, tags and projects a runner would send on
/// the query string, read off the row instead. Two callers need it -
/// <see cref="WorkController.GetQueue"/> answering <c>asRunner</c>, and
/// <see cref="Dispatch"/> asking whether a peer runner could take a ticket one
/// runner let go of - and one reading of the row is how they cannot drift.
/// </summary>
/// <param name="Principal">
/// Whose tickets <paramref name="Mine"/> means: the runner's owner. Null where
/// <paramref name="Mine"/> is false, and also where it is true and the runner
/// belongs to nobody - see <see cref="Refusal"/>.
/// </param>
public sealed record RunnerDeclaration(
    RepositoryDeclaration Repos,
    string? Under,
    bool Mine,
    TagDeclaration Tags,
    IReadOnlyList<string> Projects,
    Actor? Principal,
    string Name)
{
    /// <summary>
    /// The sentence saying why this runner cannot be answered for, or null. A
    /// <c>Mine</c> runner with no owner has no tickets of its own, and a scan
    /// for it is refused rather than guessed at.
    /// </summary>
    public string? Refusal => Mine && Principal is null
        ? $"{Name} belongs to nobody, so it has no tickets of its own - an admin sets its " +
          "owner on the API Keys page"
        : null;

    public static async Task<RunnerDeclaration> OfAsync(EfHatchRunner row, IActorDirectory actors, CancellationToken ct)
    {
        var mine = row.Mine ?? false;

        Actor? principal = null;
        if (mine && row.ForPersonId is { } forPersonId)
            principal = await actors.ResolveAsync(ActorKind.Person, forPersonId, ct);

        return new RunnerDeclaration(
            RepositoryDeclaration.From(
                row.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries), row.Standing, row.Clones),
            row.Under,
            mine,
            TagDeclaration.Of(row.Tags?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [], row.RefusesDangerous ?? false),
            WorkController.ProjectKeys(row.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
            principal,
            row.Name);
    }
}
