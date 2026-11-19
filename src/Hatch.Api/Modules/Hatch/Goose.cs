using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// One thing the goose looks at every sweep. Registered with the container in
/// the order it should run; <see cref="GooseJob"/> takes them as registered.
/// </summary>
public interface IGooseCheck
{
    /// <summary>Names the check in a run. Stable: a rename reads as a changed conclusion.</summary>
    string Name { get; }

    Task<GooseObservation> RunAsync(CancellationToken ct);
}

/// <summary>
/// What a check saw and what it made of it, both free-form text.
/// <see cref="Looked"/> is for a person reading the run and never decides
/// whether two sweeps are the same; <see cref="Concluded"/> does.
/// </summary>
public record GooseObservation(string Looked, string Concluded);

/// <summary>One check's part of a sweep, as stored in <see cref="EfHatchGooseRun.Entries"/>.</summary>
public record GooseEntry(string Check, string Looked, string Concluded, bool Failed, string? Error)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Whether two entries reached the same conclusion. Never compares
    /// <see cref="Looked"/>, and nothing here carries a timing.
    /// </summary>
    public bool SameConclusionAs(GooseEntry other) =>
        Check == other.Check && Failed == other.Failed && Concluded == other.Concluded && Error == other.Error;

    public static string Write(IReadOnlyList<GooseEntry> entries) => JsonSerializer.Serialize(entries, Json);

    public static List<GooseEntry> Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GooseEntry>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// The goose's two options, guarded in one place as <see cref="BackupService"/>
/// guards <see cref="HatchOptions.BackupKeepDays"/>: a value that cannot mean
/// anything falls back to <see cref="HatchOptions"/>'s own default.
/// </summary>
public static class GooseSettings
{
    /// <summary>Seconds between sweeps. Zero or negative falls back to the default.</summary>
    public static int IntervalSeconds(HatchOptions options) =>
        options.GooseIntervalSeconds > 0 ? options.GooseIntervalSeconds : new HatchOptions().GooseIntervalSeconds;

    /// <summary>
    /// Days a run is kept after its last sweep. Zero keeps everything - the
    /// operator turning pruning off - and only a negative falls back.
    /// </summary>
    public static int KeepDays(HatchOptions options) =>
        options.GooseKeepDays >= 0 ? options.GooseKeepDays : new HatchOptions().GooseKeepDays;

    /// <summary>
    /// How long <see cref="TrainReadyCheck"/>'s bound is. Zero turns the check
    /// off - the operator's own choice, as <see cref="KeepDays"/>'s zero is -
    /// and only a negative falls back to the default.
    /// </summary>
    public static TimeSpan TrainReadyBound(HatchOptions options) => TimeSpan.FromMinutes(
        options.GooseTrainReadyMinutes >= 0 ? options.GooseTrainReadyMinutes : new HatchOptions().GooseTrainReadyMinutes);
}

/// <summary>One label and value a <see cref="FindingCheck"/> reported for a finding, as stored in <see cref="EfHatchGooseFinding.Evidence"/>.</summary>
public record GooseFindingEvidence(string Label, string Value)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Write(IReadOnlyList<GooseFindingEvidence> evidence) => JsonSerializer.Serialize(evidence, Json);

    public static List<GooseFindingEvidence> Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GooseFindingEvidence>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Keeps two sweeps from running at once, on this instance or another.
/// <see cref="GooseJob"/> has <c>[DisallowConcurrentExecution]</c>, but Quartz
/// alone is not enough: every instance shares one scheduler instance id (see
/// <c>SchedulerCheckout</c>), so the lock lives in the database.
/// </summary>
public interface IGooseLock
{
    /// <summary>
    /// Takes the lock, or returns null when another sweep holds it. Disposing
    /// what is returned releases it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct);
}

/// <summary>
/// A Postgres session advisory lock, held on a connection of its own for the
/// whole sweep. Its own connection, so the context's work neither shares nor
/// ends the session, and a process that dies mid-sweep drops the lock when its
/// connection does.
/// </summary>
public sealed class PostgresGooseLock : IGooseLock
{
    /// <summary>The lock's key, arbitrary but fixed - "goose" in ASCII. Advisory locks are per database.</summary>
    private const long Key = 0x676F6F7365;

    public async Task<IAsyncDisposable?> TryAcquireAsync(HatchContext db, CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The Hatch context has no connection string to take the sweep lock on");

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);

            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", Key);

            if ((bool)(await command.ExecuteScalarAsync(ct))!) return new Held(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Held(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", Key);
                await command.ExecuteScalarAsync();
            }
            catch
            {
                // A connection that cannot say so has already lost the lock with its session.
            }

            await connection.DisposeAsync();
        }
    }
}
