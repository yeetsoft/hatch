using Hatch.Api.Common;
using Hatch.Api.Controllers;
using Hatch.Api.Ef;
using Hatch.Api.Jobs;
using Hatch.Api.Modules;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Settings;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Quartz;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

////////
/// DI
var builder = WebApplication.CreateBuilder(args);

// Read once, up here, because the registrations below need it as well as the
// branch at the bottom that acts on it: migrate mode wants EF to keep quiet
// about the commands that are *supposed* to fail on an empty database, and
// that has to be decided while the contexts are being configured.
var migrateMode = builder.Configuration["HATCH_MIGRATE"] == "1";

// Structured JSON console output outside local dev, so the fluent-bit ->
// OpenSearch pipeline (which tails raw container stdout, see
// deploy/cluster/observability/controllers/fluent-bit.yaml) can parse fields like State.Service
// out of each line instead of scraping human-formatted text. Left as the
// default Simple formatter in Development so `dotnet run` stays readable.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddJsonConsole();
}

builder.Services.AddSingleton(TimeProvider.System);

// .env.json is optional: a generic ISecrets source for operator secrets in local
// dev (docs/ethos.md). Production injects the same keys as environment
// variables, which the environment config provider picks up under the same
// case-insensitive names.
builder.Configuration.AddJsonFile(".env.json", optional: true);
builder.Services.AddSingleton<ISecrets>(new EnvSecrets(builder.Configuration));

// Pooled factory so services that fan out concurrent DB work can each create
// their own short-lived context instead of racing on one shared scoped instance,
// which throws "A second operation was started on this context..." under
// concurrent load. Scoped AppDbContext is still available (resolved from the
// same pool) for services that only ever touch the DB sequentially.
builder.Services.AddPooledDbContextFactory<AppDbContext>(o =>
{
    o.UseNpgsql(builder.Configuration.GetConnectionString("Hatch"));

    o.ConfigureWarnings(w =>
    {
        // In migrate mode only. The first command EF issues against an empty
        // database is a SELECT from __EFMigrationsHistory, which does not
        // exist yet - one Error-level line per context, before a single
        // migration has run, indistinguishable from a broken install to the
        // person reading it. In a serving process a failed command is news and
        // stays at Error; in the migrate process the *exception* is what
        // reports a failure, and this probe is expected to fail.
        if (migrateMode) w.Log((RelationalEventId.CommandError, LogLevel.Debug));
    });
});
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Modules - a separate schema in the same database, each with its own DbContext
// and migration history (see Modules/README.md). Hatch is the only one.
builder.Services.AddHatchModule(builder.Configuration);

// Quartz
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(sb =>
    {
        sb.UseProperties = true;
        sb.UseClustering();
        sb.UsePostgres(builder.Configuration.GetConnectionString("Quartz")!);
        sb.UseSystemTextJsonSerializer();
    });
});
// The hosted service is the only thing that stops the scheduler. It waits for
// its own background Start() to finish first, which nothing else can see:
// Quartz reports IsStarted before the clustered job store's first check-in has
// run, and a Shutdown() landing inside that check-in either threw or awaited a
// cluster-manager task Quartz had already abandoned, forever (HA-171).
//
// Not waiting for running jobs, on purpose: Quartz waits with a bare
// Task.WaitAll that no stop token reaches, so one slow job would hold a pod's
// shutdown until it is killed. This is also what every shutdown already did -
// the ApplicationStopping callback this replaced stopped the scheduler with
// `false` before the hosted service got the chance to.
builder.Services.AddQuartzHostedService(opt =>
{
    opt.WaitForJobsToComplete = false;
});
// AddQuartz only registers ISchedulerFactory; JobsInit
// needs IScheduler directly, and GetScheduler() returns the same underlying
// instance AddQuartzHostedService starts, so this stays in sync with it.
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult());

// The build's own git identity, and what Flux has reconciled from it
// (docs/plans/version.md). Singletons: the first is immutable for the life of
// the process, the second holds one HttpClient and one short-lived cache.
builder.Services.AddSingleton<IHatchRevision, HatchRevision>();
builder.Services.AddSingleton<IFluxRevisionReader, FluxRevisionReader>();

builder.Services.AddSingleton<ISiteSettingsService, SiteSettingsService>();

