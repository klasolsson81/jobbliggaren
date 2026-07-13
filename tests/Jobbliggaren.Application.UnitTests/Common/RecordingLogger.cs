using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Minimal <c>ILogger&lt;T&gt;</c> test double that records every <c>Log</c> call
/// verbatim — level, EventId, the FORMATTED message (i.e. after the
/// LoggerMessage template's placeholders are substituted), and the STRUCTURED
/// property names/values.
///
/// <para>
/// Modelled on the private nested recorder in <c>AuthAuditLoggerTests</c> (a third,
/// unrelated copy lives in <c>LocalDataKeyProviderTests</c>). Those are deliberately left
/// alone — migrating them is a separate change-reason and would touch suites this PR has no
/// business in. This is the shared one for tests that assert on structured log OUTPUT rather
/// than merely that <c>ILogger</c> was called; new tests should use it (#754).
/// </para>
///
/// <para>
/// <b>Why <see cref="Records"/> carries <c>Properties</c> and not just the message.</b>
/// A structured sink (Seq) indexes the property NAMES, and MEL derives those from the
/// placeholder TOKEN — <c>{WorkingSetBytes}</c> — not from the surrounding literal text
/// (<c>workingSetBytes=</c>), which is only prose. Seq's <c>@Properties['...']</c> lookup
/// is case-sensitive, so a query written against the prose rather than the token returns
/// rows with every selected column NULL. That is exactly what
/// <c>docs/runbooks/performance-measurement.md</c> §B/§C shipped in review, and no test
/// could see it, because a logger double that records only the formatted string cannot
/// distinguish the two. It can now (dotnet-architect, #754).
/// </para>
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties)> Records
    { get; } = [];

    public (LogLevel Level, EventId EventId, string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties) Latest => Records[^1];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // [LoggerMessage]-generated TState implements IReadOnlyList<KVP> — that list IS
        // what a structured sink writes. Anything else (a plain string) has no properties.
        var properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];

        Records.Add((logLevel, eventId, formatter(state, exception), properties));
    }
}

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
/// </summary>
internal sealed class ThrowingSinkLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => throw new InvalidOperationException("Log sink is down (test double).");
}
