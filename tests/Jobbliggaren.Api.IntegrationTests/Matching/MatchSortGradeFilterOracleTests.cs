using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// ADR 0079 STEG 5 (CTO-mandated oracle, 2026-06-23) — THE GRADE-FILTER ORACLE. The
/// anti-drift guard that pins the SQL grade-<b>WHERE</b> in
/// <see cref="PerUserJobAdSearchQuery"/> (and its recomputed count + the decoupled sort)
/// to the C# grade SSOT (<see cref="MatchGradeCalculator.Grade(MatchScore)"/> over
/// <see cref="MatchScorer"/> verdicts). The sibling <see cref="MatchSortOracleTests"/>
/// pins the match-rank ORDER BY; THIS file pins the grade-WHERE: for every non-empty
/// subset of the Fast band {Basic, Good, Strong}, the rows the SQL filter returns must
/// equal EXACTLY the seeded ads whose <c>Grade(MatchScore)</c> (Fast SSOT) is in the
/// subset — and the recomputed <see cref="PagedResult{T}.TotalCount"/> must be the size
/// of the filtered band, not the unfiltered corpus.
/// <para>
/// Runs the REAL wired Infrastructure query against real Postgres (Testcontainers,
/// ALDRIG EF-InMemory — InMemory hides BOTH the <c>HasComputedColumnSql(stored: true)</c>
/// NULL/Match distinction AND the <c>= ANY</c> / <c>int[].Contains(&lt;CASE&gt;)</c>
/// translation of <c>RankInSet</c>; memory <c>ef_strongly_typed_vo_contains</c>). Seeding
/// mirrors <see cref="MatchSortOracleTests"/> / <see cref="MatchScorerIntegrationTests"/>
/// for the raw_payload → STORED shadow column path; the helpers are copied (kept
/// self-contained per the scaffold brief) so this oracle never shares mutable state with
/// the sort oracle.
/// </para>
/// <para>
/// <b>Run-isolation:</b> all seeded ads carry a unique <c>worktime_extent_concept_id</c>
/// (the test-run tag, payload key <c>working_hours_type</c>) and the filter selects on that
/// worktime-extent only. The grade reads occupation/region/employment/municipality shadows,
/// NOT worktime-extent, so the isolation key never perturbs the grade — every seeded ad
/// (incl. untagged, SSYK not-Match) is in the filtered mass before the grade-WHERE runs.
/// </para>
/// </summary>
[Collection("Api")]
public class MatchSortGradeFilterOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The candidate's stated preferences (non-empty SSYK → the handler gate is open).
    private const string PrefGroup = "grp-gradefilter-pref";
    private const string PrefRegion = "reg-gradefilter-pref";
    private const string PrefEmployment = "emp-gradefilter-pref";
    // Non-preferred values the ad can carry to force a NoMatch contradiction (floor).
    private const string OtherGroup = "grp-gradefilter-other";
    private const string OtherRegion = "reg-gradefilter-other";

    // The ort-union (region ∪ municipality) constants.
    private const string PrefMunicipality = "mun-gradefilter-pref";

    // ---------------------------------------------------------------
    // SUT factory — the REAL wired per-user query from DI (proving the registration +
    // the EF translation of the grade-WHERE / count / sort), plus a held scope.
    // ---------------------------------------------------------------
    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewPerUserQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider
            .GetRequiredService<IPerUserJobAdSearchQuery>();
        return (scope, query);
    }

    // The real batch scorer (the C# grade-SSOT inputs). MatchScorer is internal sealed →
    // built directly with a fresh scoped AppDbContext + the real Swedish analyzer (parity
    // MatchSortOracleTests.NewSearchAndScorer / MatchScorerIntegrationTests.NewScorer).
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scorer = new MatchScorer(db, new LocalTextAnalyzer(new SnowballStemmer()));
        return (scope, scorer);
    }

    // Base Fast profile: states SSYK + region + employment so the full Fast grade ladder
    // (Basic/Good/Strong) is reachable; no CV skills (no golden lift — this oracle pins the
    // grade-WHERE, which is the Fast band only).
    private static FullCandidateMatchProfile Profile() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: [PrefGroup],
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            PreferredMunicipalityConceptIds: [PrefMunicipality]),
        CvSkillConceptIds: []);

    // Filter on the unique test-run worktime-extent only → exactly the seeded ads,
    // untagged included (the grade-WHERE then gallrar within that mass).
    private static JobAdFilterCriteria FilterFor(string runWorktimeExtent) => new(
        OccupationGroup: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [runWorktimeExtent],
        Q: null);

    // ---------------------------------------------------------------
    // Seeding — raw_payload drives the STORED shadow columns (parity
    // MatchSortOracleTests.SeedJobAdAsync). null group/region/employment → key omitted →
    // that shadow column is NULL (the NotAssessed-by-NULL path). publishedAt is explicit so
    // the decoupling test (#4) can make recency-order differ from grade-order.
    // ---------------------------------------------------------------
    private async Task<JobAdId> SeedJobAdAsync(
        string runWorktimeExtent,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        DateTimeOffset publishedAt,
        CancellationToken ct,
        string? municipalityConceptId = null)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId,
            runWorktimeExtent, employmentTypeConceptId, municipalityConceptId);

        var jobAd = JobAd.Import(
            title: "Gradefilter-orakel-annons",
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

    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string runWorktimeExtentConceptId,
        string? employmentTypeConceptId,
        string? municipalityConceptId = null)
    {
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";

        var addressJson = BuildWorkplaceAddressJson(regionConceptId, municipalityConceptId);

        var employmentJson = employmentTypeConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentTypeConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson},"
            + $"\"working_hours_type\":{{\"concept_id\":\"{runWorktimeExtentConceptId}\"}}}}";
    }

    private static string BuildWorkplaceAddressJson(
        string? regionConceptId, string? municipalityConceptId)
    {
        if (regionConceptId is null && municipalityConceptId is null)
        {
            return "null";
        }

        var keys = new List<string>(2);
        if (regionConceptId is not null)
        {
            keys.Add($"\"region_concept_id\":\"{regionConceptId}\"");
        }

        if (municipalityConceptId is not null)
        {
            keys.Add($"\"municipality_concept_id\":\"{municipalityConceptId}\"");
        }

        return $"{{{string.Join(",", keys)}}}";
    }

    private static string NewRunWorktimeExtent() => $"wt-gradefilter-{Guid.NewGuid():N}"[..23];

    // ---------------------------------------------------------------
    // Seed-helpers per intended Fast grade (each ad's SSOT grade is asserted in the test
    // body, never assumed). The grade ladder: SSYK Match earns a tag; a stated region/
    // employment the ad contradicts (NoMatch) floors to Basic; otherwise
    // 1 + #confirmed secondaries (region/employment Match).
    // ---------------------------------------------------------------

    // Strong (rank 3): region Match + employment Match (both secondaries confirmed).
    private Task<JobAdId> SeedStrongAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Good (rank 2): exactly one confirmed secondary — region Match + employment NULL
    // (NotAssessed, does NOT floor).
    private Task<JobAdId> SeedGoodAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, PrefRegion, null, publishedAt, ct);

    // Basic (rank 1) — both secondaries NotAssessed (region NULL + employment NULL).
    private Task<JobAdId> SeedBasicNeutralAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, null, null, publishedAt, ct);

    // Basic (rank 1) via the CONTRADICTION floor — region NoMatch (OtherRegion) even though
    // employment Matches; the stated-region contradiction floors to Basic.
    private Task<JobAdId> SeedBasicContradictionAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, publishedAt, ct);

    // Strong via the ORT-UNION municipality leg — region non-preferred but municipality
    // preferred → ort Match via the union; with employment Match → 2 confirmed → Strong.
    private Task<JobAdId> SeedStrongViaMunicipalityAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, publishedAt, ct,
            municipalityConceptId: PrefMunicipality);

    // Untagged (rank 0): SSYK NoMatch (ad group present, not in profile) → no tag.
    private Task<JobAdId> SeedUntaggedSsykNoMatchAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Untagged (rank 0): SSYK NotAssessed (null group) → no tag.
    private Task<JobAdId> SeedUntaggedSsykNullAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, null, PrefRegion, PrefEmployment, publishedAt, ct);

    // The 7 non-empty subsets of the Fast band — singletons, all pairs, and all-three.
    private static IReadOnlyList<IReadOnlyList<MatchGrade>> AllNonEmptyBandSubsets() =>
    [
        [MatchGrade.Basic],
        [MatchGrade.Good],
        [MatchGrade.Strong],
        [MatchGrade.Basic, MatchGrade.Good],
        [MatchGrade.Basic, MatchGrade.Strong],
        [MatchGrade.Good, MatchGrade.Strong],
        [MatchGrade.Basic, MatchGrade.Good, MatchGrade.Strong],
    ];

    // ===============================================================
    // 1. Grade WHERE ≡ Grade(Fast) over the verdict-tuple space.
    //    For EACH of the 7 non-empty band subsets, the returned ad-id set EQUALS exactly
    //    the seeded ads whose C# Grade(MatchScore) (Fast SSOT) is in the subset.
    // ===============================================================

    [Fact]
    public async Task GradeFilter_ReturnedSet_EqualsGradeSsotForEveryBandSubset()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Seed ≥2 ads per Fast band (Basic, Good, Strong) + untagged + a contradiction-
        // floored Basic + an ort-union Strong. publishedAt is monotonically decreasing so
        // recency does not accidentally coincide with grade order.
        var seeded = new List<JobAdId>
        {
            // --- Strong (≥2)
            await SeedStrongAsync(run, t.AddDays(20), ct),
            await SeedStrongAsync(run, t.AddDays(19), ct),
            // --- Strong via the ort-union municipality leg (region non-preferred)
            await SeedStrongViaMunicipalityAsync(run, t.AddDays(18), ct),

            // --- Good (≥2)
            await SeedGoodAsync(run, t.AddDays(15), ct),
            await SeedGoodAsync(run, t.AddDays(14), ct),

            // --- Basic (≥2): one neutral, one contradiction-floored
            await SeedBasicNeutralAsync(run, t.AddDays(10), ct),
            await SeedBasicContradictionAsync(run, t.AddDays(9), ct),

            // --- Untagged (rank 0) — never selectable by any grade
            await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct),
            await SeedUntaggedSsykNullAsync(run, t.AddDays(4), ct),
        };

        var profile = Profile();
        var filter = FilterFor(run);

        // C# grade-SSOT: grade every seeded ad. null grade (untagged) maps to "no band".
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync(seeded, profile.Fast, ct);
        var gradeById = seeded.ToDictionary(
            id => id.Value,
            id => MatchGradeCalculator.Grade(scores[id]));

        // Sanity: the seed genuinely spans Basic, Good, Strong AND untagged (so no subset
        // assertion is vacuously green on a degenerate set).
        var distinctGrades = gradeById.Values.Distinct().ToList();
        distinctGrades.ShouldContain(MatchGrade.Basic);
        distinctGrades.ShouldContain(MatchGrade.Good);
        distinctGrades.ShouldContain(MatchGrade.Strong);
        distinctGrades.ShouldContain((MatchGrade?)null,
            "Seeden ska innehålla minst en otaggad annons (rank 0) så positiv-only-" +
            "exkluderingen testas på riktigt.");

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        foreach (var subset in AllNonEmptyBandSubsets())
        {
            // The C# SSOT's expected id-set for this subset: ads whose (non-null) grade is
            // in the subset.
            var expectedIds = gradeById
                .Where(kvp => kvp.Value is { } g && subset.Contains(g))
                .Select(kvp => kvp.Key)
                .ToHashSet();

            var page = await query.SearchPerUserAsync(
                filter, profile, grades: subset, sort: JobAdSortBy.PublishedAtDesc,
                orderByMatchRank: true, page: 1, pageSize: 100, since: null, ct);

            var returnedIds = page.Items.Select(i => i.Id).ToHashSet();

            returnedIds.ShouldBe(expectedIds, ignoreOrder: true,
                $"Grad-WHERE:t ska returnera EXAKT de annonser vars Grade(MatchScore) ligger i " +
                $"delmängden [{string.Join(", ", subset)}] — annars har SQL-grad-WHERE:t och " +
                "C#-SSOT:en drivit isär (anti-drift-orakel för grad-filtret).");

            // The recomputed TotalCount must equal the in-band size (the line-86 fix), not
            // the unfiltered corpus.
            page.TotalCount.ShouldBe(expectedIds.Count,
                $"TotalCount ska räknas om över den grad-filtrerade mängden för delmängden " +
                $"[{string.Join(", ", subset)}] (rad-86-fixen) — inte över hela korpusen.");
        }
    }

    // ===============================================================
    // 2. Untagged (rank 0) excluded when any grade selected (positive-only).
    // ===============================================================

    [Fact]
    public async Task GradeFilter_ExcludesUntaggedRankZero_ForEveryBandSubset()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // One ad per positive band + two untagged ads (SSYK NoMatch and SSYK null). With
        // ANY grade selected, neither untagged ad may appear.
        await SeedStrongAsync(run, t.AddDays(5), ct);
        await SeedGoodAsync(run, t.AddDays(4), ct);
        await SeedBasicNeutralAsync(run, t.AddDays(3), ct);
        var untaggedNoMatch = await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(2), ct);
        var untaggedNull = await SeedUntaggedSsykNullAsync(run, t.AddDays(1), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        foreach (var subset in AllNonEmptyBandSubsets())
        {
            var page = await query.SearchPerUserAsync(
                filter, profile, grades: subset, sort: JobAdSortBy.PublishedAtDesc,
                orderByMatchRank: true, page: 1, pageSize: 100, since: null, ct);

            var returnedIds = page.Items.Select(i => i.Id).ToList();
            returnedIds.ShouldNotContain(untaggedNoMatch.Value,
                $"Otaggad (SSYK NoMatch, rank 0) får aldrig dyka upp när grad [{string.Join(", ", subset)}] " +
                "är vald — grad-filtret är positiv-only.");
            returnedIds.ShouldNotContain(untaggedNull.Value,
                $"Otaggad (SSYK null, rank 0) får aldrig dyka upp när grad [{string.Join(", ", subset)}] " +
                "är vald — grad-filtret är positiv-only.");
        }
    }

    // ===============================================================
    // 3. Count correctness (the line-86 fix) + pagination over the filtered set.
    //    TotalCount = in-band size, NOT the unfiltered corpus; a small pageSize paginates
    //    correctly over the filtered band (no phantom page).
    // ===============================================================

    [Fact]
    public async Task GradeFilter_TotalCount_EqualsInBandSize_NotUnfilteredCorpus()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Seed so the unfiltered corpus (9) clearly differs from the in-band size we assert.
        // Strong: 4 in-band. The rest (5) are out-of-band for a Strong-only filter.
        for (var i = 0; i < 4; i++)
        {
            await SeedStrongAsync(run, t.AddDays(20 - i), ct);
        }

        await SeedGoodAsync(run, t.AddDays(10), ct);
        await SeedGoodAsync(run, t.AddDays(9), ct);
        await SeedBasicNeutralAsync(run, t.AddDays(6), ct);
        await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(3), ct);
        await SeedUntaggedSsykNullAsync(run, t.AddDays(2), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        // Strong-only band → exactly the 4 Strong ads, NOT the 9-ad corpus.
        var fullPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, page: 1, pageSize: 100, since: null, ct);

        fullPage.TotalCount.ShouldBe(4,
            "TotalCount ska vara antalet annonser i Strong-bandet (4), inte hela korpusen (9) " +
            "— grad-WHERE:ts count måste räknas om (rad-86-fixen).");
        fullPage.Items.Count.ShouldBe(4);

        // Pagination over the filtered band: pageSize 2 < 4 in-band → page 1 has 2 items,
        // TotalCount still 4, and no phantom items leak onto a page beyond the band.
        var firstPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, page: 1, pageSize: 2, since: null, ct);

        firstPage.Items.Count.ShouldBe(2,
            "Sida 1 med pageSize 2 ska ha exakt 2 träffar ur det grad-filtrerade bandet.");
        firstPage.TotalCount.ShouldBe(4,
            "TotalCount ska vara bandets storlek (4) oavsett pageSize — paginering över den " +
            "grad-filtrerade mängden, ingen spök-sida.");
        firstPage.TotalPages.ShouldBe(2);

        var secondPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, page: 2, pageSize: 2, since: null, ct);

        secondPage.Items.Count.ShouldBe(2,
            "Sida 2 ska ha de resterande 2 in-band-träffarna — ingen otaggad/out-of-band " +
            "annons får läcka in.");
        secondPage.TotalCount.ShouldBe(4);

        // No overlap between page 1 and page 2, and the union is exactly the 4 in-band ads.
        var pagedIds = firstPage.Items.Select(i => i.Id)
            .Concat(secondPage.Items.Select(i => i.Id))
            .ToList();
        pagedIds.Distinct().Count().ShouldBe(4,
            "Sida 1 ∪ sida 2 ska vara exakt de 4 in-band-annonserna utan dubbletter (ingen " +
            "spök-paginering).");
    }

    // ===============================================================
    // 4. Decoupling — the grade-WHERE is independent of the sort axis.
    //    grades:[Good, Strong] + orderByMatchRank:false + sort:PublishedAtDesc → the SAME
    //    graded subset BUT ordered by recency, so a later-published Good precedes an
    //    earlier-published Strong (proving the WHERE and the ORDER BY are independent).
    // ===============================================================

    [Fact]
    public async Task GradeFilter_WithPlainSort_FiltersByBandButOrdersByRecency_NotGradeRank()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Recency order is deliberately the INVERSE of grade rank within the {Good, Strong}
        // band: the Good ad is published LATER than the Strong ad. If the sort were grade-
        // rank (orderByMatchRank), Strong would precede Good; with the plain PublishedAtDesc
        // axis, the later-published Good must precede the earlier-published Strong.
        var laterGood = await SeedGoodAsync(run, t.AddDays(10), ct);   // higher recency, lower grade
        var earlierStrong = await SeedStrongAsync(run, t.AddDays(5), ct); // lower recency, higher grade

        // An out-of-band Basic + an untagged ad that must NOT appear in the filtered set.
        var basic = await SeedBasicNeutralAsync(run, t.AddDays(20), ct); // newest, but out of band
        var untagged = await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(30), ct); // newest, rank 0

        var profile = Profile();
        var filter = FilterFor(run);

        // Confirm the C# SSOT grades (proves the seed is what the test claims).
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync(
            [laterGood, earlierStrong, basic, untagged], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[laterGood]).ShouldBe(MatchGrade.Good);
        MatchGradeCalculator.Grade(scores[earlierStrong]).ShouldBe(MatchGrade.Strong);
        MatchGradeCalculator.Grade(scores[basic]).ShouldBe(MatchGrade.Basic);
        MatchGradeCalculator.Grade(scores[untagged]).ShouldBeNull();

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        var page = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Good, MatchGrade.Strong],
            sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: false,
            page: 1, pageSize: 100, since: null, ct);

        var returnedIds = page.Items.Select(i => i.Id).ToList();

        // The WHERE still gallrar to exactly the {Good, Strong} band (Basic + untagged out).
        returnedIds.ShouldBe([laterGood.Value, earlierStrong.Value], ignoreOrder: true,
            "Grad-WHERE:t ska gallra till exakt {Good, Strong}-bandet oberoende av sort — " +
            "Basic och otaggade utesluts.");
        page.TotalCount.ShouldBe(2);

        // The ORDER BY is the recency axis, NOT grade rank: the later-published Good
        // precedes the earlier-published Strong. This proves the WHERE and the ORDER BY are
        // independent (a higher-graded ad does NOT sort first under the plain axis).
        returnedIds.IndexOf(laterGood.Value)
            .ShouldBeLessThan(returnedIds.IndexOf(earlierStrong.Value),
                "Med orderByMatchRank:false ska ordningen vara publishedAt DESC (recency), INTE " +
                "grad-rank — en senare publicerad Good ska ligga FÖRE en tidigare publicerad " +
                "Strong. Detta bevisar att grad-WHERE:t och ORDER BY:n är frikopplade.");
    }

    // ===============================================================
    // 5. Top is unreachable in the Fast band → a Strong-band filter is the ceiling.
    //    The Fast Grade(MatchScore) overload never yields Top (it has no must-have/skill
    //    input). Filtering grades:[Strong] returns the highest-Fast-band ads, and no seeded
    //    ad's Fast grade is Top — so Strong is the top of the filterable band.
    // ===============================================================

    [Fact]
    public async Task GradeFilter_StrongBand_IsTheCeiling_FastGradeNeverYieldsTop()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The strongest reachable Fast tuple: SSYK Match + region Match + employment Match
        // (both secondaries confirmed). The Fast overload tops at Strong here — Top requires
        // the Full overload's must-have/skill signal, which the Fast band cannot express.
        var strong = await SeedStrongAsync(run, t.AddDays(3), ct);
        var good = await SeedGoodAsync(run, t.AddDays(2), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync([strong, good], profile.Fast, ct);

        // The Fast SSOT never produces Top — the strongest tuple caps at Strong.
        MatchGradeCalculator.Grade(scores[strong]).ShouldBe(MatchGrade.Strong,
            "Det starkaste Fast-tupeln (yrke+ort+anställning Match) toppar på Strong i Fast-" +
            "bandet — Top kräver Full-overloadens must-have/skill-signal, oåtkomlig i Fast.");
        MatchGradeCalculator.Grade(scores[strong]).ShouldNotBe(MatchGrade.Top);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        // Strong is the top of the filterable band: the Strong filter returns the strongest
        // ad (and only it), confirming Strong is the ceiling the grade-WHERE can express.
        var page = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, page: 1, pageSize: 100, since: null, ct);

        var returnedIds = page.Items.Select(i => i.Id).ToList();
        returnedIds.ShouldBe([strong.Value],
            "Strong-bandet är taket: grad-filtret returnerar den starkaste Fast-annonsen " +
            "(och bara den) — det finns ingen Top-nivå att filtrera på i Fast-bandet.");
        page.TotalCount.ShouldBe(1);
    }
}