// Signing in with Google. One named client covers every host Google answers on
// - accounts.google.com and oauth2.googleapis.com - so it carries no
// BaseAddress and the service calls absolute URLs. The explicit timeout is the
// fail-soft convention: a slow Google leaves the person at the sign-in page
// with an error, it never stalls a request.
builder.Services.AddHttpClient(GoogleOAuthService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IGoogleOAuthService, GoogleOAuthService>();

// Auth (docs/auth-architecture.md). AuthMiddleware and AuthController both run
// unconditionally, and both no-op or allow until Auth:Enabled becomes true -
// which is why false is the whole rollback.
var authSection = builder.Configuration.GetSection(AuthOptions.SectionName);
builder.Services.Configure<AuthOptions>(authSection);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuthGate, AuthGate>();

// The second question, asked only by the routes that ask it: not "is this
// device enrolled" but "does this person's role reach this". Separate from
// IAuthGate on purpose - see Services/Auth/RoleGate.cs - and dormant wherever
// the wall is off, which is why registering it changes nothing there.
builder.Services.AddScoped<IRoleGate, RoleGate>();

// "Who is making this request", for everything that isn't the wall itself. The
// accessor is the only reason this needs a line here at all: ICallerIdentity is
// resolved from a module's controller, which has an HttpContext but no way to
// hand one to a service it did not construct. Two lines of platform, so that
// asking costs a module one constructor parameter and no knowledge of cookies -
// see Services/Auth/CallerIdentity.cs.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICallerIdentity, CallerIdentity>();

// And "who else is out there", for the things that belong to somebody rather
// than being done by them. Beside ICallerIdentity because it is the same kind of
// question and answering it twice is how two modules end up disagreeing about
// whether a revoked key is still a person you can hand work to - see
// Services/Auth/ActorDirectory.cs. Scoped for the memoization, which is what
// makes a whole board's worth of assignees two queries.
builder.Services.AddScoped<IActorDirectory, ActorDirectory>();

// Redemption is the only endpoint in the app that mints a credential, so it is
// the only one with a limiter. Bound once at startup rather than per request:
// AddPolicy's factory runs on the hot path, and the numbers are deploy-time
// config that cannot change without a restart anyway.
var authLimits = authSection.Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AuthController.RedeemRateLimitPolicy, context =>
        // Partitioned by client IP, which UseForwardedHeaders has already
        // resolved from the proxy's X-Forwarded-For by the time this runs.
        //
        // Honest caveat: the limiter is per-process, so three replicas means
        // three times this budget. That is acceptable against a 40-bit code
        // behind a 15-minute TTL and single use, and it is not the primary
        // detector anyway - the Warning logged on every refused redemption is,
        // and those reach logs.<domain> from all three replicas alike.
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, authLimits.RedeemAttemptsPerWindow),
                Window = authLimits.RedeemWindow,
                // Queueing a guess to run later is not a kindness to anyone; a
                // 429 the sign-in shell can say out loud is.
                QueueLimit = 0,
            }));
});

// Jobs
builder.Services.AddTransient<JobsInit>();

// API / HTTP

// DataProtection: nothing here uses antiforgery tokens, cookie
// authentication, session state or TempData (grepped for all four, no
// matches), so there's no key ring that needs persisting across the three
// replicas. If any of that shows up later, its keys have to move to
// Postgres before replicas > 1 - otherwise each replica issues from its own
// ephemeral ring and can't read what another replica issued.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Hatch API",
        Version = "v1",
        // No Description. It used to carry "[Open apps →](/apps/)" - a link
        // hand-written into the API's own description because the Swagger page
        // was a one-way trip out of the app picker. The bar at the top of that
        // page is the real answer to that (see UseSwaggerUI below), and this
        // was also a navigation link living in the published OpenAPI document,
        // where a generated client would read it as what the API is.
    });
});
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

