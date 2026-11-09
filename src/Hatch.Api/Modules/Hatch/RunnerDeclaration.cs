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
    HasDeclaration Has,
    ModelDeclaration Models,
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
            row.Has is null ? HasDeclaration.Unjudged : HasDeclaration.Of(row.Has.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
            ModelDeclaration.Of(row.Models?.Split('\n', StringSplitOptions.RemoveEmptyEntries), row.Agent),
            WorkController.ProjectKeys(row.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
            principal,
            row.Name);
    }

    /// <summary>
    /// The same declaration, folding in a key's live loop sessions beside its
    /// beat row - <see cref="Runner.IsLive"/>'s own additive reading of the
    /// two, carried into what a pass judges a runner on. A key with no
    /// sessions, <paramref name="live"/> empty, answers off <paramref name="runner"/>'s
    /// beat alone, byte-for-byte as <see cref="OfAsync(EfHatchRunner,IActorDirectory,CancellationToken)"/>
    /// already does - and a key with neither a beat nor a session never
    /// reaches this method, since <see cref="Runner.IsLive"/> is false for it.
    /// </summary>
    /// <remarks>
    /// <see cref="Projects"/> is the one field that cannot simply union: an
    /// unbound contributor - a beat or a session that names no project at all -
    /// can take any project, so folding it into a union would narrow what the
    /// pair can take rather than widen it. Any contributor unbound makes the
    /// whole declaration unbound, <see cref="Projects"/> empty, exactly as a
    /// lone unbound runner already reads.
    /// </remarks>
    public static async Task<RunnerDeclaration> OfAsync(
        Runner runner, IReadOnlyList<EfHatchRunnerSession> live, IActorDirectory actors, CancellationToken ct)
    {
        var beat = runner.Beat;
        if (live.Count == 0 && beat is not null) return await OfAsync(beat, actors, ct);

        var config = runner.Config;

        var repos = beat is not null
            ? RepositoryDeclaration.From(beat.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries), beat.Standing, beat.Clones)
            : RepositoryDeclaration.From(
                live.SelectMany(s => s.Remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])
                    .Distinct(StringComparer.Ordinal).ToList(),
                live.Any(s => s.Standing == true),
                live.Any(s => s.Clones == true));

        var under = beat?.Under ?? live.Select(s => s.Under).FirstOrDefault(u => u is not null) ?? config?.Under;

        var mine = beat?.Mine ?? live.Any(s => s.Mine == true);

        Actor? principal = null;
        if (mine && runner.OwnerPersonId is { } ownerId)
            principal = await actors.ResolveAsync(ActorKind.Person, ownerId, ct);

        var tags = (beat?.Tags?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Concat(config is null ? [] : RunnerTagColumn.Read(config.Tags))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var refusesDangerous = (beat?.RefusesDangerous ?? false) || live.Any(s => s.RefusesDangerous == true);

        var has = beat?.Has is null ? HasDeclaration.Unjudged : HasDeclaration.Of(beat.Has.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var agent = beat?.Agent ?? live.Select(s => s.Agent).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        var models = ModelDeclaration.Of(beat?.Models?.Split('\n', StringSplitOptions.RemoveEmptyEntries), agent);

        var contributors = new List<IReadOnlyList<string>>();
        if (beat is not null) contributors.Add(beat.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? []);
        contributors.AddRange(live.Select(s => (IReadOnlyList<string>)(s.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])));

        var projects = contributors.Any(c => c.Count == 0)
            ? []
            : WorkController.ProjectKeys(contributors.SelectMany(c => c));

        return new RunnerDeclaration(
            repos, under, mine, TagDeclaration.Of(tags, refusesDangerous), has, models, projects, principal, runner.Name);
    }
}
