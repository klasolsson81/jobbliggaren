using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// ADR 0079 STEG 6 — Finding #7 ("Ny"-tag) REGRESSION GUARD. Finding #7 is CLOSED as
/// data/state (no code bug); this oracle GUARDS that the "Ny" tag stays ORTHOGONAL to the
/// grade filter. The grade-WHERE in <see cref="PerUserJobAdSearchQuery"/> gates the SET of
/// ads, but the <see cref="JobAdDto.IsNew"/> projection is computed by the SHARED
/// <c>JobAdSearchComposition.ToDto(since)</c> that BOTH the default search path
/// (<see cref="IJobAdSearchQuery.SearchAsync"/>) and the grade-filtered per-user path
/// (<see cref="IPerUserJobAdSearchQuery.SearchPerUserAsync"/>) feed. So for the SAME seeded
/// ad + the SAME <c>since</c> window, <c>IsNew</c> must be IDENTICAL on both paths — the
/// grade WHERE may shrink the result set but must NEVER perturb the IsNew flag.
/// <para>
/// Testcontainers Postgres (NEVER InMemory — the IsNew comparison
/// <c>since != null &amp;&amp; PublishedAt &gt;= since</c> is EF-translated SQL, and the
/// grade-WHERE's <c>= ANY</c> translation is hidden by InMemory). Seeding is self-contained,
/// mirroring <see cref="MatchCountOracleTests"/>.
/// </para>
/// </summary>
[Collection("Api")]
public class IsNewTagOrthogonalityTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string PrefGroup = "grp-isnew-pref";
    private const string PrefRegion = "reg-isnew-pref";
    private const string PrefEmployment = "emp-isnew-pref";

    // The Strong profile so a fully-confirmed ad is grade-tagged (positive) and thus survives
    // the per-user grade-WHERE — so the two paths can be compared on the SAME ad.
    private static FullCandidateMatchProfile Profile() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: [PrefGroup],
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            PreferredMunicipalityConceptIds: []),
        CvSkillConceptIds: []);

    private static JobAdFilterCriteria FilterFor(string runWorktimeExtent) => new(
        OccupationGroup: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [runWorktimeExtent],
        Q: null);

    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewPerUserQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPerUserJobAdSearchQuery>();
        return (scope, query);
    }

    private (IServiceScope Scope, IJobAdSearchQuery Query) NewDefaultQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IJobAdSearchQuery>();
        return (scope, query);
    }

    // Strong ad (region Match + employment Match) — grade-tagged so it survives the per-user
    // grade-WHERE. publishedAt is explicit so the test can place it inside/outside `since`.
    private async Task<JobAdId> SeedStrongAsync(
        string runWorktimeExtent, DateTimeOffset publishedAt, CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{{\"concept_id\":\"{PrefGroup}\"}},"
            + $"\"workplace_address\":{{\"region_concept_id\":\"{PrefRegion}\"}},"
            + $"\"employment_type\":{{\"concept_id\":\"{PrefEmployment}\"}},"
            + $"\"working_hours_type\":{{\"concept_id\":\"{runWorktimeExtent}\"}}}}";

        var jobAd = JobAd.Import(
            title: "IsNew-ortogonalitets-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private static string NewRunWorktimeExtent() => $"wt-isnew-{Guid.NewGuid():N}"[..19];

    private static JobAdDto SingleFor(IReadOnlyList<JobAdDto> items, JobAdId id) =>
        items.Single(i => i.Id == id.Value);

    // ===============================================================
    // 1. An ad published INSIDE the `since` window → IsNew true on BOTH paths, and the flag
    //    is IDENTICAL across the default search and the grade-filtered per-user search. The
    //    grade WHERE gates the SET, never the IsNew projection.
    // ===============================================================

    [Fact]
    public async Task IsNew_True_IsIdenticalAcrossDefaultAndGradeFilteredPaths_WhenInsideSinceWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        // Recent enough to be inside any reasonable "Ny sedan" window.
        var publishedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var adId = await SeedStrongAsync(run, publishedAt, ct);
        var profile = Profile();
        var filter = FilterFor(run);

        // Default path — the shared IJobAdSearchQuery with the SAME since.
        var (defaultScope, defaultQuery) = NewDefaultQuery();
        using var defaultDispose = defaultScope;
        var defaultPage = await defaultQuery.SearchAsync(
            new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 100, Since: since),
            ct);

        // Grade-filtered per-user path — Strong band, SAME since.
        var (perUserScope, perUserQuery) = NewPerUserQuery();
        using var perUserDispose = perUserScope;
        var perUserPage = await perUserQuery.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: false, page: 1, pageSize: 100, since: since, ct);

        var defaultDto = SingleFor(defaultPage.Items, adId);
        var perUserDto = SingleFor(perUserPage.Items, adId);

        defaultDto.IsNew.ShouldBeTrue(
            "En annons publicerad inom `since`-fönstret ska ha IsNew=true på default-vägen.");
        perUserDto.IsNew.ShouldBeTrue(
            "Samma annons ska ha IsNew=true även på den grad-filtrerade per-användar-vägen " +
            "— grad-WHERE:t gallrar MÄNGDEN men aldrig IsNew-projektionen.");
        perUserDto.IsNew.ShouldBe(defaultDto.IsNew,
            "IsNew MÅSTE vara IDENTISK mellan default-sök och grad-filtrerad per-användar-sök " +
            "för samma annons + samma `since` — båda matas av samma delade " +
            "JobAdSearchComposition.ToDto(since). Detta är #7-regressionsvakten.");
    }

    // ===============================================================
    // 2. An ad published OLDER than `since` → IsNew false on BOTH paths (and still identical).
    // ===============================================================

    [Fact]
    public async Task IsNew_False_IsIdenticalAcrossBothPaths_WhenPublishedBeforeSinceWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        // Older than the window → not "Ny".
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var adId = await SeedStrongAsync(run, publishedAt, ct);
        var profile = Profile();
        var filter = FilterFor(run);

        var (defaultScope, defaultQuery) = NewDefaultQuery();
        using var defaultDispose = defaultScope;
        var defaultPage = await defaultQuery.SearchAsync(
            new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 100, Since: since),
            ct);

        var (perUserScope, perUserQuery) = NewPerUserQuery();
        using var perUserDispose = perUserScope;
        var perUserPage = await perUserQuery.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: false, page: 1, pageSize: 100, since: since, ct);

        var defaultDto = SingleFor(defaultPage.Items, adId);
        var perUserDto = SingleFor(perUserPage.Items, adId);

        defaultDto.IsNew.ShouldBeFalse(
            "En annons publicerad FÖRE `since`-fönstret ska ha IsNew=false på default-vägen.");
        perUserDto.IsNew.ShouldBeFalse(
            "Samma annons ska ha IsNew=false även på den grad-filtrerade per-användar-vägen.");
        perUserDto.IsNew.ShouldBe(defaultDto.IsNew,
            "IsNew=false ska också vara IDENTISK mellan vägarna — grad-filtret gör annonsen " +
            "varken nyare eller äldre.");
    }
}
