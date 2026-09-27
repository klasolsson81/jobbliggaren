using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// ADR 0146 — <see cref="DigestDispatchJob"/> reads each pass's consent again before it claims a
/// user's rows, and the follow pass also before it builds a CV-derived profile, against REAL
/// Postgres. The due set is read once at the start of a pass, so a consent that ends after it must
/// still stop that user: no claim, no email, the rows left Pending.
/// <para>
/// Two seekers share each test, and the consent of whichever is reached second is ended while the
/// first is dispatched, so the order of the due set does not matter. The consent is ended by the
/// aggregate's own methods in a context of their own: a withdrawal, or the soft delete that
/// <c>DeleteAccountCommandHandler</c> performs. A soft-deleted account keeps its Identity user
/// until the hard delete, so the address still resolves and only the JobSeeker query filter can
/// stop the send.
/// </para>
/// </summary>
[Collection("Worker")]
public class DigestDispatchJobConsentRecheckTests(WorkerTestFixture fixture)
{
    public enum ConsentEnd
    {
        Withdrawal,
        SoftDelete,
    }

    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ConsentEnd.Withdrawal)]
    [InlineData(ConsentEnd.SoftDelete)]
    public async Task RunAsync_MatchPass_WhenConsentEndsAfterTheDueSet_ThatUserIsNeitherClaimedNorEmailed(
        ConsentEnd end)
    {
        var ct = TestContext.Current.CancellationToken;
        var adId = await SeedActiveAdAsync(ct);
        var a = await SeedSeekerAsync(matchConsent: true, followConsent: false, ct);
        var b = await SeedSeekerAsync(matchConsent: true, followConsent: false, ct);
        await SeedStrongPendingMatchAsync(a.UserId, adId, ct);
        await SeedStrongPendingMatchAsync(b.UserId, adId, ct);

        var pair = new FirstOfThePair(a, b, other => EndConsent(
            other.UserId, end, seeker => seeker.UpdateNotificationConsent(enabled: false, new FixedClock(Now))));
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                pair.Reached(ci.ArgAt<string>(0));
                return Task.CompletedTask;
            });

        await RunJobAsync(emailSender, profileBuilder: null, ct);

        var (first, other) = pair.Resolved();
        await emailSender.Received(1).SendMatchNotificationEmailAsync(
            first.Email, Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendMatchNotificationEmailAsync(
            other.Email, Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
        (await MatchStatusAsync(first.UserId, adId, ct)).ShouldBe(NotificationStatus.Sent);
        (await MatchStatusAsync(other.UserId, adId, ct)).ShouldBe(NotificationStatus.Pending);

        var stored = await ReadSeekerIgnoringTheQueryFilterAsync(other.UserId, ct);
        if (end == ConsentEnd.Withdrawal)
        {
            stored.Preferences.NotificationConsentWithdrawnAt.ShouldNotBeNull();
        }
        else
        {
            // Only the query filter excludes this row: its consent still stands.
            stored.DeletedAt.ShouldNotBeNull();
            stored.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
            stored.Preferences.NotificationConsentWithdrawnAt.ShouldBeNull();
        }
    }

    // The consent ends inside the follow pass, after its due-set query. Ending it during the match
    // pass would take the user out of the follow due set before the pass began.
    [Theory]
    [InlineData(ConsentEnd.Withdrawal)]
    [InlineData(ConsentEnd.SoftDelete)]
    public async Task RunAsync_FollowPass_WhenConsentEndsAfterTheDueSet_ThatUserIsNeitherClaimedNorEmailed(
        ConsentEnd end)
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await SeedSeekerAsync(matchConsent: false, followConsent: true, ct);
        var b = await SeedSeekerAsync(matchConsent: false, followConsent: true, ct);
        var hitA = await SeedFollowHitAsync(a.UserId, onlyMatched: false, ct);
        var hitB = await SeedFollowHitAsync(b.UserId, onlyMatched: false, ct);

        var pair = new FirstOfThePair(a, b, other => EndConsent(
            other.UserId,
            end,
            seeker => seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, new FixedClock(Now))));
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendFollowedCompanyNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                pair.Reached(ci.ArgAt<string>(0));
                return Task.CompletedTask;
            });

        await RunJobAsync(emailSender, profileBuilder: null, ct);

        var (first, other) = pair.Resolved();
        await emailSender.Received(1).SendFollowedCompanyNotificationEmailAsync(
            first.Email, Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendFollowedCompanyNotificationEmailAsync(
            other.Email, Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>());
        (await HitStatusAsync(first == a ? hitA : hitB, ct)).ShouldBe(FollowedCompanyAdHitStatus.Sent);
        (await HitStatusAsync(other == a ? hitA : hitB, ct)).ShouldBe(FollowedCompanyAdHitStatus.Pending);

        var stored = await ReadSeekerIgnoringTheQueryFilterAsync(other.UserId, ct);
        if (end == ConsentEnd.Withdrawal)
        {
            stored.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldNotBeNull();
        }
        else
        {
            // Only the query filter excludes this row: its consent still stands.
            stored.DeletedAt.ShouldNotBeNull();
            stored.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
            stored.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
        }
    }

    // An OnlyMatched watch makes the follow pass build the user's CV-derived profile to grade the
    // hits. A withdrawal committed after the due set must stop that user before the build, not only
    // before the claim.
    [Fact]
    public async Task RunAsync_FollowPass_WhenConsentIsWithdrawnAfterTheDueSet_ThatUsersProfileIsNeverBuilt()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = await SeedSeekerAsync(matchConsent: false, followConsent: true, ct);
        var b = await SeedSeekerAsync(matchConsent: false, followConsent: true, ct);
        var hitA = await SeedFollowHitAsync(a.UserId, onlyMatched: true, ct);
        var hitB = await SeedFollowHitAsync(b.UserId, onlyMatched: true, ct);

        var pair = new FirstOfThePair(a, b, other => EndConsent(
            other.UserId,
            ConsentEnd.Withdrawal,
            seeker => seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, new FixedClock(Now))));
        RecordingProfileBuilder? profileBuilder = null;
        var emailSender = Substitute.For<IEmailSender>();

        await RunJobAsync(
            emailSender,
            real => profileBuilder = new RecordingProfileBuilder(real, pair.Reached),
            ct);

        var (first, other) = pair.Resolved();
        profileBuilder.ShouldNotBeNull();
        profileBuilder.BuiltFor.Where(id => id == a.UserId || id == b.UserId).ShouldBe([first.UserId]);
        await emailSender.Received(1).SendFollowedCompanyNotificationEmailAsync(
            first.Email, Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendFollowedCompanyNotificationEmailAsync(
            other.Email, Arg.Any<FollowedCompanyNotificationEmail>(), Arg.Any<CancellationToken>());
        (await HitStatusAsync(other == a ? hitA : hitB, ct)).ShouldBe(FollowedCompanyAdHitStatus.Pending);
    }

    // ─────────────────────────── SUT ───────────────────────────

    private async Task RunJobAsync(
        IEmailSender emailSender,
        Func<IMatchProfileBuilder, IMatchProfileBuilder>? profileBuilder,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var realBuilder = sp.GetRequiredService<IMatchProfileBuilder>();
        var job = new DigestDispatchJob(
            sp.GetRequiredService<IAppDbContext>(),
            emailSender,
            sp.GetRequiredService<IUserAccountService>(),
            profileBuilder is null ? realBuilder : profileBuilder(realBuilder),
            sp.GetRequiredService<IPerUserJobAdSearchQuery>(),
            new FixedClock(Now),
            sp.GetRequiredService<IOptions<DigestDispatchOptions>>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<DigestDispatchJob>());

        await job.RunAsync(DigestCadence.Weekly, ct);
    }

    private sealed record Seeker(Guid UserId, string Email);

    // The first of the pair the job reaches, by address or by user id; the other one's consent is
    // ended then.
    private sealed class FirstOfThePair(Seeker a, Seeker b, Action<Seeker> endConsent)
    {
        private Seeker? _first;

        public void Reached(string email) => Reached(
            email == a.Email ? a.UserId : email == b.Email ? b.UserId : Guid.Empty);

        public void Reached(Guid userId)
        {
            if (_first is not null || (userId != a.UserId && userId != b.UserId))
                return;

            _first = userId == a.UserId ? a : b;
            endConsent(_first == a ? b : a);
        }

        public (Seeker First, Seeker Other) Resolved()
        {
            _first.ShouldNotBeNull("the job must reach one of the pair");
            return (_first, _first == a ? b : a);
        }
    }

    private sealed class RecordingProfileBuilder(IMatchProfileBuilder inner, Action<Guid> onBuild)
        : IMatchProfileBuilder
    {
        public List<Guid> BuiltFor { get; } = [];

        public ValueTask<FullCandidateMatchProfile> BuildFullForUserIdAsync(
            Guid userId, CancellationToken cancellationToken)
        {
            BuiltFor.Add(userId);
            onBuild(userId);
            return inner.BuildFullForUserIdAsync(userId, cancellationToken);
        }

        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(
            CancellationToken cancellationToken, bool includeRelated = false) =>
            inner.BuildFromPreferencesAsync(cancellationToken, includeRelated);

        public ValueTask<FullCandidateMatchProfile> BuildFullForSortAsync(
            CancellationToken cancellationToken, bool includeRelated = false) =>
            inner.BuildFullForSortAsync(cancellationToken, includeRelated);

        public ValueTask<FullCandidateMatchProfile> BuildFullForVerdictAsync(
            CancellationToken cancellationToken, bool includeRelated = false) =>
            inner.BuildFullForVerdictAsync(cancellationToken, includeRelated);

        public ValueTask<bool> GetPreferredRemoteForNotificationCountAsync(
            CancellationToken cancellationToken) =>
            inner.GetPreferredRemoteForNotificationCountAsync(cancellationToken);
    }

    // ─────────────────────────── The consent's end ───────────────────────────

    // Synchronous because the seams that run it are: a substitute's callback and the builder hook.
    private void EndConsent(Guid userId, ConsentEnd end, Action<JobSeeker> withdraw)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = db.JobSeekers.Single(js => js.UserId == userId);
        if (end == ConsentEnd.Withdrawal)
            withdraw(seeker);
        else
            seeker.SoftDelete(new FixedClock(Now));
        db.SaveChanges();
    }

    // ─────────────────────────── Seeding + read-back ───────────────────────────

    private async Task<Seeker> SeedSeekerAsync(bool matchConsent, bool followConsent, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var email = $"digest-recheck-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();

        var seeker = JobSeeker.Register(user.Id, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        if (matchConsent)
            seeker.UpdateNotificationConsent(enabled: true, clock);
        if (followConsent)
            seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return new Seeker(user.Id, email);
    }

    private async Task<JobAdId> SeedActiveAdAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobAd = NewActiveAd($"digest-recheck-{Guid.NewGuid():N}", orgNr: null);
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task SeedStrongPendingMatchAsync(Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserJobAdMatches.Add(UserJobAdMatch.Create(
            userId, jobAdId, NotifiableMatchGrade.Strong, [], new FixedClock(Now)).Value);
        await db.SaveChangesAsync(ct);
    }

    // A followed company with one Active ad and a Pending hit on it.
    private async Task<(Guid UserId, JobAdId JobAdId, CompanyWatchId WatchId)> SeedFollowHitAsync(
        Guid userId, bool onlyMatched, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);

        var jobAd = NewActiveAd($"digest-recheck-{Guid.NewGuid():N}", orgNr);
        db.JobAds.Add(jobAd);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        if (onlyMatched)
        {
            var filter = WatchFilterSpec.Create(municipalities: null, regions: null, onlyMatched: true).Value;
            watch.SetFilter(filter).IsSuccess.ShouldBeTrue();
        }

        db.CompanyWatches.Add(watch);
        db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(userId, jobAd.Id, watch.Id, clock).Value);
        await db.SaveChangesAsync(ct);
        return (userId, jobAd.Id, watch.Id);
    }

    private static JobAd NewActiveAd(string externalId, string? orgNr)
    {
        var employer = orgNr is null
            ? ""
            : $",\"employer\":{{\"name\":\"Acme AB\",\"organization_number\":\"{orgNr}\"}}";
        var rawPayload = $"{{\"id\":\"{externalId}\"{employer}}}";
        return JobAd.Import(
            title: "Omkontroll-annons",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: new FixedClock(Now), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
    }

    private async Task<NotificationStatus> MatchStatusAsync(Guid userId, JobAdId jobAdId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var match = await db.UserJobAdMatches.AsNoTracking()
            .SingleAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);
        return match.NotificationStatus;
    }

    private async Task<FollowedCompanyAdHitStatus> HitStatusAsync(
        (Guid UserId, JobAdId JobAdId, CompanyWatchId WatchId) hit, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.FollowedCompanyAdHits.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(
                h => h.UserId == hit.UserId && h.JobAdId == hit.JobAdId && h.CompanyWatchId == hit.WatchId, ct);
        return stored.NotificationStatus;
    }

    private async Task<JobSeeker> ReadSeekerIgnoringTheQueryFilterAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(js => js.UserId == userId, ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
