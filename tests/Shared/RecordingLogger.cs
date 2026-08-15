using Microsoft.Extensions.Logging;

namespace Jobbliggaren.TestSupport;

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
///
/// <para>
/// <b>It lives in <c>tests/Shared/</c> and is LINKED, not copied</b> (the
/// <c>Compile Include</c> items in the consuming csproj files, same as
/// <see cref="TestFacets"/>). It moved here from
/// <c>Jobbliggaren.Application.UnitTests/Common/</c> when <c>Jobbliggaren.QA.Corpus</c>
/// became a second consumer: the layout corpus reads
/// <c>AutoPromoteParsedResumeCommandHandler</c>'s <c>{BlockDetail}</c> property off this
/// recorder, because <c>AutoPromoteGateVerdict</c> is <c>internal</c> to
/// <c>Jobbliggaren.Application</c> and the corpus is not in its <c>InternalsVisibleTo</c>
/// list — the production log line is the only seam it has. Two recording loggers would be
/// two homes for one test utility, so the type moved rather than being duplicated
/// (#1060 D3(β) PR 2, CTO constraint 2).
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
        //
        // SNAPSHOT, never hold the reference (#1237, measured 2026-08-08). Which state type
        // arrives depends on WHICH [LoggerMessage] generator compiled the caller, and the two
        // behave differently after Log returns:
        //   - Jobbliggaren.Application types compile against the BCL generator and pass an
        //     IMMUTABLE readonly struct. Holding it is harmless, which is why this helper
        //     appeared to work for three consumers.
        //   - Jobbliggaren.INFRASTRUCTURE types compile against the R9 generator in
        //     Microsoft.Extensions.Telemetry.Abstractions (transitive via
        //     Microsoft.Extensions.Resilience — present in Infrastructure's assets, ABSENT from
        //     Application's) and pass Microsoft.Extensions.Logging.LoggerMessageState, which is
        //     THREAD-LOCAL, POOLED and CLEARED the moment the generated method returns. Measured:
        //     Count == 2 inside this method, Count == 0 one statement after the call.
        // Holding that reference made Properties read as EMPTY for every Infrastructure logger —
        // silently, because an empty list fails no assertion that only reads names. No test had
        // ever asserted properties on an Infrastructure type, so nothing surfaced it until
        // ScalewayEmailSenderTests did.
        var properties = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
            ? pairs.ToArray()
            : [];

        Records.Add((logLevel, eventId, formatter(state, exception), properties));
    }
}