////////
/// Migrate mode - the chart's pre-install/pre-upgrade hook Job (5b.6) runs
/// this same image with HATCH_MIGRATE=1 instead of serving traffic, so the
/// migration provably runs the same code as the pods it precedes rather than
/// a second image that can drift from it. Runs once per deploy, ahead of any
/// replica; the serving pods below never take this branch.
if (migrateMode)
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Every registered module context, in whatever order the registry lists them -
    // module schemas are independent by construction, so there is nothing to order.
    // A loop rather than a block per module is the point: module #2 never edits this.
    foreach (var moduleDb in scope.ServiceProvider.GetServices<IModuleContext>())
    {
        app.Logger.LogInformation("Migrating module context {Context}", moduleDb.GetType().Name);
        await moduleDb.Database.MigrateAsync();
    }

    // The chicken-and-egg: only an Admin can promote a person, and every
    // person sign-in creates is Pending. So the first person to sign in on an
    // install with no Admin becomes one (GoogleSignInController), and this
    // says so before anybody has. It is here rather than at replica startup
    // because this branch runs exactly once per deploy ahead of any replica,
    // and three replicas would say it three times. Gated on Auth:Enabled: with
    // no wall there is no sign-in to describe. The Job must be given the same
    // Auth__* settings as the api, or it describes a different install.
    var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
    var anyAdmin = await db.People.AnyAsync(p => p.Role == PersonRole.Admin);
    if (!authOptions.Enabled)
        app.Logger.LogInformation("Auth is disabled, so there is no wall to get through and no Administrator is needed.");
    else if (FirstAdminNotice.For(authOptions, anyAdmin) is { } notice)
        app.Logger.LogWarning("{Notice}", notice);

    return;
}

// Quartz
using (var scope = app.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<JobsInit>();
    await init.WireUpJobs();
}

// A clean stop frees this node's cluster row. Attached here rather than through
// AddQuartz because it needs the scheduler instance, and resolving IScheduler
// from inside the scheduler's own construction would be a cycle.
var scheduler = app.Services.GetRequiredService<IScheduler>();
scheduler.ListenerManager.AddSchedulerListener(
    ActivatorUtilities.CreateInstance<SchedulerCheckout>(app.Services, builder.Configuration.GetConnectionString("Quartz")!));

////////
/// HTTP Server
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// `api` is reached only via a reverse proxy with no port of its own exposed
// beyond that proxy - Caddy on the isolated `edge` Docker network (see
// docs/reverse-proxy-architecture.md) on the old host, Traefik's Ingress
// controller in the cluster (docs/plans/swarm/phase-5-app-tier.md) - so the
// immediate proxy is always trustworthy, but its container/pod IP is
// assigned at startup and can't be pinned as a KnownProxy. Clearing
// KnownNetworks/KnownProxies trusts X-Forwarded-For from whatever peer
// connects, which on either deployment is only ever that one proxy. Without
// this, RemoteIpAddress (used by UiLogsController for
// actor telemetry) would just be the proxy's own container/pod IP for every
// request.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// KnownIPNetworks/KnownProxies default to loopback only; Clear() (rather than
// an object-initializer collection, which would just add to those defaults)
// is what actually drops that restriction.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Registered only where it can do anything. It is inert in both deployments
// today - neither the pod nor the container binds an HTTPS port, TLS is
// terminated at the proxy above - so unconditionally it logs one "Failed to
// determine the https port for redirect" warning on the first request and
// passes every request through thereafter. That warning is on the clean-boot
// log of an install that has nothing wrong with it. The `https` launch profile
// in Properties/launchSettings.json sets an https:// application URL, so
// `dotnet run` under it still redirects.
var httpsConfigured =
    !string.IsNullOrEmpty(builder.Configuration["HTTPS_PORT"])
    || !string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_HTTPS_PORTS"])
    || (builder.Configuration["ASPNETCORE_URLS"] ?? "")
           .Contains("https://", StringComparison.OrdinalIgnoreCase);

if (httpsConfigured) app.UseHttpsRedirection();

// Ahead of the wall, so a refusal carries the header too - "which replica
// refused me, and on what build" is unanswerable exactly when the wall is the
// thing misbehaving. Costs one header per response and nothing else.
app.UseMiddleware<HatchRevisionMiddleware>();

