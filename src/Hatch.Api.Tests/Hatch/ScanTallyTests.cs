using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The request log line and the closed list of callers (HA-2792). The meter
/// itself is read in the queue test, and the statement counter against the
/// driver in the database lane, each beside the code they check.
/// </summary>
public class ScanTallyTests
{
    private const string Page = "src/Hatch.Web/apps/hatch/docs/troubleshooting.md";

    [Fact]
    public async Task ARequestThatRanAScan_WritesOneInformationLineWithItsFields()
    {
        var logs = new CapturedLogs();
        var middleware = new ScanTallyMiddleware(
            _ =>
            {
                var tally = ScanTally.Current!;
                tally.CountStatement();
                tally.CountStatement();
                tally.RecordScan(ScanCaller.Queue, outermost: true, elapsedTicks: 0);
                tally.RecordScan(ScanCaller.Queue, outermost: false, elapsedTicks: 0);
                tally.RecordTreeBuild();
                return Task.CompletedTask;
            },
            logs.CreateLogger("test").AsLogger<ScanTallyMiddleware>());

        await middleware.InvokeAsync(new DefaultHttpContext());

        var line = Assert.Single(logs.AtLevel(LogLevel.Information));
        Assert.Contains("Dispatch reads on unrouted: 2 scans, 0 loads, 1 tree builds, 2 statements, 0 ms in scans,", line);
        Assert.Contains("by caller queue=2", line);
    }

    [Fact]
    public async Task ARequestThatOnlyRead_WritesNothing()
    {
        var logs = new CapturedLogs();
        var middleware = new ScanTallyMiddleware(
            _ =>
            {
                ScanTally.Current!.CountStatement();
                return Task.CompletedTask;
            },
            logs.CreateLogger("test").AsLogger<ScanTallyMiddleware>());

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.Empty(logs.Messages);
    }

    [Fact]
    public async Task TheTally_IsGoneWhenTheRequestEnds()
    {
        var middleware = new ScanTallyMiddleware(_ => Task.CompletedTask, new CapturedLogs().CreateLogger("test").AsLogger<ScanTallyMiddleware>());

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.Null(ScanTally.Current);
    }

    [Fact]
    public void ANestedScan_TakesItsStatementsAndTheParentKeepsItsOwn()
    {
        using var probe = new ScanMeterProbe();

        var parent = ScanFrame.Push(ScanCaller.Next);
        StatementCounter.Instance.NonQueryExecuting(null!, null!, default);
        var child = ScanFrame.Push(ScanCaller.Next);
        StatementCounter.Instance.NonQueryExecuting(null!, null!, default);
        StatementCounter.Instance.NonQueryExecuting(null!, null!, default);
        child.Pop();
        parent.Pop();

        Assert.Equal(1, parent.Statements);
        Assert.Equal(2, child.Statements);
        Assert.Equal(3, probe.Tally.Statements);
    }

    [Fact]
    public void WithNoTally_TheCounterCountsNothingAndReturnsTheResultUntouched()
    {
        var result = StatementCounter.Instance.NonQueryExecuting(null!, null!, default);

        Assert.False(result.HasResult);
        Assert.Null(ScanTally.Current);
    }

    [Fact]
    public void EveryCaller_IsNamedOnTheTroubleshootingPage()
    {
        var page = File.ReadAllText(Path.Combine(MinimalDatabases.RepositoryRoot(), Page.Replace('/', Path.DirectorySeparatorChar)));

        var missing = ScanCaller.All.Where(c => !page.Contains($"`{c.Name}`", StringComparison.Ordinal)).Select(c => c.Name).ToList();

        Assert.True(
            missing.Count == 0,
            $"troubleshooting.md does not name the scan caller(s) {string.Join(", ", missing)}. Add each to \"A busy database\".");
    }

    [Fact]
    public void TheClosedList_HoldsEveryStaticMember()
    {
        var declared = typeof(ScanCaller).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ScanCaller))
            .Select(f => ((ScanCaller)f.GetValue(null)!).Name)
            .Order()
            .ToList();

        Assert.Equal(declared, ScanCaller.All.Select(c => c.Name).Order().ToList());
    }
}

internal static class LoggerShim
{
    /// <summary>The test's captured logger, seen as the typed one a middleware asks for.</summary>
    public static ILogger<T> AsLogger<T>(this ILogger logger) => new Typed<T>(logger);

    private sealed class Typed<T>(ILogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
