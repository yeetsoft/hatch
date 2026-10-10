namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Everything Hatch puts into DI, so the module registry in ModuleRegistration
/// stays one line per app.
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
        services.Configure<HatchOptions>(configuration.GetSection(HatchOptions.SectionName));
        services.AddSingleton<IssueClaims>();

        // The runner's two horizons, on the same terms and for the same reason:
        // a configured number, no clock, no database, and every judgement taking
        // the instant it is judging against.
        services.AddSingleton<Runners>();

        // Scoped, the same reason as Dispatch beside it: it holds a scoped
        // HatchContext, taken through DI here rather than constructed, via the
        // Dispatch it decides alongside.
        services.AddScoped<Preemption>();

        // Scoped, the same reason as Dispatch and Preemption above it: it
        // holds a scoped HatchContext.
        services.AddScoped<IProjectAccess, ProjectAccess>();

        // The container runner's entrypoint authenticates the `claude` CLI it
        // starts with this - the one thing the pasted token still does. The
        // credential is its own interface so that where the token lives is one
        // class rather than a decision spread through a reader, and it reads
        // through ISiteSettingsService, which is a singleton with its own
        // cache.
        services.AddSingleton<IClaudeCredential, SiteSettingClaudeCredential>();

        return services;
    }
}