// Rewritten before anything else gets a look at the path - including the
// wall, and including the static handlers below. The board's bundle
// directory still physically contains an index.html at the old address (Vite
// still builds into wwwroot/apps/hatch), so a rewrite placed where this used
// to run - after the static block - would never get the chance: the /apps
// mount would already have answered it.
var opt = new RewriteOptions();
// The Hatch module's old /api/hatch prefix, redirected to /api (301 for a read, 308
// for everything else so a write keeps its method and body).
opt.Add(new ApiPrefixRedirectRule());
// The board is the whole origin now, so both of the house's old
// general-purpose addresses, and every deep link that used to live under
// /apps/hatch, redirect to the same path at the root. The query string
// travels with every one of these - AddRedirect appends it by default.
opt.AddRedirect("^apps/hatch$", "/", StatusCodes.Status301MovedPermanently);
// `/+` rather than a single `/`, and the sub-path captured only when it does
// not itself start with a slash or a backslash: a plain `^apps/hatch/(.*)$`
// would echo a leading `/apps/hatch//x.example` straight into a `//` Location,
// which browsers resolve as another host.
opt.AddRedirect(@"^apps/hatch/+([^/\\].*)?$", "/$1", StatusCodes.Status301MovedPermanently);
opt.AddRedirect("^apps/?$", "/", StatusCodes.Status301MovedPermanently);
// The gallery is a page of the board now (HA-2250), at the same address
// shape every other old /apps/<app> address redirects to above.
opt.AddRedirect("^apps/design/*$", "/design", StatusCodes.Status301MovedPermanently);
opt.AddRedirect(@"^apps/design/+([^/\\].*)$", "/design/$1", StatusCodes.Status301MovedPermanently);
// The sign-in shell lives at /auth/ now; the old address is a permanent
// redirect, with the query string carried so /apps/auth/?r=%2Fplan still knows
// where it was going. The bare /auth gets its slash here because the static
// file middleware never adds one to a sub-mount on its own (the same reason
// the old address needed a slash added by hand); /auth/ itself is answered by
// the mount and the fallback further down. The capture follows a literal `/auth/`, so
// whatever it echoes - even a leading slash - stays a path on this host
// rather than becoming the `//` the board's rule above has to guard against.
opt.AddRedirect("^apps/auth/?$", "/auth/", StatusCodes.Status301MovedPermanently);
opt.AddRedirect("^apps/auth/(.*)$", "/auth/$1", StatusCodes.Status301MovedPermanently);
opt.AddRedirect("^auth$", "auth/", StatusCodes.Status301MovedPermanently);
app.UseRewriter(opt);

// The wall. After UseForwardedHeaders because a refusal logs the client IP,
// and after the rewriter above so a request for an old address redirects
// rather than being evaluated against a path that is about to change; before
// the static file handlers below because otherwise every document serves to
// anyone who asks. No-ops entirely while Auth:Enabled is false
// (docs/auth-architecture.md).
app.UseMiddleware<AuthMiddleware>();

// The board's own boundary, immediately after the wall because it reads the
// grant the wall just attached, and before the static handlers below because
// it has to cover both the static bundle and the MapFallbackToFile route that
// answers every client-side path underneath it. No-ops entirely unless
// the wall is on (docs/auth-architecture.md, "The admin flag").
app.UseMiddleware<AdminAppMiddleware>();

// Placed after the wall so an unauthenticated flood is refused before it can
// consume anyone's budget. Routing is added implicitly at the head of the
// pipeline by minimal hosting, so the [EnableRateLimiting] metadata on
// AuthController is already resolved by the time this runs.
app.UseRateLimiter();

// What the /apps mount, the board's own mount, the sign-in shell's mount and
// the fallbacks below all serve through, so the three rules below are written
// once: .webmanifest
// named correctly (absent from FileExtensionContentTypeProvider's own mapping
// table, checked directly against the installed shared framework's
// Microsoft.AspNetCore.StaticFiles.dll, not assumed - without this a
// browser's manifest fetch gets application/octet-stream and silently
// refuses to treat the PWA as installable), and an explicit Cache-Control,
// since its absence leaves a browser free to apply *heuristic* freshness and
// serve index.html from cache without revalidating - on the kiosk tablets
// that turned a deploy into a no-op even across a reload, because the stale
// index.html kept naming the stale bundle. Vite content-hashes everything
// under assets/ (index-BGJobmXl.js), so those URLs are immutable by
// construction and can be cached hard; the documents that *reference* them -
// index.html above all - must be revalidated every time, which is what
// no-cache means (revalidate before reuse), as opposed to no-store (never
// keep a copy at all): a 304 on an unchanged index.html still costs nothing.
static StaticFileOptions BoardFileOptions(IFileProvider provider) => new()
{
    FileProvider = provider,
    ContentTypeProvider = new FileExtensionContentTypeProvider
    {
        Mappings = { [".webmanifest"] = "application/manifest+json" },
    },
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        ctx.Context.Response.Headers.CacheControl =
            path.Contains("/assets/", StringComparison.Ordinal)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    },
};

