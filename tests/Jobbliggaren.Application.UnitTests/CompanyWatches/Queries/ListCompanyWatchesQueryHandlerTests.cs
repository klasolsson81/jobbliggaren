using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatches;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

public class ListCompanyWatchesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    // #452 — the handler now injects an IMatchProfileBuilder (BuildFullForSortAsync) + an
    // IPerUserJobAdSearchQuery (CountPerUserByEmployerAsync). The pre-#452 tests seed no job_ads on
    // an InMemory context (which cannot populate the generated organization_number shadow column, so
    // the count>0 branch is proven only in the Testcontainers suite). Default the profile builder to
    // an EMPTY-SSYK Fast profile so MatchingAdCount is the honest not-assessed null and the search
    // port is NEVER touched (parity GetMyMatchCountQueryHandler's no-occupation gate). The
    // matching-count wiring itself is exercised by the WITH-SSYK tests further down.
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public ListCompanyWatchesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Empty-SSYK profile ⇒ not-assessed path (MatchingAdCount == null, port never called).
        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(EmptySsykProfile());
    }

    // The frozen F4-5 Fast profile embedded in a FullCandidateMatchProfile. Empty SSYK = the
    // handler's not-assessed gate (profile.Fast.SsykGroupConceptIds.Count == 0).
    private static FullCandidateMatchProfile EmptySsykProfile() =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    // A profile WITH a stated occupation ⇒ the handler calls perUserSearch.CountPerUserByEmployerAsync.
    private static FullCandidateMatchProfile ProfileWithSsyk(params string[] ssyk) =>
        new(
            new CandidateMatchProfile(
                Title: "Utvecklare",
                SsykGroupConceptIds: ssyk,
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    private ListCompanyWatchesQueryHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _profileBuilder, _perUserSearch);

    private void Add(Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, string orgNr)
        => db.CompanyWatches.Add(
            CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, _clock).Value);

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        var perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

        var result = await new ListCompanyWatchesQueryHandler(db, anon, profileBuilder, perUserSearch)
            .Handle(new ListCompanyWatchesQuery(), ct);

        result.ShouldBeEmpty();
        // Anon short-circuits BEFORE any profile build or search (no per-user surface at all).
        // DidNotReceiveWithAnyArgs ignores the argument matchers — Arg.Any placeholders keep the
        // xUnit1051 analyzer happy (no bare `default` CancellationToken).
        await profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await perUserSearch.DidNotReceiveWithAnyArgs()
            .CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForLegalEntityOrgNumber_SurfacesFullNumberUnflagged()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "5592804784"); // third digit 9 → legal entity
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        var dto = result.ShouldHaveSingleItem();
        dto.IsProtectedIdentity.ShouldBeFalse();
        dto.OrganizationNumber.ShouldBe("5592804784");
        dto.FollowedAt.ShouldBe(_clock.UtcNow);
        // #447 — no job_ads seeded → count is 0 (InMemory cannot populate the generated
        // organization_number column, so the count>0 branch is proven in the Testcontainers suite).
        dto.ActiveAdCount.ShouldBe(0);
        // #452 — empty-SSYK profile ⇒ not-assessed: MatchingAdCount is the honest null (never a
        // hard 0), and the search port is never touched.
        dto.MatchingAdCount.ShouldBeNull();
        await _perUserSearch.DidNotReceiveWithAnyArgs()
            .CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForSoleProprietor_SurfacesActiveAdCount_EvenWhenOrgNumberMasked()
    {
        // #447 + D8(c) — the active-ad count is PUBLIC data, surfaced even when the org.nr is masked.
        // Here no ads are seeded (InMemory) so the count is 0; the point is that the field is present
        // and independent of the personnummer mask (the count>0 branch is a Testcontainers oracle).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "9001011234"); // personnummer-shaped
        await db.SaveChangesAsync(ct);

        var dto = (await Handler(db).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.IsProtectedIdentity.ShouldBeTrue();
        dto.OrganizationNumber.ShouldBeNull();
        dto.ActiveAdCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ForSoleProprietorPersonnummerShapedOrgNumber_MasksAndFlags()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "9001011234"); // YYMMDD 900101 → personnummer-shaped (third digit 0)
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        var dto = result.ShouldHaveSingleItem();
        dto.IsProtectedIdentity.ShouldBeTrue();
        dto.OrganizationNumber.ShouldBeNull(); // raw personnummer-shaped value NEVER surfaced (D8(c))
    }

    [Fact]
    public async Task Handle_ExcludesSoftDeletedAndOtherUsersWatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var mineActive = CompanyWatch.Follow(_userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        var mineUnfollowed = CompanyWatch.Follow(_userId, OrganizationNumber.Create("2120000142").Value, _clock).Value;
        mineUnfollowed.SoftDelete(_clock);
        var otherUsers = CompanyWatch.Follow(Guid.NewGuid(), OrganizationNumber.Create("9696000003").Value, _clock).Value;
        db.CompanyWatches.AddRange(mineActive, mineUnfollowed, otherUsers);
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        result.ShouldHaveSingleItem().Id.ShouldBe(mineActive.Id.Value);
    }

    // ── #452 matching-count wiring (the search port is stubbed; the SQL correctness of
    //    CountPerUserByEmployerAsync itself is proven by the Testcontainers oracle/integration
    //    tests — here we pin only the handler's dict→DTO mapping + gate + call arguments) ──────────

    [Fact]
    public async Task Handle_WhenProfileHasSsyk_MapsMatchingAdCountFromSearchDict()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "5592804784"); // legal entity — present in the dict
        await db.SaveChangesAsync(ct);

        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ProfileWithSsyk("1234"));
        _perUserSearch
            .CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["5592804784"] = 3 });

        var dto = (await Handler(db).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.MatchingAdCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_WhenProfileHasSsyk_MatchingAdCountIsZero_WhenOrgNumberAbsentFromDict()
    {
        // An assessed employer with NO matching ad is a real 0, NOT null (null = not-assessed only).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "5592804784"); // matches — in dict
        Add(db, _userId, "2120000142"); // no matching ad — absent from dict
        await db.SaveChangesAsync(ct);

        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ProfileWithSsyk("1234"));
        _perUserSearch
            .CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["5592804784"] = 3 });

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        result.Single(d => d.OrganizationNumber == "5592804784").MatchingAdCount.ShouldBe(3);
        var absent = result.Single(d => d.OrganizationNumber == "2120000142");
        absent.MatchingAdCount.ShouldNotBeNull(); // assessed
        absent.MatchingAdCount.ShouldBe(0);       // ...and honestly zero
    }

    [Fact]
    public async Task Handle_WhenProfileHasSsyk_CallsSearchWithGoodAndStrongGradesAndWatchedOrgNumbers()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "5592804784");
        Add(db, _userId, "2120000142");
        await db.SaveChangesAsync(ct);

        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ProfileWithSsyk("1234"));
        _perUserSearch
            .CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        await _perUserSearch.Received(1).CountPerUserByEmployerAsync(
            // >= Good band exactly (Good + Strong; Top is not Fast-computable, Basic/Related are below).
            Arg.Is<IReadOnlyList<string>>(orgNrs =>
                orgNrs.Count == 2
                && orgNrs.Contains("5592804784")
                && orgNrs.Contains("2120000142")),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyList<MatchGrade>>(g =>
                g.Count == 2
                && g.Contains(MatchGrade.Good)
                && g.Contains(MatchGrade.Strong)),
            Arg.Any<CancellationToken>());
    }
}
