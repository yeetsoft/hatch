using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Hatch.Api.Tests.Hatch;

/// <summary>Everything the host said, so the test can assert on the absence of a level rather than on the presence of a string.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Category, string Message)> lines = new();

    public IReadOnlyCollection<string> Messages => [.. lines.Select(l => l.Message)];

    public IReadOnlyCollection<string> AtErrorOrAbove =>
        [.. lines.Where(l => l.Level >= LogLevel.Error).Select(l => $"[{l.Level}] {l.Category}: {l.Message}")];

    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, lines);

    public void Dispose() { }

    private sealed class Recorder(string category, ConcurrentQueue<(LogLevel, string, string)> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => lines.Enqueue((logLevel, category, $"{formatter(state, exception)} {exception}"));
    }
}
