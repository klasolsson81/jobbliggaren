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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// F4-14 (ADR 0076 Decision 4/5; Klas-bind 2026-06-19) — THE ORACLE (D4). The
/// anti-drift guard that pins the SQL match-sort ORDER BY in
/// <see cref="MatchSortedJobAdSearchQuery"/> to the C# grade SSOT
/// (<see cref="MatchGradeCalculator"/> over <see cref="MatchScorer"/> verdicts).
/// Runs the REAL Infrastructure match-sort query against real Postgres
/// (Testcontainers, ALDRIG EF-InMemory — InMemory hides BOTH the
/// <c>HasComputedColumnSql(stored: true)</c> NULL/Match distinction AND any
/// <c>= ANY</c>/<c>list.Contains(EF.Property)</c> translation failure; memory
/// <c>ef_strongly_typed_vo_contains</c>). Mirrors
/// <see cref="MatchScorerIntegrationTests"/> for the raw_payload → STORED shadow
/// column seeding.
/// <para>
/// <b>The oracle invariant:</b> for the SAME (profile, ad-shadow) inputs, the SQL
/// rank must agree with <see cref="MatchGradeCalculator.Grade"/> for every row —
/// grade DESC, untagged last, tie-break <c>publishedAt</c> DESC then <c>Id</c>.
/// We compute each seeded ad's expected grade via the C# SSOT
/// (<see cref="MatchScorer.ScoreBatchAsync"/> + the calculator) and assert no
/// lower-graded ad precedes a higher-graded one in the SQL-ordered page.
/// </para>
/// <para>
/// <b>Isolation against the shared corpus:</b> all seeded ads carry a unique
/// <c>worktime_extent_concept_id</c> (the test-run tag, payload key
/// <c>working_hours_type</c>) and the filter selects on that worktime-extent only.
/// The grade reads occupation/region/employment/municipality shadows, NOT
/// worktime-extent, so the isolation key never perturbs the grade — every seeded ad
/// (incl. the untagged ones, SSYK not-Match) is in the filtered mass. (Before Spår 3
/// the run tag was the municipality; the ort-union grade now reads municipality, so the
/// isolation moved to the grade-neutral worktime-extent — CTO verdict D: separate the
/// kommun signal from the run-isolation key.)
/// </para>
/// <para>
/// <b>PR-B1 (RE-BIND G3-OPT-A) — the bound sort ≠ requirement-aware grade divergence:</b>
/// the requirement-aware <see cref="MatchGradeCalculator.Grade(FullMatchScore)"/> now caps
/// the visible grade on must-have coverage. The SQL match-sort, by contrast, ranks on the
/// FAST band (+ the F4-15 golden top-5 skill lift) and structurally CANNOT assess must-have
/// (binary GIN <c>?|</c> on top-5 plaintext skills, no requirement partition, no DEK —
/// R5-REBIND Option H). The Fast <c>Grade(MatchScore)</c> overload is UNCHANGED, so the
/// existing oracle (SQL rank ≡ Fast grade) STILL HOLDS. The new divergence test below pins
/// the bound consequence: two ads with the SAME Fast tuple but DIFFERENT must-have coverage
/// are NOT separated by the SQL sort (it cannot see must-have), while
/// <see cref="MatchGradeCalculator.Grade(FullMatchScore)"/> WOULD grade them differently —
/// "sort is an honest Fast-band coarsening; grade is the per-ad requirement-aware truth."
/// </para>
/// </summary>
[Collection("Api")]
public class MatchSortOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The candidate's stated preferences (non-empty SSYK → the handler gate is open).
    // Region/employment are stated so the grade exercises Match vs NoMatch vs
    // NotAssessed (a null ad shadow on a STATED dimension is NotAssessed, never NoMatch).
    private const string PrefGroup = "grp-oracle-pref";
    private const string PrefRegion = "reg-oracle-pref";
    private const string PrefEmployment = "emp-oracle-pref";
    // A different (non-preferred) value the ad can carry to force a NoMatch contradiction.
    private const string OtherGroup = "grp-oracle-other";
    private const string OtherRegion = "reg-oracle-other";
    private const string OtherEmployment = "emp-oracle-other";

    // ---------------------------------------------------------------
    // SUT factory — the REAL wired match-sort query from DI (proving the
    // registration + the EF translation of the rank ORDER BY), plus a held scope.
    // ---------------------------------------------------------------
    private (IServiceScope Scope, IMatchSortedJobAdSearchQuery Query) NewMatchSort()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider
            .GetRequiredService<IMatchSortedJobAdSearchQuery>();
        return (scope, query);
    }

    // The real default-sort port (for the same-filter-mass invariant) + the real
    // batch scorer (the C# grade-SSOT inputs). MatchScorer is internal sealed → built
    // directly with a fresh scoped AppDbContext + the real Swedish analyzer (parity
    // MatchScorerIntegrationTests.NewScorer).
    private (IServiceScope Scope, IJobAdSearchQuery Search, MatchScorer Scorer) NewSearchAndScorer()
    {
        var scope = _factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IJobAdSearchQuery>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scorer = new MatchScorer(db, new LocalTextAnalyzer(new SnowballStemmer()));
        return (scope, search, scorer);
    }

    // F4-15 (ADR 0076 Decision 6): SearchByMatchAsync now takes a FullCandidateMatchProfile.
    // This F4-14-ladder oracle exercises the GRADE ladder only (no CV skills) → an EMPTY
    // CvSkillConceptIds, which produces NO golden lift (order ≡ F4-14). The golden top tier
    // is pinned separately in MatchSortGoldenRungOracleTests.
    private static FullCandidateMatchProfile Profile() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: [PrefGroup],
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            // Spår 3 PR-A — 5:e dimension; tom (municipality testas i PR-B, ej här).
            PreferredMunicipalityConceptIds: []),
        CvSkillConceptIds: []);

    // Filter on the unique test-run worktime-extent only → exactly the seeded ads,
    // untagged included. Spår 3: the ort-union grade now reads municipality, so the
    // run-isolation key moved off municipality to worktime-extent — a filterable
    // dimension the scorer never reads (CTO verdict D: separate the kommun signal from
    // the run-isolation key). Municipality is left free as a genuine ort signal.
    private static JobAdFilterCriteria FilterFor(string runWorktimeExtent) => new(
        OccupationGroup: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [runWorktimeExtent],
        Q: null);

    // ---------------------------------------------------------------
    // Seeding — raw_payload drives the STORED shadow columns. occupation_group +
    // employment_type + working_hours_type (the run-isolation worktime-extent) are
    // TOP-LEVEL; region lives under workplace_address (parity MatchScorerIntegrationTests
    // + JobAdGeneratedColumnsTests). null group/region/employment → key omitted → that
    // shadow column is NULL (the NotAssessed-by-NULL path). publishedAt is explicit so we
    // can prove the tie-break within a grade.
    // ---------------------------------------------------------------
    private async Task<JobAdId> SeedJobAdAsync(
        string runWorktimeExtent,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        DateTimeOffset publishedAt,
        CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId,
            runWorktimeExtent, employmentTypeConceptId);

        var jobAd = JobAd.Import(
            title: "Oracle-annons",
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

    // workplace_address carries region_concept_id (grade input) only; the test-run
    // isolation key rides the TOP-LEVEL working_hours_type → worktime_extent_concept_id
    // shadow (NAMNGLAPP: column worktime_extent ↔ payload working_hours_type), which the
    // grade never reads. A null region → workplace_address null (region shadow NULL, the
    // NotAssessed-by-NULL path); no municipality is seeded here (kept free for PR-C's
    // genuine ort-union sort cases).
    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string runWorktimeExtentConceptId,
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
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson},"
            + $"\"working_hours_type\":{{\"concept_id\":\"{runWorktimeExtentConceptId}\"}}}}";
    }

    private static string NewRunWorktimeExtent() => $"wt-oracle-{Guid.NewGuid():N}"[..20];

    // Maps a positive grade to the SQL rank integer (higher = better). null grade
    // (untagged) → 0. The SQL ORDER BY produces this ordering server-side; this is
    // the SAME ladder MatchGradeCalculator encodes (the agreement the oracle pins).
    private static int RankOf(MatchGrade? grade) => grade switch
    {
        null => 0,
        MatchGrade.Basic => 1,
        MatchGrade.Good => 2,
        MatchGrade.Strong => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "Okänd grad."),
    };

    // ===============================================================
    // 1. Ordering ≡ grade SSOT — the anti-drift oracle
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_OrderingEqualsGradeSsot_AcrossEveryReachableVerdictTuple()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Seed rows spanning every reachable verdict tuple, multiple per grade.
        // Profile: SSYK=[PrefGroup], region=[PrefRegion], employment=[PrefEmployment].
        // Grade ladder (SSYK must Match to earn a tag; a stated region/employment the
        // ad contradicts (NoMatch) floors to Basic; otherwise 1 + #confirmed secondaries).
        var seeded = new List<JobAdId>
        {
            // --- Strong (2 confirmed secondaries) — region Match + employment Match
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(10), ct),
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(9), ct),

            // --- Good (1 confirmed) — region Match + employment NotAssessed (null shadow)
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, null, t.AddDays(8), ct),
            // --- Good (1 confirmed) — region NotAssessed (null) + employment Match
            await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(7), ct),

            // --- Basic (0 confirmed, both NotAssessed) — region null + employment null
            await SeedJobAdAsync(run, PrefGroup, null, null, t.AddDays(6), ct),
            // --- Basic (contradiction floors) — region NoMatch (other), employment Match
            await SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(5), ct),
            // --- Basic (contradiction floors) — region Match, employment NoMatch (other)
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, OtherEmployment, t.AddDays(4), ct),

            // --- Untagged (SSYK NoMatch — ad group present, not in profile) — sorts last
            await SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, t.AddDays(3), ct),
            // --- Untagged (SSYK NotAssessed via null group) — sorts last
            await SeedJobAdAsync(run, null, PrefRegion, PrefEmployment, t.AddDays(2), ct),
        };

        var profile = Profile();
        var filter = FilterFor(run);

        // SQL-ordered page (the real wired impl).
        var (sortScope, matchSort) = NewMatchSort();
        using var _ = sortScope;
        var page = await matchSort.SearchByMatchAsync(
            filter, profile, page: 1, pageSize: 100, since: null, ct);

        page.Items.Count.ShouldBe(seeded.Count,
            "Match-sorten ska returnera HELA den filtrerade mängden (otaggade inkluderade, " +
            "bara sist) — ingen annons filtreras bort.");

        // C# grade-SSOT: score every seeded ad with the SAME (embedded Fast) profile,
        // grade it. The grade ladder reads the Fast tuple (the match-sort's Full profile
        // carries the same Fast).
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync(seeded, profile.Fast, ct);

        var expectedRankById = seeded.ToDictionary(
            id => id.Value,
            id => RankOf(MatchGradeCalculator.Grade(scores[id])));

        // Sanity: the seed genuinely spans all four rank buckets (3,2,1,0) so the
        // oracle is not vacuously green on a degenerate set.
        var distinctRanks = expectedRankById.Values.Distinct().OrderBy(r => r).ToList();
        distinctRanks.ShouldBe([0, 1, 2, 3],
            "Seed-mängden ska täcka alla fyra rank-hinkar (otaggad/Basic/Good/Strong).");

        // THE ORACLE: the SQL-ordered ranks must be non-increasing — no lower-graded
        // ad precedes a higher-graded one. SQL rank ≡ MatchGradeCalculator for every row.
        var sqlOrderedRanks = page.Items
            .Select(dto => expectedRankById[dto.Id])
            .ToList();

        sqlOrderedRanks.ShouldBe(
            sqlOrderedRanks.OrderByDescending(r => r).ToList(),
            "SQL-rankens ordning ska vara grad fallande (otaggade sist) och spegla " +
            "MatchGradeCalculator EXAKT — annars har SQL-ranken och C#-SSOT:en drivit isär " +
            $"(anti-drift-orakel). SQL-ordnade rank: [{string.Join(", ", sqlOrderedRanks)}].");
    }

    // ===============================================================
    // 2. Tie-break — within the same grade, newer publishedAt first, then Id
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_WithinSameGrade_OrdersByPublishedAtDescThenId()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Three Strong ads with DISTINCT publishedAt → newest first.
        var older = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), ct);
        var newest = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(3), ct);
        var middle = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), ct);

        // Two Strong ads with EQUAL publishedAt → broken deterministically by Id.
        var equalA = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(5), ct);
        var equalB = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(5), ct);

        var (sortScope, matchSort) = NewMatchSort();
        using var _ = sortScope;
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), Profile(), page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();

        // Distinct-publishedAt subset: newest → middle → older.
        orderedIds.IndexOf(newest.Value).ShouldBeLessThan(orderedIds.IndexOf(middle.Value),
            "Nyare publishedAt ska ligga före äldre inom samma grad.");
        orderedIds.IndexOf(middle.Value).ShouldBeLessThan(orderedIds.IndexOf(older.Value),
            "Nyare publishedAt ska ligga före äldre inom samma grad.");

        // Equal-publishedAt subset: broken deterministically on Id (.ThenBy(j => j.Id)).
        // The canonical Id order is whatever Postgres' uuid ordering gives via the same
        // EF expression — derived empirically (NOT predicted with .NET Guid.CompareTo,
        // which differs from Postgres' uuid byte order). The match-sort must reproduce it.
        var canonicalOrder = await CanonicalIdOrderAsync(equalA, equalB, ct);
        orderedIds.IndexOf(canonicalOrder[0].Value)
            .ShouldBeLessThan(orderedIds.IndexOf(canonicalOrder[1].Value),
                "Lika publishedAt ska brytas deterministiskt på Id — match-sortens " +
                "ordning ska matcha den kanoniska EF .ThenBy(j => j.Id)-ordningen.");
    }

    // Learns the canonical .ThenBy(j => j.Id) order Postgres produces for exactly the
    // two ids (the EXACT expression the match-sort tie-break uses), so the assertion
    // pins the real contract instead of a .NET Guid.CompareTo guess (which differs from
    // Postgres' uuid byte order). Filters by OR-equality on the strongly-typed key
    // (translation-safe — j.Id == id is what MatchScorer.ScoreAsync uses; list.Contains
    // over JobAdId does NOT translate, memory ef_strongly_typed_vo_contains).
    private async Task<List<JobAdId>> CanonicalIdOrderAsync(
        JobAdId first, JobAdId second, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobAds.AsNoTracking()
            .Where(j => j.Id == first || j.Id == second)
            .OrderBy(j => j.Id)
            .Select(j => j.Id)
            .ToListAsync(ct);
    }

    // ===============================================================
    // 3. Same filter mass — match-sort reorders, never filters out
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_TotalCount_EqualsDefaultSortTotalCount_ForSameFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Mixed grades incl. untagged — the untagged must STILL be counted (present,
        // just last). 5 ads, one of every reachable bucket.
        await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(5), ct); // Strong
        await SeedJobAdAsync(run, PrefGroup, PrefRegion, null, t.AddDays(4), ct);           // Good
        await SeedJobAdAsync(run, PrefGroup, null, null, t.AddDays(3), ct);                 // Basic
        await SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, t.AddDays(2), ct); // untagged (SSYK NoMatch)
        await SeedJobAdAsync(run, null, null, null, t.AddDays(1), ct);                      // untagged (SSYK null)

        var filter = FilterFor(run);

        var (sortScope, matchSort) = NewMatchSort();
        using var _ = sortScope;
        var matchPage = await matchSort.SearchByMatchAsync(
            filter, Profile(), page: 1, pageSize: 100, since: null, ct);

        var (searchScope, search, _) = NewSearchAndScorer();
        using var __ = searchScope;
        var defaultPage = await search.SearchAsync(
            new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, 1, 100, null), ct);

        matchPage.TotalCount.ShouldBe(defaultPage.TotalCount,
            "Match-sorten ska reordna, aldrig filtrera bort — TotalCount måste vara " +
            "identisk med default-sortens för samma filter (otaggade inkluderade).");
        matchPage.TotalCount.ShouldBe(5);
        matchPage.Items.Count.ShouldBe(5);
    }

    // ===============================================================
    // 4. NotAssessed ≠ NoMatch — the classic !list.Contains(col) bug guard
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_StatedRegionPref_NullRegionShadow_RanksHigherThanContradictingShadow()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Same SSYK Match + same employment Match on both → the ONLY difference is the
        // region shadow. The profile STATES a region preference (PrefRegion).
        //   notAssessed: region shadow NULL → NotAssessed → does NOT floor → Strong-track
        //                (employment Match confirmed) → Good (1 confirmed secondary).
        //   noMatch:     region shadow = OtherRegion (not in pref) → NoMatch → FLOORS to Basic.
        // The naive `!preferred.Contains(col)` bug treats NULL as "not contained" and
        // would WRONGLY floor the notAssessed ad to Basic too — this test catches that.
        var notAssessed = await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(2), ct);
        var noMatch = await SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(1), ct);

        var profile = Profile();

        // Confirm the C# SSOT agrees with the intended grades (proves the seed is right).
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync([notAssessed, noMatch], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[notAssessed]).ShouldBe(MatchGrade.Good,
            "NULL region-shadow med ANGIVEN region-preferens = NotAssessed → golvar EJ " +
            "(employment Match → Good).");
        MatchGradeCalculator.Grade(scores[noMatch]).ShouldBe(MatchGrade.Basic,
            "Motsägande region-shadow (NoMatch) golvar till Basic.");

        var (sortScope, matchSort) = NewMatchSort();
        using var ___ = sortScope;
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), profile, page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.IndexOf(notAssessed.Value).ShouldBeLessThan(orderedIds.IndexOf(noMatch.Value),
            "NotAssessed (NULL-shadow) ska rangordnas HÖGRE än NoMatch (motsägande shadow) " +
            "— SQL-ranken får inte behandla NULL som 'inte i listan' (klassiska " +
            "!list.Contains(col)-buggen).");
    }

    // ===============================================================
    // 5. PR-B1 (RE-BIND G3-OPT-A) — the bound "sort ≠ requirement-aware grade" divergence.
    //    Two ads with the SAME Fast tuple (Strong: occ+region+employment all Match) but
    //    DIFFERENT must-have coverage (one Match, one NoMatch), scored against a CV that
    //    HAS skills. The SQL match-sort cannot see must-have → it does NOT separate them by
    //    must-have (they tie on the Fast band, broken only by publishedAt/Id). Meanwhile
    //    MatchGradeCalculator.Grade(FullMatchScore) WOULD grade them differently (Strong-ish
    //    vs Good). This is the bound G3-OPT-A divergence — honest by construction, pinned,
    //    never silent.
    //
    //    Provenance-safe overlap (F4-2/F4-3 lesson): we make BOTH ads carry the SAME skill
    //    concept-id in extracted_terms and put that id in BOTH the CV-skill set AND the ad's
    //    must_have requirement partition for the "must-have Match" ad, while the other ad
    //    carries the skill term but a DISJOINT must_have term → must-have NoMatch. The CV
    //    top-5 plaintext set (sort path) and the full CV-skill set (grade path) both contain
    //    the shared skill, so the SORT sees equal skill overlap for both ads (no golden
    //    split) and the GRADE sees different must-have coverage.
    // ===============================================================

    private const string SharedSkillConceptId = "skill-shared-diverge-1";
    private const string SharedSkillDisplay = "Delad-skill";
    private const string OtherMustHaveConceptId = "skill-other-musthave-1";
    private const string OtherMustHaveDisplay = "Annat-skallkrav";

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    private static ExtractedTerm MustHaveTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Requirement,
            Source: ExtractedTermSource.MustHave, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    // Same seeding as SeedJobAdAsync, plus an extracted_terms VO (→ STORED extracted_lexemes
    // GIN for the sort's top-5 skill overlap AND the in-memory must-have partition the
    // grade reads).
    private async Task<JobAdId> SeedJobAdWithTermsAsync(
        string runWorktimeExtent,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        DateTimeOffset publishedAt,
        ExtractedTerms terms,
        CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId,
            runWorktimeExtent, employmentTypeConceptId);

        var jobAd = JobAd.Import(
            title: "Divergence-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        jobAd.SetExtractedTerms(terms);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // A FULL profile whose CV-skill set contains the shared skill (so BOTH ads overlap on
    // skill for the sort path AND the grade's must-have set-difference is computable).
    private static FullCandidateMatchProfile ProfileWithCvSkill(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                // Spår 3 PR-A — 5:e dimension; tom (municipality testas i PR-B, ej här).
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    [Fact]
    public async Task SearchByMatch_DoesNotSeparateSameFastTupleByMustHave_WhileGradeWould_DivergenceG3OptA()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Both ads: SAME Fast tuple (occ + region + employment all Match → Strong-band) and
        // BOTH carry the shared skill term (equal sort-path skill overlap → no golden split).
        // mustHaveMatch:   must_have = the shared skill (CV covers it) → must-have Match.
        // mustHaveNoMatch: must_have = a DISJOINT skill (CV does not cover it) → must-have NoMatch.
        var mustHaveMatchTerms = ExtractedTerms.From(
        [
            SkillTerm(SharedSkillConceptId, SharedSkillDisplay),
            MustHaveTerm(SharedSkillConceptId, SharedSkillDisplay),
        ]);
        var mustHaveNoMatchTerms = ExtractedTerms.From(
        [
            SkillTerm(SharedSkillConceptId, SharedSkillDisplay),
            MustHaveTerm(OtherMustHaveConceptId, OtherMustHaveDisplay),
        ]);

        // Give mustHaveNoMatch the NEWER publishedAt so, IF the sort ignored must-have (the
        // bound G3-OPT-A behaviour), the newer ad sorts FIRST despite its worse grade —
        // proving the sort is a Fast-band coarsening, not the requirement-aware grade.
        var mustHaveMatch = await SeedJobAdWithTermsAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), mustHaveMatchTerms, ct);
        var mustHaveNoMatch = await SeedJobAdWithTermsAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), mustHaveNoMatchTerms, ct);

        var profile = ProfileWithCvSkill(SharedSkillConceptId);

        // ---- The GRADE axis (per-ad requirement-aware truth) WOULD separate them. ----
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var full = await scorer.ScoreFullBatchAsync([mustHaveMatch, mustHaveNoMatch], profile, ct);

        var gradeMatch = MatchGradeCalculator.Grade(full[mustHaveMatch]);
        var gradeNoMatch = MatchGradeCalculator.Grade(full[mustHaveNoMatch]);

        // must-have Match + both secondaries + skill Match → Top; must-have NoMatch caps at
        // Good. The point is only that they DIFFER (the grade sees must-have).
        gradeMatch.ShouldBe(MatchGrade.Top,
            "must-have Match + occ/region/employment Match + skill Match → Top (grade ser must-have).");
        gradeNoMatch.ShouldBe(MatchGrade.Good,
            "must-have NoMatch golvar grade-yta under Strong → Good (grade ser must-have).");
        gradeMatch.ShouldNotBe(gradeNoMatch,
            "Grade-axeln SEPARERAR de två annonserna på must-have-täckning.");

        // ---- The SORT axis (fast coarse relevance) does NOT separate them by must-have. ----
        var (sortScope, matchSort) = NewMatchSort();
        using var ___ = sortScope;
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), profile, page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(2, "Båda annonserna ska returneras.");

        // The sort cannot see must-have → both share the Fast band + equal top-5 skill lift,
        // so the tie-break (publishedAt DESC) wins: the NEWER must-have-NoMatch ad sorts
        // FIRST — exactly the bound G3-OPT-A divergence (a higher-graded ad does NOT
        // necessarily sort first, because the sort is a Fast-band coarsening, not the grade).
        orderedIds.IndexOf(mustHaveNoMatch.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(mustHaveMatch.Value),
                "G3-OPT-A: SQL-sorten ser INTE must-have → de två annonserna skiljs inte åt på " +
                "must-have-täckning; tie-break (nyare publishedAt) avgör. Sorten är en ärlig " +
                "Fast-band-coarsening, INTE den requirement-aware graden — graden (per-ad) " +
                "skiljer dem (Top vs Good), sorten gör det inte. Detta är den bundna " +
                "divergensen (G3-OPT-A), pinnad, aldrig tyst.");
    }
}
