using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Who asked for a dispatch scan. A closed list: the constructor is internal,
/// so a call site cannot invent a name, and <see cref="Dispatch.ScanAsync"/>
/// takes one as its first argument, so leaving it out does not compile. The
/// name is lowercase kebab, because it becomes a metric tag and a word in the
/// log line (docs: troubleshooting, "A busy database").
/// </summary>
public sealed record ScanCaller
{
    internal ScanCaller(string name) => Name = name;

    public string Name { get; }

    /// <summary><c>GET /api/work/next</c>, and the named dispatch it shares a controller with.</summary>
    public static readonly ScanCaller Next = new("next");

    /// <summary><c>GET /api/work/queue</c>, and the takers it asks for.</summary>
    public static readonly ScanCaller Queue = new("queue");

    /// <summary>The attention page: deadlocks, runner-less work, and rows held at a project line.</summary>
    public static readonly ScanCaller Attention = new("attention");

    /// <summary>A heartbeat that asks whether its claim should yield to emergency work.</summary>
    public static readonly ScanCaller ClaimHeartbeat = new("claim-heartbeat");

    /// <summary>The goose's sweep, on its own timer.</summary>
    public static readonly ScanCaller Goose = new("goose");

    /// <summary>Every caller a scan may name outside tests, for the docs check to read.</summary>
    public static IReadOnlyList<ScanCaller> All { get; } = [Next, Queue, Attention, ClaimHeartbeat, Goose];

    public override string ToString() => Name;
}

/// <summary>
/// The scans and statements one request, or one goose sweep, made. Begun by
/// <see cref="ScanTallyMiddleware"/> or the sweep; read by the log line, and by
/// a test through <see cref="Current"/>. Thread-safe: a request's reads may
/// overlap.
/// </summary>
public sealed class ScanTally
{
    private static readonly AsyncLocal<ScanTally?> CurrentTally = new();

    private readonly object gate = new();
    private readonly SortedDictionary<string, int> byCaller = new(StringComparer.Ordinal);
    private readonly long began = Stopwatch.GetTimestamp();
    private long statements;
    private int scans;
    private int treeBuilds;
    private long scanTicks;

    private ScanTally(string route) => Route = route;

    /// <summary>The tally of the request or sweep this code is running inside, or null.</summary>
    public static ScanTally? Current => CurrentTally.Value;

    /// <summary>The route pattern of the request, or the sweep's own name.</summary>
    public string Route { get; }

    /// <summary>Every command sent in the request, whether a scan sent it or not.</summary>
    public long Statements => Interlocked.Read(ref statements);

    public int Scans { get { lock (gate) return scans; } }

    public int TreeBuilds { get { lock (gate) return treeBuilds; } }

    /// <summary>Scans started by each caller, nested ones included.</summary>
    public IReadOnlyDictionary<string, int> ByCaller { get { lock (gate) return new Dictionary<string, int>(byCaller); } }

    /// <summary>True once a scan ran or a priority tree was built: the only requests worth a log line.</summary>
    public bool Ran { get { lock (gate) return scans > 0 || treeBuilds > 0; } }

    /// <summary>Starts a tally for the code that follows, until the result is disposed.</summary>
    public static Scope Begin(string route)
    {
        var tally = new ScanTally(route);
        var previous = CurrentTally.Value;
        CurrentTally.Value = tally;
        return new Scope(tally, previous);
    }

    internal void CountStatement() => Interlocked.Increment(ref statements);

    internal void RecordScan(ScanCaller caller, bool outermost, long elapsedTicks)
    {
        lock (gate)
        {
            scans++;
            byCaller[caller.Name] = byCaller.GetValueOrDefault(caller.Name) + 1;

            // Outermost only: a scan that starts peer scans is already timing
            // them, so adding theirs again would count the same time twice.
            if (outermost) scanTicks += elapsedTicks;
        }
    }

    internal void RecordTreeBuild()
    {
        lock (gate) treeBuilds++;
    }

    /// <summary>Writes the one line, when a scan ran or a tree was built. Returns whether it wrote.</summary>
    public bool Log(ILogger logger)
    {
        if (!Ran) return false;

        int scanCount, builds;
        long scanMs;
        string callers;
        lock (gate)
        {
            scanCount = scans;
            builds = treeBuilds;
            scanMs = (long)Stopwatch.GetElapsedTime(0, scanTicks).TotalMilliseconds;
            callers = string.Join(' ', byCaller.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key}={p.Value}")));
        }

        var requestMs = (long)Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        logger.LogInformation(
            "Dispatch reads on {Route}: {Scans} scans, {TreeBuilds} tree builds, {Statements} statements, {ScanMs} ms in scans, {RequestMs} ms in all, by caller {Callers}",
            Route, scanCount, builds, Statements, scanMs, requestMs, callers.Length == 0 ? "none" : callers);
        return true;
    }

