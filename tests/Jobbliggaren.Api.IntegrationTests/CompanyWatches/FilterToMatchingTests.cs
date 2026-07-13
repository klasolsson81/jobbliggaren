using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// Bevaknings-reconcile PR-F1 (issue #799, RF-3=3D / RF-5=5A, 2026-07-12) — the Testcontainers oracle
/// for <see cref="IPerUserJobAdSearchQuery.FilterToMatchingAsync"/>, the follow-rail's "endast matchade"
/// membership filter (of a set of ads, those whose Fast grade is ≥Good — the FIXED "matchande"-floor).
/// Real wired Infrastructure query against real Postgres (Testcontainers, NEVER EF-InMemory — InMemory
/// hides BOTH the STORED generated grade shadow columns AND the <c>= ANY</c> FromSql + <c>int[].Contains
/// (&lt;CASE&gt;)</c> grade-rank translation; memory <c>ef_strongly_typed_vo_contains</c>).
/// <para>
/// Sibling of <see cref="CompanyWatchMatchCountTests"/> (same seed idiom — raw_payload drives the grade
/// shadows; each intended grade is verified by the real scorer, never merely assumed). This class owns
/// the DIFFERENT SUT method (<c>FilterToMatchingAsync</c>), so it lives as its own oracle class (house
/// pattern: one oracle concern per class, parity <c>MatchCountOracleTests</c> /
/// <c>MatchSortGradeFilterOracleTests</c>) rather than bloating the count class.
/// </para>
/// <para>
/// THE ORACLE (membership, not just cardinality): the set FilterToMatchingAsync returns EQUALS the set
/// of ads graded ≥Good by the real <see cref="MatchScorer"/> + <see cref="MatchGradeCalculator"/> over
/// the FULL band — CV-skills only elevate WITHIN the notifiable band (Good→Strong→Top) and never lift a
/// Basic/Related/untagged ACROSS the Good threshold, so the cheap SQL Fast ≥Good set is EXACT.
/// </para>
/// </summary>
[Collection("Api")]
public class FilterToMatchingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The candidate's stated preferences (non-empty SSYK → assessable).
    private const string PrefGroup = "grp-f2m-pref";
    private const string PrefRegion = "reg-f2m-pref";
    private const string PrefEmployment = "emp-f2m-pref";
    private const string OtherGroup = "grp-f2m-other";
    private const string RelatedGroup = "grp-f2m-related";
    private const string OtherRegion = "reg-f2m-other";

    // A CV skill concept-id feeding the WITH-skills seeds (proves ≥Good MEMBERSHIP is identical Fast vs
    // Full even when skills re-rank Strong→Top inside the band).
    private const string CvSkillConceptId = "skill-f2m-0001";
    private const string CvSkillDisplay = "F2m-skill";

    // ── SUT + scorer factories (the REAL wired per-user query + the real batch scorer) ──────────

    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewPerUserQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPerUserJobAdSearchQuery>();
        return (scope, query);
    }

    // MatchScorer is internal sealed → built directly with a fresh scoped AppDbContext + the real
    // Swedish analyzer (parity CompanyWatchMatchCountTests.NewScorer).
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scorer = new MatchScorer(db, new LocalTextAnalyzer(new SnowballStemmer()));
        return (scope, scorer);
    }

    // Fast profile: states SSYK + region + employment (the full Fast ladder is reachable). Optional CV
    // skills feed the Full scorer's skill/nice dimensions (the WITH-skills case).
    private static FullCandidateMatchProfile Profile(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    // As Profile, but also declares RelatedGroup as a SUBSTITUTABLE (related) occupation → an ad in
    // RelatedGroup grades Related (rank 2, BELOW Good), never ≥Good. Exercises that the Related-cap is
    // consistent between the SQL GradeRankExpression and the C# calculator (both read RelatedSsyk).
    private static FullCandidateMatchProfile ProfileWithRelated(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: [])
            {
                RelatedSsykGroupConceptIds = [RelatedGroup],
            },
            cvSkillConceptIds);

    // ── Seeding — raw_payload drives the grade shadows (parity CompanyWatchMatchCountTests) ──────

    private async Task<JobAdId> SeedAdAsync(
        string orgNr,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        CancellationToken ct,
        ExtractedTerms? terms = null,
        bool archived = false,
        bool expired = false)
    {
        var externalId = $"f2m-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, orgNr, occupationGroupConceptId, regionConceptId, employmentTypeConceptId);

        var jobAd = JobAd.Import(
            title: "Matchande annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        if (terms is not null)
            jobAd.SetExtractedTerms(terms);

        // Archived: a real domain transition (status='Archived') — excluded by the status='Active' gate.
        if (archived)
            jobAd.Archive(clock);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);

        // JobAd has no domain Expire transition; stamp the value-converted Status shadow via EF direct
        // so the status='Active' gate excludes it.
        if (expired)
        {
            db.Entry(jobAd).Property(nameof(JobAd.Status)).CurrentValue = JobAdStatus.Expired;
            await db.SaveChangesAsync(ct);
        }


        return jobAd.Id;
    }

    // occupation_group / employment_type TOP-LEVEL; region under workplace_address (parity the scorer
    // shadow columns). org.nr nested under employer (irrelevant to FilterToMatchingAsync — it takes
    // explicit ids, not an org.nr set — but kept for payload parity / a valid importable shape).
    private static string BuildRawPayload(
        string externalId,
        string orgNr,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId)
    {
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";

        var addressJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";

        var employmentJson = employmentTypeConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentTypeConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"Test Company AB\",\"organization_number\":\"{orgNr}\"}},"
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson}}}";
    }

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    // Org.nr UNIQUE per test (the [Collection("Api")] Postgres is SHARED) — though FilterToMatchingAsync
    // takes explicit ids (never an org.nr set), a unique value keeps the seed payloads distinct.
    private static string NewOrgNr() => $"55{Random.Shared.Next(10000000, 99999999)}";

    // Grade-band seed helpers (each ad's SSOT grade is verified by the oracle's scorer, never assumed).

    // Strong (rank 4): region Match + employment Match (both secondaries confirmed).
    private Task<JobAdId> SeedStrongAsync(string orgNr, CancellationToken ct, ExtractedTerms? terms = null) =>
        SeedAdAsync(orgNr, PrefGroup, PrefRegion, PrefEmployment, ct, terms);

    // Good (rank 3): exactly one confirmed secondary — region Match + employment NULL.
    private Task<JobAdId> SeedGoodAsync(string orgNr, CancellationToken ct, ExtractedTerms? terms = null) =>
        SeedAdAsync(orgNr, PrefGroup, PrefRegion, employmentTypeConceptId: null, ct, terms);

    // Basic (rank 1): both secondaries NotAssessed (region NULL + employment NULL).
    private Task<JobAdId> SeedBasicNeutralAsync(string orgNr, CancellationToken ct) =>
        SeedAdAsync(orgNr, PrefGroup, regionConceptId: null, employmentTypeConceptId: null, ct);

    // Basic (rank 1) via the CONTRADICTION floor — region NoMatch even though employment Matches.
    private Task<JobAdId> SeedBasicContradictionAsync(string orgNr, CancellationToken ct) =>
        SeedAdAsync(orgNr, PrefGroup, OtherRegion, PrefEmployment, ct);

    // Untagged (rank 0): SSYK NoMatch (ad group present, in neither exact nor related) → no tag.
    private Task<JobAdId> SeedUntaggedAsync(string orgNr, CancellationToken ct) =>
        SeedAdAsync(orgNr, OtherGroup, PrefRegion, PrefEmployment, ct);

    // Related (rank 2, below Good): ad group ∈ related set, ∉ exact set (needs ProfileWithRelated).
    private Task<JobAdId> SeedRelatedAsync(string orgNr, CancellationToken ct) =>
        SeedAdAsync(orgNr, RelatedGroup, PrefRegion, PrefEmployment, ct);

    // =========================================================================================
    // 1. THE ORACLE — FilterToMatchingAsync returns EXACTLY the ≥Good ads ({Good, Strong}), proven
    //    at the SET-MEMBERSHIP level against the real Full scorer + MatchGradeCalculator over the
    //    whole verdict space (Strong/Good/Basic/untagged/Related, with and without CV skills).
    // =========================================================================================

    [Fact]
    public async Task FilterToMatching_ReturnsExactlyGoodAndStrongAds_AcrossVerdictSpace()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        var skillTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        var strongTop = await SeedStrongAsync(orgNr, ct, skillTerms); // Fast Strong / Full Top  → ≥Good
        var strong = await SeedStrongAsync(orgNr, ct);                // Fast Strong / Full Good → ≥Good
        var goodSkill = await SeedGoodAsync(orgNr, ct, skillTerms);   // Fast Good   / Full Good → ≥Good
        var good = await SeedGoodAsync(orgNr, ct);                    // Fast Good   / Full Good → ≥Good
        var basicNeutral = await SeedBasicNeutralAsync(orgNr, ct);    // Basic → below
        var basicContra = await SeedBasicContradictionAsync(orgNr, ct); // Basic (floor) → below
        var untagged = await SeedUntaggedAsync(orgNr, ct);           // untagged → below
        var related = await SeedRelatedAsync(orgNr, ct);             // Related → below Good

        var all = new List<JobAdId>
        {
            strongTop, strong, goodSkill, good, basicNeutral, basicContra, untagged, related,
        };
        var profile = ProfileWithRelated(CvSkillConceptId);

        // FULL-band ≥Good SSOT — grade every seeded ad via the real Full scorer + MatchGradeCalculator
        // and collect the ≥Good SET (Top is a Full-only ≥Good rung; the Fast SQL tops at Strong but the
        // ≥Good MEMBERSHIP must coincide).
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var fullScores = await scorer.ScoreFullBatchAsync(all, profile, ct);
        var expectedGoodOrBetter = all
            .Where(id => MatchGradeCalculator.Grade(fullScores[id].Score, fullScores[id].SsykIsRelated)
                is MatchGrade.Good or MatchGrade.Strong or MatchGrade.Top)
            .ToHashSet();

        // Sanity: the seed genuinely spans the band (not vacuously green) — exactly the four ≥Good ads.
        expectedGoodOrBetter.ShouldBe(
            new[] { strongTop, strong, goodSkill, good }, ignoreOrder: true,
            "Seeden ska ge exakt 4 ≥Good-annonser i Full-bandet (2 Strong-shaped + 2 Good); " +
            "Basic/otaggad/Related ligger under tröskeln.");

        // Prove the Fast-vs-Full asymmetry is REALLY exercised: at least one ad is Full Top.
        all.Any(id => MatchGradeCalculator.Grade(fullScores[id].Score, fullScores[id].SsykIsRelated)
                == MatchGrade.Top)
            .ShouldBeTrue("Minst en annons ska bli Full Top (skill på en Strong) — annars testar " +
                "orakelt inte Fast≡Full-asymmetrin.");

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var matching = await query.FilterToMatchingAsync(profile, all, ct);

        matching.ShouldBe(expectedGoodOrBetter, ignoreOrder: true,
            "FilterToMatchingAsync (Fast ≥Good via den delade GradeRankExpression) MÅSTE returnera " +
            "EXAKT Full-bandets ≥Good-MÄNGD — {Good, Strong}-annonserna — aldrig Basic/Related/otaggad. " +
            "CV-skills lyfter aldrig en match ÖVER Good-tröskeln, så den billiga SQL-mängden är EXAKT.");
    }

    // =========================================================================================
    // 2. Non-Active ads are excluded even at a Good grade (an expired/archived/soft-deleted ad is
    //    not "matchande") — the status='Active' gate, which is the whole exclusion (#821).
    // =========================================================================================

    [Fact]
    public async Task FilterToMatching_ExcludesNonActiveAds_EvenAtGoodGrade()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        var activeGood = await SeedGoodAsync(orgNr, ct);
        var expiredGood = await SeedAdAsync(orgNr, PrefGroup, PrefRegion, employmentTypeConceptId: null, ct, expired: true);
        var archivedGood = await SeedAdAsync(orgNr, PrefGroup, PrefRegion, employmentTypeConceptId: null, ct, archived: true);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var matching = await query.FilterToMatchingAsync(
            Profile(), [activeGood, expiredGood, archivedGood], ct);

        matching.ShouldBe(new[] { activeGood }, ignoreOrder: true,
            "en utgången/arkiverad annons är inte 'matchande' även med Good-grad — bara den Active " +
            "annonsen returneras (status='Active'-grinden ÄR hela exkluderingen, #821).");
    }

    // =========================================================================================
    // 3. Empty jobAdIds → empty set (no DB round-trip needed; short-circuited before the query).
    // =========================================================================================

    [Fact]
    public async Task FilterToMatching_EmptyJobAdIds_ReturnsEmptySet()
    {
        var ct = TestContext.Current.CancellationToken;

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var matching = await query.FilterToMatchingAsync(Profile(), [], ct);

        matching.ShouldBeEmpty("tom jobAdIds → tom mängd");
    }

    // =========================================================================================
    // 4. Fail-fast — an empty-SSYK (non-assessable) profile throws ArgumentException (the dishonest-0
    //    trap: an empty result would MEAN "not assessable", not "zero matches"; the caller MUST branch
    //    on assessability before calling — RF-5 under-fork (i)).
    // =========================================================================================

    [Fact]
    public async Task FilterToMatching_EmptySsykProfile_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;
        var emptySsykProfile = new FullCandidateMatchProfile(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        // Throws on the empty-SSYK profile BEFORE any DB access (the SSYK guard precedes the
        // empty-ids short-circuit), so a throwaway id proves the fail-fast without a seed.
        await Should.ThrowAsync<ArgumentException>(async () =>
            await query.FilterToMatchingAsync(emptySsykProfile, [new JobAdId(Guid.NewGuid())], ct));
    }
}
