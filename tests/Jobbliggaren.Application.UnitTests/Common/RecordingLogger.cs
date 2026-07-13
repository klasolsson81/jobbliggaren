using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Minimal <c>ILogger&lt;T&gt;</c> test double that records every <c>Log</c> call
/// verbatim — level, EventId, and the FORMATTED message (i.e. after the
/// LoggerMessage template's placeholders are substituted). Mirrors the
/// house pattern already used privately in <c>AuthAuditLoggerTests</c>;
/// extracted here so tests that need to assert on structured log OUTPUT —
/// not just that <c>ILogger</c> was called — share one implementation
/// instead of re-declaring it per test file (#754).
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message)> Records { get; } = [];

    public (LogLevel Level, EventId EventId, string Message) Latest => Records[^1];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => Records.Add((logLevel, eventId, formatter(state, exception)));
}
