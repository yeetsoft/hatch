using System.Security.Cryptography;
using System.Threading.Channels;
using Hatch.Api.Common;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// <see cref="ChatListenService"/>: which connections a tick opens and closes,
/// what a refusal costs, what is written on the workspace row, and what a press
/// does to the reading. The provider is <see cref="FakeChatProvider"/>, the lock
/// is a fake, and every wait is seen and released by the test, so nothing here
/// depends on how long anything takes. <see cref="ChatListenLockDatabaseTests"/>
/// holds the real lock.
/// </summary>
public class ChatListenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly ChatPress Press = new("U1", "C1", "1700000000.000100", "approve", "HA-1", "https://hooks.example/reply-address-1");

    [Fact]
    public async Task OneConnection_IsOpenedPerWorkspaceWithAToken_AndNoneWithout()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        var globex = await h.AddAsync("TGLOBEX", FakeChatProvider.GlobexAppToken);
        var bare = await h.AddAsync("TBARE", appToken: null);

        await h.Service.TickAsync(default);
        await h.UntilAsync(async () => (await h.RowAsync(acme)).ListeningSince is not null && (await h.RowAsync(globex)).ListeningSince is not null);

        Assert.Equal(
            new[] { FakeChatProvider.AcmeAppToken, FakeChatProvider.GlobexAppToken }.Order(),
            h.Provider.ListenCalls.Order());
        Assert.Equal(Now, (await h.RowAsync(acme)).ListeningSince);
        Assert.Null((await h.RowAsync(bare)).ListeningSince);
    }

    [Fact]
    public async Task AnotherTick_OpensNothingMore()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        await h.OpenAsync(acme);

        await h.Service.TickAsync(default);
        await h.Service.TickAsync(default);

        Assert.Single(h.Provider.ListenCalls);
        Assert.Equal(1, h.Provider.Listening);
    }

    [Fact]
    public async Task ATokenSet_IsFollowedInOneTick()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", appToken: null);
        await h.Service.TickAsync(default);
        Assert.Empty(h.Provider.ListenCalls);

        await h.SetTokenAsync(acme, FakeChatProvider.AcmeAppToken);
        await h.Service.TickAsync(default);
        await h.UntilAsync(async () => (await h.RowAsync(acme)).ListeningSince is not null);

        Assert.Equal([FakeChatProvider.AcmeAppToken], h.Provider.ListenCalls);
    }

    [Fact]
    public async Task ATokenReplaced_ClosesTheOldConnection_AndOpensTheNewOne()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        await h.OpenAsync(acme);

        await h.SetTokenAsync(acme, FakeChatProvider.AcmeNewAppToken);
        await h.Service.TickAsync(default);
        await h.UntilAsync(() => h.Provider.ListenCalls.Contains(FakeChatProvider.AcmeNewAppToken) && h.Provider.Listening == 1);

        Assert.Equal([FakeChatProvider.AcmeAppToken, FakeChatProvider.AcmeNewAppToken], h.Provider.ListenCalls);
        await h.UntilAsync(async () => (await h.RowAsync(acme)).ListeningSince is not null);
    }

    [Fact]
    public async Task ATokenRemoved_ClosesTheConnection_AndClearsTheThreeColumns()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("no") }]);
        await h.Service.TickAsync(default);
        await h.UntilAsync(async () => (await h.RowAsync(acme)).ListenFailingSince is not null);

        await h.SetTokenAsync(acme, appToken: null);
        await h.Service.TickAsync(default);

        var row = await h.RowAsync(acme);
        Assert.Null(row.ListeningSince);
        Assert.Null(row.ListenFailingSince);
        Assert.Null(row.ListenLastRefusal);
        Assert.Equal(0, h.Provider.Listening);
        Assert.Single(h.Provider.ListenCalls);
    }

    [Fact]
    public async Task AWorkspaceRemoved_ClosesItsConnection()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        await h.OpenAsync(acme);

        await h.RemoveAsync(acme);
        await h.Service.TickAsync(default);

        Assert.Equal(0, h.Provider.Listening);
        Assert.False(h.Log.Any(LogLevel.Error));
        Assert.False(h.Log.Any(LogLevel.Warning));
    }

    [Fact]
    public async Task AWorkspaceRemovedWhileTheConnectionWaits_EndsTheConnection_WithoutAnError()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([new ListenScript { End = ChatListenEnd.Reopen("the service closed it") }]);
        await h.Service.TickAsync(default);
        var wait = await h.NextWaitAsync();

        await h.RemoveAsync(acme);
        wait.Release();
        await h.UntilAsync(() => h.Provider.ListenCalls.Count == 2 && h.Provider.Listening == 0);

        Assert.False(h.Log.Any(LogLevel.Error));
        Assert.False(h.Log.Any(LogLevel.Warning));
    }

    [Fact]
    public async Task Refusals_BackOffByTheSendersSchedule()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new(Enumerable.Repeat(
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("the service said no") }, 4));

        await h.Service.TickAsync(default);

        foreach (var failures in new[] { 1, 2, 3, 4 })
        {
            var wait = await h.NextWaitAsync();
            Assert.Equal(ChatDelivery.Backoff(failures), wait.Span);
            wait.Release();
        }
    }

    [Fact]
    public async Task AThrottle_IsWaitedOutForAsLongAsItAsked()
    {
        await using var h = new Harness();
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("slow down", TimeSpan.FromSeconds(77)) },
        ]);

        await h.Service.TickAsync(default);

        Assert.Equal(TimeSpan.FromSeconds(77), (await h.NextWaitAsync()).Span);
    }

    [Fact]
    public async Task ADisabledLink_WaitsTheLongest()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { CallOpened = false, End = ChatListenEnd.LinkDisabled("socket mode is off for this app") },
        ]);

        await h.Service.TickAsync(default);

        var wait = await h.NextWaitAsync();
        Assert.Equal(ChatDelivery.Backoff(20), wait.Span);
        Assert.Equal(TimeSpan.FromMinutes(10), wait.Span);
        Assert.Equal("socket mode is off for this app", (await h.RowAsync(acme)).ListenLastRefusal);
    }

    [Fact]
    public async Task TheFailureRun_StartsAtTheFirstRefusal_SurvivesLaterOnes_AndClearsOnceOpen()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("first sentence") },
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("second sentence") },
        ]);

        await h.Service.TickAsync(default);
        var first = await h.NextWaitAsync();
        var row = await h.RowAsync(acme);
        Assert.Equal(Now, row.ListenFailingSince);
        Assert.Equal("first sentence", row.ListenLastRefusal);
        Assert.Null(row.ListeningSince);

        h.Time.Advance(TimeSpan.FromMinutes(3));
        first.Release();
        var second = await h.NextWaitAsync();
        row = await h.RowAsync(acme);
        Assert.Equal(Now, row.ListenFailingSince);
        Assert.Equal("second sentence", row.ListenLastRefusal);

        h.Time.Advance(TimeSpan.FromMinutes(3));
        second.Release();
        await h.UntilAsync(async () => (await h.RowAsync(acme)).ListeningSince is not null);

        row = await h.RowAsync(acme);
        Assert.Equal(Now.AddMinutes(6), row.ListeningSince);
        Assert.Null(row.ListenFailingSince);
        Assert.Null(row.ListenLastRefusal);
    }

    [Fact]
    public async Task AConnectionThatWasWorkingAndEnded_IsReopenedAfterAPause_WithNoFailure()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { End = ChatListenEnd.Reopen("the service asked for a new connection") },
            new ListenScript { End = ChatListenEnd.Reopen("again") },
        ]);

        await h.Service.TickAsync(default);
        for (var i = 0; i < 2; i++)
        {
            var wait = await h.NextWaitAsync();
            Assert.Equal(ChatListen.ReopenPause, wait.Span);
            var row = await h.RowAsync(acme);
            Assert.Null(row.ListenFailingSince);
            Assert.Null(row.ListenLastRefusal);
            wait.Release();
        }

        await h.UntilAsync(() => h.Provider.ListenCalls.Count == 3);
        Assert.Null((await h.RowAsync(acme)).ListenFailingSince);
    }

    [Fact]
    public async Task AReopenThatNeverOpened_CountsAsARefusal()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { CallOpened = false, End = ChatListenEnd.Reopen("ended before it was ready") },
        ]);

        await h.Service.TickAsync(default);

        Assert.Equal(ChatDelivery.Backoff(1), (await h.NextWaitAsync()).Span);
        var row = await h.RowAsync(acme);
        Assert.Equal(Now, row.ListenFailingSince);
        Assert.Equal("ended before it was ready", row.ListenLastRefusal);
    }

    [Fact]
    public async Task ARefusalSentence_IsCutAsTheSenderCutsOne()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        var long500 = new string('x', EfHatchChatWorkspace.MaxRefusalLength + 50);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused(long500) },
        ]);

        await h.Service.TickAsync(default);
        await h.NextWaitAsync();

        var stored = (await h.RowAsync(acme)).ListenLastRefusal!;
        Assert.Equal(EfHatchChatWorkspace.MaxRefusalLength, stored.Length);
        Assert.EndsWith("…", stored);
    }

    [Fact]
    public async Task ATokenThatCannotBeOpened_IsRecordedAsAFailure_AndNeverSentToTheProvider()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", appToken: null);
        var other = new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));
        Assert.True(other.TrySeal(FakeChatProvider.AcmeAppToken, SecretStores.ChatWorkspaceAppToken("fake", "TACME"), out var sealedUnderAnotherKey));
        await h.SetSealedAsync(acme, sealedUnderAnotherKey);

        await h.Service.TickAsync(default);

        var wait = await h.NextWaitAsync();
        Assert.Equal(ChatDelivery.Backoff(1), wait.Span);
        var row = await h.RowAsync(acme);
        Assert.Equal(ChatListen.UnreadableToken, row.ListenLastRefusal);
        Assert.Equal(Now, row.ListenFailingSince);
        Assert.Empty(h.Provider.ListenCalls);
    }

    [Fact]
    public async Task APress_ReachesEveryHandler_EachTimeInAFreshScope()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.Listens[FakeChatProvider.AcmeAppToken] = new ListenScript { Presses = [Press, Press with { MessageId = "second" }] };

        await h.Service.TickAsync(default);
        await h.UntilAsync(() => h.Handled.Count == 4);

        var seen = h.Handled.ToArray();
        Assert.Equal(2, seen.Select(s => s.Press.MessageId).Distinct().Count());
        Assert.Equal(["first", "second"], seen.Select(s => s.Handler).Distinct().Order());
        Assert.All(seen, s => Assert.Equal(acme, s.Workspace));
        // A scope per press, shared by that press's handlers and by no other press.
        var byPress = seen.GroupBy(s => s.Press.MessageId).Select(g => g.Select(s => s.Scope).Distinct().Count()).ToArray();
        Assert.All(byPress, count => Assert.Equal(1, count));
        Assert.Equal(2, seen.Select(s => s.Scope).Distinct().Count());
    }

    [Fact]
    public async Task AHandlerThatThrows_DoesNotStopTheNextHandler_OrTheNextPress()
    {
        await using var h = new Harness { ThrowOn = "first" };
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.Listens[FakeChatProvider.AcmeAppToken] = new ListenScript { Presses = [Press, Press with { MessageId = "second" }] };

        await h.Service.TickAsync(default);
        await h.UntilAsync(() => h.Handled.Count(s => s.Handler == "second") == 2);

        Assert.Equal(1, h.Provider.Listening);
        Assert.True(h.Log.Any(LogLevel.Warning));
    }

    [Fact]
    public async Task AHandlerThatHangsPastItsTimeout_DoesNotStopTheNextPress()
    {
        await using var h = new Harness { HangOn = "first" };
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.Listens[FakeChatProvider.AcmeAppToken] = new ListenScript { Presses = [Press, Press with { MessageId = "second" }] };

        await h.Service.TickAsync(default);
        await h.UntilAsync(() => h.Handled.Any(s => s.Handler == "first"));
        await h.AdvanceUntilAsync(() => h.Handled.Count(s => s.Handler == "second") == 2, ChatListen.HandlerTimeout);

        Assert.Contains(h.Handled, s => s.Handler == "second" && s.Press.MessageId == "second");
        Assert.Equal(1, h.Provider.Listening);
    }

    [Fact]
    public async Task APress_NeverWaitsForAHandler_ToBeRead()
    {
        await using var h = new Harness { HangOn = "first" };
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        var presses = Enumerable.Range(0, ChatListen.PressQueue + 50).Select(i => Press with { MessageId = $"m{i}" }).ToArray();
        h.Provider.Listens[FakeChatProvider.AcmeAppToken] = new ListenScript { Presses = presses };

        await h.Service.TickAsync(default);
        await h.UntilAsync(() => h.Log.Count(LogLevel.Warning, "was dropped") >= 49);

        // The reading finished while the first press was still in a handler's hands.
        var dropped = h.Log.Count(LogLevel.Warning, "was dropped");
        Assert.InRange(dropped, 49, 50);
    }

    [Fact]
    public async Task ALostLock_StopsEveryConnection_UntilItIsTakenAgain()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        var globex = await h.AddAsync("TGLOBEX", FakeChatProvider.GlobexAppToken);
        await h.OpenAsync(acme, globex);

        h.Lock.Holds[0].Lost = true;
        h.Lock.Available = false;
        await h.Service.TickAsync(default);

        Assert.Equal(0, h.Provider.Listening);
        Assert.True(h.Lock.Holds[0].Disposed);

        h.Lock.Available = true;
        await h.Service.TickAsync(default);
        await h.UntilAsync(() => h.Provider.Listening == 2);
        Assert.Equal(2, h.Lock.Holds.Count);
    }

    [Fact]
    public async Task WithoutTheLock_NothingConnects()
    {
        await using var h = new Harness();
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Lock.Available = false;

        await h.Service.TickAsync(default);
        await h.Service.TickAsync(default);

        Assert.Empty(h.Provider.ListenCalls);
        Assert.Equal(2, h.Lock.Asked);
    }

    [Fact]
    public async Task TakingTheLock_ClearsWhatADeadInstanceLeftSet()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", appToken: null);
        await h.UpdateAsync(acme, row => row.ListeningSince = Now.AddHours(-3));

        await h.Service.TickAsync(default);

        Assert.Null((await h.RowAsync(acme)).ListeningSince);
    }

    [Fact]
    public async Task ATickThatFails_IsLoggedAndTriedAgain()
    {
        await using var h = new Harness();
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Lock.Throws = new InvalidOperationException("the database is away");

        await h.Service.TickAsync(default);
        Assert.Empty(h.Provider.ListenCalls);
        Assert.True(h.Log.Any(LogLevel.Warning));

        h.Lock.Throws = null;
        await h.OpenAnyAsync();
        Assert.Single(h.Provider.ListenCalls);
    }

    [Fact]
    public async Task Shutdown_ClosesTheConnections_ClearsListening_AndReleasesTheLock()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        await h.OpenAsync(acme);

        await h.Service.StopAsync(default);

        Assert.Equal(0, h.Provider.Listening);
        Assert.Null((await h.RowAsync(acme)).ListeningSince);
        Assert.True(h.Lock.Holds[0].Disposed);
    }

    [Fact]
    public async Task Shutdown_ReturnsInsideItsBudget_EvenWithAConnectionThatIgnoresTheStop()
    {
        await using var h = new Harness();
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.Listens[FakeChatProvider.AcmeAppToken] = new ListenScript { IgnoresCancellation = true };
        await h.OpenAsync(acme);

        var stopping = h.Service.StopAsync(default);
        await h.AdvanceUntilAsync(() => stopping.IsCompleted, ChatListen.StopBudget);

        await stopping;
        Assert.True(h.Lock.Holds[0].Disposed);
    }

    [Fact]
    public async Task NoLogLine_AndNoStoredRefusal_HoldsATokenOrAnAddress()
    {
        await using var h = new Harness { ThrowOn = "first" };
        var acme = await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Provider.ListenQueues[FakeChatProvider.AcmeAppToken] = new([
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("invalid_auth") },
            new ListenScript { CallOpened = false, End = ChatListenEnd.LinkDisabled("socket mode is off") },
            new ListenScript { CallOpened = false, End = ChatListenEnd.Refused("ratelimited", TimeSpan.FromSeconds(30)) },
            new ListenScript { Presses = [Press], End = ChatListenEnd.Reopen("refreshing") },
        ]);

        await h.Service.TickAsync(default);
        for (var i = 0; i < 4; i++) (await h.NextWaitAsync()).Release();
        await h.UntilAsync(() => h.Handled.Count(s => s.Handler == "second") == 1);

        Assert.DoesNotContain(FakeChatProvider.AcmeAppToken, h.Log.Text());
        Assert.DoesNotContain("xapp-", h.Log.Text());
        Assert.DoesNotContain("hooks.example", h.Log.Text());
        Assert.DoesNotContain("reply-address", h.Log.Text());
        var row = await h.RowAsync(acme);
        Assert.DoesNotContain("xapp-", row.ListenLastRefusal ?? "");
        Assert.DoesNotContain("https://", row.ListenLastRefusal ?? "");
    }

    [Fact]
    public async Task AHostWithNoPostgres_StartsWithTheServiceRegistered_AndNothingConnects()
    {
        var h = new Harness();
        await h.AddAsync("TACME", FakeChatProvider.AcmeAppToken);
        h.Lock.Throws = new InvalidOperationException("nothing should ask for the lock here");

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IChatListenLock>(h.Lock);
                services.AddSingleton<IChatProvider>(h.Provider);
                services.AddSingleton(h.Protector);
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddSingleton<TimeProvider>(h.Time);
                services.AddLogging();
                services.AddScoped(_ => h.NewContext());
                services.AddHostedService<ChatListenService>();
            })
            .Build();

        await host.StartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await host.StopAsync();

        Assert.Contains(host.Services.GetServices<IHostedService>(), s => s is ChatListenService);
        Assert.Equal(0, h.Lock.Asked);
        Assert.Empty(h.Provider.ListenCalls);
        await h.DisposeAsync();
    }

    [Fact]
    public void TheHatchModule_RegistersTheListener_AndItsLock()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddHatchModule(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ChatListenService));
        Assert.Contains(services, d => d.ServiceType == typeof(IChatListenLock) && d.ImplementationType == typeof(PostgresChatListenLock));
    }

    /// <summary>One service on one in-memory database, with the lock, the provider and the handlers faked.</summary>
    internal sealed class Harness : IAsyncDisposable
    {
        private readonly DbContextOptions<HatchContext> options =
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private readonly Channel<Wait> waits = Channel.CreateUnbounded<Wait>();
        private readonly ServiceProvider services;

        public Harness()
        {
            Protector = new SecretProtector(new SecretCipher(RandomNumberGenerator.GetBytes(32), null));

            var collection = new ServiceCollection();
            collection.AddSingleton<IChatProvider>(Provider);
            collection.AddScoped(_ => NewContext());
            collection.AddScoped<ScopeMarker>();
            collection.AddScoped<IChatPressHandler>(sp => new RecordingHandler("first", this, sp.GetRequiredService<ScopeMarker>()));
            collection.AddScoped<IChatPressHandler>(sp => new RecordingHandler("second", this, sp.GetRequiredService<ScopeMarker>()));
            services = collection.BuildServiceProvider();

            Service = new ChatListenService(
                services.GetRequiredService<IServiceScopeFactory>(), Lock, Protector,
                new ConfigurationBuilder().Build(), Time, Log)
            {
                Waiter = (span, ct) =>
                {
                    var wait = new Wait(span);
                    waits.Writer.TryWrite(wait);
                    return wait.Done.Task.WaitAsync(ct);
                },
            };
        }

        public FakeTimeProvider Time { get; } = new(Now);
        public FakeChatProvider Provider { get; } = new();
        public FakeListenLock Lock { get; } = new();
        public SecretProtector Protector { get; }
        public CapturingLog Log { get; } = new();
        public ChatListenService Service { get; }
        public System.Collections.Concurrent.ConcurrentQueue<Handled> Handled { get; } = new();

        /// <summary>The handler named <c>first</c> or <c>second</c> that throws, when set.</summary>
        public string? ThrowOn { get; init; }

        /// <summary>The handler named <c>first</c> or <c>second</c> that never returns and never looks at its token, when set.</summary>
        public string? HangOn { get; init; }

        public HatchContext NewContext() => new(options);

        public async Task<int> AddAsync(string externalId, string? appToken)
        {
            await using var db = NewContext();
            var row = new EfHatchChatWorkspace
            {
                Provider = "fake", ExternalId = externalId, Name = externalId, Token = "sealed", CreatedAt = Now, UpdatedAt = Now,
            };
            db.ChatWorkspaces.Add(row);
            await db.SaveChangesAsync();
            if (appToken is not null) await SetTokenAsync(row.Id, appToken);
            return row.Id;
        }

        public async Task SetTokenAsync(int id, string? appToken)
        {
            if (appToken is null)
            {
                await SetSealedAsync(id, null);
                return;
            }

            await using var db = NewContext();
            var row = await db.ChatWorkspaces.SingleAsync(w => w.Id == id);
            Assert.True(Protector.TrySeal(appToken, SecretStores.ChatWorkspaceAppToken(row.Provider, row.ExternalId), out var sealedToken));
            await SetSealedAsync(id, sealedToken);
        }

        public Task SetSealedAsync(int id, string? sealedToken) => UpdateAsync(id, row => row.AppToken = sealedToken);

        public async Task UpdateAsync(int id, Action<EfHatchChatWorkspace> change)
        {
            await using var db = NewContext();
            var row = await db.ChatWorkspaces.SingleAsync(w => w.Id == id);
            change(row);
            await db.SaveChangesAsync();
        }

        public async Task RemoveAsync(int id)
        {
            await using var db = NewContext();
            db.ChatWorkspaces.Remove(await db.ChatWorkspaces.SingleAsync(w => w.Id == id));
            await db.SaveChangesAsync();
        }

        public async Task<EfHatchChatWorkspace> RowAsync(int id)
        {
            await using var db = NewContext();
            return await db.ChatWorkspaces.AsNoTracking().SingleAsync(w => w.Id == id);
        }

        /// <summary>A tick, then until every given workspace says it is listening.</summary>
        public async Task OpenAsync(params int[] workspaces)
        {
            await Service.TickAsync(default);
            await UntilAsync(async () =>
            {
                foreach (var id in workspaces)
                    if ((await RowAsync(id)).ListeningSince is null) return false;
                return true;
            });
        }

        public async Task OpenAnyAsync()
        {
            await Service.TickAsync(default);
            await UntilAsync(() => Provider.Listening > 0);
        }

        /// <summary>The next wait a connection makes, seen before it is released.</summary>
        public async Task<Wait> NextWaitAsync() =>
            await waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        public Task UntilAsync(Func<bool> condition) => UntilAsync(() => Task.FromResult(condition()));

        public async Task UntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition never came true.\n" + Log.Text());
                await Task.Delay(5);
            }
        }

        /// <summary>
        /// Moves the clock by <paramref name="step"/> until the condition holds: a
        /// timer is only created once the code under test gets to it, so one
        /// advance can come early, and the next does not hurt.
        /// </summary>
        public async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan step)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition never came true.\n" + Log.Text());
                Time.Advance(step);
                await Task.Delay(10);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(default);
            await services.DisposeAsync();
        }
    }

    /// <summary>One wait a connection made: how long, and a way to let it end.</summary>
    internal sealed class Wait(TimeSpan span)
    {
        public TimeSpan Span { get; } = span;

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => Done.TrySetResult();
    }

    internal sealed record Handled(string Handler, int Workspace, ChatPress Press, Guid Scope);

    /// <summary>One per scope, so two handlers in one press share it and a second press does not.</summary>
    internal sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    internal sealed class RecordingHandler(string name, Harness h, ScopeMarker scope) : IChatPressHandler
    {
        public async Task HandleAsync(int workspaceId, ChatPress press, CancellationToken ct)
        {
            h.Handled.Enqueue(new Handled(name, workspaceId, press, scope.Id));

            if (h.ThrowOn == name) throw new InvalidOperationException("the handler failed");

            // The handler's own task, not the token, is what is waited on: a handler that does not heed its token.
            if (h.HangOn == name && press.MessageId != "second") await new TaskCompletionSource().Task;
        }
    }

    /// <summary>A lock with no database: one holder at a time, a hold that can be lost, and a count of who asked.</summary>
    internal sealed class FakeListenLock : IChatListenLock
    {
        public bool Available { get; set; } = true;
        public Exception? Throws { get; set; }
        public int Asked { get; private set; }
        public List<FakeHold> Holds { get; } = [];

        public Task<IChatListenHold?> TryAcquireAsync(CancellationToken ct)
        {
            Asked++;
            if (Throws is not null) throw Throws;
            if (!Available) return Task.FromResult<IChatListenHold?>(null);

            var hold = new FakeHold();
            Holds.Add(hold);
            return Task.FromResult<IChatListenHold?>(hold);
        }
    }

    internal sealed class FakeHold : IChatListenHold
    {
        public bool Lost { get; set; }
        public bool Disposed { get; private set; }

        public Task<bool> StillHeldAsync(CancellationToken ct) => Task.FromResult(!Lost);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Every line logged to the service, with its level and any exception.</summary>
    internal sealed class CapturingLog : ILogger<ChatListenService>
    {
        private readonly List<(LogLevel Level, string Text)> lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (lines) lines.Add((logLevel, $"{formatter(state, exception)} {exception}"));
        }

        public bool Any(LogLevel level) => Count(level, "") > 0;

        public int Count(LogLevel level, string containing)
        {
            lock (lines) return lines.Count(l => l.Level == level && l.Text.Contains(containing));
        }

        public string Text()
        {
            lock (lines) return string.Join(Environment.NewLine, lines.Select(l => $"[{l.Level}] {l.Text}"));
        }
    }
}
