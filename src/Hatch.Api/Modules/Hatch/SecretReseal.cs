using System.Text;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Npgsql;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Reseals every stored secret under the current key, once, when the API
/// starts (HA-2749). After an operator sets a key, or rotates one, a restart
/// leaves each value in <see cref="SecretStores.All"/> as <c>v2</c> under the
/// current key and opens to the same secret, with nothing to enter again.
///
/// <para>It is a hosted service, so it never runs in migrate mode, where
/// Program.cs returns before the host starts; it checks the setting as well.
/// With no current key it returns before it touches the lock or the database.</para>
///
/// <para>Two API instances starting together do not both reseal: the first
/// takes a Postgres advisory lock on a connection of its own and the other
/// says so and goes on. A row is updated only if it still holds the text that
/// was read, so a secret saved while this runs is never overwritten. A value
/// that cannot be opened is left exactly as it is, and the log names its row
/// and the key id it names, never the value.</para>
///
/// <para>A reseal that fails logs the error and lets the API start. Values stay
/// readable under both keys, and the next start tries again.</para>
///
/// <para>Each store in <see cref="SecretStores.All"/> says which rows are
/// secrets at all (HA-2774): the site settings walk is limited to
/// <see cref="SiteSettingKeys.Secrets"/>, so an ordinary setting such as a
/// name or a number is never opened as an older-form secret, resealed, or
/// named in the "could not be opened" log. A row an earlier build resealed by
/// mistake, before this limit, cannot be told from a real secret once it
/// reads <c>v2</c> - this fix does not try to find or repair one.</para>
/// </summary>
public sealed class SecretReseal(
    SecretProtector protector, SecretCipher cipher, IConfiguration configuration, DatabaseConnections connections, ILogger<SecretReseal> logger) : IHostedService
{
    /// <summary>The lock's key, arbitrary but fixed - "reseal" in ASCII. Advisory locks are per database.</summary>
    internal const long LockKey = 0x72657365616C;

    /// <summary>
    /// Runs once the lock is held, before the walk. A test holds one instance
    /// here so the other has to find the lock taken rather than win a race.
    /// </summary>
    internal Func<CancellationToken, Task>? WhileHeld { get; set; }

    /// <summary>What happened to one row.</summary>
    internal enum RowOutcome
    {
        /// <summary>Already <c>v2</c> under the current key; not touched.</summary>
        Current,

        /// <summary>Rewritten as <c>v2</c> under the current key.</summary>
        Resealed,

        /// <summary>Could not be opened; left exactly as it was.</summary>
        Unopened,

        /// <summary>No longer held what was read, so it was saved meanwhile; left as that save wrote it.</summary>
        SavedMeanwhile,
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!cipher.CanSeal) return;
        if (configuration["HATCH_MIGRATE"] == "1") return;

        try
        {
            var connectionString = connections.Hatch;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("There is no ConnectionStrings:Hatch to reseal stored secrets in");

            await using var held = await PostgresAdvisoryLock.TryAcquireAsync(connectionString, LockKey, cancellationToken);
            if (held is null)
            {
                logger.LogInformation("Another instance is resealing stored secrets, so this one leaves them to it.");
                return;
            }

            if (WhileHeld is not null) await WhileHeld(cancellationToken);

            await ResealAsync(connectionString, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping; the next start tries again.
        }
        catch (Exception e)
        {
            logger.LogError(e, "Resealing stored secrets failed, so they stay as they are and are tried again at the next start.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ResealAsync(string connectionString, CancellationToken ct)
    {
        int resealed = 0, unopened = 0;

        foreach (var store in SecretStores.All)
        {
            // Every row is read before any is written, so no reader is open
            // across an update. Rows are few.
            var rows = await ReadAsync(connectionString, store, ct);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);

            foreach (var (ids, stored) in rows)
            {
                switch (await ResealRowAsync(connection, store, ids, stored, ct))
                {
                    case RowOutcome.Resealed: resealed++; break;
                    case RowOutcome.Unopened: unopened++; break;
                }
            }
        }

        logger.LogInformation(
            "Resealed {Resealed} stored secrets under key {KeyId}; {Unopened} could not be opened.",
            resealed, cipher.CurrentKeyId, unopened);
    }

    /// <summary>
    /// The id values and the sealed text of every row of <paramref name="store"/>
    /// that holds one, on a connection of its own. When <see cref="SecretStore.OnlyKeys"/>
    /// is set, a row whose first id column is not in it is never read: an
    /// ordinary setting such as a name is not an older-form secret, and is
    /// never opened, resealed, or named in the "could not be opened" log.
    /// </summary>
    private static async Task<List<(string[] Ids, string Stored)>> ReadAsync(string connectionString, SecretStore store, CancellationToken ct)
    {
        var columns = string.Join(", ", store.IdColumns.Select(Quote));
        var sql = $"SELECT {columns}, {Quote(store.Column)} FROM {Table(store)} " +
                  $"WHERE {Quote(store.Column)} IS NOT NULL AND {Quote(store.Column)} <> ''";
        if (store.OnlyKeys is not null) sql += $" AND {Quote(store.IdColumns[0])} = ANY(@only)";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        if (store.OnlyKeys is not null) command.Parameters.AddWithValue("only", store.OnlyKeys);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<(string[], string)>();
        while (await reader.ReadAsync(ct))
        {
            var ids = new string[store.IdColumns.Length];
            for (var i = 0; i < ids.Length; i++) ids[i] = reader.GetString(i);
            rows.Add((ids, reader.GetString(ids.Length)));
        }

        return rows;
    }

    /// <summary>
    /// Reseals one row, or leaves it. The update matches the old text as well
    /// as the row, so a secret saved since it was read is not overwritten with
    /// one sealed from the older value.
    /// </summary>
    internal async Task<RowOutcome> ResealRowAsync(
        NpgsqlConnection connection, SecretStore store, string[] ids, string stored, CancellationToken ct)
    {
        if (protector.IsCurrent(stored)) return RowOutcome.Current;

        var rowName = store.RowName(ids);

        var secret = protector.Unprotect(stored, rowName);
        string resealed = string.Empty;
        if (secret is null || !protector.TrySeal(secret.Reveal(), rowName, out resealed) || resealed.Length == 0)
        {
            logger.LogWarning(
                "The stored secret on {Row} could not be opened, so it is left as it is. It names key {KeyId}.",
                rowName, SecretProtector.KeyIdOf(stored) ?? "none (v1, untagged or unreadable)");
            return RowOutcome.Unopened;
        }

        var sql = new StringBuilder($"UPDATE {Table(store)} SET {Quote(store.Column)} = @new WHERE ");
        for (var i = 0; i < store.IdColumns.Length; i++) sql.Append($"{Quote(store.IdColumns[i])} = @id{i} AND ");
        sql.Append($"{Quote(store.Column)} = @old");

        await using var command = new NpgsqlCommand(sql.ToString(), connection);
        command.Parameters.AddWithValue("new", resealed);
        command.Parameters.AddWithValue("old", stored);
        for (var i = 0; i < ids.Length; i++) command.Parameters.AddWithValue($"id{i}", ids[i]);

        if (await command.ExecuteNonQueryAsync(ct) > 0) return RowOutcome.Resealed;

        logger.LogInformation("The stored secret on {Row} was saved while resealing, so it is left as that save wrote it.", rowName);
        return RowOutcome.SavedMeanwhile;
    }

    private static string Table(SecretStore store) =>
        store.Schema is null ? Quote(store.Table) : $"{Quote(store.Schema)}.{Quote(store.Table)}";

    // Every identifier comes from SecretStores, never from a row; values are always parameters.
    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
}
