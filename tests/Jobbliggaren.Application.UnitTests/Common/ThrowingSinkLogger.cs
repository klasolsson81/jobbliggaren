using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// An <c>ILogger&lt;T&gt;</c> whose sink is BROKEN — every <c>Log</c> call throws, the way MEL
/// does when a provider faults (it aggregates provider exceptions and rethrows).
///
/// <para>
/// This is the double that proves a telemetry guard is real rather than decorative. A guard of
/// the shape <c>catch (Exception ex) { LogFailed(logger, ex); }</c> looks like it swallows
/// everything — but if the SINK is what threw, the handler throws for the same reason and the
/// exception escapes exactly as if there were no guard at all. That is not a hypothetical: it
/// is the single most likely way to reach the handler (#754, CTO bind Q1 — "a telemetry
/// component must never be able to fault the process it monitors").
/// </para>
///
/// <para>
/// It stayed in this project when <c>RecordingLogger&lt;T&gt;</c> moved to
/// <c>tests/Shared/</c>: only the recorder has a second consumer, and relocating a type no
/// other assembly links would widen the shared surface for nothing.
/// </para>
/// </summary>
internal sealed class ThrowingSinkLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => throw new InvalidOperationException("Log sink is down (test double).");
}
