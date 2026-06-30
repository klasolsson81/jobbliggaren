using Jobbliggaren.Api.IntegrationTests.Infrastructure;
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
/// ADR 0079 STEG 6 (CTO-mandated oracle) — THE MATCH-COUNT ORACLE. The anti-drift guard for
/// <see cref="IPerUserJobAdSearchQuery.CountPerUserAsync"/>: the live-notis number
/// ("Det finns X jobb som matchar din profil") must NEVER diverge from the linked /jobb
/// page's <see cref="Application.Common.PagedResult{T}.TotalCount"/> for the SAME profile +
/// grade-set. They share the same filter-SPOT (<c>JobAdSearchComposition.ApplyFilter</c>)
/// and the same shared <c>GradeRankExpression</c>; THIS oracle pins that the standalone
/// COUNT-query path produces the identical cardinality as the list path's recomputed
/// TotalCount, and that both equal the C# grade-SSOT (<see cref="MatchGradeCalculator"/>).
/// <para>
/// Runs the REAL wired Infrastructure query against real Postgres (Testcontainers, NEVER
/// EF-InMemory — InMemory hides BOTH the <c>HasComputedColumnSql(stored: true)</c>
/// NULL/Match distinction AND the <c>= ANY</c> / <c>int[].Contains(&lt;CASE&gt;)</c>
/// translation; memory <c>ef_strongly_typed_vo_contains</c>). Seeding mirrors the sibling
/// <see cref="MatchSortGradeFilterOracleTests"/> (kept self-contained per the scaffold brief
/// so the count oracle never shares mutable state with the grade-filter oracle).
/// </para>
/// </summary>
[Collection("Api")]
public class MatchCountOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The candidate's stated preferences (non-empty SSYK → matchable).
    private const string PrefGroup = "grp-matchcount-pref";
    private const string PrefRegion = "reg-matchcount-pref";
    private const string PrefEmployment = "emp-matchcount-pref";
    private const string PrefMunicipality = "mun-matchcount-pref";
    // Non-preferred values to force a NoMatch contradiction (floor) / an untagged ad.
    private const string OtherGroup = "grp-matchcount-other";
    private const string OtherRegion = "reg-matchcount-other";

    // PR-4 (#300, ADR 0084) — a ssyk-4 in the RELATED set only (∉ exact). The count is LIST-ONLY
    // (ADR-question D): Related is filterable on /jobb but does NOT drive the headline count
    // {Good, Strong}. The profile states RelatedSsykGroupConceptIds = [RelatedGroup] so the related
    // ad is grade-tagged (Related), yet must be EXCLUDED from the headline-band count.
    private const string RelatedGroup = "grp-matchcount-related";
    private static readonly string[] ExactGroups = [PrefGroup];
    private static readonly string[] RelatedGroups = [RelatedGroup];

    // ---------------------------------------------------------------
    // SUT factory — the REAL wired per-user query from DI (proves the registration + the EF
    // translation of CountPerUserAsync's count + SearchPerUserAsync's recomputed TotalCount).
    // ---------------------------------------------------------------
    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewPerUserQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPerUserJobAdSearchQuery>();
        return (scope, query);
    }

    // The real batch scorer (the C# grade-SSOT inputs). MatchScorer is internal sealed → built
    // directly with a fresh scoped AppDbContext + the real Swedish analyzer (parity the
    // grade-filter oracle's NewScorer).
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scorer = new MatchScorer(db, new LocalTextAnalyzer(new SnowballStemmer()));
        return (scope, scorer);
    }

    // Base Fast profile: states SSYK + region + employment + municipality so the full Fast
    // grade ladder (Basic/Good/Strong) is reachable; no CV skills (no golden lift — count is
    // the Fast band only).
    private static FullCandidateMatchProfile Profile() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: ExactGroups,
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            PreferredMunicipalityConceptIds: [PrefMunicipality])
        {
            // PR-4 (#300, ADR 0084): the related set the grade-WHERE tags the Related rung from.
            // Empty in pre-PR-4 → behaviour-inert; non-empty so the list-only-vs-count regression
            // below can seed a genuinely Related-tagged ad. The headline count must still exclude it.
            RelatedSsykGroupConceptIds = RelatedGroups,
        },
        CvSkillConceptIds: []);

    // Filter on the unique test-run worktime-extent only → exactly the seeded ads, untagged
    // included (the grade-WHERE then gallrar within that mass).
    private static JobAdFilterCriteria FilterFor(string runWorktimeExtent) => new(
        OccupationGroup: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [runWorktimeExtent],
        Employer: [],
        Q: null);

    // ---------------------------------------------------------------
    // Seeding — raw_payload drives the STORED shadow columns. null group/region/employment →
    // key omitted → that shadow column is NULL (the NotAssessed-by-NULL path).
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
            title: "Matchcount-orakel-annons",
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

    private static string NewRunWorktimeExtent() => $"wt-matchcount-{Guid.NewGuid():N}"[..23];

    // ---------------------------------------------------------------
    // Seed-helpers per intended Fast grade (each ad's SSOT grade is asserted in the test body,
    // never assumed).
    // ---------------------------------------------------------------

    // Strong (rank 3): region Match + employment Match (both secondaries confirmed).
    private Task<JobAdId> SeedStrongAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Good (rank 2): exactly one confirmed secondary — region Match + employment NULL.
    private Task<JobAdId> SeedGoodAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, PrefRegion, null, publishedAt, ct);

    // Basic (rank 1): both secondaries NotAssessed (region NULL + employment NULL).
    private Task<JobAdId> SeedBasicNeutralAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, null, null, publishedAt, ct);

    // Basic (rank 1) via the CONTRADICTION floor — region NoMatch even though employment Matches.
    private Task<JobAdId> SeedBasicContradictionAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, publishedAt, ct);

    // Related (PR-4 #300): occupation group ∈ the RELATED set only (∉ exact) → grade-tagged Related
    // (flat cap, even with both secondaries Match). Filterable on /jobb but NOT in the headline count.
    private Task<JobAdId> SeedRelatedAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, RelatedGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Untagged (rank 0): SSYK NoMatch (ad group present, not in profile) → no tag.
    private Task<JobAdId> SeedUntaggedSsykNoMatchAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Untagged (rank 0): SSYK NotAssessed (null group) → no tag.
    private Task<JobAdId> SeedUntaggedSsykNullAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, null, PrefRegion, PrefEmployment, publishedAt, ct);

    // ===============================================================
    // 1. THE LOAD-BEARING COHERENCE INVARIANT — count == list TotalCount for the SAME
    //    profile + grade-set. For [Good,Strong], [Strong], and a singleton, the standalone
    //    CountPerUserAsync must equal the list path's recomputed TotalCount. This pins that
    //    the notice number can NEVER diverge from the linked /jobb page's count.
    // ===============================================================

    [Fact]
    public async Task Count_EqualsListTotalCount_ForTheSameProfileAndGradeSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // A known mixed distribution (Strong ×3, Good ×2, Basic ×2, untagged ×2).
        await SeedStrongAsync(run, t.AddDays(20), ct);
        await SeedStrongAsync(run, t.AddDays(19), ct);
        await SeedStrongAsync(run, t.AddDays(18), ct);
        await SeedGoodAsync(run, t.AddDays(15), ct);
        await SeedGoodAsync(run, t.AddDays(14), ct);
        await SeedBasicNeutralAsync(run, t.AddDays(10), ct);
        await SeedBasicContradictionAsync(run, t.AddDays(9), ct);
        await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct);
        await SeedUntaggedSsykNullAsync(run, t.AddDays(4), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        // The grade-sets that matter: the headline band {Good, Strong}, the {Strong}
        // ceiling, and a {Good} singleton.
        IReadOnlyList<IReadOnlyList<MatchGrade>> gradeSets =
        [
            [MatchGrade.Good, MatchGrade.Strong],
            [MatchGrade.Strong],
            [MatchGrade.Good],
        ];

        foreach (var grades in gradeSets)
        {
            var count = await query.CountPerUserAsync(filter, profile, grades, ct);

            // pageSize:1 — the count is what we assert, not the items; the list path's
            // recomputed TotalCount is the coherence target.
            var page = await query.SearchPerUserAsync(
                filter, profile, grades, sort: JobAdSortBy.PublishedAtDesc,
                orderByMatchRank: false, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 1, ct);

            count.ShouldBe(page.TotalCount,
                $"CountPerUserAsync MÅSTE vara lika med list-vägens recomputed TotalCount för " +
                $"samma profil + grad-set [{string.Join(", ", grades)}] — annars kan notis-siffran " +
                "divergera från den länkade /jobb-sidans count (de delar ApplyFilter + " +
                "GradeRankExpression; detta är det bärande koherens-orakelet för STEG 6).");
        }
    }

    // ===============================================================
    // 2. count == the C# grade-SSOT cardinality. CountPerUserAsync(..., [Good, Strong]) must
    //    equal the number of seeded ads whose Grade(MatchScore) ∈ {Good, Strong} (computed
    //    via the real scorer + MatchGradeCalculator — the same SSOT the grade-filter oracle uses).
    // ===============================================================

    [Fact]
    public async Task Count_EqualsGradeSsotCardinality_ForHeadlineBand()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var seeded = new List<JobAdId>
        {
            await SeedStrongAsync(run, t.AddDays(20), ct),
            await SeedStrongAsync(run, t.AddDays(19), ct),
            await SeedGoodAsync(run, t.AddDays(15), ct),
            await SeedGoodAsync(run, t.AddDays(14), ct),
            await SeedBasicNeutralAsync(run, t.AddDays(10), ct),
            await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct),
            await SeedUntaggedSsykNullAsync(run, t.AddDays(4), ct),
        };

        var profile = Profile();
        var filter = FilterFor(run);

        // C# grade-SSOT: grade every seeded ad and count those in {Good, Strong}.
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync(seeded, profile.Fast, ct);
        var headline = new[] { MatchGrade.Good, MatchGrade.Strong };
        var expectedCount = seeded.Count(id =>
            MatchGradeCalculator.Grade(scores[id]) is { } g && headline.Contains(g));

        // Sanity: the seed genuinely spans the band (Strong + Good present) so the assertion
        // is not vacuously green.
        expectedCount.ShouldBe(4,
            "Seeden ska ge exakt 4 annonser i {Good, Strong}-bandet (2 Strong + 2 Good).");

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        var count = await query.CountPerUserAsync(filter, profile, headline, ct);

        count.ShouldBe(expectedCount,
            "CountPerUserAsync([Good, Strong]) ska vara antalet seeded annonser vars " +
            "Grade(MatchScore) ligger i {Good, Strong} (C#-SSOT) — annars har SQL-count:en och " +
            "grad-SSOT:en drivit isär.");
    }

    // ===============================================================
    // 3. Empty grades → count over the FULL filtered (active) set, no grade-gallring.
    // ===============================================================

    [Fact]
    public async Task Count_WithEmptyGrades_EqualsFullFilteredSet_NoGradeGallring()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // 6 ads across every band incl. untagged → with NO grade filter, ALL 6 count
        // (untagged is excluded only when a grade is selected; empty grades counts the
        // whole filtered set).
        await SeedStrongAsync(run, t.AddDays(20), ct);
        await SeedGoodAsync(run, t.AddDays(15), ct);
        await SeedBasicNeutralAsync(run, t.AddDays(10), ct);
        await SeedBasicContradictionAsync(run, t.AddDays(9), ct);
        await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct);
        await SeedUntaggedSsykNullAsync(run, t.AddDays(4), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        var count = await query.CountPerUserAsync(filter, profile, grades: [], ct);

        count.ShouldBe(6,
            "Tom grad-mängd ska räkna över HELA den filtrerade (aktiva) mängden (6, inkl. " +
            "otaggade) — ingen grad-gallring när inga grader valts.");

        // Coherence still holds at the empty-grade boundary: count == list TotalCount.
        var page = await query.SearchPerUserAsync(
            filter, profile, grades: [], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: false, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 1, ct);
        count.ShouldBe(page.TotalCount,
            "Även vid tom grad-mängd ska CountPerUserAsync == list-vägens TotalCount.");
    }

    // ===============================================================
    // 4. PR-4 (#300, ADR 0084 fråga D) — the count is LIST-ONLY: a Related-grade ad does NOT
    //    contribute to the headline count {Good, Strong}. A Related ad is filterable on /jobb (the
    //    {Related} band counts it) but the notification headline never includes Related — pinning
    //    that broadening the gate did not silently inflate the live-notis number.
    // ===============================================================

    [Fact]
    public async Task Count_HeadlineBand_ExcludesRelatedAds_ButRelatedBandCountsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // One Strong + one Good (the headline band = 2) + two Related ads (excluded from headline).
        var strong = await SeedStrongAsync(run, t.AddDays(20), ct);
        var good = await SeedGoodAsync(run, t.AddDays(15), ct);
        var related1 = await SeedRelatedAsync(run, t.AddDays(12), ct);
        var related2 = await SeedRelatedAsync(run, t.AddDays(11), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        // Cross-check the SSOT: the two Related ads grade to Related (Fast band + isRelated cap) via
        // the broadened full scorer; Strong/Good do not carry the related bit.
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scored = await scorer.ScoreFullBatchAsync([strong, good, related1, related2], profile, ct);
        MatchGradeCalculator.Grade(scored[related1].Score.Fast, scored[related1].SsykIsRelated)
            .ShouldBe(MatchGrade.Related);
        MatchGradeCalculator.Grade(scored[related2].Score.Fast, scored[related2].SsykIsRelated)
            .ShouldBe(MatchGrade.Related);
        scored[strong].SsykIsRelated.ShouldBeFalse();
        scored[good].SsykIsRelated.ShouldBeFalse();

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        // Headline band {Good, Strong} → exactly the Strong + Good ads (2). The two Related ads are
        // NOT counted — the headline count is list-only-blind to Related (ADR-question D).
        var headlineCount = await query.CountPerUserAsync(
            filter, profile, [MatchGrade.Good, MatchGrade.Strong], ct);
        headlineCount.ShouldBe(2,
            "Headline-counten {Good, Strong} ska EXKLUDERA Related-annonserna (counten är list-only; " +
            "Related driver inte notis-siffran, ADR 0084 fråga D).");

        // But the {Related} band IS reachable on /jobb → counts exactly the two Related ads (proving
        // they are genuinely Related-tagged, not silently dropped — just absent from the headline).
        var relatedCount = await query.CountPerUserAsync(filter, profile, [MatchGrade.Related], ct);
        relatedCount.ShouldBe(2,
            "{Related}-bandet ska räkna de två Related-annonserna (list-filtrerbart) — Related är " +
            "exkluderat från headline-counten, inte från korpusen.");
    }
}
