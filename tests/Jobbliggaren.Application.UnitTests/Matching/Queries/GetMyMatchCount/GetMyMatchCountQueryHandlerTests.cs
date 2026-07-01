using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Queries.GetMyMatchCount;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.GetMyMatchCount;

/// <summary>
/// ADR 0079 STEG 6 — the Översikt live-notis count handler. Mirrors the
/// <see cref="GetJobAdMatchBatchQueryHandlerTests"/> style: hand-rolled ValueTask fakes for
/// the two ValueTask-returning ports (NSubstitute trips CA2012 at the ValueTask call-setup
/// site in this project), no DB. The handler's contract:
/// <list type="bullet">
/// <item>builds the DEK-free SORT profile (<see cref="IMatchProfileBuilder.BuildFullForSortAsync"/>),
/// NEVER the DEK-warmed verdict profile;</item>
/// <item>SSYK-gate: empty <c>Fast.SsykGroupConceptIds</c> → honest <see cref="MyMatchCountDto.Zero"/>
/// AND the count port is never touched (no query for a profile that cannot match);</item>
/// <item>with a stated occupation: counts via <see cref="IPerUserJobAdSearchQuery.CountPerUserAsync"/>
/// over an EMPTY filter with grades == [Good, Strong] (the headline band), returning the
/// port's count unchanged.</item>
/// </list>
/// RED until <c>GetMyMatchCountQueryHandler</c> exists.
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

    private sealed class FakePerUserSearch(int countToReturn) : IPerUserJobAdSearchQuery
    {
        public int CountCallCount { get; private set; }
        public JobAdFilterCriteria? LastFilter { get; private set; }
        public FullCandidateMatchProfile? LastProfile { get; private set; }
        public IReadOnlyList<MatchGrade>? LastGrades { get; private set; }

        public ValueTask<int> CountPerUserAsync(
            JobAdFilterCriteria filter,
            FullCandidateMatchProfile profile,
            IReadOnlyList<MatchGrade> grades,
            CancellationToken cancellationToken)
        {
            CountCallCount++;
            LastFilter = filter;
            LastProfile = profile;
            LastGrades = grades;
            return new ValueTask<int>(countToReturn);
        }

        // The list path — not used by the count handler.
        public ValueTask<PagedResult<JobAdDto>> SearchPerUserAsync(
            JobAdFilterCriteria filter, FullCandidateMatchProfile profile,
            IReadOnlyList<MatchGrade> grades, JobAdSortBy sort, bool orderByMatchRank,
            JobAdStatusFilter status, JobSeekerId seekerId,
            int page, int pageSize, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "SearchPerUserAsync ska inte anropas av count-handlern — den counter:ar bara.");

        // #383 — status-only list path; not used by the count handler.
        public ValueTask<PagedResult<JobAdDto>> SearchByStatusAsync(
            JobAdFilterCriteria filter, JobSeekerId seekerId, JobAdStatusFilter status,
            JobAdSortBy sort, int page, int pageSize, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "SearchByStatusAsync ska inte anropas av count-handlern — den counter:ar bara.");

        // #452 — the per-employer hub match-count path; not used by the Översikt count handler
        // (it belongs to the company-watch hub, ListCompanyWatchesQueryHandler).
        public ValueTask<IReadOnlyDictionary<string, int>> CountPerUserByEmployerAsync(
            IReadOnlyList<string> organizationNumbers, FullCandidateMatchProfile profile,
            IReadOnlyList<MatchGrade> grades, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "CountPerUserByEmployerAsync ska inte anropas av Översikts count-handler — den " +
                "counter:ar den globala match-siffran, aldrig per-arbetsgivare-hub-counten.");
    }

    // ---------------------------------------------------------------
    // Profile builders.
    // ---------------------------------------------------------------
    private static FullCandidateMatchProfile ProfileWithOccupation() =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ["grp_12345"],
                PreferredRegionConceptIds: ["region_AB"],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    // No stated occupation (no user / unconfigured profile) → empty SSYK → the gate.
    private static FullCandidateMatchProfile EmptyProfile() =>
        new(new CandidateMatchProfile(string.Empty, [], [], [], []), []);

    private static GetMyMatchCountQueryHandler CreateHandler(
        FakeProfileBuilder builder, FakePerUserSearch search) =>
        new(builder, search);

    // =================================================================
    // SSYK-gate: empty occupation → honest 0, count port NEVER called.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnZero_WhenProfileHasNoStatedOccupation()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(EmptyProfile());
        var search = new FakePerUserSearch(countToReturn: 999);
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
        var search = new FakePerUserSearch(countToReturn: 999);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // Honest 0 without touching the corpus — no query for a profile that cannot match.
        search.CountCallCount.ShouldBe(0);
    }

    // =================================================================
    // Stated occupation → counts via the port with the headline band + empty filter.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldReturnPortCountUnchanged_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var search = new FakePerUserSearch(countToReturn: 42);
        var sut = CreateHandler(builder, search);

        var result = await sut.Handle(new GetMyMatchCountQuery(), ct);

        // The notis number is the port's count verbatim — never massaged, never a mock.
        result.Count.ShouldBe(42);
        search.CountCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldCountWithHeadlineGradesGoodAndStrong_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var search = new FakePerUserSearch(countToReturn: 7);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // The headline band (Klas 2026-06-24): EXACTLY {Good, Strong} — never Basic, never
        // Top (Fast band, G3-OPT-A), and never Related (PR-4 #300, ADR-question D = count is
        // LIST-ONLY — Related does NOT drive the notification count). This set MUST stay coherent
        // with the FE notis link (?matchGrades=Good&matchGrades=Strong) so the number == the linked
        // /jobb count.
        search.LastGrades.ShouldNotBeNull();
        search.LastGrades!.ShouldBe([MatchGrade.Good, MatchGrade.Strong], ignoreOrder: true);
        search.LastGrades!.ShouldNotContain(MatchGrade.Basic);
        search.LastGrades!.ShouldNotContain(MatchGrade.Related,
            "Related ingår ALDRIG i headline-counten (ADR 0084 fråga D — counten är list-only; " +
            "Related driver inte notis-siffran).");
        search.LastGrades!.ShouldNotContain(MatchGrade.Top);
    }

    [Fact]
    public async Task Handle_ShouldCountOverEmptyFilter_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var search = new FakePerUserSearch(countToReturn: 3);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // The notis counts over the WHOLE active corpus — only the profile grade gallrar,
        // no search/dimension filter. An empty filter-SPOT = all Active ads (Q null, every
        // dimension list empty).
        search.LastFilter.ShouldNotBeNull();
        var filter = search.LastFilter!;
        filter.Q.ShouldBeNull();
        filter.OccupationGroup.ShouldBeEmpty();
        filter.Municipality.ShouldBeEmpty();
        filter.Region.ShouldBeEmpty();
        filter.EmploymentType.ShouldBeEmpty();
        filter.WorktimeExtent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldPassTheBuiltSortProfileToTheCountPort_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = ProfileWithOccupation();
        var builder = new FakeProfileBuilder(profile);
        var search = new FakePerUserSearch(countToReturn: 1);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // The exact profile the builder produced is what the count is taken over (no
        // re-projection between build and count).
        search.LastProfile.ShouldBeSameAs(profile);
    }

    // =================================================================
    // DEK-free path: builds the SORT profile, never the verdict (DEK) profile.
    // =================================================================

    [Fact]
    public async Task Handle_ShouldBuildTheSortProfile_NotTheVerdictProfile_WhenOccupationStated()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new FakeProfileBuilder(ProfileWithOccupation());
        var search = new FakePerUserSearch(countToReturn: 1);
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
        var search = new FakePerUserSearch(countToReturn: 1);
        var sut = CreateHandler(builder, search);

        await sut.Handle(new GetMyMatchCountQuery(), ct);

        // Even the gate path reads the profile via the DEK-free SORT builder — never DEK.
        builder.SortCallCount.ShouldBe(1);
        builder.VerdictCallCount.ShouldBe(0);
    }
}
