using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — unit tests for the deregister-sweep safety gate. The invariant "a partial or
/// under-floor run must NEVER flip the untouched majority to Deregistered" is the sweep's whole point;
/// it is pinned here DB-free (parity the JobTech snapshot floor-guard).
/// </summary>
public class ScbSweepGateTests
{
    private static ScbRegisterOptions Options(int floorAbsolute = 500_000, double relativeRatio = 0.80) =>
        new() { FloorAbsolute = floorAbsolute, FloorRelativeRatio = relativeRatio };

    private static ScbSyncOutcome Outcome(int fetched, bool truncated = false)
    {
        var outcome = new ScbSyncOutcome();
        outcome.RecordFetched(fetched);
        if (truncated)
            outcome.MarkTruncatedOrErrored();
        return outcome;
    }

    [Fact]
    public void Decide_Skips_WhenTruncatedOrErrored_EvenAboveFloors()
    {
        var (apply, reason) = ScbSweepGate.Decide(
            Outcome(1_000_000, truncated: true), Options(), maxObserved: 1_000_000);

        apply.ShouldBeFalse();
        reason.ShouldBe("truncated-or-errored");
    }

    [Fact]
    public void Decide_Skips_WhenBelowAbsoluteFloor()
    {
        var (apply, reason) = ScbSweepGate.Decide(
            Outcome(400_000), Options(floorAbsolute: 500_000), maxObserved: null);

        apply.ShouldBeFalse();
        reason.ShouldBe("below-absolute-floor");
    }

    [Fact]
    public void Decide_Skips_WhenBelowRelativeFloor()
    {
        // Fetched 600k clears the absolute floor but is < 0.80 × prior 1M = 800k.
        var (apply, reason) = ScbSweepGate.Decide(
            Outcome(600_000), Options(floorAbsolute: 500_000, relativeRatio: 0.80), maxObserved: 1_000_000);

        apply.ShouldBeFalse();
        reason.ShouldBe("below-relative-floor");
    }

    [Fact]
    public void Decide_Applies_OnFirstCleanRun_NoPriorBaseline()
    {
        // No prior audit row → maxObserved null → only the absolute floor applies.
        var (apply, reason) = ScbSweepGate.Decide(
            Outcome(1_000_000), Options(floorAbsolute: 500_000), maxObserved: null);

        apply.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact]
    public void Decide_Applies_WhenCleanAndAboveBothFloors()
    {
        var (apply, reason) = ScbSweepGate.Decide(
            Outcome(950_000), Options(floorAbsolute: 500_000, relativeRatio: 0.80), maxObserved: 1_000_000);

        apply.ShouldBeTrue();
        reason.ShouldBeNull();
    }
}
