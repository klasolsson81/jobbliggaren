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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #452 (ADR 0087 D5-tillägg) — the Testcontainers proof for
/// <see cref="IPerUserJobAdSearchQuery.CountPerUserByEmployerAsync"/>, the hub "matchande
/// annonser"-count per watched employer. Real wired Infrastructure query against real Postgres
/// (Testcontainers, NEVER EF-InMemory — InMemory hides BOTH the STORED generated
/// <c>organization_number</c> + grade shadow columns AND the <c>= ANY</c> /
/// <c>int[].Contains(&lt;CASE&gt;)</c> grade-rank translation; memory
/// <c>ef_strongly_typed_vo_contains</c>).
/// <para>
/// This file carries TWO CTO-mandated proofs:
/// <list type="number">
/// <item><b>THE ORACLE</b> — the Fast-band ≥Good count from the cheap SQL
/// <c>GROUP BY organization_number</c> EQUALS the Full-band ≥Good count computed via the real
/// <see cref="MatchScorer"/> + <see cref="MatchGradeCalculator.Grade(FullMatchScore, bool)"/>.
/// This is the load-bearing correctness claim: CV-skills only elevate a match WITHIN the
/// notifiable band (Good→Strong→Top) and never lift a Basic across the Good threshold, so the
/// cheap SQL count is EXACT, not an approximation — Top's SQL-incomputability (G3-OPT-A) is
/// irrelevant to a ≥Good COUNT.</item>
/// <item><b>THE DIRECT INTEGRATION TEST</b> — the per-org.nr dict is correct over a mix of
/// matching/non-matching/Archived/soft-deleted ads across two employers (the seeding
/// recipe mirrors #447 <see cref="CompanyWatchesTests"/> + the grade shadows of
/// <see cref="Matching.MatchCountOracleTests"/>).</item>
/// </list>
/// </para>
/// Seeding combines BOTH the org.nr (nested <c>employer.organization_number</c>) AND the grade
/// shadows (top-level <c>occupation_group</c> / <c>employment_type</c> / <c>working_hours_type</c>,
/// nested <c>workplace_address.region_concept_id</c>) in ONE raw_payload — the org.nr is the GROUP
/// key, the grade shadows drive the <c>GradeRankExpression</c> WHERE.
/// </summary>
[Collection("Api")]
public class CompanyWatchMatchCountTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The candidate's stated preferences (non-empty SSYK → matchable).
    private const string PrefGroup = "grp-cwmatch-pref";
    private const string PrefRegion = "reg-cwmatch-pref";
    private const string PrefEmployment = "emp-cwmatch-pref";
    private const string OtherGroup = "grp-cwmatch-other";
    private const string OtherRegion = "reg-cwmatch-other";

    // A CV skill concept-id used by the oracle's WITH-skills seed (proves that even when skills are
    // present, the ≥Good BAND MEMBERSHIP is identical Fast vs Full — skills only re-rank inside it).
    private const string CvSkillConceptId = "skill-cwmatch-0001";
    private const string CvSkillDisplay = "Cwmatch-skill";

    // The headline band the hub counts (parity ListCompanyWatchesQueryHandler.MatchingGrades).
    private static readonly IReadOnlyList<MatchGrade> HeadlineGrades =
        [MatchGrade.Good, MatchGrade.Strong];

    // ── SUT + scorer factories (the REAL wired per-user query + the real batch scorer) ──────────

    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewPerUserQuery()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPerUserJobAdSearchQuery>();
        return (scope, query);
    }

    // MatchScorer is internal sealed → built directly with a fresh scoped AppDbContext + the real
    // Swedish analyzer (parity MatchCountOracleTests.NewScorer).
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scorer = new MatchScorer(db, new LocalTextAnalyzer(new SnowballStemmer()));
        return (scope, scorer);
    }

    // Fast profile: states SSYK + region + employment (so the full Fast grade ladder is reachable).
    // Optional CV skills feed the Full scorer's skill/nice dimensions (the oracle's WITH-skills case).
    private static FullCandidateMatchProfile Profile(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    // ── Seeding — raw_payload drives BOTH the org.nr column AND the grade shadows ────────────────

    private async Task<JobAdId> SeedAdAsync(
        string orgNr,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        CancellationToken ct,
        ExtractedTerms? terms = null,
        bool archived = false)
    {
        var externalId = $"cwm-{Guid.NewGuid():N}";

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
            clock: clock, declaredContacts: []).Value;

        if (terms is not null)
            jobAd.SetExtractedTerms(terms);

        // Archived: a real domain transition (status='Archived') — excluded by the status='Active'
        // predicate (parity #447's archived case). Archived is the ONLY non-Active witness this
        // class carries, deliberately (#886 CTO-bind): the count is KEYED on organization_number
        // and Erase() nulls that column, so an Erased row exits via the org.nr key rather than the
        // status gate and cannot witness it — and the fabricated Expired stamp that used to sit
        // here pinned the predicate against a row no writer could produce (#843 fiction class).
        if (archived)
            jobAd.Archive(clock);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);

        return jobAd.Id;
    }

    // occupation_group / employment_type are TOP-LEVEL; region under workplace_address; the org.nr
    // NESTED under employer (raw_payload->'employer'->>'organization_number', ADR 0087 D1). A
    // grade-neutral working_hours_type is omitted (not needed — the per-employer test isolates on
    // the private org.nr, not a worktime-extent filter).
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

    // Org.nrs UNIQUE per test (the [Collection("Api")] Postgres is SHARED, never reset — a private
    // org.nr keeps every count deterministic; memory api_integration_shared_db_contamination). Third
    // digit ≥ 2 → legal entity; OrganizationNumber.Create validates only 10 digits (no Luhn).
    private static string NewOrgNr() =>
        // 10 digits, third digit 5 (legal), the tail unique per test run.
        $"55{Random.Shared.Next(10000000, 99999999)}";

    // Grade-band seed helpers per intended Fast grade (each ad's SSOT grade is verified by the
    // oracle's scorer, never merely assumed).

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

    // Untagged (rank 0): SSYK NoMatch (ad group present, not in profile) → no tag.
    private Task<JobAdId> SeedUntaggedAsync(string orgNr, CancellationToken ct) =>
        SeedAdAsync(orgNr, OtherGroup, PrefRegion, PrefEmployment, ct);

    // =========================================================================================
    // 1. THE ORACLE (CTO-mandated) — the Fast-band ≥Good count from CountPerUserByEmployerAsync
    //    EQUALS the Full-band ≥Good count computed via the real scorer + MatchGradeCalculator over
    //    the verdict-tuple space. Proves CV-skills never cross the Good threshold, so the cheap SQL
    //    count is EXACT. Seeds ads across region/employment/skill combinations for ONE watched org.nr.
    // =========================================================================================

    [Fact]
    public async Task CountPerUserByEmployer_EqualsFullBandGradeSsotCardinality_ForHeadlineBand()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();

        var skillTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        // A distribution spanning the whole verdict space for ONE watched employer:
        //   Strong ×2 (one WITH a shared CV skill — would be Top on Full; still ≥Good),
        //   Good ×2 (one WITH a shared CV skill),
        //   Basic (neutral + contradiction),
        //   untagged. Skills present so the Fast-vs-Full asymmetry (Fast Strong vs Full Good, and
        //   Full Top) is genuinely exercised — the ≥Good MEMBERSHIP must still coincide.
        var seeded = new List<JobAdId>
        {
            await SeedStrongAsync(orgNr, ct, skillTerms), // Fast Strong / Full Top → both ≥Good
            await SeedStrongAsync(orgNr, ct),             // Fast Strong / Full Good → both ≥Good
            await SeedGoodAsync(orgNr, ct, skillTerms),   // Fast Good / Full Good → both ≥Good
            await SeedGoodAsync(orgNr, ct),               // Fast Good / Full Good → both ≥Good
            await SeedBasicNeutralAsync(orgNr, ct),       // Basic → below ≥Good on both
            await SeedBasicContradictionAsync(orgNr, ct), // Basic (floor) → below on both
            await SeedUntaggedAsync(orgNr, ct),           // untagged → below on both
        };

        var profile = Profile(CvSkillConceptId);

        // FULL-band ≥Good SSOT — grade every seeded ad via the real Full scorer +
        // MatchGradeCalculator.Grade(FullMatchScore) and count those in {Good, Strong, Top}. Top is a
        // Full-only ≥Good rung; the Fast SQL count tops at Strong but the ≥Good SET must be identical.
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var fullScores = await scorer.ScoreFullBatchAsync(seeded, profile, ct);
        var expectedFullBandAtLeastGood = seeded.Count(id =>
            MatchGradeCalculator.Grade(fullScores[id].Score, fullScores[id].SsykIsRelated)
                is MatchGrade.Good or MatchGrade.Strong or MatchGrade.Top);

        // Sanity: the seed genuinely spans the band (not vacuously green). 4 ads are ≥Good.
        expectedFullBandAtLeastGood.ShouldBe(4,
            "Seeden ska ge exakt 4 ≥Good-annonser i Full-bandet (2 Strong-shaped + 2 Good).");

        // Prove the Fast-vs-Full asymmetry is REALLY exercised: at least one ad is Full Top (skill on
        // a Strong) — if it were not, the oracle would not be testing the interesting claim.
        seeded.Any(id =>
                MatchGradeCalculator.Grade(fullScores[id].Score, fullScores[id].SsykIsRelated) == MatchGrade.Top)
            .ShouldBeTrue("Minst en annons ska bli Full Top (skill på en Strong) — annars testar " +
                "orakelt inte Fast≡Full-asymmetrin.");

        // The cheap SQL count (Fast-band ≥Good = {Good, Strong}) over the GROUP BY.
        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var dict = await query.CountPerUserByEmployerAsync([orgNr], profile, HeadlineGrades, ct);

        dict.GetValueOrDefault(orgNr).ShouldBe(expectedFullBandAtLeastGood,
            "CountPerUserByEmployerAsync (Fast-band ≥Good via GradeRankExpression) MÅSTE vara lika " +
            "med Full-bandets ≥Good-kardinalitet (C#-SSOT via MatchGradeCalculator.Grade(FullMatchScore)) " +
            "— CV-skills lyfter aldrig en match ÖVER Good-tröskeln, så den billiga SQL-counten är EXAKT, " +
            "inte en approximation (ADR 0087 D5-tillägg Fast≡Full-orakel).");
    }

    // =========================================================================================
    // 2. DIRECT INTEGRATION — the per-org.nr dict is correct over a mix of matching / Basic /
    //    untagged / Archived ads across TWO employers. Proves the status='Active'
    //    exclusion (the whole exclusion — JobAd has no soft-delete axis, #821) and the org.nr GROUP key.
    // =========================================================================================

    [Fact]
    public async Task CountPerUserByEmployer_CountsMatchingActiveAds_ExcludesArchived()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgA = NewOrgNr();
        var orgB = NewOrgNr();

        // orgA: 2 matching Active (Strong + Good = ≥Good) + 1 Basic (below) + 1 untagged (below)
        //       + 1 Archived-matching (excluded by status)
        //       → the ≥Good Active count must be exactly 2.
        await SeedStrongAsync(orgA, ct);
        await SeedGoodAsync(orgA, ct);
        await SeedBasicNeutralAsync(orgA, ct);
        await SeedUntaggedAsync(orgA, ct);
        await SeedAdAsync(orgA, PrefGroup, PrefRegion, PrefEmployment, ct, archived: true);

        // orgB: 1 matching Active (Strong = ≥Good) → count 1. Proves per-org.nr GROUP keying (orgA's
        //       ads never bleed into orgB's count).
        await SeedStrongAsync(orgB, ct);

        var profile = Profile();

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var dict = await query.CountPerUserByEmployerAsync([orgA, orgB], profile, HeadlineGrades, ct);

        dict.GetValueOrDefault(orgA).ShouldBe(2,
            "orgA ska räkna exakt 2 ≥Good Active-annonser (Strong + Good) — Basic/untagged under " +
            "tröskeln, Archived exkluderad av status='Active', soft-deleted exkluderad av " +
            "den globala soft-delete-query-filtren (ADR 0048).");
        dict.GetValueOrDefault(orgB).ShouldBe(1,
            "orgB ska räkna exakt 1 ≥Good Active-annons — orgA:s annonser ska aldrig blöda in " +
            "(per-org.nr GROUP BY).");
    }

    [Fact]
    public async Task CountPerUserByEmployer_OmitsEmployersWithZeroMatches()
    {
        // An employer with only Basic/untagged ads is ABSENT from the dict (the caller
        // defaults it to 0 — a null-vs-0 distinction the handler owns).
        var ct = TestContext.Current.CancellationToken;
        var orgWithMatch = NewOrgNr();
        var orgWithoutMatch = NewOrgNr();

        await SeedStrongAsync(orgWithMatch, ct);
        await SeedBasicNeutralAsync(orgWithoutMatch, ct);
        await SeedUntaggedAsync(orgWithoutMatch, ct);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var dict = await query.CountPerUserByEmployerAsync(
            [orgWithMatch, orgWithoutMatch], Profile(), HeadlineGrades, ct);

        dict.GetValueOrDefault(orgWithMatch).ShouldBe(1);
        dict.ContainsKey(orgWithoutMatch).ShouldBeFalse(
            "En arbetsgivare utan ≥Good-match ska vara FRÅNVARANDE ur dicten (anroparen defaultar " +
            "den till 0) — inte en 0-post.");
    }

    [Fact]
    public async Task CountPerUserByEmployer_EmptyOrganizationNumbers_ReturnsEmptyDict()
    {
        var ct = TestContext.Current.CancellationToken;

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var dict = await query.CountPerUserByEmployerAsync([], Profile(), HeadlineGrades, ct);

        dict.ShouldBeEmpty();
    }

    [Fact]
    public async Task CountPerUserByEmployer_EmptyGrades_ReturnsEmptyDict()
    {
        // Defensive: an empty grade band → empty dict (the handler already gates the no-SSYK case,
        // but the port must not degenerate to "count all" for an empty selectedRanks).
        var ct = TestContext.Current.CancellationToken;
        var orgNr = NewOrgNr();
        await SeedStrongAsync(orgNr, ct);

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var dict = await query.CountPerUserByEmployerAsync([orgNr], Profile(), grades: [], ct);

        dict.ShouldBeEmpty();
    }
}
