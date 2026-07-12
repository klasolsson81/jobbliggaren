using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — the company-follow USER-read watermark
/// <see cref="JobSeeker.SetLastSeenFollowedAds"/>. Sibling invariant to <c>SetLastSeenJobs</c> /
/// <c>SetLastSeenMatches</c>: the watermark advances to the SEEN-THROUGH window (never past it),
/// it is MONOTONIC (a stale/duplicate/out-of-order call never rewinds it), a future-dated
/// seenThrough is CLAMPED to now (a bad client clock can never run the watermark past reality), a
/// fresh seeker has never seen the follow rail (null), and it is INDEPENDENT of both the matches
/// watermark and the SYSTEM follow-scan mark (<c>LastCompanyWatchScanAt</c>). Drives the Översikt
/// "nya annonser från bevakade företag"-count.
/// </summary>
public class JobSeekerLastSeenFollowedAdsTests
{
    private static readonly FakeDateTimeProvider BaseClock = FakeDateTimeProvider.Default;

    private static JobSeeker NewSeeker() =>
        JobSeeker.Register(Guid.NewGuid(), "Klas Olsson", BaseClock).Value;

    private static FakeDateTimeProvider Later(int hours) =>
        FakeDateTimeProvider.At(BaseClock.UtcNow.AddHours(hours));

    [Fact]
    public void FreshSeeker_ShouldHaveNullLastSeenFollowedAds()
    {
        var seeker = NewSeeker();

        seeker.LastSeenFollowedAdsAt.ShouldBeNull();
    }

    [Fact]
    public void SetLastSeenFollowedAds_FromNull_SetsToSeenThrough()
    {
        var seeker = NewSeeker();
        var clock = Later(3);
        var seenThrough = clock.UtcNow;

        seeker.SetLastSeenFollowedAds(seenThrough, clock);

        seeker.LastSeenFollowedAdsAt.ShouldBe(seenThrough);
        seeker.UpdatedAt.ShouldBe(clock.UtcNow);
    }

    // The watermark is the SEEN window (an older seenThrough than clock-now), NOT clock-now — so a
    // hit created between seenThrough and this call has CreatedAt > watermark and stays flagged new
    // (parity #477 Low 4 / #759 for the /jobb rail).
    [Fact]
    public void SetLastSeenFollowedAds_SetsToSeenThrough_NotClockNow()
    {
        var seeker = NewSeeker();
        var clock = Later(3);
        var seenThrough = clock.UtcNow.AddMinutes(-5);

        seeker.SetLastSeenFollowedAds(seenThrough, clock);

        seeker.LastSeenFollowedAdsAt.ShouldBe(seenThrough);
        seeker.LastSeenFollowedAdsAt.ShouldNotBe(clock.UtcNow, "the watermark is the seen window, not clock-now");
        seeker.UpdatedAt.ShouldBe(clock.UtcNow);
    }

    // A future-dated seenThrough (bad client clock) is clamped to now — it must never push the
    // watermark past reality and silently swallow later hits.
    [Fact]
    public void SetLastSeenFollowedAds_FutureSeenThrough_ClampedToNow()
    {
        var seeker = NewSeeker();
        var clock = Later(3);
        var future = clock.UtcNow.AddHours(1);

        seeker.SetLastSeenFollowedAds(future, clock);

        seeker.LastSeenFollowedAdsAt.ShouldBe(clock.UtcNow, "future seenThrough is clamped to now");
    }

    [Fact]
    public void SetLastSeenFollowedAds_WithLaterSeenThrough_Advances()
    {
        var seeker = NewSeeker();
        var first = Later(1);
        seeker.SetLastSeenFollowedAds(first.UtcNow, first);

        var later = Later(3);
        seeker.SetLastSeenFollowedAds(later.UtcNow, later);

        seeker.LastSeenFollowedAdsAt.ShouldBe(later.UtcNow);
    }

    [Fact]
    public void SetLastSeenFollowedAds_WithEarlierOrEqualSeenThrough_IsIgnored()
    {
        // Monotonic: a stale seenThrough (out-of-order load) must not move the watermark backwards —
        // else the next visit's new-set would inflate (already-seen hits re-flagged new).
        var seeker = NewSeeker();
        var current = Later(3);
        seeker.SetLastSeenFollowedAds(current.UtcNow, current);

        seeker.SetLastSeenFollowedAds(Later(1).UtcNow, current); // earlier seenThrough
        seeker.LastSeenFollowedAdsAt.ShouldBe(current.UtcNow);

        seeker.SetLastSeenFollowedAds(current.UtcNow, current); // equal seenThrough
        seeker.LastSeenFollowedAdsAt.ShouldBe(current.UtcNow);
    }

    // Equal seenThrough is a STRICT no-op — the <= guard must NOT bump UpdatedAt (kills the <=→<
    // boundary mutant + a mutant that drops the early return before UpdatedAt = now). Uses a LATER
    // clock on the equal call so a mutated `seenThrough < current` would fall through and stamp
    // UpdatedAt = the later now — which this test would then catch. Parity
    // SetLastSeenJobs_AtEqualSeenThrough_IsNoOp_AndDoesNotBumpUpdatedAt.
    [Fact]
    public void SetLastSeenFollowedAds_AtEqualSeenThrough_IsNoOp_AndDoesNotBumpUpdatedAt()
    {
        var seeker = NewSeeker();
        var at = Later(3);
        seeker.SetLastSeenFollowedAds(at.UtcNow, at);
        var updatedAfterFirst = seeker.UpdatedAt;

        seeker.SetLastSeenFollowedAds(at.UtcNow, Later(9)); // same watermark value, later clock

        seeker.LastSeenFollowedAdsAt.ShouldBe(at.UtcNow);
        seeker.UpdatedAt.ShouldBe(updatedAfterFirst,
            "equal watermark is a no-op — no spurious UpdatedAt churn");
    }

    // The follow USER-read watermark is a DISTINCT concern from the SYSTEM follow-scan mark
    // (LastCompanyWatchScanAt) and from the matches watermark — advancing it touches neither.
    [Fact]
    public void SetLastSeenFollowedAds_IsIndependentOfScanMarkAndMatches()
    {
        var seeker = NewSeeker();
        var clock = Later(1);

        seeker.SetLastSeenFollowedAds(clock.UtcNow, clock);

        seeker.LastSeenFollowedAdsAt.ShouldBe(clock.UtcNow);
        seeker.LastCompanyWatchScanAt.ShouldBeNull("the user-read mark is distinct from the system scan mark");
        seeker.LastSeenMatchesAt.ShouldBeNull();
        seeker.LastSeenJobsAt.ShouldBeNull();
    }
}
