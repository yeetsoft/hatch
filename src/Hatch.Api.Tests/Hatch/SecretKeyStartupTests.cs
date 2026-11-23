using System.Security.Cryptography;
using Hatch.Api.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzLogProvider = Quartz.Logging.LogProvider;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The secret key at startup, against the real host off the real Program.cs
/// (HA-2745). A key that is set and unusable stops the API and names the
/// variable; no key starts it with one warning; a previous key alone still
/// opens what it sealed. Keys are made at run time; none is written in a
/// tracked file.
///
/// <para>Skippable on the same variable as <see cref="MinimalStartupTests"/>,
/// and in the same collection, because Quartz keeps its logging provider in a
/// static that two hosts running at once would fight over.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class SecretKeyStartupTests
{
    private const int CaseBudgetSeconds = 60;
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private const string Row = "site-setting/AnthropicApiKey";

    private static string NewKey(int length = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));

    [SkippableTheory(Timeout = CaseTimeoutMs)]
    [InlineData(16, false)]
    [InlineData(33, false)]
    [InlineData(16, true)]
    [InlineData(33, true)]
    public async Task A_key_of_the_wrong_length_stops_the_host_and_names_the_variable(int length, bool previous)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var value = NewKey(length);

        var (logs, failure) = await BootAsync(previous ? null : value, previous ? value : null, whenUp: null);

        AssertStoppedNaming(previous ? "HATCH_PREVIOUS_SECRET_KEY" : "HATCH_SECRET_KEY", value, logs, failure);
    }

    [SkippableTheory(Timeout = CaseTimeoutMs)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_key_that_is_not_base64_stops_the_host_and_names_the_variable(bool previous)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        const string value = "this is *not* base64!";

        var (logs, failure) = await BootAsync(previous ? null : value, previous ? value : null, whenUp: null);

        AssertStoppedNaming(previous ? "HATCH_PREVIOUS_SECRET_KEY" : "HATCH_SECRET_KEY", value, logs, failure);
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task With_no_key_the_host_starts_and_warns_once()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var (logs, failure) = await BootAsync(null, null, services =>
        {
            var cipher = services.GetRequiredService<SecretCipher>();
            Assert.False(cipher.CanSeal);
            Assert.Null(cipher.CurrentKeyId);
            return Task.CompletedTask;
        });

        Assert.Null(failure);
        var warning = Assert.Single(logs.Messages, m => m.Contains("HATCH_SECRET_KEY"));
        Assert.Contains("secrets cannot be saved until one is set", warning);
    }

    /// <summary>
    /// The development key (HA-2747) reaches the API through <c>.env.json</c>.
    /// The name starts with a dot, which a file provider hides unless it is told
    /// not to, so this goes through the real Program.cs rather than a
    /// configuration builder of its own.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task A_key_in_the_dot_env_json_file_is_the_key_and_no_warning_is_logged()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var root = Path.Combine(Path.GetTempPath(), $"hatch-env-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
        var key = NewKey();
        await File.WriteAllTextAsync(Path.Combine(root, ".env.json"), $$"""{ "Hatch": { "SecretKey": "{{key}}" } }""");

        try
        {
            var (logs, failure) = await BootAsync(null, null, services =>
            {
                var cipher = services.GetRequiredService<SecretCipher>();
                Assert.True(cipher.CanSeal);
                Assert.Equal(SecretKeys.KeyId(Convert.FromBase64String(key)), cipher.CurrentKeyId);
                return Task.CompletedTask;
            }, root);

            Assert.Null(failure);
            Assert.DoesNotContain(logs.Messages, m => m.Contains("HATCH_SECRET_KEY"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task With_only_a_previous_key_the_host_warns_and_still_opens_what_it_sealed()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var previous = NewKey();
        SecretKeys.TryParse(previous, out var previousBytes);
        var sealedValue = new SecretCipher(previousBytes, null).Seal("hunter2", Row);

        var (logs, failure) = await BootAsync(null, previous, services =>
        {
            var cipher = services.GetRequiredService<SecretCipher>();
            Assert.False(cipher.CanSeal);
            Assert.True(cipher.TryOpen(sealedValue, Row, out var opened));
            Assert.Equal("hunter2", opened);
            return Task.CompletedTask;
        });

        Assert.Null(failure);
        Assert.Single(logs.Messages, m => m.Contains("HATCH_SECRET_KEY"));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(previous));
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task With_a_key_set_the_host_starts_without_a_warning_and_never_logs_the_key()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var current = NewKey();
        var previous = NewKey();

        var (logs, failure) = await BootAsync(current, previous, services =>
        {
            var cipher = services.GetRequiredService<SecretCipher>();
            Assert.True(cipher.CanSeal);
            Assert.NotNull(cipher.Seal("hunter2", Row));
            return Task.CompletedTask;
        });

        Assert.Null(failure);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("HATCH_SECRET_KEY"));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(current) || m.Contains(previous));
    }

    /// <summary>
    /// Rotation end to end (HA-2749). Values sealed under one key, a start with a
    /// second current and the first previous leaves every value under the second;
    /// a start with the second alone still opens every one.
    /// </summary>
    [SkippableFact(Timeout = CaseTimeoutMs * 2)]
    public async Task A_rotated_key_reseals_every_stored_secret_at_start_and_the_old_key_can_then_go()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var first = NewKey();
        var second = NewKey();
        SecretKeys.TryParse(first, out var firstBytes);
        SecretKeys.TryParse(second, out var secondBytes);

        Dictionary<string, string> plaintexts = [];
        var databases = await MinimalDatabases.CreateAsync();
        try
        {
            await MinimalDatabases.MigrateAsync(databases.Hatch);
            plaintexts = await SecretResealTests.SeedForRotationAsync(databases.Hatch, firstBytes!);

            var rotated = await BootAsync(second, first, services => AssertEveryValueIsUnderAsync(services, secondBytes!, plaintexts), existingHatch: databases.Hatch);
            Assert.Null(rotated.Failure);
            Assert.Single(rotated.Logs.Messages, m => m.Contains($"Resealed {plaintexts.Count} stored secrets under key {SecretKeys.KeyId(secondBytes!)}; 0 could not be opened."));
            Assert.DoesNotContain(rotated.Logs.Messages, m => m.Contains(first) || m.Contains(second));

            var alone = await BootAsync(second, null, services => AssertEveryValueIsUnderAsync(services, secondBytes!, plaintexts), existingHatch: databases.Hatch);
            Assert.Null(alone.Failure);
            Assert.Single(alone.Logs.Messages, m => m.Contains("Resealed 0 stored secrets"));
        }
        finally
        {
            await MinimalDatabases.DropAsync(databases.Hatch, databases.Quartz);
        }
    }

    private static async Task AssertEveryValueIsUnderAsync(IServiceProvider services, byte[] key, Dictionary<string, string> plaintexts)
    {
        // Stopping a host while its Quartz scheduler is still starting can hang the stop for a
        // minute. The other cases here stop theirs about as fast, but they have less to do.
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        for (var waited = 0; !scheduler.IsStarted && waited < 100; waited++) await Task.Delay(50);

        var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Hatch")!;
        var protector = new SecretProtector(new SecretCipher(key, null));

        var stored = await SecretResealTests.ReadAsync(connectionString);
        Assert.Equal(plaintexts.Keys.Order(), stored.Keys.Order());
        foreach (var (row, value) in stored)
        {
            Assert.True(protector.IsCurrent(value), $"{row} is not under the current key");
            Assert.Equal(plaintexts[row], protector.Unprotect(value, row));
        }
    }

    private static void AssertStoppedNaming(string variable, string value, CapturedLogs logs, Exception? failure)
    {
        Assert.NotNull(failure);
        var sentence = Describe(failure);

        Assert.Contains(variable, sentence);
        Assert.Contains("base64 of exactly 32 bytes", sentence);
        Assert.DoesNotContain(value, sentence);
        Assert.DoesNotContain(logs.Messages, m => m.Contains(value));

        // The other variable is not named: only the bad one is.
        if (variable == "HATCH_SECRET_KEY") Assert.DoesNotContain("HATCH_PREVIOUS_SECRET_KEY", sentence);
    }

    /// <summary>Every message down the exception, so a wrapper around the validation failure does not hide it.</summary>
    private static string Describe(Exception exception)
    {
        var messages = new List<string>();
        var pending = new Stack<Exception>([exception]);
        while (pending.Count > 0)
        {
            var e = pending.Pop();
            messages.Add(e.Message);
            if (e is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
            else if (e.InnerException is not null)
                pending.Push(e.InnerException);
        }

        return string.Join(Environment.NewLine, messages);
    }

    /// <summary>
    /// Starts the host with the two settings (a null one is left unset), runs
    /// <paramref name="whenUp"/> against its services if it came up, and returns
    /// what it logged and the exception starting it threw, if any. The scratch
    /// databases and Quartz's static are handled as MinimalStartupTests handles them.
    /// </summary>
    private static async Task<(CapturedLogs Logs, Exception? Failure)> BootAsync(
        string? current, string? previous, Func<IServiceProvider, Task>? whenUp, string? contentRoot = null,
        string? existingHatch = null)
    {
        var logs = new CapturedLogs();
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), () => string.Join(Environment.NewLine, logs.Messages));

        // A Hatch database passed in belongs to the caller, who has seeded it and drops it. The
        // Quartz one is always fresh: two hosts one after the other on one Quartz database
        // leave the first's scheduler thread logging into a host that has gone.
        var (made, quartz) = await deadline.BoundAsync("creating the scratch databases", MinimalDatabases.CreateAsync);
        var hatch = existingHatch ?? made;

        // See MinimalStartupTests: Quartz binds its logging to the first host
        // that builds a scheduler, so it is cleared on the way in and out.
        QuartzLogProvider.SetCurrentLogProvider(null);

        var factory = new MinimalFactory(hatch, quartz, logs);
        WebApplicationFactory<Program>? derived = null;
        Exception? failure = null;
        try
        {
            derived = factory.WithWebHostBuilder(builder =>
            {
                // Only the captured logs. A Quartz job can fire as the host stops, and its failure is
                // logged through every provider; the Windows event log's is already disposed by then
                // and throws out of the scheduler thread into whichever test is running.
                builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs));
                if (contentRoot is not null) builder.UseContentRoot(contentRoot);
                if (current is not null) builder.UseSetting("Hatch:SecretKey", current);
                if (previous is not null) builder.UseSetting("Hatch:PreviousSecretKey", previous);
            });

            try
            {
                using var client = await deadline.BoundAsync("starting the host", () => Task.Run(() => derived.CreateClient()));
            }
            catch (Exception e) when (e is not DeadlineExceededException)
            {
                failure = e;
            }

            if (failure is null && whenUp is not null)
                await deadline.BoundAsync("inspecting the host", () => whenUp(derived.Services));
        }
        finally
        {
            if (derived is not null) await deadline.BoundAsync("stopping the derived host", () => derived.DisposeAsync().AsTask());
            await deadline.BoundAsync("stopping the host", () => factory.DisposeAsync().AsTask());
            QuartzLogProvider.SetCurrentLogProvider(null);

            await deadline.BoundAsync("dropping the scratch databases", () => MinimalDatabases.DropAsync(made, quartz));
        }

        return (logs, failure);
    }
}
