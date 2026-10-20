using Hatch.Api.Tests.Hatch;
using System.Net;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests;

/// <summary>
/// The story's first criterion, asserted rather than remembered: the API,
/// given a database connection and nothing else, comes up.
///
/// <para>Nothing here is mocked. It boots the real host off the real
/// Program.cs against a real Postgres, supplying exactly the three settings a
/// friend's <c>docker run</c> supplies - the two connection strings and
/// <c>Auth:Enabled=false</c> - and no Home Assistant, no go2rtc, no share, no
/// kiosk host and no log-shipper token. That is the point: the failure it
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
    /// <summary>Everything the four house jobs and BackfillChannelHistory are registered under - criterion 6 is that no trigger for it exists.</summary>
    private const string JobGroup = "Hatch.Api";

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
    /// The exit 139. Resolving JobsInit constructs every job, one of which takes
    /// an HADotNet client whose registration blocks on a SiteSettings read - and
    /// on a database the migrate step has not touched, that table does not
    /// exist. The exception used to unwind out of Main before the first request.
    ///
    /// <para>What is asserted is criterion 1's second half exactly: it stays up,
    /// it serves, and it says what it decided. Not the absence of Error lines -
    /// EF reports the failed read at Error, correctly, because a serving process
    /// that cannot run a command has something wrong with it. Something is: the
    /// migrate step has not run. Criterion 5's clean log is the case below,
    /// which is the one a friend is actually left in.</para>
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AgainstADatabaseNobodyHasMigrated_ItStaysUpAndSaysWhy()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var logs = await AssertStartsAndServesAsync(migrated: false);

        Assert.Contains(logs.Messages, m => m.Contains("Could not read the site settings", StringComparison.Ordinal));
    }

    /// <summary>
    /// The install a friend is left with once the migrate step has run:
    /// criteria 1, 5 and 6 at startup. Nothing at Error or above, and not one
    /// of the four house jobs holding a trigger - which is what says the
    /// weather call that would have gone out to the internet, and the 7,200 log
    /// lines a day, are not going to happen.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AgainstAMigratedDatabase_ItStartsCleanlyAndSchedulesNoHouseJob()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var logs = await AssertStartsAndServesAsync(migrated: true);

        Assert.True(logs.AtErrorOrAbove.Count == 0, string.Join(Environment.NewLine, logs.AtErrorOrAbove));
    }

    /// <summary>
    /// What both cases share: the host builds and starts, /health/ready answers
    /// 200, and the store holds no trigger for any house job - every phase of
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
            Assert.Empty(await deadline.BoundAsync("reading job triggers", () => MinimalDatabases.TriggerNamesAsync(quartz, JobGroup)));

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
