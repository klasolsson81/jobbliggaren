using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — the /jobb user-read watermark
/// <see cref="JobSeeker.SetLastSeenJobs"/>. Sibling invariant to
/// <c>SetLastSeenMatches</c>: the watermark is MONOTONIC (a stale/duplicate/out-of-order call
/// never rewinds it), a fresh seeker has never seen jobs (null), and it is independent of the
/// matches watermark. Drives the per-user "Ny = ingested since your last visit" tag.
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
    public void SetLastSeenJobs_FromNull_SetsToNow()
    {
        var seeker = NewSeeker();
        var seenClock = Later(1);

        seeker.SetLastSeenJobs(seenClock);

        seeker.LastSeenJobsAt.ShouldBe(seenClock.UtcNow);
        seeker.UpdatedAt.ShouldBe(seenClock.UtcNow);
    }

    [Fact]
    public void SetLastSeenJobs_WithLaterClock_Advances()
    {
        var seeker = NewSeeker();
        seeker.SetLastSeenJobs(Later(1));

        var laterClock = Later(3);
        seeker.SetLastSeenJobs(laterClock);

        seeker.LastSeenJobsAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SetLastSeenJobs_WithEarlierOrEqualClock_IsIgnored()
    {
        // Monotonic: a stale clock (e.g. an out-of-order load) must not move the watermark
        // backwards — else the next visit's NY set would inflate (old ads re-flagged new).
        var seeker = NewSeeker();
        var current = Later(3);
        seeker.SetLastSeenJobs(current);

        seeker.SetLastSeenJobs(Later(1)); // earlier
        seeker.LastSeenJobsAt.ShouldBe(current.UtcNow);

        seeker.SetLastSeenJobs(Later(3)); // equal
        seeker.LastSeenJobsAt.ShouldBe(current.UtcNow);
    }

    [Fact]
    public void SetLastSeenJobs_IsIndependentOfLastSeenMatches()
    {
        var seeker = NewSeeker();

        seeker.SetLastSeenJobs(Later(1));

        // The two watermarks are distinct concerns — advancing jobs must NOT touch matches.
        seeker.LastSeenJobsAt.ShouldBe(Later(1).UtcNow);
        seeker.LastSeenMatchesAt.ShouldBeNull();
    }
}
