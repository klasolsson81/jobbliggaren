using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

/// <summary>One captured log record: category, level, event-id and the rendered message.</summary>
public sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message);

/// <summary>
/// Minimal <see cref="ILoggerProvider"/> that captures every log record into an in-memory queue,
/// so a test can assert that a specific event (e.g. <c>session_store_unavailable</c>, #512) was
/// emitted. Thread-safe. Register it as an <see cref="ILoggerProvider"/> singleton on the host
/// under test, or wrap it in a <see cref="LoggerFactory"/> for a pure unit test.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Logs);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(new CapturedLog(category, logLevel, eventId, formatter(state, exception)));
    }
}
