using System.Threading.Channels;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Chat;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The numbers the listener runs on. Constants and not settings: nothing here
/// is true of one install and not another.
/// </summary>
public static class ChatListen
{
    /// <summary>How often the listener looks at the lock and at the workspaces.</summary>
    public static readonly TimeSpan Interval = ChatDelivery.Interval;

    /// <summary>The wait before a connection that was working is opened again.</summary>
    public static readonly TimeSpan ReopenPause = TimeSpan.FromSeconds(1);

    /// <summary>How long one handler gets for one press before it is left behind.</summary>
    public static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a stop waits for the connections, in all.</summary>
    public static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(5);

    /// <summary>The presses waiting for the handlers, per connection. One more than this is dropped.</summary>
    public const int PressQueue = 100;

    /// <summary>What the wait is worked out from when the service says the app has the connection turned off: the longest wait there is.</summary>
    public const int LinkDisabledFailures = 20;

    /// <summary>What a workspace says when the stored app-level token will not open.</summary>
    public const string UnreadableToken = "the stored app-level token cannot be opened; paste it again";
}

/// <summary>
/// Keeps two listeners from running at once, on this instance or another. A
/// workspace's app-level token opens one connection at a time in practice, and
/// two listeners would each handle every press.
/// </summary>
public interface IChatListenLock
{
    /// <summary>Takes the lock, or returns null when another listener holds it. Disposing what is returned releases it.</summary>
    Task<IChatListenHold?> TryAcquireAsync(CancellationToken ct);
}

/// <summary>A taken <see cref="IChatListenLock"/>: it can be asked whether it is still held.</summary>
public interface IChatListenHold : IAsyncDisposable
{
    /// <summary>False once the lock is gone, which a session that died has lost. Never throws for that.</summary>
    Task<bool> StillHeldAsync(CancellationToken ct);
}

/// <summary>
/// A Postgres session advisory lock held on one connection for as long as the
/// process listens, probed each tick. A process that is killed frees the lock at
/// once. A host that vanishes is noticed by Postgres through the keepalive
/// settings below, about 50 seconds on. A transaction-pooling proxy would break
/// this, as it already breaks <see cref="PostgresChatSendLock"/> and the goose's.
/// </summary>
public sealed class PostgresChatListenLock(DatabaseConnections connections, ILogger<PostgresChatListenLock>? logger = null) : IChatListenLock
{
    /// <summary>The lock's key, arbitrary but fixed - "list" in ASCII. Advisory locks are per database.</summary>
    internal const long Key = 0x6C697374;

    /// <summary>Idle seconds, the gap between probes and the probes missed before Postgres drops the session: 20 + 10 x 3.</summary>
    private static readonly string[] Keepalive =
    [
        "SET tcp_keepalives_idle = 20",
        "SET tcp_keepalives_interval = 10",
        "SET tcp_keepalives_count = 3",
    ];

    public async Task<IChatListenHold?> TryAcquireAsync(CancellationToken ct)
    {
        var connectionString = connections.Hatch;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("There is no ConnectionStrings:Hatch to take the chat listen lock on");

        var held = await PostgresAdvisoryLock.TryHoldAsync(connectionString, Key, SetKeepaliveAsync, ct);
        return held is null ? null : new Hold(held);
    }

