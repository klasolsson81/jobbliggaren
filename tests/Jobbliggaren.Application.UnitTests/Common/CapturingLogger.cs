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
    private readonly List<Exception?> _exceptions = [];

    public IReadOnlyList<(LogLevel Level, int EventId, string Message)> Records
    {
        get
        {
            lock (_records)
                return [.. _records];
        }
    }

    /// <summary>The exception each line in <see cref="Records"/> carried, in the same order.</summary>
    public IReadOnlyList<Exception?> Exceptions
    {
        get
        {
            lock (_records)
                return [.. _exceptions];
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_records)
        {
            _records.Add((logLevel, eventId.Id, formatter(state, exception)));
            _exceptions.Add(exception);
        }
    }
}
