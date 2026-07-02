using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Companies.Abstractions;
using Jobbliggaren.Application.Companies.Queries.LookupCompany;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Companies.Queries;

/// <summary>
/// #454 (ADR 0088 D4/D5) — the lookup handler. The load-bearing case is the
/// TRANSMISSION-FAIL-CLOSED invariant (§12 merge-blocker 1): a personnummer-shaped org.nr is
/// refused BEFORE the registry port is invoked — never transmitted, never cached, never surfaced,
/// and the refusal payload never echoes the typed value back.
/// </summary>
public class LookupCompanyQueryHandlerTests
{
    private const string LegalEntityOrgNr = "5592804784"; // third digit 9 → legal entity
    private const string PnrShapedOrgNr = "1901012384"; // third digit 0 → personnummer-shaped

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICompanyRegistry _registry = Substitute.For<ICompanyRegistry>();
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public LookupCompanyQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Default: empty-SSYK profile ⇒ not-assessed (MatchingAdCount null, search port untouched) —
        // parity ListCompanyWatchesQueryHandlerTests.
        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(EmptySsykProfile());
    }

    private static FullCandidateMatchProfile EmptySsykProfile() =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    private static FullCandidateMatchProfile ProfileWithSsyk(params string[] ssyk) =>
        new(
            new CandidateMatchProfile(
                Title: "Utvecklare",
                SsykGroupConceptIds: ssyk,
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    private LookupCompanyQueryHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _registry, _profileBuilder, _perUserSearch);

    private void StubFound(string orgNr, string name) =>
        _registry.LookupAsync(
                Arg.Is<OrganizationNumber>(o => o.Value == orgNr), Arg.Any<CancellationToken>())
            .Returns(CompanyRegistryLookup.Found(new CompanyRegistryEntry(orgNr, name)));

    [Fact]
    public async Task Handle_FoundLegalEntity_SurfacesUnmaskedNameAndNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        StubFound(LegalEntityOrgNr, "Testbolaget AB");

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.Status.ShouldBe(CompanyLookupDto.StatusFound);
        dto.IsProtectedIdentity.ShouldBeFalse();
        dto.OrganizationNumber.ShouldBe(LegalEntityOrgNr);
        dto.CompanyName.ShouldBe("Testbolaget AB");
        // No job_ads seeded (InMemory cannot populate the generated organization_number column —
        // the count>0 branch is proven in the Testcontainers suite): the honest 0-ad story.
        dto.ActiveAdCount.ShouldBe(0);
        // Empty-SSYK ⇒ not-assessed null (never a hard 0), search port untouched.
        dto.MatchingAdCount.ShouldBeNull();
        dto.CompanyWatchId.ShouldBeNull();
        await _perUserSearch.DidNotReceiveWithAnyArgs().CountPerUserByEmployerAsync(
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PnrShapedOrgNr_RefusesBeforeAnyTransmission()
    {
        // §12 MERGE-BLOCKER 1 (ADR 0088 D4, security-bound Posture A): the registry port must NEVER
        // be invoked with a personnummer-shaped org.nr — refuse-before-transmit, fail-closed.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new LookupCompanyQuery(PnrShapedOrgNr), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyLookup.ProtectedIdentity");
        // The refusal payload never ECHOES the typed value (a potential personnummer must not be
        // reflected into a response) — security-auditor MUST 2026-07-02.
        result.Error.Message.ShouldNotContain(PnrShapedOrgNr);
        await _registry.DidNotReceiveWithAnyArgs()
            .LookupAsync(Arg.Any<OrganizationNumber>(), Arg.Any<CancellationToken>());
        // Nothing downstream runs either — no profile build, no search, no watch read.
        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Handle_MalformedOrgNr_FailsValidationWithoutTransmission()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new LookupCompanyQuery("123"), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OrganizationNumber.Invalid");
        await _registry.DidNotReceiveWithAnyArgs()
            .LookupAsync(Arg.Any<OrganizationNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_Maps200StatusNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        _registry.LookupAsync(Arg.Any<OrganizationNumber>(), Arg.Any<CancellationToken>())
            .Returns(CompanyRegistryLookup.NotFound);

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CompanyLookupDto.StatusNotFound);
        result.Value.OrganizationNumber.ShouldBeNull();
        result.Value.CompanyName.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_Unavailable_Maps200StatusUnavailable_NeverThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        _registry.LookupAsync(Arg.Any<OrganizationNumber>(), Arg.Any<CancellationToken>())
            .Returns(CompanyRegistryLookup.Unavailable);

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CompanyLookupDto.StatusUnavailable);
    }

    [Fact]
    public async Task Handle_WithStatedOccupation_MapsMatchingCountFromPort()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        StubFound(LegalEntityOrgNr, "Testbolaget AB");
        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ProfileWithSsyk("ssyk-1"));
        _perUserSearch.CountPerUserByEmployerAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == LegalEntityOrgNr),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { [LegalEntityOrgNr] = 3 });

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.Value.MatchingAdCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_WithStatedOccupation_AbsentFromCountMap_IsHardZero()
    {
        // #452 parity: with a stated occupation, an employer absent from the count map is an honest
        // 0 ("no matching ads"), distinct from the not-assessed null.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        StubFound(LegalEntityOrgNr, "Testbolaget AB");
        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ProfileWithSsyk("ssyk-1"));
        _perUserSearch.CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.Value.MatchingAdCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WhenAlreadyFollowing_SurfacesCompanyWatchId()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = CompanyWatch.Follow(
            _userId, OrganizationNumber.Create(LegalEntityOrgNr).Value, _clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        StubFound(LegalEntityOrgNr, "Testbolaget AB");

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.Value.CompanyWatchId.ShouldBe(watch.Id.Value);
    }

    [Fact]
    public async Task Handle_AnotherUsersWatch_IsNeverSurfaced()
    {
        // Owner-scoping: someone ELSE following the company must not light "bevakar redan" for me
        // (no cross-user surface, ADR 0087 D8).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        db.CompanyWatches.Add(CompanyWatch.Follow(
            Guid.NewGuid(), OrganizationNumber.Create(LegalEntityOrgNr).Value, _clock).Value);
        await db.SaveChangesAsync(ct);
        StubFound(LegalEntityOrgNr, "Testbolaget AB");

        var result = await Handler(db).Handle(new LookupCompanyQuery(LegalEntityOrgNr), ct);

        result.Value.CompanyWatchId.ShouldBeNull();
    }
}
