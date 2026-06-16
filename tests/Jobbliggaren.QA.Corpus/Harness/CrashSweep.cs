using Jobbliggaren.QA.Corpus.Generation;

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>
/// The outcome of running one corpus case through an engine. PII-SAFE by construction
/// (ADR 0074 Invariant 1): it carries only the stable case <see cref="Label"/> (stratum+index),
/// the stratum, whether it threw, and the exception <i>type</i> — NEVER the exception message
/// or any input text (a thrown engine could otherwise echo CV content, incl. a fake pnr, into
/// the message). A crash is reproduced by regenerating the exact case from (seed, stratum,
/// index), not from a captured message.
/// </summary>
public sealed record CrashOutcome(string Label, CorpusStratum Stratum, bool Threw, string? ExceptionType);

/// <summary>
/// The aggregating, per-strata crash-safety sweep (CTO Fork 5 = 5C). Crash-safety is a MUST
/// (100%) correctness invariant for both engines, so the sweep GATES (it is asserted, never
/// observe-only). Unlike a per-input <c>Should.NotThrow</c>, this runs EVERY case and collects
/// all crashes in one pass — so a hardening iteration sees every failing input at once, and
/// the per-strata breakdown makes "100% per edge-class" measurable.
/// </summary>
public static class CrashSweep
{
    /// <summary>
    /// Runs <paramref name="probe"/> over every case, capturing any exception per case (never
    /// rethrowing mid-sweep). The broad <c>catch</c> below is the deliberate ACTION of this
    /// QA harness — register the crashing input id so 100% crash-safety is measurable
    /// (CTO Fork 5C) — NOT a swallow; this is a test frontend, never production code
    /// (CLAUDE.md §5 "catch-all without action" does not apply here).
    /// </summary>
    public static async Task<IReadOnlyList<CrashOutcome>> RunAsync<T>(
        IReadOnlyList<T> cases,
        Func<T, string> label,
        Func<T, CorpusStratum> stratum,
        Func<T, CancellationToken, Task> probe,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var outcomes = new List<CrashOutcome>(cases.Count);
        foreach (var c in cases)
        {
            try
            {
                await probe(c, ct);
                outcomes.Add(new CrashOutcome(label(c), stratum(c), Threw: false, ExceptionType: null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A test-host cancellation is NOT an engine crash — abort the sweep, don't misreport it.
                throw;
            }
            catch (Exception ex) // QA crash-sweep: register + report the crashing case id (action), never swallow.
            {
                outcomes.Add(new CrashOutcome(label(c), stratum(c), Threw: true, ex.GetType().FullName));
            }
        }

        return outcomes;
    }

    /// <summary>The cases that threw (empty when crash-safety holds 100%).</summary>
    public static IReadOnlyList<CrashOutcome> Crashes(this IReadOnlyList<CrashOutcome> outcomes) =>
        [.. outcomes.Where(o => o.Threw)];
}
