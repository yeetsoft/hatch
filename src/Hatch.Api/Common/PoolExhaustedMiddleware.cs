using Npgsql;

namespace Hatch.Api.Common;

/// <summary>
/// Answers 503 for a request that could not get a pooled database connection
/// within the wait (<c>Database:PoolWaitSeconds</c>, <see cref="Ef.DatabaseConnections"/>),
/// instead of the 500 an unhandled exception would otherwise become. HA-2802:
/// a pool this cannot grow out of is only a fix if the caller is told to come
/// back rather than queued forever.
///
/// <para>Registered right after <c>UseForwardedHeaders</c>
/// (<c>Program.cs</c>), so a database read from inside the auth gate itself is
/// covered too.</para>
///
/// <para>Matches only Npgsql's own pool-exhausted exception - an
/// <see cref="NpgsqlException"/> whose inner exception is a
/// <see cref="TimeoutException"/> and whose message says the pool is
/// exhausted - walking the whole chain, since EF wraps it in its own
/// <see cref="InvalidOperationException"/> ("likely due to a transient
/// failure") before it reaches a controller. A statement that ran past its
/// own command timeout throws the same outer/inner exception pair with a
/// different message, and has to stay a 500: that is a stuck statement, not
/// a full pool, and retrying it immediately is not the fix.</para>
/// </summary>
public class PoolExhaustedMiddleware(RequestDelegate next, ILogger<PoolExhaustedMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception e) when (IsPoolExhausted(e) && !context.Response.HasStarted)
        {
            logger.LogWarning(e, "The database's connection pool was full, so {Path} is answered 503 rather than left to wait indefinitely.", context.Request.Path.Value);

            context.Response.Headers.RetryAfter = "1";
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("The database is busy. Try again in a moment.");
        }
    }

    internal static bool IsPoolExhausted(Exception e)
    {
        for (var current = e; current is not null; current = current.InnerException)
            if (current is NpgsqlException { InnerException: TimeoutException } npgsql
                && npgsql.Message.Contains("pool has been exhausted", StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}
