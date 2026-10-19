using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Commands.SetNewCriterionAdsSeen;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries.ListNewCriterionAds;

/// <summary>
/// ADR 0175 D7 (#1663 PR 2) — the branch logic of the Branschbevakning count, page and seen command: the closed
/// states, the cap and its window, the read-time mark's assessed fork and the refusals. SQL translation, the
/// count-equals-page property and the monotone UPDATE are the Testcontainers suite's (<c>CriterionNewAdsEndpointTests</c>):
/// InMemory hides a translation failure and does not run <c>ExecuteUpdate</c>, so nothing here claims either.
/// </summary>
public class CriterionNewAdHandlersTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 3, 55, 0, TimeSpan.Zero);

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ═══ The count

    [Fact]
    public async Task Counts_ReturnEmpty_WhenNoAuthenticatedUser()
    {
        using var db = TestAppDbContextFactory.Create();

        var result = await new GetCriterionNewAdCountsQueryHandler(db, UserWith(null))
            .Handle(new GetCriterionNewAdCountsQuery(), Ct);

        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Counts_GiveANumberOnlyForAScannedCriterion_AndNeverAnotherUsersCriterion()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var scanned = SeedCriterion(db, userId, CompanyWatchCriterionScanOutcome.Scanned);
        var tooBroad = SeedCriterion(db, userId, CompanyWatchCriterionScanOutcome.TooBroad);
        var foreign = SeedCriterion(db, Guid.NewGuid(), CompanyWatchCriterionScanOutcome.Scanned);
        SeedHit(db, scanned, T0.AddHours(1));
        SeedHit(db, scanned, T0.AddHours(2));
        SeedHit(db, foreign, T0.AddHours(1));
        await db.SaveChangesAsync(Ct);

        var result = await new GetCriterionNewAdCountsQueryHandler(db, UserWith(userId))
            .Handle(new GetCriterionNewAdCountsQuery(), Ct);

        result.Items.ShouldBe(
            [new CriterionNewAdCountDto(scanned.Id.Value, 2), new CriterionNewAdCountDto(tooBroad.Id.Value, null)],
            ignoreOrder: true);
    }

    // ═══ The page

    [Fact]
    public async Task Page_IsNull_ForACriterionTheCallerDoesNotOwn_AndTheProbeIsLogged()
    {
        using var db = TestAppDbContextFactory.Create();
        var foreign = SeedCriterion(db, Guid.NewGuid(), CompanyWatchCriterionScanOutcome.Scanned);
        await db.SaveChangesAsync(Ct);
        var caller = Guid.NewGuid();

        var result = await Page(db, caller, foreign.Id);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", foreign.Id.Value, caller, "ListNewCriterionAds");
    }

    [Theory]
    [InlineData(CompanyWatchCriterionScanOutcome.TooBroad, true, false)]
    [InlineData(CompanyWatchCriterionScanOutcome.NotMaterialised, false, true)]
    public async Task Page_NamesAnUnscannedOrRefusedCriterion_AndReadsNoHit(
        CompanyWatchCriterionScanOutcome? outcome, bool tooBroad, bool notMaterialised)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var criterion = SeedCriterion(db, userId, outcome);
        SeedHit(db, criterion, T0.AddHours(1));
        await db.SaveChangesAsync(Ct);

        var result = (await Page(db, userId, criterion.Id))!;

        result.TooBroad.ShouldBe(tooBroad);
        result.NotMaterialised.ShouldBe(notMaterialised);
        result.Rows.ShouldBeEmpty();
        result.AcknowledgedThrough.ShouldBeNull();
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Page_ShowsTheWindowNewestFirst_AcknowledgesItsNewestHit_AndMarksWhatMatches()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var criterion = SeedCriterion(db, userId, CompanyWatchCriterionScanOutcome.Scanned);
        var older = SeedHit(db, criterion, T0.AddHours(1));
        var newer = SeedHit(db, criterion, T0.AddHours(2));
        await db.SaveChangesAsync(Ct);
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(AssessableProfile());
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { newer });

        var result = (await Page(db, userId, criterion.Id))!;

        result.Rows.Select(r => (r.Ad.Id, r.MatchesYou)).ShouldBe([(newer.Value, (bool?)true), (older.Value, false)]);
        result.MatchingAssessed.ShouldBeTrue();
        result.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        result.Truncated.ShouldBeFalse();
    }

    [Fact]
    public async Task Page_CutsAtTheCap_AndAcknowledgesOnlyWhatItShowed()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var criterion = SeedCriterion(db, userId, CompanyWatchCriterionScanOutcome.Scanned);
        for (var i = 0; i <= ListNewCriterionAdsQueryHandler.MaxRows; i++)
            SeedHit(db, criterion, T0.AddMinutes(1 + i));
        await db.SaveChangesAsync(Ct);
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(ProfilelessProfile());

        var result = (await Page(db, userId, criterion.Id))!;

        result.Truncated.ShouldBeTrue();
        result.Rows.Count.ShouldBe(ListNewCriterionAdsQueryHandler.MaxRows);
        result.AcknowledgedThrough.ShouldBe(T0.AddMinutes(ListNewCriterionAdsQueryHandler.MaxRows),
            "the oldest MaxRows are shown, so the newest hit stays above the watermark for the next visit");
        result.MatchingAssessed.ShouldBeFalse();
        result.Rows.ShouldAllBe(r => r.MatchesYou == null);
    }

    // ═══ The seen command's refusals (the UPDATE itself is the Testcontainers suite's)

    [Fact]
    public void Seen_WithoutAToken_IsInvalid()
    {
        var validator = new SetNewCriterionAdsSeenCommandValidator();

        validator.Validate(new SetNewCriterionAdsSeenCommand(Guid.NewGuid(), null)).IsValid.ShouldBeFalse();
        validator.Validate(new SetNewCriterionAdsSeenCommand(Guid.NewGuid(), T0)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Seen_ForACriterionTheCallerDoesNotOwn_IsNotFound_AndTheProbeIsLogged()
    {
        using var db = TestAppDbContextFactory.Create();
        var foreign = SeedCriterion(db, Guid.NewGuid(), CompanyWatchCriterionScanOutcome.Scanned);
        await db.SaveChangesAsync(Ct);
        var caller = Guid.NewGuid();

        var result = await new SetNewCriterionAdsSeenCommandHandler(db, UserWith(caller), _failedAccess, new FixedClock(T0))
            .Handle(new SetNewCriterionAdsSeenCommand(foreign.Id.Value, T0), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        _failedAccess.Received(1).LogCrossUserAttempt(
            "CompanyWatchCriterion", foreign.Id.Value, caller, "SetNewCriterionAdsSeen");
    }

    [Fact]
    public async Task Seen_WithoutAnAuthenticatedUser_Fails()
    {
        using var db = TestAppDbContextFactory.Create();

        var result = await new SetNewCriterionAdsSeenCommandHandler(db, UserWith(null), _failedAccess, new FixedClock(T0))
            .Handle(new SetNewCriterionAdsSeenCommand(Guid.NewGuid(), T0), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    // ─────────────────────────── Seeding through the production writers

    private Task<NewCriterionAdsDto?> Page(AppDbContext db, Guid userId, CompanyWatchCriterionId id) =>
        new ListNewCriterionAdsQueryHandler(db, UserWith(userId), _failedAccess, _profileBuilder, _perUserSearch)
            .Handle(new ListNewCriterionAdsQuery(id.Value), Ct).AsTask();

    private static CompanyWatchCriterion SeedCriterion(
        AppDbContext db, Guid userId, CompanyWatchCriterionScanOutcome? outcome)
    {
        var criterion = CompanyWatchCriterion.Create(
            userId, CompanyWatchCriteriaSpec.Create(["63910"], ["0180"]).Value, null, new FixedClock(T0)).Value;
        if (outcome is { } o)
            criterion.RecordScan(o, T0.AddMinutes(30), new FixedClock(T0.AddMinutes(30)));
        db.CompanyWatchCriteria.Add(criterion);
        return criterion;
    }

    // An Active ad ingested before the hit is stamped, the order the scan's window guarantees.
    private static JobAdId SeedHit(AppDbContext db, CompanyWatchCriterion criterion, DateTimeOffset hitAt)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var ad = JobAd.Import(
            title: "Roll",
            company: Company.Create("Bolag AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: T0,
            expiresAt: T0.AddDays(60),
            clock: new FixedClock(hitAt.AddMinutes(-5)), declaredContacts: [], extractTerms: TestKeywordExtraction.None)
            .Value;
        db.JobAds.Add(ad);
        db.CompanyWatchCriterionAdHits.Add(CompanyWatchCriterionAdHit.Create(criterion, ad.Id, new FixedClock(hitAt)).Value);
        return ad.Id;
    }

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
