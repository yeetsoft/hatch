using Hatch.Api.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Everything Hatch puts into DI, so Program.cs stays one line for the module.
/// </summary>
public static class HatchModule
{
    public static IServiceCollection AddHatchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleContext<HatchContext>(configuration, HatchContext.Schema);

        // Scoped, because it reads and rewrites rows through the request's own
        // context - a renumbered column and the card that caused it have to
        // land in one SaveChanges.
        services.AddScoped<RankService>();

        // Scoped for the same reason: it holds a scoped HatchContext. Not for
        // WorkController's sake - it builds its own from its own primary-ctor
        // params, so WorkControllerTests.cs never has to change - but for
        // every other consumer of "what would a pass do" that takes it through
        // ordinary DI instead.
        services.AddScoped<Dispatch>();

        // Singleton, because it is a pure function with a class around it: it
        // reads a string and returns a tree, and touches neither the database
        // nor the clock.
        services.AddSingleton<PlanImportParser>();

        // The claim's TTL, and the one class that judges a lease against it.
        // Bound here rather than in Program.cs because a module owns its own
        // registrations (Modules/README.md).
        //
        // Singleton for the reason above it: IssueClaims reads an options value
        // and holds nothing else, and every judgement it makes takes the
        // instant it is judging against rather than reading a clock.
        //
        // Validated on start because a malformed Android asset link must stop
        // the API rather than reach /.well-known/assetlinks.json and fail
        // Android's own, invisible verification later.
        services.AddOptions<HatchOptions>()
            .Bind(configuration.GetSection(HatchOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HatchOptions>, AssetLinksValidation>();
        services.AddSingleton<IssueClaims>();

        // The one list of model families a pinned id is judged against - a
        // pure value with nothing configurable yet, so the default is the
        // whole of it.
        services.AddSingleton(ModelFamilies.Default);

        // The runner's two horizons, on the same terms and for the same reason:
        // a configured number, no clock, no database, and every judgement taking
        // the instant it is judging against.
        services.AddSingleton<Runners>();

        // A session's own judgement, on the same terms as Runners beside it,
        // and built on it rather than beside it: liveness is the same horizon
        // a beat already draws.
        services.AddSingleton<RunnerSessions>();

        // The board's own horizon, on the same terms as Runners above it.
        services.AddSingleton<DoneHorizon>();

        // Scoped, the same reason as Dispatch beside it: it holds a scoped
        // HatchContext, taken through DI here rather than constructed, via the
        // Dispatch it decides alongside.
        services.AddScoped<Preemption>();

        // Scoped, the same reason as Dispatch and Preemption above it: it
        // holds a scoped HatchContext.
        services.AddScoped<IProjectAccess, ProjectAccess>();
        services.AddScoped<ICallerScope, CallerScope>();

        // Scoped, so the filter that resolves the caller's scope and the
        // HatchContext whose query filters read it share one holder for the
        // life of a request. A context built outside a request is handed
        // the same holder and finds it empty with no HTTP context, which is
        // unbounded - that is how a job reads everything.
        services.AddScoped<RequestScope>();
        services.Configure<MvcOptions>(o => o.Filters.Add<ScopeResolutionFilter>());

        // The loser of a race to move an issue (StatusId is a concurrency
        // token) is answered with a 409 rather than a 500.
        services.Configure<MvcOptions>(o => o.Filters.Add<ConcurrentMoveFilter>());

        // The container runner's entrypoint authenticates the `claude` CLI it
        // starts with this - the one thing the pasted token still does. The
        // credential is its own interface so that where the token lives is one
        // class rather than a decision spread through a reader, and it reads
        // through ISiteSettingsService, which is a singleton with its own
        // cache.
        services.AddSingleton<IAgentCredential, SiteSettingAgentCredential>();

        // Scoped, the same reason as RankService and Dispatch above it: it
        // holds a scoped HatchContext. Registered here rather than in
        // Program.cs's four house jobs, because this one reads Hatch's own
        // context and is a module's own, not the house's.
        services.AddScoped<IAppJob, ReflectionCadenceJob>();

        // Scoped like ReflectionCadenceJob, though it holds only singletons: a
        // module's own job, not one of Program.cs's house jobs. Its "on start,
        // then daily" is JobsInit's, not a trigger of its own.
        services.AddScoped<IAppJob, BackupJob>();

        // Singletons, both: the status must outlive the scoped job that
        // writes to it between runs, and the service itself holds only
        // options and a lock - nothing scoped to a request.
        services.AddSingleton<BackupStatus>();
        services.AddSingleton<IBackupProcess, SystemBackupProcess>();
        services.AddSingleton<BackupService>();

        // Scoped like the two jobs above. The lock is a singleton because it
        // holds no state, only takes a connection of its own per sweep.
        services.AddScoped<IAppJob, GooseJob>();
        services.AddSingleton<IGooseLock, PostgresGooseLock>();
        services.AddSingleton<GooseWatch>();

        // Scoped like Dispatch: it holds a scoped HatchContext, and its one
        // cached ReadySnapshot is meant to live no longer than the sweep that
        // built it - see ReadyWork's own remarks.
        services.AddScoped<ReadyWork>();

        // The sweep's own checks, in registration order - a sweep with none
        // still records a run, so this list is additive and never required.
        services.AddScoped<IGooseCheck, TrainReadyCheck>();
        services.AddScoped<IGooseCheck, ReadyUnclaimedCheck>();
        services.AddScoped<IGooseCheck, ReadyNoRunnerCheck>();
        services.AddScoped<IGooseCheck, RepeatClaimsCheck>();
        services.AddScoped<IGooseCheck, QuestionsWaitingCheck>();

        return services;
    }
}