    /// <summary>Puts the previous tally back when disposed.</summary>
    public readonly struct Scope(ScanTally tally, ScanTally? previous) : IDisposable
    {
        public ScanTally Tally { get; } = tally;

        public void Dispose() => CurrentTally.Value = previous;
    }
}

/// <summary>
/// One scan in flight, so the statements it sends are counted against it and
/// not its parent. A scan that has to judge a deferral runs a scan per live
/// peer from inside itself; each of those has a frame of its own, and the
/// parent's count leaves them out.
/// </summary>
internal sealed class ScanFrame
{
    private static readonly AsyncLocal<ScanFrame?> CurrentFrame = new();

    private long statements;

    private ScanFrame(ScanCaller caller, ScanFrame? parent)
    {
        Caller = caller;
        Parent = parent;
    }

    public static ScanFrame? Current => CurrentFrame.Value;

    public ScanCaller Caller { get; }

    public ScanFrame? Parent { get; }

    public long Statements => Interlocked.Read(ref statements);

    public static ScanFrame Push(ScanCaller caller)
    {
        var frame = new ScanFrame(caller, CurrentFrame.Value);
        CurrentFrame.Value = frame;
        return frame;
    }

    public void Pop() => CurrentFrame.Value = Parent;

    public void CountStatement() => Interlocked.Increment(ref statements);
}

/// <summary>
/// What an exporter reads: <c>dotnet-counters monitor --counters Hatch.Dispatch</c>,
/// or any OpenTelemetry meter listener. Nothing here exports anything.
/// </summary>
public static class ScanMetrics
{
    public const string MeterName = "Hatch.Dispatch";

    private static readonly Meter Source = new(MeterName);

    private static readonly Counter<long> Scans = Source.CreateCounter<long>(
        "hatch.dispatch.scans", "{scan}", "Dispatch scans started, by the caller that asked for them.");

    private static readonly Histogram<double> Duration = Source.CreateHistogram<double>(
        "hatch.dispatch.scan.duration", "ms", "How long a scan took, peer scans it ran included.");

    private static readonly Histogram<int> Candidates = Source.CreateHistogram<int>(
        "hatch.dispatch.scan.candidates", "{row}", "Rows a scan judged.");

    private static readonly Histogram<long> Statements = Source.CreateHistogram<long>(
        "hatch.dispatch.scan.statements", "{statement}", "Commands a scan sent, not counting the scans it ran.");

    private static readonly Counter<long> TreeBuilds = Source.CreateCounter<long>(
        "hatch.dispatch.tree_builds", "{build}", "Priority trees built, by the scan or the route that built one.");

    internal static void ScanFinished(ScanCaller caller, double milliseconds, int candidates, long statements)
    {
        var tag = new KeyValuePair<string, object?>("caller", caller.Name);
        Scans.Add(1, tag);
        Duration.Record(milliseconds, tag);
        Candidates.Record(candidates, tag);
        Statements.Record(statements, tag);
    }

    internal static void TreeBuilt(string caller) =>
        TreeBuilds.Add(1, new KeyValuePair<string, object?>("caller", caller));
}

/// <summary>
/// Counts the commands a context sends while a <see cref="ScanTally"/> is
/// begun. It only counts: the command, its result and the SQL are returned as
/// they came, so no statement is added or changed. With no tally current it
/// does nothing.
/// </summary>
public sealed class StatementCounter : DbCommandInterceptor
{
    public static StatementCounter Instance { get; } = new();

    private static void Count()
    {
        ScanTally.Current?.CountStatement();
        ScanFrame.Current?.CountStatement();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Count();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Count();
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// Begins a <see cref="ScanTally"/> for each request, named by its route
/// pattern, and writes the one log line when the request ran a scan or built a
/// tree. Placed ahead of the wall, so the wall's own reads are counted.
/// </summary>
public sealed class ScanTallyMiddleware(RequestDelegate next, ILogger<ScanTallyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // The pattern and never the path: a path carries an issue key or a
        // token, and the pattern is one of a few hundred, safe as a tag.
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unrouted";

        using var scope = ScanTally.Begin(route);
        try
        {
            await next(context);
        }
        finally
        {
            scope.Tally.Log(logger);
        }
    }
}
