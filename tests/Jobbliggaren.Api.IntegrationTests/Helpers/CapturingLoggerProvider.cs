using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.IntegrationTests.Helpers;

/// <summary>One captured log record: category, level, event-id and the rendered message.</summary>
public sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message)
{
    /// <summary>The exception's full text, or null when the record carries none.</summary>
    public string? Exception { get; init; }

    /// <summary>The record's structured state, one <c>key=value</c> per entry.</summary>
    public IReadOnlyList<string> State { get; init; } = [];

    /// <summary>Every scope active when the record was written, key-value scopes one entry per value.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Everything the record carries as text, so an absence check covers all of it.</summary>
    public string AllText =>
        string.Join('\n', new[] { Category, Message, Exception ?? string.Empty }.Concat(State).Concat(Scopes));
}

/// <summary>
/// Minimal <see cref="ILoggerProvider"/> that captures every log record into an in-memory queue,
/// so a test can assert that a specific event (e.g. <c>store_unavailable</c>, #512) was
/// emitted. Thread-safe. Register it as an <see cref="ILoggerProvider"/> singleton on the host
/// under test, or wrap it in a <see cref="LoggerFactory"/> for a pure unit test. It takes the
/// factory's scopes, so a record also carries the exception text and the scope values a sink sees.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider? _scopes;

    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Logs, () => _scopes);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose() { }

    private static IEnumerable<string> Entries(object? value) =>
        value is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.Select(pair => $"{pair.Key}={pair.Value}")
            : [value?.ToString() ?? string.Empty];

    private sealed class CapturingLogger(
        string category, ConcurrentQueue<CapturedLog> sink, Func<IExternalScopeProvider?> scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var active = new List<string>();
            scopes()?.ForEachScope((scope, list) => list.AddRange(Entries(scope)), active);
            sink.Enqueue(new CapturedLog(category, logLevel, eventId, formatter(state, exception))
            {
                Exception = exception?.ToString(),
                State = Entries(state).ToList(),
                Scopes = active,
            });
        }
    }
}
