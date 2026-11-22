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
            clock: _clock, declaredContacts: [], applicationUrl: null, extractTerms: TestKeywordExtraction.None);
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

    // ── Branschbevakning (#1663 PR 3) ───────────────────────────────────────────────────────

    /// <summary>
    /// A Branschbevakning created at <see cref="T0"/> whose last scan answered <paramref name="outcome"/> at T0 + 1 day,
    /// through the aggregate (<see cref="CompanyWatchCriterion.Create"/>, then <see cref="CompanyWatchCriterion.RecordScan"/>),
    /// so <c>NewCriterionAdSet.NewHits</c> admits its hits written after T0 when the outcome is Scanned.
    /// </summary>
    public CompanyWatchCriterionId Criterion(
        AppDbContext db, Guid userId, CompanyWatchCriterionScanOutcome? outcome = CompanyWatchCriterionScanOutcome.Scanned)
    {
        _clock.UtcNow.Returns(T0);
        var criterion = CompanyWatchCriterion.Create(
            userId, CompanyWatchCriteriaSpec.Create(["63910"], ["0180"]).Value, null, _clock).Value;
        if (outcome is { } answered)
        {
            _clock.UtcNow.Returns(T0.AddDays(1));
            criterion.RecordScan(answered, T0.AddDays(1), _clock);
        }

        db.CompanyWatchCriteria.Add(criterion);
        db.SaveChanges();
        return criterion.Id;
    }

    /// <summary>
    /// ONE criterion's hit of an ad read in the bell. A click reads every hit of the ad, but the state in between is
    /// produced by the switch-on backlog mark (<c>CompanyWatchCriterionAdFeedSource.MarkBacklogAsync</c>), which flags the
    /// hits created after an off-stamp and can leave an older hit of the same ad unread. InMemory cannot run that
    /// statement, so the flag is set through the change tracker.
    /// </summary>
    public static void CriterionHitReadInBell(AppDbContext db, CompanyWatchCriterionId criterionId, JobAdId adId)
    {
        var hit = db.CompanyWatchCriterionAdHits.Single(h => h.CompanyWatchCriterionId == criterionId && h.JobAdId == adId);
        db.Entry(hit).Property(h => h.ReadInNotificationFeed).CurrentValue = true;
        db.SaveChanges();
    }

    /// <summary>A hit of <paramref name="adId"/> under the criterion, through <see cref="CompanyWatchCriterionAdHit.Create"/>, as the scan writes it.</summary>
    public void CriterionHit(AppDbContext db, CompanyWatchCriterionId criterionId, JobAdId adId, DateTimeOffset at)
    {
        var criterion = db.CompanyWatchCriteria.Single(c => c.Id == criterionId);
        _clock.UtcNow.Returns(at);
        db.CompanyWatchCriterionAdHits.Add(CompanyWatchCriterionAdHit.Create(criterion, adId, _clock).Value);
        db.SaveChanges();
    }

    /// <summary>
    /// The user opened this ad's notification in the bell: every Branschbevakning hit of the ad is read. Production writes
    /// it with one set-based UPDATE (<c>CompanyWatchCriterionAdFeedSource.MarkRowAsync</c>), which EF InMemory cannot run;
    /// the state that leaves behind is this flag, set here through the change tracker.
    /// </summary>
    public static void CriterionHitsReadInBell(AppDbContext db, Guid userId, JobAdId adId)
    {
        foreach (var hit in db.CompanyWatchCriterionAdHits.Where(h => h.UserId == userId && h.JobAdId == adId))
            db.Entry(hit).Property(h => h.ReadInNotificationFeed).CurrentValue = true;
        db.SaveChanges();
    }

    /// <summary>
    /// The user's X on this ad's notification: every Branschbevakning hit of the ad is dismissed and read, as
    /// <c>CompanyWatchCriterionAdFeedSource.DismissRowAsync</c>'s one statement leaves it; InMemory cannot run it.
    /// </summary>
    public static void CriterionHitsDismissed(AppDbContext db, Guid userId, JobAdId adId)
    {
        foreach (var hit in db.CompanyWatchCriterionAdHits.Where(h => h.UserId == userId && h.JobAdId == adId))
        {
            db.Entry(hit).Property(h => h.ReadInNotificationFeed).CurrentValue = true;
            db.Entry(hit).Property(h => h.DismissedFromNotificationFeed).CurrentValue = true;
        }

        db.SaveChanges();
    }

    /// <summary>
    /// The user's visit to the criterion's page of new ads acknowledged its hits through <paramref name="through"/>.
    /// Production writes it with the seen command's conditional UPDATE (<c>SetNewCriterionAdsSeenCommandHandler</c>),
    /// which InMemory cannot run; the state it leaves is this column.
    /// </summary>
    public static void SeeCriterionNewAdsThrough(AppDbContext db, CompanyWatchCriterionId criterionId, DateTimeOffset through)
    {
        var criterion = db.CompanyWatchCriteria.Single(c => c.Id == criterionId);
        db.Entry(criterion).Property(c => c.NewAdsSeenThrough).CurrentValue = through;
        db.SaveChanges();
    }

    public void SwitchFeedCriterionAds(AppDbContext db, JobSeeker seeker, bool on, DateTimeOffset at)
    {
        _clock.UtcNow.Returns(at);
        seeker.SetNotificationFeedCriterionAds(on, _clock);
        db.SaveChanges();
    }
}
