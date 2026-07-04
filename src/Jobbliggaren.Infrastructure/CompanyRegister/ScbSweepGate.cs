using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — the deregister-sweep safety gate, extracted as a PURE decision so the "never
/// falsely deregister" invariant is unit-testable without a DB (parity the JobTech snapshot
/// floor-guard). The sweep marks companies absent from a fresh extract as Deregistered; it may run
/// ONLY when the extract completed cleanly AND cleared both floors, so a partial run (SCB 503 mid-run,
/// an un-sliceable partition, a cancelled job) can never flip the untouched majority.
/// </summary>
internal static class ScbSweepGate
{
    /// <summary>
    /// Decides whether the deregister sweep may run. <paramref name="maxObserved"/> is the largest
    /// prior-run fetched total (null on the first run — only the absolute floor then applies).
    /// </summary>
    public static (bool Apply, string? SkipReason) Decide(
        ScbSyncOutcome outcome, ScbRegisterOptions options, int? maxObserved)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(options);

        if (outcome.TruncatedOrErrored)
            return (false, "truncated-or-errored");

        if (outcome.TotalRowsFetched < options.FloorAbsolute)
            return (false, "below-absolute-floor");

        if (maxObserved is { } prior && outcome.TotalRowsFetched < prior * options.FloorRelativeRatio)
            return (false, "below-relative-floor");

        return (true, null);
    }
}
