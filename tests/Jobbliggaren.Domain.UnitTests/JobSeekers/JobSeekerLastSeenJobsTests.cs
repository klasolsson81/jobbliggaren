using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — the /jobb user-read watermark
/// <see cref="JobSeeker.SetLastSeenJobs"/>. Sibling invariant to <c>SetLastSeenMatches</c>: the
/// watermark advances to the SEEN-THROUGH window (the max CreatedAt the user rendered), NOT
/// clock-now (#759 / #477 Low 4 — an ad ingested between the fetch and the mark-seen stays flagged
/// "Ny"); it is MONOTONIC (a stale/duplicate/out-of-order call never rewinds it); a future-dated
/// seenThrough is CLAMPED to now (a bad client clock can never run the watermark past reality); a
/// fresh seeker has never seen jobs (null); and it is independent of the matches watermark. Drives
/// the per-user "Ny = ingested since your last visit" tag.
/// </summary>
public class JobSeekerLastSeenJobsTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(Guid.NewGuid(), "Klas Olsson", BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    [Fact]
    public void FreshSeeker_ShouldHaveNullLastSeenJobs()
    {
        var seeker = NewSeeker();

        seeker.LastSeenJobsAt.ShouldBeNull();
    }

    [Fact]
    public void SetLastSeenJobs_FromNull_SetsToSeenThrough()
    {
        var seeker = NewSeeker();
        var clock = Later(3);
        var seenThrough = clock.UtcNow;

        seeker.SetLastSeenJobs(seenThrough, clock);

        seeker.LastSeenJobsAt.ShouldBe(seenThrough);
        seeker.UpdatedAt.ShouldBe(clock.UtcNow);
    }

    // #759 (sibling of #477 Low 4) — the core fix: the watermark is the SEEN window (an older
    // seenThrough than clock-now), NOT clock-now. An ad ingested between seenThrough and this call
    // then has CreatedAt > watermark and stays flagged "Ny".
    [Fact]
    public void SetLastSeenJobs_SetsToSeenThrough_NotClockNow()
    {
        var seeker = NewSeeker();
        var clock = Later(3);
        var seenThrough = clock.UtcNow.AddMinutes(-5); // the newest ad the FE rendered

        seeker.SetLastSeenJobs(seenThrough, clock);

        seeker.LastSeenJobsAt.ShouldBe(seenThrough);
        seeker.LastSeenJobsAt.ShouldNotBe(clock.UtcNow, "the fix: watermark is the seen window, not clock-now");
        // UpdatedAt still tracks the mutation moment (clock-now), not the seen window.
        seeker.UpdatedAt.ShouldBe(clock.UtcNow);
    }

    // A future-dated seenThrough (bad client clock) is clamped to now — it must never push the
    // watermark past reality and silently swallow later ads.
    [Fact]
    public void SetLastSeenJobs_FutureSeenThrough_ClampedToNow()
    {
        var seeker = NewSeeker();
        var clock = Later(3);
        var future = clock.UtcNow.AddHours(1);

        seeker.SetLastSeenJobs(future, clock);

        seeker.LastSeenJobsAt.ShouldBe(clock.UtcNow, "future seenThrough is clamped to now");
    }

    [Fact]
    public void SetLastSeenJobs_WithLaterSeenThrough_Advances()
    {
        var seeker = NewSeeker();
        var first = Later(1);
        seeker.SetLastSeenJobs(first.UtcNow, first);

        var later = Later(3);
        seeker.SetLastSeenJobs(later.UtcNow, later);

        seeker.LastSeenJobsAt.ShouldBe(later.UtcNow);
    }

    [Fact]
    public void SetLastSeenJobs_WithEarlierOrEqualSeenThrough_IsIgnored()
    {
        // Monotonic: a stale seenThrough (e.g. an out-of-order load) must not move the watermark
        // backwards — else the next visit's NY set would inflate (old ads re-flagged new).
        var seeker = NewSeeker();
        var current = Later(3);
        seeker.SetLastSeenJobs(current.UtcNow, current);

        seeker.SetLastSeenJobs(Later(1).UtcNow, current); // earlier seenThrough
        seeker.LastSeenJobsAt.ShouldBe(current.UtcNow);

        seeker.SetLastSeenJobs(current.UtcNow, current); // equal seenThrough
        seeker.LastSeenJobsAt.ShouldBe(current.UtcNow);
    }

    // Equal seenThrough is a STRICT no-op — the <= guard must NOT bump UpdatedAt (kills the
    // <=→< boundary mutant + a mutant that drops the early return before UpdatedAt = now). Uses
    // a LATER clock on the equal call so a mutated `seenThrough < current` would fall through and
    // stamp UpdatedAt = the later now — which this test would then catch. Parity with the sibling
    // SetLastSeenMatches_AtEqualSeenThrough_IsNoOp_AndDoesNotBumpUpdatedAt.
    [Fact]
    public void SetLastSeenJobs_AtEqualSeenThrough_IsNoOp_AndDoesNotBumpUpdatedAt()
    {
        var seeker = NewSeeker();
        var at = Later(3);
        seeker.SetLastSeenJobs(at.UtcNow, at);
        var updatedAfterFirst = seeker.UpdatedAt;

        seeker.SetLastSeenJobs(at.UtcNow, Later(9)); // same watermark value, later clock

        seeker.LastSeenJobsAt.ShouldBe(at.UtcNow);
        seeker.UpdatedAt.ShouldBe(updatedAfterFirst,
            "equal watermark is a no-op — no spurious UpdatedAt churn");
    }

    [Fact]
    public void SetLastSeenJobs_IsIndependentOfLastSeenMatches()
    {
        var seeker = NewSeeker();
        var clock = Later(1);

        seeker.SetLastSeenJobs(clock.UtcNow, clock);

        // The two watermarks are distinct concerns — advancing jobs must NOT touch matches.
        seeker.LastSeenJobsAt.ShouldBe(clock.UtcNow);
        seeker.LastSeenMatchesAt.ShouldBeNull();
    }
}