    /// <summary>A server that refuses a setting is not a reason to give up the lock: it only means a vanished host is noticed later.</summary>
    private async Task SetKeepaliveAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        foreach (var sql in Keepalive)
        {
            try
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(ct);
            }
            catch (PostgresException e)
            {
                logger?.LogDebug("The chat listen lock's session refused {Setting}: {Reason}", sql, e.SqlState);
            }
        }
    }

    internal sealed class Hold(PostgresAdvisoryLock.AdvisoryHold inner) : IChatListenHold
    {
        /// <summary>The session the lock lives on, for a test to ask it what was set.</summary>
        internal NpgsqlConnection Connection => inner.Connection;

        public Task<bool> StillHeldAsync(CancellationToken ct) => inner.StillHeldAsync(ct);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

/// <summary>
/// Hatch's one chat listener. Each tick it holds a database lock - so one
/// instance in the install listens - and keeps a connection open for every
/// workspace with an app-level token, following tokens set, replaced and
/// removed and workspaces removed. A connection that was working and ended is
/// opened again at once and is no failure. One the service refuses is waited
/// on by <see cref="ChatDelivery.Backoff"/>, or as long as a throttle asked,
/// and the workspace row says why. What a press does is up to the registered
/// <see cref="IChatPressHandler"/>s, run apart from the connection.
///
/// <para>The first service in Hatch that runs for the life of the server:
/// <see cref="SecretReseal"/> runs once. It does nothing in migrate mode or
/// where the context is not Postgres, so the in-memory hosts the tests use are
/// untouched.</para>
///
/// <para>A connection is its own task and scope, since the provider is scoped.
/// A press goes into a bounded queue and a reader of its own takes it, so a
/// slow or throwing handler never holds up the next read or an acknowledgement.
/// A press is never logged: it carries an address meant for one answer.</para>
/// </summary>
public sealed class ChatListenService(
    IServiceScopeFactory scopes, IChatListenLock lockbox, SecretProtector protector, IConfiguration configuration,
    TimeProvider time, ILogger<ChatListenService> logger) : IHostedService
{
    private readonly Dictionary<int, Connection> running = [];
    private IChatListenHold? hold;

    /// <summary>
    /// How a wait is made, replaced by a test that needs to see and release each
    /// one. Left alone it is <c>Task.Delay(span, time, ct)</c>.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task>? Waiter { get; set; }
    private bool listeningCleared;
    private CancellationTokenSource? stopping;
    private Task? loop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration["HATCH_MIGRATE"] == "1") return;

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetService<HatchContext>();
            if (db is null || !db.Database.IsNpgsql()) return;
        }

        stopping = new CancellationTokenSource();
        var ct = stopping.Token;
        loop = Task.Run(() => LoopAsync(ct), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(ChatListen.StopBudget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, cancellationToken);
        var ct = linked.Token;

        // The loop first: while a tick may still be adding to the connections, they are not ours to walk.
        if (stopping is not null) await stopping.CancelAsync();

        try
        {
            await (loop ?? Task.CompletedTask).WaitAsync(ct);

            foreach (var connection in running.Values) connection.Cts.Cancel();
            await Task.WhenAll(running.Values.SelectMany(c => new[] { c.Run, c.Reader })).WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("The chat listener did not stop inside {Seconds} seconds, so it is left behind.", ChatListen.StopBudget.TotalSeconds);
        }
        catch (Exception e)
        {
            logger.LogWarning("The chat listener ended with {Error} while stopping.", e.GetType().Name);
        }

        // Whatever the wait ended on, no connection is left running with its stop unsaid.
        try
        {
            foreach (var connection in running.Values.ToArray()) connection.Cts.Cancel();
        }
        catch (InvalidOperationException)
        {
            // A tick that would not stop was still changing them.
        }

        running.Clear();

        if (hold is not null)
        {
            try
            {
                await ClearListeningAsync(ct);
            }
            catch (Exception e)
            {
                logger.LogDebug("Could not clear which workspaces were listening at stop: {Error}", e.GetType().Name);
            }

            await DropHoldAsync();
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(ChatListen.Interval, time);
            do
            {
                await TickAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// One look: take or check the lock, then make the connections match the
    /// workspaces. Never throws except when <paramref name="ct"/> is cancelled.
    /// </summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        try
        {
            if (hold is null)
            {
                hold = await lockbox.TryAcquireAsync(ct);
                if (hold is null) return;

                logger.LogInformation("This instance now holds the chat listen lock.");
                listeningCleared = false;
            }
            else if (!await StillHeldAsync(ct))
            {
                // Not cleared here: whoever takes the lock next clears what this left set.
                logger.LogWarning("The chat listen lock was lost, so every connection is closed until it is taken again.");
                await DropHoldAsync();
                await StopAllAsync(ct);
                return;
            }

            if (!listeningCleared)
            {
                await ClearListeningAsync(ct);
                listeningCleared = true;
            }

            await FollowWorkspacesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "A chat listen tick failed; the next one tries again.");
        }
    }

    private async Task<bool> StillHeldAsync(CancellationToken ct)
    {
        try
        {
            return await hold!.StillHeldAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task DropHoldAsync()
    {
        var dropped = hold;
        hold = null;
        if (dropped is null) return;

        try
        {
            await dropped.DisposeAsync();
        }
        catch (Exception e)
        {
            logger.LogDebug("Releasing the chat listen lock failed: {Error}", e.GetType().Name);
        }
    }

    /// <summary>A dead instance may have left <c>ListeningSince</c> set; only the holder of the lock may say who is listening.</summary>
    private async Task ClearListeningAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        var rows = await db.ChatWorkspaces.Where(w => w.ListeningSince != null).ToListAsync(ct);
        if (rows.Count == 0) return;

        foreach (var row in rows) row.ListeningSince = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A workspace removed meanwhile has nothing to clear.
            db.ChangeTracker.Clear();
        }
    }

    private async Task FollowWorkspacesAsync(CancellationToken ct)
    {
        List<WorkspaceToken> wanted;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
            var provider = scope.ServiceProvider.GetRequiredService<IChatProvider>().Name;

            wanted = await db.ChatWorkspaces.AsNoTracking()
                .Where(w => w.Provider == provider && w.AppToken != null && w.AppToken != "")
                .Select(w => new WorkspaceToken(w.Id, w.Provider, w.ExternalId, w.AppToken!))
                .ToListAsync(ct);
        }

        var byId = wanted.ToDictionary(w => w.Id);

        foreach (var (id, connection) in running.ToList())
        {
            var token = byId.GetValueOrDefault(id);
            if (token is not null && token.Sealed == connection.Sealed && !connection.Run.IsCompleted) continue;

            await CloseAsync(connection, ct);
            running.Remove(id);

            // A token taken away is no longer listened to; a workspace taken away has no row to say so on.
            if (token is null) await ClearColumnsAsync(id, ct);
        }

        foreach (var token in wanted)
        {
            if (running.ContainsKey(token.Id)) continue;

            var connection = new Connection(token.Id, SecretStores.ChatWorkspaceAppToken(token.Provider, token.ExternalId), token.Sealed);
            connection.Reader = Task.Run(() => ReadPressesAsync(connection), CancellationToken.None);
            connection.Run = Task.Run(() => RunAsync(connection), CancellationToken.None);
            running[token.Id] = connection;
            logger.LogInformation("Chat workspace {WorkspaceId} has an app-level token, so a connection is being opened.", token.Id);
        }
    }

    private async Task StopAllAsync(CancellationToken ct)
    {
        foreach (var connection in running.Values) connection.Cts.Cancel();
        foreach (var connection in running.Values) await CloseAsync(connection, ct);
        running.Clear();
    }

    private async Task CloseAsync(Connection connection, CancellationToken ct)
    {
        await connection.Cts.CancelAsync();

        try
        {
            await Task.WhenAll(connection.Run, connection.Reader).WaitAsync(ChatListen.StopBudget, time, ct);
            connection.Cts.Dispose();
        }
        catch (TimeoutException)
        {
            logger.LogWarning("The connection for chat workspace {WorkspaceId} did not stop in time, so it is left behind.", connection.Id);
        }
    }

    private async Task ClearColumnsAsync(int workspaceId, CancellationToken ct) =>
        await UpdateRowAsync(workspaceId, row =>
        {
            row.ListeningSince = null;
            row.ListenFailingSince = null;
            row.ListenLastRefusal = null;
        }, ct);

    /// <summary>
    /// Changes one workspace row in a scope of its own, and says whether the
    /// row was still there: one removed meanwhile is not an error.
    /// </summary>
    private async Task<bool> UpdateRowAsync(int workspaceId, Action<EfHatchChatWorkspace> change, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();

        var row = await db.ChatWorkspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (row is null) return false;

        change(row);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>One workspace's connection for as long as it has a token: open, listen, wait, again.</summary>
    private async Task RunAsync(Connection c)
    {
        var ct = c.Cts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var opened = false;
                ChatListenEnd end;

                await using (var scope = scopes.CreateAsyncScope())
                {
                    var secret = protector.Unprotect(c.Sealed, c.RowName);
                    if (secret is null)
                    {
                        end = ChatListenEnd.Refused(ChatListen.UnreadableToken);
                    }
                    else
                    {
                        var provider = scope.ServiceProvider.GetRequiredService<IChatProvider>();
                        try
                        {
                            end = await provider.ListenAsync(
                                secret.Reveal(),
                                async openedCt =>
                                {
                                    opened = true;
                                    c.Streak = 0;
                                    if (!await RecordOpenedAsync(c, openedCt)) await c.Cts.CancelAsync();
                                },
                                press => Enqueue(c, press),
                                ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception e)
                        {
                            // The provider says it never throws; what it would say is not logged in case it held the token.
                            logger.LogWarning("The connection for chat workspace {WorkspaceId} ended with {Error}.", c.Id, e.GetType().Name);
                            end = ChatListenEnd.Refused("the connection ended unexpectedly");
                        }
                    }
                }

                if (ct.IsCancellationRequested) return;

                TimeSpan wait;
                if (end.Kind == ChatListenEndKind.Reopen && opened)
                {
                    wait = ChatListen.ReopenPause;
                }
                else
                {
                    c.Streak++;
                    wait = end.Kind == ChatListenEndKind.LinkDisabled
                        ? ChatDelivery.Backoff(ChatListen.LinkDisabledFailures)
                        : end.RetryAfter ?? ChatDelivery.Backoff(c.Streak);

                    logger.LogInformation(
                        "The connection for chat workspace {WorkspaceId} was refused ({Failures} in a row); trying again in {Seconds} seconds.",
                        c.Id, c.Streak, (int)wait.TotalSeconds);

                    if (!await RecordRefusalAsync(c, end.Sentence ?? "the connection was refused", ct)) return;
                }

                await (Waiter?.Invoke(Clamp(wait), ct) ?? Task.Delay(Clamp(wait), time, ct));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // The next tick sees a finished connection with a token still set, and opens it again.
            logger.LogError(e, "The connection for chat workspace {WorkspaceId} stopped with an error.", c.Id);
        }
    }

    private static TimeSpan Clamp(TimeSpan wait) =>
        wait < TimeSpan.Zero ? TimeSpan.Zero : wait > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : wait;

    private async Task<bool> RecordOpenedAsync(Connection c, CancellationToken ct)
    {
        try
        {
            var now = time.GetUtcNow();
            var there = await UpdateRowAsync(c.Id, row =>
            {
                row.ListeningSince = now;
                row.ListenFailingSince = null;
                row.ListenLastRefusal = null;
            }, ct);

            if (there) logger.LogInformation("The connection for chat workspace {WorkspaceId} is open.", c.Id);
            return there;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("Could not record that chat workspace {WorkspaceId} is listening: {Error}", c.Id, e.GetType().Name);
            return true;
        }
    }

    private async Task<bool> RecordRefusalAsync(Connection c, string sentence, CancellationToken ct)
    {
        try
        {
            var now = time.GetUtcNow();
            return await UpdateRowAsync(c.Id, row =>
            {
                row.ListeningSince = null;
                row.ListenFailingSince ??= now;
                row.ListenLastRefusal = ChatSendJob.Short(sentence);
            }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("Could not record why chat workspace {WorkspaceId} is not listening: {Error}", c.Id, e.GetType().Name);
            return true;
        }
    }

    private void Enqueue(Connection c, ChatPress press)
    {
        if (!c.Presses.Writer.TryWrite(press))
            logger.LogWarning("A button press for chat workspace {WorkspaceId} was dropped: {Waiting} are already waiting.", c.Id, ChatListen.PressQueue);
    }

    /// <summary>The one reader of a connection's presses: each goes to every handler, in a fresh scope.</summary>
    private async Task ReadPressesAsync(Connection c)
    {
        var ct = c.Cts.Token;

        try
        {
            await foreach (var press in c.Presses.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await DeliverAsync(c.Id, press, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Handing a button press for chat workspace {WorkspaceId} to its handlers failed.", c.Id);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task DeliverAsync(int workspaceId, ChatPress press, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        foreach (var handler in scope.ServiceProvider.GetServices<IChatPressHandler>())
        {
            ct.ThrowIfCancellationRequested();

            using var timeout = new CancellationTokenSource(ChatListen.HandlerTimeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            Task? task = null;

            try
            {
                task = handler.HandleAsync(workspaceId, press, linked.Token);
                await task.WaitAsync(ChatListen.HandlerTimeout, time, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (e is TimeoutException || (e is OperationCanceledException && timeout.IsCancellationRequested))
            {
                logger.LogWarning(
                    "{Handler} did not finish a button press for chat workspace {WorkspaceId} in {Seconds} seconds, so the next is read.",
                    handler.GetType().Name, workspaceId, (int)ChatListen.HandlerTimeout.TotalSeconds);

                // A handler that ignores its token runs on; its failure, if any, is not left unobserved.
                if (task is { IsCompleted: false })
                    _ = task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "{Handler} failed on a button press for chat workspace {WorkspaceId}.", handler.GetType().Name, workspaceId);
            }
        }
    }

    private sealed record WorkspaceToken(int Id, string Provider, string ExternalId, string Sealed);

    /// <summary>What the listener holds for one workspace: its own cancellation, its press queue and its two tasks.</summary>
    private sealed class Connection(int id, string rowName, string sealedToken)
    {
        public int Id { get; } = id;

        /// <summary>The name the token was sealed under.</summary>
        public string RowName { get; } = rowName;

        /// <summary>The token as stored, to tell a replaced one from the same one.</summary>
        public string Sealed { get; } = sealedToken;

        public CancellationTokenSource Cts { get; } = new();

        public Channel<ChatPress> Presses { get; } = Channel.CreateBounded<ChatPress>(
            new BoundedChannelOptions(ChatListen.PressQueue) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

        /// <summary>Refusals in a row, kept here and not on the row: a restart begins from none.</summary>
        public int Streak { get; set; }

        public Task Run { get; set; } = Task.CompletedTask;

        public Task Reader { get; set; } = Task.CompletedTask;
    }
}
