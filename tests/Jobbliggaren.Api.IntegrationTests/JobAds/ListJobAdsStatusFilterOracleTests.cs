using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

// Application-typen krockar med Jobbliggaren.Application-namespacet — alias per fil
// (paritet GetApplicationsQueryHandlerIntegrationTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #383 (CTO-bind <c>cto-7f3a9c2e1b4d8a6f</c>, Approach B) — THE STATUS-FILTER ORACLE. The
/// anti-drift guard that pins the per-user <c>EXISTS</c>/<c>NOT EXISTS</c>-stacks in
/// <see cref="PerUserJobAdSearchQuery"/>'s <c>ApplyStatusFilter</c> (saved / applied / hide-
/// applied) + the recomputed count to the seeded SavedJobAd/Application rows. Runs the REAL
/// wired Infrastructure query (<see cref="IPerUserJobAdSearchQuery.SearchByStatusAsync"/> and
/// <see cref="IPerUserJobAdSearchQuery.SearchPerUserAsync"/>) against real Postgres
/// (Testcontainers via <see cref="ApiFactory"/>), ALDRIG EF-InMemory: the status predicate is
/// <c>db.SavedJobAds.Any(s =&gt; s.JobSeekerId == seeker &amp;&amp; s.JobAdId == j.Id)</c> — a VO==VO
/// column comparison (<c>ef_strongly_typed_vo_contains</c>-lärdomen, CI 2026-05-23). InMemory
/// is not relational and hides whether that comparison + the soft-delete-<c>HasQueryFilter</c>
/// on <c>Application</c> translate; only real Postgres proves the EXISTS subquery.
/// <para>
/// <b>Run-isolation (parity MatchSortGradeFilterOracleTests.FilterFor):</b> every seeded ad
/// carries a UNIQUE <c>working_hours_type</c> concept-id (the test-run tag), and the filter
/// selects on that worktime-extent only — so the run's seeded mass is provably disjoint from
/// every other ad in the shared <c>[Collection("Api")]</c> Postgres. The status EXISTS keys on
/// the SEEKER, so two seekers within one run are naturally isolated too (the seeker-isolation
/// test below proves that predicate).
/// </para>
/// </summary>
[Collection("Api")]
public class ListJobAdsStatusFilterOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------
    // SUT factory — the REAL wired per-user query from DI (proving the registration + the EF
    // translation of the status-EXISTS / recomputed count), plus a held scope.
    // ---------------------------------------------------------------
    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewPerUserQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPerUserJobAdSearchQuery>();
        return (scope, query);
    }

    private (AppDbContext Db, IServiceScope Scope, IDateTimeProvider Clock) NewScope()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        return (db, scope, clock);
    }

    // Filter on the unique test-run worktime-extent only → exactly the run's seeded ads.
    private static JobAdFilterCriteria FilterFor(string runWorktimeExtent) => new(
        OccupationGroup: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [runWorktimeExtent],
        Q: null);

    // A run tag (≤23 chars to match the grade oracle's column width assumptions).
    private static string NewRunWorktimeExtent() => $"wt-statusfilter-{Guid.NewGuid():N}"[..23];

    // ---------------------------------------------------------------
    // Seeding — JobAds tagged with the run's worktime-extent (isolation key). The status path
    // reads NO occupation/region/employment shadow, so a minimal raw_payload with just the
    // working_hours_type tag suffices.
    // ---------------------------------------------------------------
    private async Task<JobAdId> SeedJobAdAsync(string runWorktimeExtent, DateTimeOffset publishedAt)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var (db, scope, clock) = NewScope();
        using (scope)
        {
            var rawPayload =
                $"{{\"id\":\"{externalId}\","
                + $"\"working_hours_type\":{{\"concept_id\":\"{runWorktimeExtent}\"}}}}";

            var jobAd = JobAd.Import(
                title: "Statusfilter-orakel-annons",
                company: Company.Create("Test Company AB").Value,
                description: "beskrivning",
                url: $"https://example.com/jobs/{externalId}",
                external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
                rawPayload: rawPayload,
                publishedAt: publishedAt,
                expiresAt: clock.UtcNow.AddDays(30),
                clock: clock).Value;

            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return jobAd.Id;
        }
    }

    private async Task<JobSeekerId> SeedSeekerAsync()
    {
        var (db, scope, clock) = NewScope();
        using (scope)
        {
            var seeker = JobSeeker.Register(Guid.NewGuid(), "Status Seeker", clock).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return seeker.Id;
        }
    }

    private async Task SaveJobAdAsync(JobSeekerId seeker, JobAdId jobAd)
    {
        var (db, scope, clock) = NewScope();
        using (scope)
        {
            db.SavedJobAds.Add(SavedJobAd.Save(seeker, jobAd, clock.UtcNow));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private async Task ApplyToJobAdAsync(JobSeekerId seeker, JobAdId jobAd)
    {
        var (db, scope, clock) = NewScope();
        using (scope)
        {
            // A Draft application suffices — ApplyStatusFilter's EXISTS matches ANY application
            // row for (seeker, jobAd) regardless of status (no AppliedAt filter in the predicate).
            var app = DomainApplication.Create(seeker, jobAd, null, null, clock).Value;
            db.Applications.Add(app);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    private static HashSet<Guid> IdSet(IEnumerable<JobAdId> ids) => ids.Select(i => i.Value).ToHashSet();

    // ===============================================================
    // 1. savedOnly → exactly the saved ads; TotalCount == saved count.
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_SavedOnly_ReturnsExactlySavedAds_WithMatchingCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        var saved1 = await SeedJobAdAsync(run, T0.AddDays(10));
        var saved2 = await SeedJobAdAsync(run, T0.AddDays(9));
        var unsaved = await SeedJobAdAsync(run, T0.AddDays(8));
        await SaveJobAdAsync(seeker, saved1);
        await SaveJobAdAsync(seeker, saved2);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        var page = await query.SearchByStatusAsync(
            FilterFor(run), seeker, new JobAdStatusFilter(SavedOnly: true, false, false),
            JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 100, ct);

        IdSet(page.Items.Select(i => new JobAdId(i.Id))).ShouldBe(IdSet([saved1, saved2]), ignoreOrder: true,
            "savedOnly ska returnera EXAKT de sparade annonserna (EXISTS i SavedJobAds), " +
            "aldrig den osparade.");
        page.Items.Select(i => i.Id).ShouldNotContain(unsaved.Value);
        page.TotalCount.ShouldBe(2,
            "TotalCount ska räknas över den status-filtrerade mängden (2 sparade), inte korpusen.");
    }

    // ===============================================================
    // 2. appliedOnly → exactly the applied ads; TotalCount == applied count.
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_AppliedOnly_ReturnsExactlyAppliedAds_WithMatchingCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        var applied1 = await SeedJobAdAsync(run, T0.AddDays(10));
        var applied2 = await SeedJobAdAsync(run, T0.AddDays(9));
        var notApplied = await SeedJobAdAsync(run, T0.AddDays(8));
        await ApplyToJobAdAsync(seeker, applied1);
        await ApplyToJobAdAsync(seeker, applied2);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        var page = await query.SearchByStatusAsync(
            FilterFor(run), seeker, new JobAdStatusFilter(false, AppliedOnly: true, false),
            JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 100, ct);

        IdSet(page.Items.Select(i => new JobAdId(i.Id))).ShouldBe(IdSet([applied1, applied2]), ignoreOrder: true,
            "appliedOnly ska returnera EXAKT de ansökta annonserna (EXISTS i Applications).");
        page.Items.Select(i => i.Id).ShouldNotContain(notApplied.Value);
        page.TotalCount.ShouldBe(2);
    }

    // ===============================================================
    // 3. savedOnly + appliedOnly → the UNION (OR). An ad both saved AND applied appears ONCE.
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_SavedOrApplied_ReturnsUnion_WithoutDoubleCountingOverlap()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        var savedOnlyAd = await SeedJobAdAsync(run, T0.AddDays(12));
        var appliedOnlyAd = await SeedJobAdAsync(run, T0.AddDays(11));
        var both = await SeedJobAdAsync(run, T0.AddDays(10)); // saved AND applied
        var neither = await SeedJobAdAsync(run, T0.AddDays(9));

        await SaveJobAdAsync(seeker, savedOnlyAd);
        await SaveJobAdAsync(seeker, both);
        await ApplyToJobAdAsync(seeker, appliedOnlyAd);
        await ApplyToJobAdAsync(seeker, both);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        var page = await query.SearchByStatusAsync(
            FilterFor(run), seeker, new JobAdStatusFilter(SavedOnly: true, AppliedOnly: true, false),
            JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 100, ct);

        var expected = IdSet([savedOnlyAd, appliedOnlyAd, both]);
        IdSet(page.Items.Select(i => new JobAdId(i.Id))).ShouldBe(expected, ignoreOrder: true,
            "savedOnly+appliedOnly ska vara UNIONEN (OR) — sparade ELLER ansökta.");
        page.Items.Select(i => i.Id).ShouldNotContain(neither.Value);
        page.Items.Count(i => i.Id == both.Value).ShouldBe(1,
            "en annons som är BÅDE sparad OCH ansökt får dyka upp exakt EN gång (ingen dubblett).");
        page.TotalCount.ShouldBe(3,
            "TotalCount för unionen ska vara |sparade ∪ ansökta| (3), överlappet räknas en gång.");
    }

    // ===============================================================
    // 4. hideApplied → the run's ads MINUS the applied ones (NOT EXISTS).
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_HideApplied_ReturnsRunAdsMinusApplied()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        var applied = await SeedJobAdAsync(run, T0.AddDays(10));
        var notApplied1 = await SeedJobAdAsync(run, T0.AddDays(9));
        var notApplied2 = await SeedJobAdAsync(run, T0.AddDays(8));
        await ApplyToJobAdAsync(seeker, applied);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        var page = await query.SearchByStatusAsync(
            FilterFor(run), seeker, new JobAdStatusFilter(false, false, HideApplied: true),
            JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 100, ct);

        IdSet(page.Items.Select(i => new JobAdId(i.Id))).ShouldBe(
            IdSet([notApplied1, notApplied2]), ignoreOrder: true,
            "hideApplied ska returnera körningens annonser MINUS de ansökta (NOT EXISTS).");
        page.Items.Select(i => i.Id).ShouldNotContain(applied.Value);
        page.TotalCount.ShouldBe(2);
    }

    // ===============================================================
    // 5. savedOnly + hideApplied → saved ads that are NOT applied (AND composition).
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_SavedAndHideApplied_ReturnsSavedAdsNotYetApplied()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        var savedNotApplied = await SeedJobAdAsync(run, T0.AddDays(12));
        var savedAndApplied = await SeedJobAdAsync(run, T0.AddDays(11));
        var appliedNotSaved = await SeedJobAdAsync(run, T0.AddDays(10));
        var neither = await SeedJobAdAsync(run, T0.AddDays(9));

        await SaveJobAdAsync(seeker, savedNotApplied);
        await SaveJobAdAsync(seeker, savedAndApplied);
        await ApplyToJobAdAsync(seeker, savedAndApplied);
        await ApplyToJobAdAsync(seeker, appliedNotSaved);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        var page = await query.SearchByStatusAsync(
            FilterFor(run), seeker, new JobAdStatusFilter(SavedOnly: true, false, HideApplied: true),
            JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 100, ct);

        IdSet(page.Items.Select(i => new JobAdId(i.Id))).ShouldBe(IdSet([savedNotApplied]), ignoreOrder: true,
            "savedOnly+hideApplied = 'sparade jag inte sökt ännu' — endast sparade som EJ är ansökta.");
        var returned = page.Items.Select(i => i.Id).ToList();
        returned.ShouldNotContain(savedAndApplied.Value, "en sparad men ansökt annons döljs av hideApplied.");
        returned.ShouldNotContain(appliedNotSaved.Value);
        returned.ShouldNotContain(neither.Value);
        page.TotalCount.ShouldBe(1);
    }

    // ===============================================================
    // 6. Seeker isolation — seeker A's savedOnly must NOT return seeker B's saved ads, and
    //    vice versa (proves the `s.JobSeekerId == seeker` predicate).
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_SavedOnly_IsScopedToTheSeeker_NeverLeaksAnotherSeekersSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seekerA = await SeedSeekerAsync();
        var seekerB = await SeedSeekerAsync();

        var savedByA = await SeedJobAdAsync(run, T0.AddDays(10));
        var savedByB = await SeedJobAdAsync(run, T0.AddDays(9));
        var savedByBoth = await SeedJobAdAsync(run, T0.AddDays(8));

        await SaveJobAdAsync(seekerA, savedByA);
        await SaveJobAdAsync(seekerB, savedByB);
        await SaveJobAdAsync(seekerA, savedByBoth);
        await SaveJobAdAsync(seekerB, savedByBoth);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;
        var saved = new JobAdStatusFilter(SavedOnly: true, false, false);

        var pageA = await query.SearchByStatusAsync(
            FilterFor(run), seekerA, saved, JobAdSortBy.PublishedAtDesc, 1, 100, ct);
        var pageB = await query.SearchByStatusAsync(
            FilterFor(run), seekerB, saved, JobAdSortBy.PublishedAtDesc, 1, 100, ct);

        IdSet(pageA.Items.Select(i => new JobAdId(i.Id))).ShouldBe(
            IdSet([savedByA, savedByBoth]), ignoreOrder: true,
            "seeker A:s savedOnly ska bara se A:s egna sparade (inkl. den båda sparat), aldrig B:s.");
        pageA.Items.Select(i => i.Id).ShouldNotContain(savedByB.Value,
            "seeker A får ALDRIG se en annons bara B sparat (bevisar s.JobSeekerId == seeker).");

        IdSet(pageB.Items.Select(i => new JobAdId(i.Id))).ShouldBe(
            IdSet([savedByB, savedByBoth]), ignoreOrder: true);
        pageB.Items.Select(i => i.Id).ShouldNotContain(savedByA.Value);
    }

    // ===============================================================
    // 7. Pagination over the status-filtered set — pageSize < count keeps TotalCount full, no
    //    phantom page, no overlap, union == the full filtered set.
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_Pagination_OverFilteredSet_KeepsFullCount_NoPhantomPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        // 4 saved (in-band) + 2 unsaved (out-of-band) → the corpus (6) differs from the in-band 4.
        var saved = new List<JobAdId>();
        for (var i = 0; i < 4; i++)
        {
            var ad = await SeedJobAdAsync(run, T0.AddDays(20 - i));
            await SaveJobAdAsync(seeker, ad);
            saved.Add(ad);
        }

        await SeedJobAdAsync(run, T0.AddDays(5));
        await SeedJobAdAsync(run, T0.AddDays(4));

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;
        var savedFilter = new JobAdStatusFilter(SavedOnly: true, false, false);

        var firstPage = await query.SearchByStatusAsync(
            FilterFor(run), seeker, savedFilter, JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 2, ct);

        firstPage.Items.Count.ShouldBe(2, "sida 1 med pageSize 2 ska ha exakt 2 träffar ur den sparade mängden.");
        firstPage.TotalCount.ShouldBe(4,
            "TotalCount ska vara hela den status-filtrerade mängden (4) oavsett pageSize — ingen spök-sida.");
        firstPage.TotalPages.ShouldBe(2);

        var secondPage = await query.SearchByStatusAsync(
            FilterFor(run), seeker, savedFilter, JobAdSortBy.PublishedAtDesc, page: 2, pageSize: 2, ct);

        secondPage.Items.Count.ShouldBe(2, "sida 2 ska ha de resterande 2 sparade — ingen osparad får läcka in.");
        secondPage.TotalCount.ShouldBe(4);

        var pagedIds = firstPage.Items.Select(i => i.Id)
            .Concat(secondPage.Items.Select(i => i.Id)).ToList();
        pagedIds.Distinct().Count().ShouldBe(4, "sida 1 ∪ sida 2 = exakt de 4 sparade, inga dubbletter.");
        IdSet(pagedIds.Select(g => new JobAdId(g))).ShouldBe(IdSet(saved), ignoreOrder: true);
    }

    // ===============================================================
    // 8. Status composes with the GRADE/match path: SearchPerUserAsync with a stated profile,
    //    a grade subset AND an active status → the returned set == (graded ∩ status-filtered),
    //    and TotalCount == that size (status applied BEFORE grade-WHERE/count).
    // ===============================================================

    private const string PrefGroup = "grp-statusfilter-pref";

    private static FullCandidateMatchProfile Profile() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: [PrefGroup],
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: []),
        CvSkillConceptIds: []);

    // Seeds a JobAd whose occupation_group == PrefGroup (→ Fast SSYK Match, both secondaries
    // NotAssessed → Basic rung) plus the run's worktime-extent tag. The status path is what we
    // compose; the grade is uniformly Basic for every seeded ad so the {Basic} subset selects
    // the whole run's graded mass and the status filter does the gallring we assert.
    private async Task<JobAdId> SeedBasicGradedAdAsync(string runWorktimeExtent, DateTimeOffset publishedAt)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var (db, scope, clock) = NewScope();
        using (scope)
        {
            var rawPayload =
                $"{{\"id\":\"{externalId}\","
                + $"\"occupation_group\":{{\"concept_id\":\"{PrefGroup}\"}},"
                + $"\"working_hours_type\":{{\"concept_id\":\"{runWorktimeExtent}\"}}}}";

            var jobAd = JobAd.Import(
                title: "Statusfilter-grad-orakel-annons",
                company: Company.Create("Test Company AB").Value,
                description: "beskrivning",
                url: $"https://example.com/jobs/{externalId}",
                external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
                rawPayload: rawPayload,
                publishedAt: publishedAt,
                expiresAt: clock.UtcNow.AddDays(30),
                clock: clock).Value;

            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return jobAd.Id;
        }
    }

    [Fact]
    public async Task SearchPerUser_StatusComposesWithGradeWhere_ReturnsGradedIntersectStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        // All four ads grade Basic (SSYK Match, secondaries NotAssessed). Saved = a strict subset.
        var savedBasic1 = await SeedBasicGradedAdAsync(run, T0.AddDays(10));
        var savedBasic2 = await SeedBasicGradedAdAsync(run, T0.AddDays(9));
        var unsavedBasic1 = await SeedBasicGradedAdAsync(run, T0.AddDays(8));
        var unsavedBasic2 = await SeedBasicGradedAdAsync(run, T0.AddDays(7));
        await SaveJobAdAsync(seeker, savedBasic1);
        await SaveJobAdAsync(seeker, savedBasic2);

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        // {Basic} grade subset selects the whole run (all Basic); savedOnly intersects to the
        // two saved ads → the returned set is (graded ∩ status-filtered).
        var page = await query.SearchPerUserAsync(
            FilterFor(run), Profile(), grades: [MatchGrade.Basic], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: new JobAdStatusFilter(SavedOnly: true, false, false),
            seekerId: seeker, page: 1, pageSize: 100, ct);

        IdSet(page.Items.Select(i => new JobAdId(i.Id))).ShouldBe(
            IdSet([savedBasic1, savedBasic2]), ignoreOrder: true,
            "den status-komponerade match-vägen ska returnera (grad-filtrerad ∩ status-filtrerad) " +
            "— Basic-bandet ∩ savedOnly = de två sparade Basic-annonserna.");
        var returned = page.Items.Select(i => i.Id).ToList();
        returned.ShouldNotContain(unsavedBasic1.Value,
            "en osparad annons (även om den graderar Basic) får inte passera savedOnly-status:en.");
        returned.ShouldNotContain(unsavedBasic2.Value);
        page.TotalCount.ShouldBe(2,
            "TotalCount ska räknas över (grad ∩ status)-mängden (2), inte hela det Basic-graderade " +
            "bandet (4) — status appliceras FÖRE grad-WHERE/count.");
    }

    // ===============================================================
    // 9. An empty status result is honest (no rows when nothing matches) — a savedOnly query
    //    for a seeker who saved nothing returns an empty page with TotalCount 0, not the corpus.
    // ===============================================================

    [Fact]
    public async Task SearchByStatus_SavedOnly_SeekerWithNoSaves_ReturnsEmptyPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var seeker = await SeedSeekerAsync();

        // Three ads exist in the run, but this seeker saved none of them.
        await SeedJobAdAsync(run, T0.AddDays(10));
        await SeedJobAdAsync(run, T0.AddDays(9));
        await SeedJobAdAsync(run, T0.AddDays(8));

        var (scope, query) = NewPerUserQuery();
        using var _ = scope;

        var page = await query.SearchByStatusAsync(
            FilterFor(run), seeker, new JobAdStatusFilter(SavedOnly: true, false, false),
            JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 100, ct);

        page.Items.ShouldBeEmpty("en seeker utan sparade annonser ska få en tom savedOnly-sida.");
        page.TotalCount.ShouldBe(0,
            "TotalCount ska vara 0 (status-filtrerad mängd), aldrig korpus-counten (3).");
    }
}
