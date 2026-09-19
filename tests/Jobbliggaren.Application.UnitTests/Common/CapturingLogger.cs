using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Records every line with its level, event id and rendered message. <see cref="IsEnabled"/> answers true for
/// every level: an NSubstitute logger answers false, so a <c>[LoggerMessage]</c> method would skip the
/// write and an assertion on it would pass vacuously. Thread-safe, for loggers a background loop writes to.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, int EventId, string Message)> _records = [];

    public IReadOnlyList<(LogLevel Level, int EventId, string Message)> Records
    {
        get
        {
            lock (_records)
                return [.. _records];
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_records)
            _records.Add((logLevel, eventId.Id, formatter(state, exception)));
    }
}
