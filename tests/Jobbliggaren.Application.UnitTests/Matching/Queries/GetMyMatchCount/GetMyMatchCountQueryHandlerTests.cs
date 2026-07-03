using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6, harmoniserad 2026-07-03 (Klas "samma siffra"; CTO-bind H2) — the
/// Översikt notis count handler. Hand-rolled ValueTask fakes (NSubstitute trips CA2012 at
/// the ValueTask call-setup site in this project), no DB. The harmonized contract:
/// <list type="bullet">
/// <item>builds the DEK-free SORT profile (<see cref="IMatchProfileBuilder.BuildFullForSortAsync"/>),
/// NEVER the DEK-warmed verdict profile;</item>
/// <item>SSYK-gate: empty <c>Fast.SsykGroupConceptIds</c> → honest <see cref="MyMatchCountDto.Zero"/>
/// AND the count port is never touched;</item>
/// <item>with a stated occupation: the SAVED facets (yrke/ort/form) become HARD filters in a
/// <see cref="JobAdFilterCriteria"/> counted via <see cref="IJobAdSearchQuery.CountAsync"/> —
/// the same anon-cacheable SPOT as the setup preview counter, NO grade band. The number is
/// therefore identical to the setup counter and the linked /jobb page's TotalCount by
/// construction.</item>
/// </list>
/// </summary>
public class GetMyMatchCountQueryHandlerTests
{
    // ---------------------------------------------------------------
    // Hand-rolled ValueTask fakes (CA2012-safe) with call counters / argument capture.
    // ---------------------------------------------------------------
    private sealed class FakeProfileBuilder(FullCandidateMatchProfile sortProfile) : IMatchProfileBuilder
    {
        public int SortCallCount { get; private set; }
        public int VerdictCallCount { get; private set; }
        public int PreferencesCallCount { get; private set; }

        // The SORT path — the one the count handler uses (DEK-free).
        public ValueTask<FullCandidateMatchProfile> BuildFullForSortAsync(CancellationToken cancellationToken, bool includeRelated = false)
        {
            SortCallCount++;
            return new ValueTask<FullCandidateMatchProfile>(sortProfile);
        }

        // The VERDICT (DEK-warmed) path — must NEVER be called by the count handler.
        public ValueTask<FullCandidateMatchProfile> BuildFullForVerdictAsync(CancellationToken cancellationToken, bool includeRelated = false)
        {
            VerdictCallCount++;
            throw new NotSupportedException(
                "Count-handlern ska bygga DEK-fria SORT-profilen, aldrig verdict-profilen (DEK).");
        }

        public ValueTask<CandidateMatchProfile> BuildFromPreferencesAsync(CancellationToken cancellationToken, bool includeRelated = false)
        {
            PreferencesCallCount++;
            return new ValueTask<CandidateMatchProfile>(sortProfile.Fast);
        }

        // ADR 0080 Vag 4 PR-2 — the background by-id builder. Not used by the count handler.
        public ValueTask<FullCandidateMatchProfile> BuildFullForUserIdAsync(
            Guid userId, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "Count-handlern bygger den request-scopade SORT-profilen, aldrig by-id-bakgrundsvarianten.");
    }

    // H2: notisen räknar via den anon-cachebara sök-porten (samma som setup-preview),
    // ALDRIG per-användar-grad-porten.
    private sealed class FakeJobAdSearchQuery(int countToReturn) : IJobAdSearchQuery
    {
        public int CountCallCount { get; private set; }
        public JobAdFilterCriteria? LastFilter { get; private set; }

        public ValueTask<int> CountAsync(
            JobAdFilterCriteria criteria, CancellationToken cancellationToken)
        {
            CountCallCount++;
            LastFilter = criteria;
            return new ValueTask<int>(countToReturn);
        }

        public ValueTask<PagedResult<JobAdDto>> SearchAsync(
            JobAdSearchCriteria criteria, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "SearchAsync ska inte anropas av count-handlern — den counter:ar bara.");

        public ValueTask<IReadOnlyDictionary<string, int>> FacetCountsAsync(
            JobAdFilterCriteria criteria, FacetDimension dimension, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "FacetCountsAsync ska inte anropas av count-handlern.");
    }

