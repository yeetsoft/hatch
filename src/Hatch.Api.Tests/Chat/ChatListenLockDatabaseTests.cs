using System.Security.Cryptography;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The half of the listener the database owns: the advisory lock that keeps two
/// instances from both listening, the probe that notices a session that has
/// gone, and the keepalive settings that let Postgres notice a host that has.
/// Skips without a Postgres; run it with <c>make test-api-db</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ChatListenLockDatabaseTests
{
    [SkippableFact]
    public async Task OfTwoHolders_OneGetsTheLock()
    {
        var lockbox = await NewLockAsync();

        await using var first = await lockbox.TryAcquireAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(await lockbox.TryAcquireAsync(CancellationToken.None));
        Assert.True(await first.StillHeldAsync(CancellationToken.None));
    }

    [SkippableFact]
    public async Task TheOther_GetsTheLock_OnceTheFirstIsDisposed()
    {
        var lockbox = await NewLockAsync();
        var first = await lockbox.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await lockbox.TryAcquireAsync(CancellationToken.None));

        await first.DisposeAsync();

        await using var second = await lockbox.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(second);
    }

    [SkippableFact]
    public async Task TheOther_GetsTheLock_WhenTheFirstsSessionIsTerminated_AndTheFirstKnowsItLostIt()
    {
        var connection = await PrepareAsync();
        var lockbox = new PostgresChatListenLock(new DatabaseConnections(connection, null));
        await using var first = await lockbox.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await lockbox.TryAcquireAsync(CancellationToken.None));

        await using (var admin = new NpgsqlConnection(connection))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND granted AND objid::bigint = @key", admin);
            command.Parameters.AddWithValue("key", PostgresChatListenLock.Key);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        }

        Assert.False(await first.StillHeldAsync(CancellationToken.None));
        await using var second = await lockbox.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(second);
    }

    [SkippableFact]
    public async Task TheHeldSession_ChecksForAVanishedHostInAboutFiftySeconds()
    {
        var lockbox = await NewLockAsync();

        await using var held = await lockbox.TryAcquireAsync(CancellationToken.None);

        var hold = Assert.IsType<PostgresChatListenLock.Hold>(held);
        Assert.Equal(["20", "10", "3"], await ReadAsync(hold.Connection, "tcp_keepalives_idle", "tcp_keepalives_interval", "tcp_keepalives_count"));
    }

    [SkippableFact]
    public async Task TheListenLock_IsNotTheSendersOrTheGoosesLock()
    {
        var connection = await PrepareAsync();
        var lockbox = new PostgresChatListenLock(new DatabaseConnections(connection, null));
        await using var listen = await lockbox.TryAcquireAsync(CancellationToken.None);

        await using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
        await using var send = await new PostgresChatSendLock().TryAcquireAsync(db, CancellationToken.None);
        await using var goose = await new PostgresGooseLock().TryAcquireAsync(db, CancellationToken.None);

        Assert.NotNull(listen);
        Assert.NotNull(send);
        Assert.NotNull(goose);
    }

    [SkippableFact]
    public async Task TheService_StartedOnPostgres_ListensAndWritesTheRow_AndStopsCleanly()
    {
        var connection = await PrepareAsync();
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options;
        var protector = new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));
        var provider = new FakeChatProvider();

        int id;
        await using (var seed = new HatchContext(options))
        {
            var row = new EfHatchChatWorkspace
            {
                Provider = provider.Name, ExternalId = "TACME", Name = "Acme", Token = "sealed", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            Assert.True(protector.TrySeal(FakeChatProvider.AcmeAppToken, SecretStores.ChatWorkspaceAppToken(provider.Name, "TACME"), out var sealedToken));
            row.AppToken = sealedToken;
            seed.ChatWorkspaces.Add(row);
            await seed.SaveChangesAsync();
            id = row.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(protector);
        services.AddSingleton<IChatProvider>(provider);
        services.AddScoped(_ => new HatchContext(options));
        services.AddSingleton<IChatListenLock>(new PostgresChatListenLock(new DatabaseConnections(connection, null)));
        services.AddSingleton<ChatListenService>();
        await using var root = services.BuildServiceProvider();
        var service = root.GetRequiredService<ChatListenService>();

        await service.StartAsync(default);
        try
        {
            await UntilAsync(async () =>
            {
                await using var read = new HatchContext(options);
                return (await read.ChatWorkspaces.AsNoTracking().SingleAsync(w => w.Id == id)).ListeningSince is not null;
            });

            Assert.Equal([FakeChatProvider.AcmeAppToken], provider.ListenCalls);
            Assert.Null(await new PostgresChatListenLock(new DatabaseConnections(connection, null)).TryAcquireAsync(CancellationToken.None));
        }
        finally
        {
            await service.StopAsync(default);
        }

        await using var after = new HatchContext(options);
        Assert.Null((await after.ChatWorkspaces.AsNoTracking().SingleAsync(w => w.Id == id)).ListeningSince);
        Assert.Equal(0, provider.Listening);
        await using var freed = await new PostgresChatListenLock(new DatabaseConnections(connection, null)).TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(freed);
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The listener never wrote the row.");
            await Task.Delay(50);
        }
    }

    /// <summary>The settings as the session reports them, with the unit Postgres shows (<c>20s</c>) taken off.</summary>
    private static async Task<string[]> ReadAsync(NpgsqlConnection connection, params string[] settings)
    {
        var values = new List<string>();
        foreach (var setting in settings)
        {
            await using var command = new NpgsqlCommand($"SHOW {setting}", connection);
            values.Add(((string)(await command.ExecuteScalarAsync())!).TrimEnd('s'));
        }

        return [.. values];
    }

    private static async Task<PostgresChatListenLock> NewLockAsync() =>
        new(new DatabaseConnections(await PrepareAsync(), null));

    private static async Task<string> PrepareAsync()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");
        return await HatchDatabase.PrepareAsync();
    }
}