// Apps (static landing page + SPAs living under wwwroot/apps, outside the REST API)
var appsPath = Path.Combine(app.Environment.WebRootPath, "apps");
if (Directory.Exists(appsPath))
{
    var appsFiles = new PhysicalFileProvider(appsPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = appsFiles,
        RequestPath = "/apps"
    });
    var appsOptions = BoardFileOptions(appsFiles);
    appsOptions.RequestPath = "/apps";
    app.UseStaticFiles(appsOptions);
}

// The board itself, at the origin's root. A PhysicalFileProvider rooted at
// its own bundle directory rather than at WebRootPath: the latter would also
// reach wwwroot/hatch-runner/*, which Dockerfile.api places beside wwwroot/apps
// rather than inside it precisely so that mounting "apps at /apps and nothing
// else" (the comment there, now also true of the root) keeps those binaries
// reachable only through RunnerController and its own [RequireAdmin].
var boardPath = Path.Combine(appsPath, "hatch");
PhysicalFileProvider? board = null;
if (Directory.Exists(boardPath))
{
    board = new PhysicalFileProvider(boardPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = board });
    app.UseStaticFiles(BoardFileOptions(board));
}

// The sign-in shell, at /auth. Mounted the way the board is - its own provider
// rooted at its own bundle directory - rather than left to the generic /apps
// mount above, which now only reaches it at the address that redirects.
var authPath = Path.Combine(appsPath, "auth");
PhysicalFileProvider? authShell = null;
if (Directory.Exists(authPath))
{
    authShell = new PhysicalFileProvider(authPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = authShell, RequestPath = "/auth" });
    var authOptions = BoardFileOptions(authShell);
    authOptions.RequestPath = "/auth";
    app.UseStaticFiles(authOptions);
}

app.UseAuthorization();
app.MapControllers();
// An unrouted /api/* answers 404, not the board's index.html: without this,
// the fallback below - a catch-all with no literal segment to lose to - would
// be the only endpoint left standing for a typo'd or retired API route, and
// would answer it with a page of HTML rather than the 404 a caller checking a
// status code expects. A real controller route still wins regardless of
// registration order: endpoint routing picks by specificity, and a literal
// segment always outranks both this and the fallback (RootMountTests proves it).
app.Map("/api/{**rest}", () => Results.NotFound());
// Split so a database blip fails readiness (pod leaves the Service) without
// failing liveness (pod gets restarted) - restarting every replica over a
// dependency outage just turns one outage into a thundering-herd reconnect.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// SPA fallback so client-side routes (e.g. /issues/AER-12) survive a hard
// refresh. The :nonfile constraint excludes paths with a dot in the last
// segment (e.g. assets/index-abc123.js, or hatch-runner/win-x64/hatch.exe) so
// real static assets still resolve via UseStaticFiles above instead of being
// swallowed by this catch-all route during endpoint matching - and so a
// runner binary 404s rather than getting index.html, there being no bundle
// under this provider with its name anyway.
//
// Hatch deep links are the point rather than a nicety: /issues/AER-12 is what
// gets pasted into a chat window and into a VS Code prompt, so it has to
// survive being opened cold (docs/hatch.md).
if (board is not null)
{
    app.MapFallbackToFile("{*path:nonfile}", "index.html", BoardFileOptions(board));
}

if (authShell is not null)
{
    app.MapFallbackToFile("/auth/{*path:nonfile}", "index.html", BoardFileOptions(authShell));
}

app.UseSwagger();
app.UseSwaggerUI(c => c.DocumentTitle = "Hatch API");
app.MapSwagger();

await app.RunAsync();

// Named so WebApplicationFactory<Program> can find this assembly's entry point:
// top-level statements compile to an internal Program, which the test host cannot
// see. One line, and it changes nothing about how the app runs.
public partial class Program;
