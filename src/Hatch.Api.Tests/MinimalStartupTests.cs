using Hatch.Api.Tests.Hatch;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests;

/// <summary>
/// The story's first criterion, asserted rather than remembered: the API,
/// given a database connection and nothing else, comes up.
///
/// <para>Nothing here is mocked. It boots the real host off the real
/// Program.cs against a real Postgres, supplying exactly the three settings a
/// friend's <c>docker run</c> supplies - the two connection strings and
/// <c>Auth:Enabled=false</c> - and no log-shipper token. That is the point: the failure it
/// guards against was a database read from inside DI resolution, which no
/// unit test of any one class can see.</para>
///
/// <para>It runs twice over the same pair of scratch databases: once against
/// one nobody has migrated, which is what used to exit 139 before the first
/// request, and once after MigrateAsync. Both have to start.</para>
///
/// <para>Skippable on the same variable as <see cref="HatchDatabase"/>, and
/// for the same reason - asserted in CI, where ci.yml's api job sets it, and
/// skipped out loud on a laptop with no Postgres rather than reported as
/// passed.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class MinimalStartupTests
{
    /// <summary>
    /// Everything ReflectionCadence and Backup are registered under - the two
    /// jobs, and so the two triggers, this group is left holding.
    /// </summary>
    private const string JobGroup = "Hatch.Api";

    /// <summary>The only triggers an install carries - see <see cref="JobGroup"/>.</summary>
    private static readonly string[] ExpectedTriggers = ["ReflectionCadence_Trigger", "Backup_Trigger"];

    /// <summary>
    /// The whole assembly runs in about twenty seconds today and each of these
    /// cases takes a few, so this is comfortably under the two minutes of
    /// inactivity that aborts a run (<c>API_TEST_CLAMP</c> in the Makefile, and
    /// the same flags in ci.yml) - a stalled phase fails this case alone,
    /// rather than taking the clamp's abort of the whole host with it.
    /// </summary>
    private const int CaseBudgetSeconds = 60;

    /// <summary>
    /// Belt and braces over <see cref="Deadline"/>, a little past its own
    /// budget so it only fires if <see cref="Deadline"/> itself fails to bound
    /// something - it names no phase and captures no log, which is why it is
    /// not the thing doing the bounding.
    /// </summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>
    /// The exit 139. Resolving JobsInit constructs every job, and a service
    /// that read the database while DI was resolving it, on a database the
    /// migrate step has not touched, found a table that did not exist. The
    /// exception used to unwind out of Main before the first request.
    ///
    /// <para>What is asserted is that it stays up and it serves. Not the
    /// absence of Error lines - a job's first firing against an unmigrated
    /// database may report the missing table at Error, correctly, because a
    /// serving process that cannot read its database has something wrong with
    /// it. Something is: the migrate step has not run. The clean log is the case
    /// below, which is the one a friend is actually left in.</para>
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AgainstADatabaseNobodyHasMigrated_ItStaysUpAndServes()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await AssertStartsAndServesAsync(migrated: false);
    }

    /// <summary>
    /// The install a friend is left with once the migrate step has run:
    /// nothing at Error or above, and exactly the two triggers
    /// <see cref="ExpectedTriggers"/> names.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AgainstAMigratedDatabase_ItStartsCleanlyAndSchedulesOnlyTheTwoJobs()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var logs = await AssertStartsAndServesAsync(migrated: true);

        Assert.True(logs.AtErrorOrAbove.Count == 0, string.Join(Environment.NewLine, logs.AtErrorOrAbove));
    }

    /// <summary>
    /// ReflectionCadence's and Backup's triggers start now, so their first
    /// firings land inside the few hundred milliseconds between /health/ready
    /// answering and this host being disposed. Disposed mid-firing, Quartz
    /// fails to build the job from a service provider that is already gone and
    /// says so at Error - the test's own teardown, not a fault in startup. So
    /// the host is not stopped until both firings have run their course; the
    /// next is fifteen minutes away for one and a day for the other. Backup
    /// with no path set is a no-op, but it is still a firing to be caught
    /// mid-construction. Bounded at ten seconds and then let go, so a firing that
    /// never comes is not this case's failure to report.
    ///
    /// <para>"Fired" and "executing" are not the same instant: Quartz records
    /// the previous fire time first, then builds the job, and only lists it
    /// among the currently executing jobs once it is running. A single sample
    /// of "fired and nothing executing" can land in that gap - and did, in CI,
    /// with the host disposed underneath the job's construction. So it takes
    /// several samples in a row, spanning far longer than the gap, before the
    /// firing counts as over.</para>
    /// </summary>
    private static async Task SettleFirstFiringAsync(IScheduler scheduler)
    {
        const int IdleSamples = 10;

        var triggers = new[]
        {
            new TriggerKey("ReflectionCadence_Trigger", JobGroup),
            new TriggerKey("Backup_Trigger", JobGroup),
        };
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        var idle = 0;

        while (DateTimeOffset.UtcNow < until)
        {
            var fired = true;
            foreach (var trigger in triggers)
            {
                fired &= (await scheduler.GetTrigger(trigger))?.GetPreviousFireTimeUtc() is not null;
            }


            idle = fired && (await scheduler.GetCurrentlyExecutingJobs()).Count == 0 ? idle + 1 : 0;
            if (idle >= IdleSamples) return;
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// What both cases share: the host builds and starts, /health/ready answers
    /// 200, and the store holds exactly the expected triggers - every phase of
    /// it, including making the scratch databases, bounded against one
    /// <see cref="Deadline"/> so a stalled phase fails this case alone, naming
    /// itself, rather than taking the whole run with it.
    /// </summary>
    private static async Task<CapturedLogs> AssertStartsAndServesAsync(bool migrated)
    {
        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), () => string.Join(Environment.NewLine, logs.Messages));

        var (hatch, quartz) = await deadline.BoundAsync("creating the scratch databases", MinimalDatabases.CreateAsync);

        if (migrated) await deadline.BoundAsync("migrating", () => MinimalDatabases.MigrateAsync(hatch));

        // Quartz keeps its logging provider in a static, bound to whichever
        // host built a scheduler first. Production has one host per process and
        // never notices; a test file with two of them gets the *first* host's
        // LoggerFactory, disposed with it, and the second host dies resolving
        // IScheduler. Clearing it lets this host's AddQuartz rebind to its own.
        //
        // Cleared on the way out as well, and that half is not symmetry for its
        // own sake: the binding outlives the host it points at, so a host left
        // in the static is a disposed LoggerFactory every later scheduler in
        // this process resolves through. JobsInitTests is the one that pays -
        // ObjectDisposedException out of StdSchedulerFactory.GetScheduler,
        // which is why leaving here matters as much as arriving.
        QuartzLogProvider.SetCurrentLogProvider(null);

        var factory = new MinimalFactory(hatch, quartz, logs);
        try
        {
            // Creating the client is what builds and starts the host - before
            // this line nothing has run, and the exit 139 came out of exactly
            // here. factory.CreateClient() blocks on Task.Wait() rather than
            // awaiting, so Task.Run is what gives Deadline something to race.
            using var client = await deadline.BoundAsync("starting the host", () => Task.Run(() => factory.CreateClient()));

            var health = await deadline.BoundAsync("serving /health/ready", () => client.GetAsync("/health/ready"));
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            // Routes that no longer exist: the platform config seam, the
            // kiosk-era app-version probe and the four family apps. This
            // factory has the wall off, so a 404 is the route being gone
            // rather than gated.
            foreach (var gone in new[]
            {
                "/api/apps/config", "/api/app-version/hatch",
                "/api/gather", "/api/photos", "/api/quill", "/api/storage",
            })
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(gone)).StatusCode);

            // The OpenAPI document still generates: two types sharing a name
            // is what makes Swashbuckle throw here, and nothing else reads it
            // until somebody opens the page.
            var swagger = await deadline.BoundAsync("serving /swagger/v1/swagger.json", () => client.GetAsync("/swagger/v1/swagger.json"));
            Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
            using (JsonDocument.Parse(await swagger.Content.ReadAsStringAsync()))
            {
            }

            // The page itself carries the shared bar and the sheet that dresses
            // it from the tokens: the token file first, so the sheet has
            // something to read, then the bar's config and script.
            var page = await deadline.BoundAsync("serving /swagger/index.html", () => client.GetAsync("/swagger/index.html"));
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            var tokens = html.IndexOf("/apps/chrome/topbar.css", StringComparison.Ordinal);
            var sheet = html.IndexOf("/apps/chrome/swagger.css", StringComparison.Ordinal);
            Assert.True(tokens >= 0, "the page does not link /apps/chrome/topbar.css");
            Assert.True(sheet > tokens, "the page does not link /apps/chrome/swagger.css after the token file");
            Assert.Contains("/apps/chrome/topbar.js", html);
            Assert.Contains("window.hatchTopBar", html);

            await deadline.BoundAsync("letting the jobs' first firings finish", () => SettleFirstFiringAsync(factory.Services.GetRequiredService<IScheduler>()));
        }
        finally
        {
            await deadline.BoundAsync("stopping the host", () => factory.DisposeAsync().AsTask());
            QuartzLogProvider.SetCurrentLogProvider(null);
        }

        // The drop is in its own finally, separate from the host's above: an
        // assertion below failing is not a reason to leave the pair on the
        // server right alongside the one that says so.
        try
        {
            // Unordered: the query has no ORDER BY, so compare sorted.
            var triggerNames = await deadline.BoundAsync("reading job triggers", () => MinimalDatabases.TriggerNamesAsync(quartz, JobGroup));
            Assert.Equal(ExpectedTriggers.Order(), triggerNames.Order());

            if (migrated)
            {
                // Not the four Init alone seeds: TheFlow and Playbooks run too, in
                // the same migration chain HATCH_MIGRATE=1 applies in full, and
                // they are what a fresh install's board actually ends up with -
                // the seven-column flow TheFlow's own doc comment names.
                var statuses = await deadline.BoundAsync("reading the seeded statuses", () => MinimalDatabases.StatusNamesAsync(hatch));
                Assert.Equal(["Draft", "Breakdown", "Backlog", "To Do", "In Progress", "In Review", "Done"], statuses);
            }
        }
        finally
        {
            await deadline.BoundAsync("dropping the scratch databases", () => MinimalDatabases.DropAsync(hatch, quartz));
        }

        return logs;
    }
}
