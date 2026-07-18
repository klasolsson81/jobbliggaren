using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
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
    private readonly IProtectedIdentityTokenizer _tokenizer = Substitute.For<IProtectedIdentityTokenizer>();

    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public ListCompanyWatchesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        // Deterministic 64-char token (distinct from plaintext). Only invoked when a pnr-shaped watch
        // is present; these tests seed AB org.nrs, so it stays inert unless a test opts in.
        _tokenizer.Tokenize(Arg.Any<string>()).Returns(ci => "hmac" + ci.Arg<string>().PadLeft(60, '0'));
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

    // Empty catalogue by default; a group test passes a synthetic one.
    private readonly StubBrandGroupProvider _brandGroups = StubProvider();

    private ListCompanyWatchesQueryHandler Handler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, IBrandGroupProvider? brandGroups = null) =>
        new(db, _currentUser, _profileBuilder, _perUserSearch, _tokenizer, brandGroups ?? _brandGroups);

    private static StubBrandGroupProvider StubProvider(
        params (string Slug, string DisplayName, string[] Members)[] groups)
    {
        var dict = groups.ToDictionary(
            g => g.Slug, g => new BrandGroup(g.Slug, g.DisplayName, g.Members), StringComparer.Ordinal);
        return new StubBrandGroupProvider(new BrandGroupCatalog("test.v1", dict));
    }

    private sealed class StubBrandGroupProvider(BrandGroupCatalog catalog) : IBrandGroupProvider
    {
        public BrandGroupCatalog Catalog { get; } = catalog;
    }

    private void Add(Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, string orgNr)
        => db.CompanyWatches.Add(
            CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, _clock).Value);

    private void AddGroup(Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, string slug)
        => db.CompanyWatches.Add(
            CompanyWatch.FollowBrandGroup(userId, BrandGroupId.Create(slug).Value, _clock).Value);

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        var perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

        var result = await new ListCompanyWatchesQueryHandler(
                db, anon, profileBuilder, perUserSearch, Substitute.For<IProtectedIdentityTokenizer>(),
                _brandGroups)
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

    // ── F4b (#803) — the per-watch filter projection. The FE needs it for TWO things: to pre-fill the
    //    editor, and to render the RESTING-state disclosure (BC-9′) that is the only surface able to
    //    tell the user their notifications are narrowed when no digest email is sent at all. A dropped
    //    or re-homed axis here is therefore not a cosmetic bug — it is a silent-narrowing failure. ──

    [Fact]
    public async Task Handle_WhenWatchHasFilter_ProjectsBothGeoAxesAndOnlyMatched()
    {
        // The two axes are DISJOINT JobTech namespaces. A whole-län pick lives in Regions as ONE län
        // concept-id and must reach the DTO unexpanded and un-swapped — expanding it into the län's
        // kommuner (or crossing the axes) would produce a filter that matches nothing.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = CompanyWatch.Follow(
            _userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        watch.SetFilter(
            WatchFilterSpec.Create(["gbg_kn"], ["skane_lan"], onlyMatched: true).Value)
            .IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);

        var dto = (await Handler(db).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.Filter.ShouldNotBeNull();
        dto.Filter!.Municipalities.ShouldBe(["gbg_kn"]);
        dto.Filter.Regions.ShouldBe(["skane_lan"],
            "läns-axeln måste nå DTO:n som ETT läns-id — aldrig expanderad till länets kommuner, " +
            "aldrig flyttad till kommun-axeln");
        dto.Filter.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenWatchHasOnlyMatchedFilterWithoutGeo_ProjectsEmptyAxesNotNullFilter()
    {
        // A geo-free filter is a REAL filter: the watch is narrowed even though both ort lists are
        // empty. Projecting null here (because "no orter") would erase the disclosure for exactly the
        // watch whose notifications may be suppressed hardest.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var watch = CompanyWatch.Follow(
            _userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value)
            .IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);

        var dto = (await Handler(db).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.Filter.ShouldNotBeNull();
        dto.Filter!.Municipalities.ShouldBeEmpty();
        dto.Filter.Regions.ShouldBeEmpty();
        dto.Filter.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenWatchHasNoFilter_ProjectsNullFilter()
    {
        // null = no filter, mirroring the domain's canonical NULL column. NOT an empty WatchFilterDto:
        // two representations of "no filter" would mean every reader has to know both, and the FE's
        // "absence = unfiltered" rule (no disclosure rendered) would break against the wrong one.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        Add(db, _userId, "5592804784");
        await db.SaveChangesAsync(ct);

        var dto = (await Handler(db).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.Filter.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ProjectsFilterPerWatch_NeverBleedingBetweenWatches()
    {
        // Two watches, one filtered. A projection bug that reused one watch's filter for the whole
        // list would silently claim the unfiltered watch is narrowed (and vice versa) — the row-level
        // disclosure is only trustworthy if it is per-watch.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var filteredWatch = CompanyWatch.Follow(
            _userId, OrganizationNumber.Create("5592804784").Value, _clock).Value;
        filteredWatch.SetFilter(
            WatchFilterSpec.Create(["gbg_kn"], [], onlyMatched: false).Value)
            .IsSuccess.ShouldBeTrue();
        var unfilteredWatch = CompanyWatch.Follow(
            _userId, OrganizationNumber.Create("2120000142").Value, _clock).Value;
        db.CompanyWatches.AddRange(filteredWatch, unfilteredWatch);
        await db.SaveChangesAsync(ct);

        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        var filtered = result.Single(d => d.OrganizationNumber == "5592804784");
        filtered.Filter.ShouldNotBeNull();
        filtered.Filter!.Municipalities.ShouldBe(["gbg_kn"]);
        result.Single(d => d.OrganizationNumber == "2120000142").Filter.ShouldBeNull();
    }

    [Fact]
    public void WatchFilterDto_CarriesNoOrgNrAndNoGradeValue()
    {
        // D8 (ADR 0087) — the filter DTO is a taxonomy-reference carrier, not an identity carrier: an
        // enskild-firma org.nr CAN BE a personnummer, so the type must not even have a member to leak
        // one through. The architecture guard's fail-closed partition would flag an org.nr member as
        // unclassified; this pins the STRONGER rule for this type (it may never be classified INTO the
        // surfacing set at all). Also pins the Goodhart bind: no grade value crosses the boundary.
        var members = typeof(WatchFilterDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        // #551 PR-B D6 — Remote is a taxonomy-adjacent boolean axis (no identity, no grade value),
        // so it may cross the surfacing boundary alongside the ort axes.
        members.ShouldBe(["Municipalities", "Regions", "OnlyMatched", "Remote"], ignoreOrder: true);
    }

    // ─────────────────────────── BrandGroup (#311 PR-5, ADR 0087 D4)

    [Fact]
    public async Task Handle_BrandGroupWatch_SurfacesCatalogueNameAndNoOrgNr()
    {
        // The DTO shape for a group watch: TargetType=BrandGroup, BrandGroupId set, org.nr NULL (never
        // surfaced), IsProtectedIdentity false, CompanyName = the CATALOGUE display name (not job_ads).
        // Counts are 0 here (InMemory has no job_ads / generated org.nr column — the summed counts over
        // real ads are pinned by the Testcontainers integration test).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        AddGroup(db, _userId, "volvo-koncernen");
        await db.SaveChangesAsync(ct);
        var provider = StubProvider(("volvo-koncernen", "Volvo (koncern)", ["5560125790"]));

        var result = await Handler(db, provider).Handle(new ListCompanyWatchesQuery(), ct);

        var dto = result.ShouldHaveSingleItem();
        dto.TargetType.ShouldBe(CompanyWatchTargetType.BrandGroup);
        dto.BrandGroupId.ShouldBe("volvo-koncernen");
        dto.OrganizationNumber.ShouldBeNull();
        dto.IsProtectedIdentity.ShouldBeFalse();
        dto.CompanyName.ShouldBe("Volvo (koncern)");
    }

    [Fact]
    public async Task Handle_BrandGroupWatch_WithOrphanedSlug_HasNoNameAndZeroCounts()
    {
        // An orphaned slug (removed from the catalogue) resolves to null: no curated name, zero counts —
        // honest, never an exception.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        AddGroup(db, _userId, "removed-group");
        await db.SaveChangesAsync(ct);

        // Empty catalogue (default _brandGroups) — the slug is not curated.
        var result = await Handler(db).Handle(new ListCompanyWatchesQuery(), ct);

        var dto = result.ShouldHaveSingleItem();
        dto.TargetType.ShouldBe(CompanyWatchTargetType.BrandGroup);
        dto.BrandGroupId.ShouldBe("removed-group");
        dto.CompanyName.ShouldBeNull();
        dto.ActiveAdCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_BrandGroupWatch_MatchingAdCount_SumsOverMembers_AndPassesMembersToSearch()
    {
        // #452 for a group: the matching-count is SUMMED over the group's members (m1→2, m2→1 ⇒ 3), and
        // the members must be handed to CountPerUserByEmployerAsync (countOrgNrs = employer ∪ members) —
        // otherwise a group would silently report 0/null. Pins the non-null arm of the null-guard + the
        // member SUM + the arg threading (the ActiveAdCount SUM has a Testcontainers oracle; this covers
        // the parallel #452 gap flagged by code-reviewer/test-writer).
        const string m1 = "5560125790";
        const string m2 = "5569876543";
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        AddGroup(db, _userId, "volvo");
        await db.SaveChangesAsync(ct);
        var provider = StubProvider(("volvo", "Volvo (koncern)", [m1, m2]));

        _profileBuilder
            .BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ProfileWithSsyk("1234"));
        _perUserSearch
            .CountPerUserByEmployerAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyList<MatchGrade>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { [m1] = 2, [m2] = 1 });

        var dto = (await Handler(db, provider).Handle(new ListCompanyWatchesQuery(), ct)).ShouldHaveSingleItem();

        dto.MatchingAdCount.ShouldBe(3); // 2 + 1 over the two members
        // The member org.nrs were passed to the search port (else the group would report 0).
        await _perUserSearch.Received().CountPerUserByEmployerAsync(
            Arg.Is<IReadOnlyList<string>>(o => o.Contains(m1) && o.Contains(m2)),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(),
            Arg.Any<CancellationToken>());
    }
}
