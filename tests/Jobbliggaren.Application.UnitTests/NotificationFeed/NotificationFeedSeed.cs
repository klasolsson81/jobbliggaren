using System.Globalization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// Seeds the bell's sources through their aggregates, at chosen instants, so every row is one
/// production writes: a match through <see cref="UserJobAdMatch.Create"/>, a hit through
/// <see cref="FollowedCompanyAdHit.Create"/>, consent through <see cref="JobSeeker.UpdateNotificationConsent"/>,
/// a watermark through the monotonic setters and an archived ad through <see cref="JobAd.Archive"/>.
/// The clock is a substitute pinned per call, since every aggregate stamps <c>clock.UtcNow</c>.
/// </summary>
internal sealed class NotificationFeedSeed
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public NotificationFeedSeed() => _clock.UtcNow.Returns(T0);

    public JobSeeker Seeker(AppDbContext db, Guid userId, bool backgroundMatchConsent)
    {
        _clock.UtcNow.Returns(T0);
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(_clock), _clock).Value;
        if (backgroundMatchConsent)
            seeker.UpdateNotificationConsent(true, _clock);
        db.JobSeekers.Add(seeker);
        db.SaveChanges();
        return seeker;
    }

    public void SetConsent(AppDbContext db, JobSeeker seeker, bool enabled, DateTimeOffset at)
    {
        _clock.UtcNow.Returns(at);
        seeker.UpdateNotificationConsent(enabled, _clock);
        db.SaveChanges();
    }

    public void SeeMatchesThrough(AppDbContext db, JobSeeker seeker, DateTimeOffset through)
    {
        _clock.UtcNow.Returns(through.AddDays(1));
        seeker.SetLastSeenMatches(through, _clock);
        db.SaveChanges();
    }

    public void SeeFollowedAdsThrough(AppDbContext db, JobSeeker seeker, DateTimeOffset through)
    {
        _clock.UtcNow.Returns(through.AddDays(1));
        seeker.SetLastSeenFollowedAds(through, _clock);
        db.SaveChanges();
    }

    public JobAdId ActiveAd(AppDbContext db, string title = "Roll", string company = "Bolag AB")
    {
        _clock.UtcNow.Returns(T0);
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var import = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: T0,
            expiresAt: T0.AddDays(60),
            clock: _clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None);
        import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import must succeed ({import.Error?.Code})");
        db.JobAds.Add(import.Value);
        db.SaveChanges();
        return import.Value.Id;
    }

    public void Archive(AppDbContext db, JobAdId adId)
    {
        _clock.UtcNow.Returns(T0.AddDays(30));
        db.JobAds.Single(j => j.Id == adId).Archive(_clock).IsSuccess.ShouldBeTrue();
        db.SaveChanges();
    }

    public void Match(AppDbContext db, Guid userId, JobAdId adId, NotifiableMatchGrade grade, DateTimeOffset at)
    {
        _clock.UtcNow.Returns(at);
        db.UserJobAdMatches.Add(UserJobAdMatch.Create(userId, adId, grade, ["skill"], _clock).Value);
        db.SaveChanges();
    }

    /// <summary>
    /// The user opened this match in the bell (#2130). Production writes the flag with one set-based UPDATE
    /// (<c>MatchFeedSource</c>'s three marks), which EF InMemory cannot run; the state that leaves behind is this one
    /// boolean, set here through the change tracker. It touches no watermark.
    /// </summary>
    public static void MatchReadInBell(AppDbContext db, Guid userId, JobAdId adId)
    {
        var match = db.UserJobAdMatches.Single(m => m.UserId == userId && m.JobAdId == adId);
        db.Entry(match).Property(m => m.ReadInNotificationFeed).CurrentValue = true;
        db.SaveChanges();
    }

    /// <summary>The user opened this ad's notification in the bell: every hit of the ad is read, as a click reads them.</summary>
    public static void HitsReadInBell(AppDbContext db, Guid userId, JobAdId adId)
    {
        foreach (var hit in db.FollowedCompanyAdHits.Where(h => h.UserId == userId && h.JobAdId == adId))
            hit.MarkReadInNotificationFeed();
        db.SaveChanges();
    }

    /// <summary>The user's X on this ad's notification (#2151): every hit of the ad is dismissed, through the aggregate, as the source does it.</summary>
    public static void HitsDismissed(AppDbContext db, Guid userId, JobAdId adId)
    {
        foreach (var hit in db.FollowedCompanyAdHits.Where(h => h.UserId == userId && h.JobAdId == adId))
            hit.DismissFromNotificationFeed();
        db.SaveChanges();
    }

    /// <summary>
    /// ONE hit of an ad read in the bell. The click reads them all, but the state in between is produced by the
    /// switch-on mark (it flags the hits created after an off-stamp), which can leave an ad's older hit unread beside a
    /// newer one that is read.
    /// </summary>
    public static void HitReadInBell(AppDbContext db, Guid userId, JobAdId adId, CompanyWatchId watchId)
    {
        db.FollowedCompanyAdHits
            .Single(h => h.UserId == userId && h.JobAdId == adId && h.CompanyWatchId == watchId)
            .MarkReadInNotificationFeed();
        db.SaveChanges();
    }

    /// <summary>The user's switch for a type (#2130), through the aggregate's own setter.</summary>
    public void SwitchFeedMatches(AppDbContext db, JobSeeker seeker, bool on, DateTimeOffset at)
    {
        _clock.UtcNow.Returns(at);
        seeker.SetNotificationFeedMatches(on, _clock);
        db.SaveChanges();
    }

    public void SwitchFeedFollowedAds(AppDbContext db, JobSeeker seeker, bool on, DateTimeOffset at)
    {
        _clock.UtcNow.Returns(at);
        seeker.SetNotificationFeedFollowedAds(on, _clock);
        db.SaveChanges();
    }

    public CompanyWatchId Watch(AppDbContext db, Guid userId, bool onlyMatched)
    {
        _clock.UtcNow.Returns(T0);
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, _clock).Value;
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        db.SaveChanges();
        return watch.Id;
    }

    /// <summary>Unfollows a watch the way the user does (soft delete); its hits stay, as in production.</summary>
    public void Unfollow(AppDbContext db, CompanyWatchId watchId)
    {
        _clock.UtcNow.Returns(T0.AddDays(30));
        db.CompanyWatches.Single(w => w.Id == watchId).SoftDelete(_clock);
        db.SaveChanges();
    }

    public void Hit(AppDbContext db, Guid userId, JobAdId adId, CompanyWatchId watchId, DateTimeOffset at)
    {
        _clock.UtcNow.Returns(at);
        db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(userId, adId, watchId, _clock).Value);
        db.SaveChanges();
    }
}
