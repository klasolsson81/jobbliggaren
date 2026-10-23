using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email.Counting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>A tally that keeps what it was asked to count, through the real <see cref="EmailSendTally.RecordAsync"/>.</summary>
internal sealed class RecordingEmailSendTally() : EmailSendTally(NullLogger.Instance)
{
    public List<(EmailKind Kind, EmailSendOutcome Outcome)> Records { get; } = [];

    protected override Task WriteAsync(EmailKind kind, EmailSendOutcome outcome, CancellationToken cancellationToken)
    {
        Records.Add((kind, outcome));
        return Task.CompletedTask;
    }
}

/// <summary>A tally whose write does whatever the test scripts: throw at once, throw later, or never finish.</summary>
internal sealed class ScriptedEmailSendTally(ILogger logger, Func<CancellationToken, Task> write)
    : EmailSendTally(logger)
{
    public int Writes { get; private set; }

    protected override Task WriteAsync(EmailKind kind, EmailSendOutcome outcome, CancellationToken cancellationToken)
    {
        Writes++;
        return write(cancellationToken);
    }
}

/// <summary>
/// A logger that keeps the exception object it was handed as well as the rendered message and a snapshot of the
/// structured properties, so a test can assert that nothing reaches a sink beyond what the message template names.
/// </summary>
internal sealed class CapturingLogger : ILogger
{
    public List<Captured> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state is IReadOnlyList<KeyValuePair<string, object?>> pairs ? pairs.ToArray() : [];
        Records.Add(new Captured(logLevel, eventId, formatter(state, exception), exception, properties));
    }

    internal sealed record Captured(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception,
        IReadOnlyList<KeyValuePair<string, object?>> Properties)
    {
        /// <summary>Everything a sink could write from this record.</summary>
        public string Surface =>
            Message + "|" + string.Join("|", Properties.Select(p => $"{p.Key}={p.Value}"));
    }
}
