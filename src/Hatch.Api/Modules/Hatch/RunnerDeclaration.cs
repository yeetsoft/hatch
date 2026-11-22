using Hatch.Api.Services.Auth;
using Hatch.Contracts;

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
/// <param name="Refused">
/// The models the runner's key refuses, from its config row's <c>RefusedModels</c>
/// - empty for a keyless row and for a key with no config row. See
/// <see cref="Hatch.Contracts.RunnerModels.Refuses"/>.
/// </param>
/// <param name="StatusMode">
/// The runner's column rule, from its config row - <c>all</c> for a keyless
/// row and for a key with no config row. See <see cref="Dispatch.ColumnFold"/>.
/// </param>
/// <param name="StatusIds">The column ids <paramref name="StatusMode"/> reads against.</param>
/// <param name="Preferred">
/// Whether the runner's key is marked to take work before unmarked runners.
/// False for a keyless beat and for a key with no config row. Nothing in
/// <c>Dispatch</c> reads it yet.
/// </param>
/// <param name="OwnerPersonId">
/// The person the runner's key belongs to, whether or not <paramref name="Mine"/>
/// is set - <paramref name="Principal"/> is only resolved under <c>Mine</c>.
/// What a peer scan reads the runner's scope by: a key reads through its
/// owner's scope. Null for a runner that belongs to nobody, which is unbounded.
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
    string Name,
    IReadOnlyList<string> Refused,
    Guid? OwnerPersonId = null,
    string StatusMode = RunnerStatusMode.All,
    IReadOnlyList<int>? StatusIds = null,
    bool Preferred = false,
    SigningDeclaration? SigningAnswer = null,
    ForgeDeclaration? ForgeAnswer = null)
{
    /// <summary>
    /// Whether the runner signs its commits, as <see cref="Dispatch.SigningFold"/> judges it. A
    /// declaration is always judged: a runner that never said reads as having
    /// no answer, and is held off a project that allows only signed commits.
    /// </summary>
    public SigningDeclaration Signing => SigningAnswer ?? new SigningDeclaration(Name, null, null, true);

    /// <summary>
    /// Whether the runner can reach its forge, as <see cref="Dispatch.ForgeFold"/> judges it. A
    /// runner that never said, and every runner older than the check, reads as no answer, which
    /// folds nothing.
    /// </summary>
    public ForgeDeclaration Forge => ForgeAnswer ?? new ForgeDeclaration(Name, null, null);

    /// <summary>What this runner refuses - models and columns - as a pass judges it.</summary>
    public RunnerRefusal ToRefusal() => new(Name, Refused, StatusMode, StatusIds);

    /// <summary>
    /// The sentence saying why this runner cannot be answered for, or null. A
    /// <c>Mine</c> runner with no owner has no tickets of its own, and a scan
    /// for it is refused rather than guessed at.
    /// </summary>
    public string? Refusal => Mine && Principal is null
        ? $"{Name} belongs to nobody, so it has no tickets of its own - an admin sets its " +
          "owner on the API Keys page"
        : null;

    public static async Task<RunnerDeclaration> OfAsync(
        EfHatchRunner row, IActorDirectory actors, CancellationToken ct, EfHatchRunnerConfig? config = null)
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
            row.Name,
            config is null ? [] : RunnerTagColumn.Read(config.RefusedModels),
            row.ForPersonId,
            config?.StatusMode ?? RunnerStatusMode.All,
            config is null ? [] : RunnerStatusIdsColumn.Read(config.StatusIds),
            config?.Preferred ?? false);
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
        if (live.Count == 0 && beat is not null) return await OfAsync(beat, actors, ct, runner.Config);

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

        var declaredHas = Declared(beat?.Has, live.Select(s => s.Has));
        var has = declaredHas.Count == 0 ? HasDeclaration.Unjudged : HasDeclaration.Of(declaredHas);

        var agent = beat?.Agent ?? live.Select(s => s.Agent).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        var declaredModels = Declared(beat?.Models, live.Select(s => s.Models));
        var models = ModelDeclaration.Of(declaredModels.Count == 0 ? null : declaredModels, agent);

        var contributors = new List<IReadOnlyList<string>>();
        if (beat is not null) contributors.Add(beat.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? []);
        contributors.AddRange(live.Select(s => (IReadOnlyList<string>)(s.Projects?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])));

        var projects = contributors.Any(c => c.Count == 0)
            ? []
            : WorkController.ProjectKeys(contributors.SelectMany(c => c));

        return new RunnerDeclaration(
            repos, under, mine, TagDeclaration.Of(tags, refusesDangerous), has, models, projects, principal, runner.Name,
            config is null ? [] : RunnerTagColumn.Read(config.RefusedModels),
            runner.OwnerPersonId,
            config?.StatusMode ?? RunnerStatusMode.All,
            config is null ? [] : RunnerStatusIdsColumn.Read(config.StatusIds),
            config?.Preferred ?? false,
            SigningOf(runner.Name, live),
            ForgeDeclarationOf(runner.Name, live));
    }

    /// <summary>
    /// What a runner's live loop sessions say about signing its commits: the worst of
    /// their answers, see <see cref="RunnerSigning.Worst"/>. Always judged - no answer is
    /// an answer, that the runner has not said. Shared by this declaration and
    /// <see cref="RunnersController"/>'s page, so the two cannot drift apart.
    /// </summary>
    internal static SigningDeclaration SigningOf(string runner, IEnumerable<EfHatchRunnerSession> live)
    {
        var worst = RunnerSigning.Worst(live.Where(s => s.Kind == EfHatchRunner.LoopKind).Select(s => (s.Signing, s.SigningWhy)));
        return new SigningDeclaration(runner, worst?.State, worst?.Why, true);
    }

    /// <summary>
    /// What a runner's live loop sessions say about its forge sign-in: the worst of their
    /// answers, see <see cref="RunnerForge.Worst"/>. Null when none said. Read by the
    /// runners page, and by dispatch through <see cref="ForgeDeclarationOf"/>.
    /// </summary>
    internal static (string State, string? Why)? ForgeOf(IEnumerable<EfHatchRunnerSession> live) =>
        RunnerForge.Worst(live.Where(s => s.Kind == EfHatchRunner.LoopKind).Select(s => (s.Forge, s.ForgeWhy)));

    /// <summary>
    /// <see cref="ForgeOf"/> as the declaration a pass judges: the runner's name, the worst
    /// word and its reason. No answer is kept as none, which folds nothing.
    /// </summary>
    internal static ForgeDeclaration ForgeDeclarationOf(string runner, IEnumerable<EfHatchRunnerSession> live)
    {
        var worst = ForgeOf(live);
        return new ForgeDeclaration(runner, worst?.State, worst?.Why);
    }

    /// <summary>
    /// Per field, <c>has</c> and <c>models</c> alike: <paramref name="beatLines"/>
    /// when the beat said any, else the distinct ordinal union of
    /// <paramref name="sessionLines"/> in session order, else nothing. One
    /// reading shared by this declaration and <see cref="Runners.Project"/>'s
    /// <see cref="RunnerDto"/>, so the two cannot drift apart.
    /// </summary>
    internal static IReadOnlyList<string> Declared(string? beatLines, IEnumerable<string?> sessionLines) =>
        beatLines is not null
            ? beatLines.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : sessionLines
                .SelectMany(s => s?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToList();
}