    // ---------------------------------------------------------------
    // Profile builders.
    // ---------------------------------------------------------------
    private static FullCandidateMatchProfile ProfileWithFacets() =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ["grp_12345"],
                PreferredRegionConceptIds: ["region_AB"],
                PreferredEmploymentTypeConceptIds: ["et_fast"],
                PreferredMunicipalityConceptIds: ["kommun_0180"]),
            CvSkillConceptIds: []);

    // No stated occupation (no user / unconfigured profile) → empty SSYK → the gate.
    private static FullCandidateMatchProfile EmptyProfile() =>
        new(new CandidateMatchProfile(string.Empty, [], [], [], []), []);

    private static GetMyMatchCountQueryHandler CreateHandler(
        FakeProfileBuilder builder, FakeJobAdSearchQuery search) =>
        new(builder, search);

    // =================================================================
    // SSYK-gate: empty occupation → honest 0, count port NEVER called.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnZero_WhenProfileHasNoStatedOccupation()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(EmptyProfile());
        var search = new FakeJobAdSearchQuery(countToReturn: 999);
        var sut = CreateHandler(builder, search);

        var result = await sut.Handle(new GetMyMatchCountQuery(), ct);

        result.ShouldBe(MyMatchCountDto.Zero);
        result.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ShouldNotCallCountPort_WhenProfileHasNoStatedOccupation()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(EmptyProfile());
        var search = new FakeJobAdSearchQuery(countToReturn: 999);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // Honest 0 without touching the corpus — no query for a profile that cannot match.
        search.CountCallCount.ShouldBe(0);
    }

    // =================================================================
    // Stated occupation → the SAVED facets become HARD filters (H2), count via
    // the shared anon-cacheable SPOT — no grade band anywhere.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnPortCountUnchanged_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithFacets());
        var search = new FakeJobAdSearchQuery(countToReturn: 183);
        var sut = CreateHandler(builder, search);

        var result = await sut.Handle(new GetMyMatchCountQuery(), ct);

        // The notis number is the port's count verbatim — never massaged, never a mock.
        result.Count.ShouldBe(183);
        search.CountCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldMapSavedFacetsToHardFilters_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithFacets());
        var search = new FakeJobAdSearchQuery(countToReturn: 5);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // H2 (Klas "samma siffra" 2026-07-03): every saved dimension is a HARD filter —
        // exactly the setup preview's semantics, so the two numbers can never diverge.
        search.LastFilter.ShouldNotBeNull();
        var filter = search.LastFilter!;
        filter.OccupationGroup.ShouldBe(["grp_12345"]);
        filter.Region.ShouldBe(["region_AB"]);
        filter.Municipality.ShouldBe(["kommun_0180"]);
        filter.EmploymentType.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task Handle_ShouldLeaveWorktimeEmployerAndQEmpty_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithFacets());
        var search = new FakeJobAdSearchQuery(countToReturn: 5);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // Matchningen exponerar varken omfattnings-, arbetsgivar- eller fritext-dimensionen.
        var filter = search.LastFilter!;
        filter.WorktimeExtent.ShouldBeEmpty();
        filter.Employer.ShouldBeEmpty();
        filter.Q.ShouldBeNull();
    }

    // =================================================================
    // DEK-free path: builds the SORT profile, never the verdict (DEK) profile.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldBuildTheSortProfile_NotTheVerdictProfile_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithFacets());
        var search = new FakeJobAdSearchQuery(countToReturn: 1);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        builder.SortCallCount.ShouldBe(1);      // DEK-free SORT path
        builder.VerdictCallCount.ShouldBe(0);   // never the DEK-warmed verdict path
    }

    [Fact]
    public async Task Handle_ShouldBuildTheSortProfile_EvenWhenGateClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(EmptyProfile());
        var search = new FakeJobAdSearchQuery(countToReturn: 1);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // Even the gate path reads the profile via the DEK-free SORT builder — never DEK.
        builder.SortCallCount.ShouldBe(1);
        builder.VerdictCallCount.ShouldBe(0);
    }
}
