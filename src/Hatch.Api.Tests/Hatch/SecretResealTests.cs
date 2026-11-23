using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="SecretReseal"/> against a real Postgres (HA-2749): the advisory
/// lock and the conditional update are both things in-memory EF has no way to
/// say. The tests that need no database - no key, and migrate mode - run
/// everywhere; the rest skip loudly without <c>HATCH_TEST_DATABASE_URL</c>.
/// Rotation through the real host is <see cref="SecretKeyStartupTests"/>.
///
/// <para>One migrated scratch database is shared by the class (xUnit runs a
/// class's tests one at a time) and its two secret tables are emptied before
/// each test.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class SecretResealTests(SecretResealTests.Scratch scratch) : IClassFixture<SecretResealTests.Scratch>
{
    private const string SkipReason = "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.";

    private static readonly byte[] KeyA = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] KeyB = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] KeyC = RandomNumberGenerator.GetBytes(32);

    /// <summary>One migrated database for the class, made on first use and dropped with it.</summary>
    public sealed class Scratch : IAsyncLifetime
    {
        private string? quartz;

        public string? Hatch { get; private set; }

        public async Task InitializeAsync()
        {
            if (!HatchDatabase.Available) return;

            (var hatch, quartz) = await MinimalDatabases.CreateAsync();
            await MinimalDatabases.MigrateAsync(hatch);
            Hatch = hatch;
        }

        public async Task DisposeAsync()
        {
            if (Hatch is not null && quartz is not null) await MinimalDatabases.DropAsync(Hatch, quartz);
        }
    }

    /// <summary>Everything the seed writes, as it is before any reseal: row name to the plaintext it holds.</summary>
    private sealed record Seeded(Dictionary<string, string> Plaintexts, Dictionary<string, string> Stored, string[] CurrentKept);

    [SkippableFact]
    public async Task Every_old_value_is_resealed_under_the_current_key_and_a_second_run_changes_nothing()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var connectionString = await EmptyAsync();
        var seeded = await SeedAsync(connectionString);

        var logs = new CapturedLogs();
        await Service(KeyB, KeyA, connectionString, logs).StartAsync(CancellationToken.None);

        var first = await ReadAsync(connectionString);
        var protector = new SecretProtector(new SecretCipher(KeyB, KeyA));
        Assert.Equal(seeded.Plaintexts.Keys.Order(), first.Keys.Order());
        foreach (var (row, stored) in first)
        {
            Assert.True(protector.IsCurrent(stored), $"{row} is not v2 under the current key");
            Assert.Equal(seeded.Plaintexts[row], protector.Unprotect(stored, row));
        }

        // The two already under the current key were not rewritten.
        foreach (var kept in seeded.CurrentKept) Assert.Equal(seeded.Stored[kept], first[kept]);
        Assert.Contains(logs.Messages, m => m.Contains($"Resealed 5 stored secrets under key {SecretKeys.KeyId(KeyB)}; 0 could not be opened."));

        await Service(KeyB, KeyA, connectionString, new CapturedLogs()).StartAsync(CancellationToken.None);

        Assert.Equal(first, await ReadAsync(connectionString));
    }

    [SkippableFact]
    public async Task Two_instances_started_together_do_not_both_reseal()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var connectionString = await EmptyAsync();
        var seeded = await SeedAsync(connectionString);

        var firstLogs = new CapturedLogs();
        var secondLogs = new CapturedLogs();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var first = Service(KeyB, KeyA, connectionString, firstLogs);
        first.WhileHeld = async _ =>
        {
            entered.SetResult();
            await release.Task;
        };

        var running = Task.Run(() => first.StartAsync(CancellationToken.None));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The first holds the lock and has resealed nothing yet; the second starts now.
        await Service(KeyB, KeyA, connectionString, secondLogs).StartAsync(CancellationToken.None);

        Assert.Contains(secondLogs.Messages, m => m.Contains("Another instance is resealing"));
        Assert.DoesNotContain(secondLogs.Messages, m => m.Contains("Resealed "));
        Assert.Equal(seeded.Stored, await ReadAsync(connectionString));

        release.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains(firstLogs.Messages, m => m.Contains("Resealed 5 stored secrets"));
        Assert.DoesNotContain(firstLogs.Messages, m => m.Contains("Another instance"));
    }

    [SkippableFact]
    public async Task The_advisory_lock_is_free_again_once_a_holder_is_disposed()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var connectionString = await EmptyAsync();

        var held = await PostgresAdvisoryLock.TryAcquireAsync(connectionString, SecretReseal.LockKey, CancellationToken.None);
        Assert.NotNull(held);
        Assert.Null(await PostgresAdvisoryLock.TryAcquireAsync(connectionString, SecretReseal.LockKey, CancellationToken.None));

        // A different key is a different lock.
        var other = await PostgresAdvisoryLock.TryAcquireAsync(connectionString, SecretReseal.LockKey + 1, CancellationToken.None);
        Assert.NotNull(other);
        await other.DisposeAsync();

        await held.DisposeAsync();

        var again = await PostgresAdvisoryLock.TryAcquireAsync(connectionString, SecretReseal.LockKey, CancellationToken.None);
        Assert.NotNull(again);
        await again.DisposeAsync();
    }

    [SkippableFact]
    public async Task A_value_that_cannot_be_opened_is_left_alone_and_named_without_its_value()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var connectionString = await EmptyAsync();

        var plaintext = "plaintext-that-must-never-be-logged";
        var unknownKeyId = SecretKeys.KeyId(KeyC);
        var unknown = $"v2:{new SecretCipher(KeyC, null).Seal(plaintext, SecretStores.SiteSetting(SiteSettingKeys.XaiApiKey))}";
        var damaged = "v2:" + SecretKeys.KeyId(KeyA) + ".AAAA.BBBB";
        var forThisKeyOnly = $"v2:{new SecretCipher(KeyA, null).Seal("opens-fine", SecretStores.SiteSetting(SiteSettingKeys.CodexApiKey))}";

        await UpsertSettingAsync(connectionString, SiteSettingKeys.XaiApiKey, unknown);
        await UpsertSettingAsync(connectionString, SiteSettingKeys.AnthropicApiKey, damaged);
        await UpsertSettingAsync(connectionString, SiteSettingKeys.CodexApiKey, forThisKeyOnly);

        var logs = new CapturedLogs();
        await Service(KeyB, KeyA, connectionString, logs).StartAsync(CancellationToken.None);

        var after = await ReadAsync(connectionString);
        Assert.Equal(unknown, after[SecretStores.SiteSetting(SiteSettingKeys.XaiApiKey)]);
        Assert.Equal(damaged, after[SecretStores.SiteSetting(SiteSettingKeys.AnthropicApiKey)]);

        // The rest were still resealed.
        var protector = new SecretProtector(new SecretCipher(KeyB, KeyA));
        var codex = SecretStores.SiteSetting(SiteSettingKeys.CodexApiKey);
        Assert.True(protector.IsCurrent(after[codex]));
        Assert.Equal("opens-fine", protector.Unprotect(after[codex], codex));

        var named = Assert.Single(logs.Messages, m => m.Contains("site-setting/XaiApiKey"));
        Assert.Contains(unknownKeyId, named);
        Assert.Contains(logs.Messages, m => m.Contains("site-setting/AnthropicApiKey"));
        Assert.Contains(logs.Messages, m => m.Contains($"Resealed 1 stored secrets under key {SecretKeys.KeyId(KeyB)}; 2 could not be opened."));

        foreach (var message in logs.Messages)
        {
            Assert.DoesNotContain(plaintext, message);
            Assert.DoesNotContain(unknown, message);
            Assert.DoesNotContain(damaged, message);
            Assert.DoesNotContain(forThisKeyOnly, message);
        }
    }

    [SkippableFact]
    public async Task A_secret_saved_after_it_was_read_is_not_overwritten()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);
        var connectionString = await EmptyAsync();

        var row = SecretStores.SiteSetting(SiteSettingKeys.AnthropicApiKey);
        var protector = new SecretProtector(new SecretCipher(KeyB, KeyA));
        var readOld = "v1:" + SecretObfuscator.Obfuscate("the old secret");
        await UpsertSettingAsync(connectionString, SiteSettingKeys.AnthropicApiKey, readOld);

        // The reseal has read the old text; a save lands before its update does.
        Assert.True(protector.TrySeal("the new secret", row, out var saved));
        await UpsertSettingAsync(connectionString, SiteSettingKeys.AnthropicApiKey, saved);

        var logs = new CapturedLogs();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var outcome = await Service(KeyB, KeyA, connectionString, logs)
            .ResealRowAsync(connection, SecretStores.SiteSettings, [SiteSettingKeys.AnthropicApiKey], readOld, CancellationToken.None);

        Assert.Equal(SecretReseal.RowOutcome.SavedMeanwhile, outcome);
        var after = (await ReadAsync(connectionString))[row];
        Assert.Equal(saved, after);
        Assert.Equal("the new secret", protector.Unprotect(after, row));
    }

    [SkippableFact]
    public async Task A_database_without_the_tables_is_logged_and_does_not_stop_the_start()
    {
        Skip.IfNot(HatchDatabase.Available, SkipReason);

        var (hatch, quartz) = await MinimalDatabases.CreateAsync();
        try
        {
            var logs = new CapturedLogs();
            await Service(KeyB, KeyA, hatch, logs).StartAsync(CancellationToken.None);

            var error = Assert.Single(logs.AtErrorOrAbove);
            Assert.Contains("Resealing stored secrets failed", error);
            Assert.DoesNotContain(logs.Messages, m => m.Contains("Resealed "));
        }
        finally
        {
            await MinimalDatabases.DropAsync(hatch, quartz);
        }
    }

    [Fact]
    public async Task With_no_current_key_it_touches_neither_the_lock_nor_the_database()
    {
        // Nothing listens here, so any connection at all would be an error.
        const string nowhere = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1";

        foreach (var (current, previous) in new (byte[]?, byte[]?)[] { (null, null), (null, KeyA) })
        {
            var logs = new CapturedLogs();
            await Service(current, previous, nowhere, logs).StartAsync(CancellationToken.None);
            Assert.Empty(logs.Messages);
        }
    }

    [Fact]
    public async Task In_migrate_mode_it_does_nothing_even_with_a_key()
    {
        const string nowhere = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1";

        var logs = new CapturedLogs();
        await Service(KeyB, KeyA, nowhere, logs, migrate: true).StartAsync(CancellationToken.None);

        Assert.Empty(logs.Messages);
    }

    [Fact]
    public void A_stored_value_is_current_only_when_v2_names_the_current_key()
    {
        var protector = new SecretProtector(new SecretCipher(KeyB, KeyA));
        var row = SecretStores.SiteSetting(SiteSettingKeys.XaiApiKey);

        Assert.True(protector.TrySeal("x", row, out var current));
        var previous = $"v2:{new SecretCipher(KeyA, null).Seal("x", row)}";

        Assert.True(protector.IsCurrent(current));
        Assert.False(protector.IsCurrent(previous));
        Assert.False(protector.IsCurrent("v1:" + SecretObfuscator.Obfuscate("x")));
        Assert.False(protector.IsCurrent(SecretObfuscator.Obfuscate("x")));
        Assert.False(protector.IsCurrent(""));
        Assert.False(protector.IsCurrent(null));
        Assert.False(new SecretProtector(new SecretCipher(null, KeyA)).IsCurrent(previous));
    }

    [Fact]
    public void The_key_id_is_read_only_from_a_v2_value_shaped_like_one()
    {
        var row = SecretStores.SiteSetting(SiteSettingKeys.XaiApiKey);
        var sealedValue = $"v2:{new SecretCipher(KeyA, null).Seal("x", row)}";

        Assert.Equal(SecretKeys.KeyId(KeyA), SecretProtector.KeyIdOf(sealedValue));
        Assert.Null(SecretProtector.KeyIdOf("v1:" + SecretObfuscator.Obfuscate("x")));
        Assert.Null(SecretProtector.KeyIdOf(SecretObfuscator.Obfuscate("x")));
        Assert.Null(SecretProtector.KeyIdOf("v2:hunter2.AAAA.BBBB"));
        Assert.Null(SecretProtector.KeyIdOf("v2:"));
        Assert.Null(SecretProtector.KeyIdOf("v2:abcdef12"));
        Assert.Null(SecretProtector.KeyIdOf(""));
        Assert.Null(SecretProtector.KeyIdOf(null));
    }

    private static SecretReseal Service(byte[]? current, byte[]? previous, string connectionString, CapturedLogs logs, bool migrate = false)
    {
        var cipher = new SecretCipher(current, previous);
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Hatch"] = connectionString };
        if (migrate) settings["HATCH_MIGRATE"] = "1";

        return new SecretReseal(
            new SecretProtector(cipher),
            cipher,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            LoggerFactory.Create(b => b.AddProvider(logs)).CreateLogger<SecretReseal>());
    }

    /// <summary>The shared database with both secret tables emptied, and its connection string.</summary>
    private async Task<string> EmptyAsync()
    {
        var connectionString = scratch.Hatch ?? throw new InvalidOperationException("The scratch database was not made");
        await ExecuteAsync(connectionString, "DELETE FROM hatch.\"ChatWorkspaces\"");
        await ExecuteAsync(connectionString, "DELETE FROM \"SiteSettings\" WHERE \"Key\" = ANY(@keys)", ("keys", SiteSettingKeys.Secrets.ToArray()));
        return connectionString;
    }

    /// <summary>
    /// Seven rows across both stores: a <c>v1</c>, an untagged and a previous-key
    /// value in the settings, a <c>v1</c> and a previous-key value on workspaces,
    /// and one under the current key in each store. Five of them need resealing.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string connectionString)
    {
        var current = new SecretCipher(KeyB, null);
        var previous = new SecretCipher(KeyA, null);
        string V2(SecretCipher cipher, string plaintext, string row) => $"v2:{cipher.Seal(plaintext, row)}";

        var plaintexts = new Dictionary<string, string>();
        var settings = new Dictionary<string, string>();
        var workspaces = new Dictionary<(string, string), string>();

        void Setting(string key, string plaintext, Func<string, string, string> stored)
        {
            var row = SecretStores.SiteSetting(key);
            plaintexts[row] = plaintext;
            settings[key] = stored(plaintext, row);
        }

        void Workspace(string externalId, string plaintext, Func<string, string, string> stored)
        {
            var row = SecretStores.ChatWorkspace("slack", externalId);
            plaintexts[row] = plaintext;
            workspaces[("slack", externalId)] = stored(plaintext, row);
        }

        Setting(SiteSettingKeys.AnthropicApiKey, "setting-one", (p, _) => "v1:" + SecretObfuscator.Obfuscate(p));
        Setting(SiteSettingKeys.XaiApiKey, "setting-two", (p, _) => SecretObfuscator.Obfuscate(p));
        Setting(SiteSettingKeys.CodexApiKey, "setting-three", (p, r) => V2(previous, p, r));
        Setting(SiteSettingKeys.GoogleClientSecret, "setting-four", (p, r) => V2(current, p, r));
        Workspace("T1", "workspace-one", (p, _) => "v1:" + SecretObfuscator.Obfuscate(p));
        Workspace("T2", "workspace-two", (p, r) => V2(previous, p, r));
        Workspace("T3", "workspace-three", (p, r) => V2(current, p, r));

        foreach (var (key, value) in settings) await UpsertSettingAsync(connectionString, key, value);
        foreach (var ((provider, externalId), value) in workspaces)
        {
            await ExecuteAsync(connectionString,
                "INSERT INTO hatch.\"ChatWorkspaces\" (\"Provider\", \"ExternalId\", \"Name\", \"Token\", \"CreatedAt\", \"UpdatedAt\") " +
                "VALUES (@p, @e, 'Workspace', @t, now(), now())",
                ("p", provider), ("e", externalId), ("t", value));
        }

        var stored = await ReadAsync(connectionString);
        return new Seeded(plaintexts, stored,
            [SecretStores.SiteSetting(SiteSettingKeys.GoogleClientSecret), SecretStores.ChatWorkspace("slack", "T3")]);
    }

    private static Task UpsertSettingAsync(string connectionString, string key, string value) =>
        ExecuteAsync(connectionString,
            "INSERT INTO \"SiteSettings\" (\"Key\", \"Value\") VALUES (@k, @v) ON CONFLICT (\"Key\") DO UPDATE SET \"Value\" = EXCLUDED.\"Value\"",
            ("k", key), ("v", value));

    /// <summary>Every stored secret in both stores, by row name.</summary>
    internal static async Task<Dictionary<string, string>> ReadAsync(string connectionString)
    {
        var stored = new Dictionary<string, string>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var command = new NpgsqlCommand("SELECT \"Key\", \"Value\" FROM \"SiteSettings\" WHERE \"Key\" = ANY(@keys) AND \"Value\" <> ''", connection))
        {
            command.Parameters.AddWithValue("keys", SiteSettingKeys.Secrets.ToArray());
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) stored[SecretStores.SiteSetting(reader.GetString(0))] = reader.GetString(1);
        }

        await using (var command = new NpgsqlCommand("SELECT \"Provider\", \"ExternalId\", \"Token\" FROM hatch.\"ChatWorkspaces\" WHERE \"Token\" <> ''", connection))
        {
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) stored[SecretStores.ChatWorkspace(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return stored;
    }

    /// <summary>Seeds the migrated database at <paramref name="connectionString"/> with a value sealed under <paramref name="sealedUnder"/> in every secret setting and on one workspace, for the host test; returns their plaintexts by row name.</summary>
    internal static async Task<Dictionary<string, string>> SeedForRotationAsync(string connectionString, byte[] sealedUnder)
    {
        var cipher = new SecretCipher(sealedUnder, null);
        var plaintexts = new Dictionary<string, string>();

        foreach (var key in SiteSettingKeys.Secrets)
        {
            var row = SecretStores.SiteSetting(key);
            plaintexts[row] = $"plaintext of {key}";
            await UpsertSettingAsync(connectionString, key, $"v2:{cipher.Seal(plaintexts[row], row)}");
        }

        var workspace = SecretStores.ChatWorkspace("slack", "T9");
        plaintexts[workspace] = "plaintext of the workspace";
        await ExecuteAsync(connectionString,
            "INSERT INTO hatch.\"ChatWorkspaces\" (\"Provider\", \"ExternalId\", \"Name\", \"Token\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES ('slack', 'T9', 'Workspace', @t, now(), now())",
            ("t", $"v2:{cipher.Seal(plaintexts[workspace], workspace)}"));

        return plaintexts;
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
