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
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// F4-14 (ADR 0076 Decision 4/5; Klas-bind 2026-06-19) — THE ORACLE (D4). The
/// anti-drift guard that pins the SQL match-sort ORDER BY in
/// <see cref="PerUserJobAdSearchQuery"/> to the C# grade SSOT
/// (<see cref="MatchGradeCalculator"/> over <see cref="MatchScorer"/> verdicts).
/// Runs the REAL Infrastructure match-sort query against real Postgres
/// (Testcontainers, ALDRIG EF-InMemory — InMemory hides BOTH the
/// <c>HasComputedColumnSql(stored: true)</c> NULL/Match distinction AND any
/// <c>= ANY</c>/<c>list.Contains(EF.Property)</c> translation failure; memory
/// <c>ef_strongly_typed_vo_contains</c>). Mirrors
/// <see cref="MatchScorerIntegrationTests"/> for the raw_payload → facet-column
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
/// <para>
/// <b>The UNIFIED Fast/Full rule (#371/#382, senior-cto-advisor bind):</b> the /jobb list's
/// SORT + grade-FILTER + headline-COUNT all pin to the FAST band
/// (<see cref="MatchGradeCalculator.Grade(MatchScore)"/>, mirrored in SQL); the card BADGE is the Full
/// requirement-aware grade (<see cref="MatchGradeCalculator.Grade(FullMatchScore)"/>). The Fast/Full
/// divergence is bound + bidirectional + oracle-pinned (G3-OPT-A) — this file pins the SORT side; the
/// filter-side twin lives in <see cref="MatchSortGradeFilterOracleTests"/>. See ADR 0076 (amendment)
/// and issues #371/#382.
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

    // Spår 3 PR-C — the ort-union (region ∪ municipality) constants. PrefMunicipality is the
    // candidate's stated kommun preference; OtherMunicipality is a present-but-non-preferred
    // kommun the ad can carry to force the "same län, different kommun" NoMatch (the ort
    // floor) without ever touching the region dimension.
    private const string PrefMunicipality = "mun-oracle-pref";
    private const string OtherMunicipality = "mun-oracle-other";

    // #477 Low 1 / #552 — a containment län (the parent län of a preferred kommun). Under #552 a
    // STATED-ort NULL shadow is NoMatch (floors), so the ONLY path to a NotAssessed ort secondary
    // — hence the ONLY reachable Good for a both-stated profile — is the #477 containment carve-out:
    // a LÄN-ONLY ad (region = this län, municipality NULL) whose region ∈ ContainmentRegionConceptIds
    // reads NotAssessed (neither floors nor lifts) → employment Match makes it Good. DISTINCT from
    // PrefRegion AND OtherRegion so no other seed's grade changes when it is added to the profile.
    private const string ContainmentLan = "reg-oracle-containment-lan";

    // PR-4 (#300, ADR 0084) — a ssyk-4 in the RELATED (substitutable) set, NOT the exact set.
    // The Related oracle states RelatedSsykGroupConceptIds = [RelatedGroup] so the SQL grade-rank
    // tags an ad whose occupation_group = RelatedGroup at the Related rung (rank 2, between Basic=1
    // and Good=3). Hoisted to a single-element static readonly array so the profile builder does not
    // allocate an inline array per call (CA1861).
    private const string RelatedGroup = "grp-oracle-related";
    private static readonly string[] RelatedGroups = [RelatedGroup];
    private static readonly string[] ExactGroups = [PrefGroup];

    // ---------------------------------------------------------------
    // SUT factory — the REAL wired match-sort query from DI (proving the
    // registration + the EF translation of the rank ORDER BY), plus a held scope.
    // ---------------------------------------------------------------
    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewMatchSort()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider
            .GetRequiredService<IPerUserJobAdSearchQuery>();
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

    // F4-15 (ADR 0076 Decision 6): SearchPerUserAsync now takes a FullCandidateMatchProfile.
    // This F4-14-ladder oracle exercises the GRADE ladder only (no CV skills) → an EMPTY
    // CvSkillConceptIds, which produces NO golden lift (order ≡ F4-14). The golden top tier
    // is pinned separately in MatchSortGoldenRungOracleTests. This base profile states a
    // REGION ort preference but NO municipality (the legacy region-only cases); the ort-union
    // cases below state both via ProfileWithOrt.
    private static FullCandidateMatchProfile Profile() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: [PrefGroup],
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            // Region-only ort preference (the legacy cases); the union cases use ProfileWithOrt.
            PreferredMunicipalityConceptIds: [])
        {
            // #477 / #552 — set DIRECTLY (the taxonomy derivation is tested elsewhere). Behaviour-
            // inert for every seed except a län-only ad whose region == ContainmentLan (municipality
            // NULL), which reads NotAssessed → the only reachable Good under a both-stated profile.
            ContainmentRegionConceptIds = [ContainmentLan],
        },
        CvSkillConceptIds: []);

    // Spår 3 PR-C — a profile that STATES the ort-union: a region preference AND a
    // municipality preference (region ∪ municipality). The grade reads the ort-union RegionFit
    // verdict from BOTH lists, so this drives the region-hit / municipality-hit / both-hit /
    // floor / NotAssessed sort cases. SSYK + employment are stated as usual so the grade ladder
    // is reachable; CvSkillConceptIds stays empty (no golden lift — pure F4-14 ladder).
    private static FullCandidateMatchProfile ProfileWithOrt(
        IReadOnlyList<string> preferredRegions,
        IReadOnlyList<string> preferredMunicipalities) => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: [PrefGroup],
            PreferredRegionConceptIds: preferredRegions,
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            PreferredMunicipalityConceptIds: preferredMunicipalities)
        {
            // #477 / #552 — see Profile(). Behaviour-inert except for a län-only ContainmentLan ad.
            ContainmentRegionConceptIds = [ContainmentLan],
        },
        CvSkillConceptIds: []);

    // PR-4 (#300, ADR 0084) — a profile that STATES the related set { RelatedGroup } alongside the
    // exact set { PrefGroup }. The SQL grade-rank's broadened gate (exact ∪ related) tags a
    // RelatedGroup ad at the Related rung. Region + employment are stated so the related-only ads
    // can also carry secondary signals (proving the FLAT cap: Related regardless of secondaries).
    private static FullCandidateMatchProfile ProfileWithRelated() => new(
        new CandidateMatchProfile(
            Title: string.Empty,
            SsykGroupConceptIds: ExactGroups,
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            PreferredMunicipalityConceptIds: [])
        {
            RelatedSsykGroupConceptIds = RelatedGroups,
            // #477 / #552 — see Profile(). Lets the exact-hit Good rung stay reachable via containment.
            ContainmentRegionConceptIds = [ContainmentLan],
        },
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
        Employer: [],
        Remote: false,
        Q: null);

    // ---------------------------------------------------------------
    // Seeding — raw_payload drives the facet columns. occupation_group +
    // employment_type + working_hours_type (the run-isolation worktime-extent) are
    // TOP-LEVEL; region AND municipality live under workplace_address (parity
    // MatchScorerIntegrationTests + JobAdFacetsSurvivePurgeTests). null group/region/
    // employment → key omitted → that shadow column is NULL (the NotAssessed-by-NULL
    // path). publishedAt is explicit so we can prove the tie-break within a grade.
    // <para>
    // Spår 3 PR-C (ADR 0076-amendment 2026-06-21): the OPTIONAL municipalityConceptId
    // (default null, LAST positional arg) re-adds the kommun seed dimension that PR-B
    // moved off the run-isolation key. It is SEPARATE from the worktime-extent run tag
    // (which stays the grade-neutral isolation): municipality is a GENUINE ort signal the
    // ort-union grade reads, worktime-extent is the filter we isolate on. With
    // municipalityConceptId == null every pre-existing case is byte-for-byte the old
    // region-only payload (workplace_address carries only the present location key(s)).
    // </para>
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
            title: "Oracle-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // workplace_address carries region_concept_id AND/OR municipality_concept_id (both grade
    // inputs to the ort-union RegionFit) — exactly the present location key(s); the test-run
    // isolation key rides the TOP-LEVEL working_hours_type → worktime_extent_concept_id
    // shadow (NAMNGLAPP: column worktime_extent ↔ payload working_hours_type), which the
    // grade never reads. Both location ids null → workplace_address null (both ort shadows
    // NULL, the NotAssessed-by-NULL path); region-present + municipality-null is byte-for-byte
    // the legacy single-key shape (so every pre-PR-C case is unaffected). Parity
    // MatchScorerIntegrationTests.BuildWorkplaceAddressJson.
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

    // workplace_address carries only the present location key(s): both null → "null" (both
    // ort shadows NULL); region only → {"region_concept_id":...} (legacy shape, NULL
    // municipality shadow — the impl-trap NULL-municipality case); municipality only →
    // {"municipality_concept_id":...} (NULL region shadow); both → both keys. Parity
    // MatchScorerIntegrationTests.BuildWorkplaceAddressJson.
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

    private static string NewRunWorktimeExtent() => $"wt-oracle-{Guid.NewGuid():N}"[..20];

    // Maps a positive grade to the SQL rank integer (higher = better). null grade
    // (untagged) → 0. The SQL ORDER BY produces this ordering server-side; this is
    // the SAME ladder MatchGradeCalculator encodes (the agreement the oracle pins).
    private static int RankOf(MatchGrade? grade) => grade switch
    {
        null => 0,
        MatchGrade.Basic => 1,
        // PR-4 (#300, ADR 0084): Related inserted BETWEEN Basic and Good; Good/Strong renumbered up.
        // This mirrors the SQL grade-rank integer scheme (untagged/null=0, Basic=1, Related=2,
        // Good=3, Strong=4) the ORDER BY produces server-side.
        MatchGrade.Related => 2,
        MatchGrade.Good => 3,
        MatchGrade.Strong => 4,
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

            // --- Good (1 confirmed) — under #552 the only reachable Good for a both-stated
            //     profile is the #477 containment carve-out: a län-only ad (region = ContainmentLan,
            //     kommun NULL) reads RegionFit NotAssessed (neither floors nor lifts) + employment
            //     Match → Good. (Pre-#552 a stated-ort NULL shadow was NotAssessed too, but #552
            //     makes it NoMatch → Basic, so region-Match+employment-NULL and region-NULL+employment-
            //     Match now BOTH floor to Basic; containment is what still grades Good.)
            await SeedJobAdAsync(run, PrefGroup, ContainmentLan, PrefEmployment, t.AddDays(8), ct),
            await SeedJobAdAsync(run, PrefGroup, ContainmentLan, PrefEmployment, t.AddDays(7), ct),

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
        var page = await matchSort.SearchPerUserAsync(
            filter, profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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

        // Sanity: the seed genuinely spans the untagged/Basic/Good/Strong rank buckets so the
        // oracle is not vacuously green on a degenerate set. PR-4 (#300): the Fast Grade(MatchScore)
        // overload never yields Related (no related input), so this seed has NO rank-2 row; the
        // renumbered ranks are {0,1,3,4} (Related=2 is exercised in the dedicated Related oracle
        // below). The non-increasing oracle assertion is unaffected by the absolute values.
        var distinctRanks = expectedRankById.Values.Distinct().OrderBy(r => r).ToList();
        distinctRanks.ShouldBe([0, 1, 3, 4],
            "Seed-mängden ska täcka otaggad/Basic/Good/Strong (rank 0/1/3/4 i PR-4-schemat; " +
            "Related=2 saknas medvetet — Fast-overloaden producerar aldrig Related).");

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
    // 1b. PR-4 (#300, ADR 0084) — the RELATED-rung SQL grade-rank oracle. With a profile that
    //     states RelatedSsykGroupConceptIds = [RelatedGroup], the SQL ORDER BY must tag a
    //     RelatedGroup ad at the Related rung (rank 2, strictly BETWEEN Basic=1 and Good=3) and
    //     the cap must be FLAT (Related regardless of secondaries) and BEFORE the RB1 floor (a
    //     related-only ad in the wrong city is Related, NOT Basic). The C# SSOT is the broadened
    //     ScoreFullBatchAsync (it broadens the gate AND surfaces SsykIsRelated), graded via
    //     MatchGradeCalculator.Grade(FullMatchScore, isRelated) — the EXACT production path the SQL
    //     rank must mirror across the full exact × related × secondary tuple space.
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_RelatedRung_OrderingEqualsGradeSsot_BetweenBasicAndGood()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Profile: exact=[PrefGroup], related=[RelatedGroup], region=[PrefRegion],
        // employment=[PrefEmployment]. The seed spans every reachable rung incl. the Related rung
        // (rank 2), reached ONLY via the related set + the flat cap.
        var seeded = new List<JobAdId>
        {
            // --- Strong (rank 4): exact occupation + both secondaries Match.
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(20), ct),
            // --- Good (rank 3): exact occupation + exactly one confirmed secondary. Under #552 the
            //     reachable Good is the #477 containment carve-out (län-only ContainmentLan ad, kommun
            //     NULL → RegionFit NotAssessed) + employment Match (region-Match+employment-NULL now floors).
            await SeedJobAdAsync(run, PrefGroup, ContainmentLan, PrefEmployment, t.AddDays(16), ct),

            // --- Related (rank 2) + BOTH secondaries Match: an exact hit here would be Strong, but
            //     the flat related-cap pins it to Related (rank 2 — BELOW Good=3/Strong=4, ABOVE Basic=1).
            await SeedJobAdAsync(run, RelatedGroup, PrefRegion, PrefEmployment, t.AddDays(12), ct),
            // --- Related (rank 2) + region NoMatch (wrong city): for an EXACT hit RB1 would floor to
            //     Basic; the related-cap is BEFORE RB1 → still Related (rank 2, NOT Basic=1).
            await SeedJobAdAsync(run, RelatedGroup, OtherRegion, PrefEmployment, t.AddDays(11), ct),

            // --- Basic (rank 1): exact occupation, both secondaries NotAssessed.
            await SeedJobAdAsync(run, PrefGroup, null, null, t.AddDays(8), ct),

            // --- Untagged (rank 0): SSYK in NEITHER exact nor related → gate fails even broadened.
            await SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, t.AddDays(4), ct),
        };

        var profile = ProfileWithRelated();
        var filter = FilterFor(run);

        // SQL-ordered page (the real wired impl — its broadened gate + Related rung under test).
        var (sortScope, matchSort) = NewMatchSort();
        using var _ = sortScope;
        var page = await matchSort.SearchPerUserAsync(
            filter, profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        page.Items.Count.ShouldBe(seeded.Count,
            "Match-sorten ska returnera HELA den filtrerade mängden (otaggade inkluderade).");

        // C# grade-SSOT mirrors the SQL grade-rank, which ranks on the FAST band (G3-OPT-A — the
        // SQL cannot compute must-have, so Strong is its ceiling). We therefore grade via the FAST
        // overload Grade(MatchScore, isRelated) over the embedded Fast score, NOT the requirement-
        // aware Full overload (which would cap a no-CV exact ad at Good via the F1(b) gate and so
        // diverge from the SQL). We still use ScoreFullBatchAsync — it surfaces SsykIsRelated (the
        // Fast ScoreBatchAsync does not), and its embedded .Score.Fast is the broadened-gate Fast
        // score (a RelatedGroup ad reads SsykOverlap=Match via exact ∪ related). The flat Related
        // cap is applied by passing SsykIsRelated to the Fast overload.
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scored = await scorer.ScoreFullBatchAsync(seeded, profile, ct);

        var expectedRankById = seeded.ToDictionary(
            id => id.Value,
            id => RankOf(MatchGradeCalculator.Grade(scored[id].Score.Fast, scored[id].SsykIsRelated)));

        // Sanity: the seed spans EVERY rung incl. the Related rung — distinct ranks == {0,1,2,3,4}.
        var distinctRanks = expectedRankById.Values.Distinct().OrderBy(r => r).ToList();
        distinctRanks.ShouldBe([0, 1, 2, 3, 4],
            "Related-seeden ska täcka otaggad/Basic/Related/Good/Strong (rank 0/1/2/3/4) så " +
            "Related-rungen (rank 2) faktiskt utövas mellan Basic och Good.");

        // Cross-check the two Related ads (seed positions 2 + 3 — both RelatedGroup) genuinely carry
        // the related bit via the broadened gate (not a degenerate exact tag), and an exact ad does not.
        scored[seeded[2]].SsykIsRelated.ShouldBeTrue(
            "RelatedGroup-annonsen ska bära SsykIsRelated=true via det breddade gate:t.");
        scored[seeded[3]].SsykIsRelated.ShouldBeTrue(
            "RelatedGroup-annonsen (fel stad) ska bära SsykIsRelated=true.");
        scored[seeded[0]].SsykIsRelated.ShouldBeFalse(
            "en exakt-yrke-annons (Strong) ska aldrig bära SsykIsRelated.");

        // THE RELATED ORACLE: the SQL-ordered ranks must be non-increasing — Related sits strictly
        // between Basic (1) and Good (3), and a related-only ad in the wrong city ranks at Related
        // (2), NOT Basic (1). SQL rank ≡ MatchGradeCalculator(Grade with isRelated) for every row.
        var sqlOrderedRanks = page.Items
            .Select(dto => expectedRankById[dto.Id])
            .ToList();

        sqlOrderedRanks.ShouldBe(
            sqlOrderedRanks.OrderByDescending(r => r).ToList(),
            "SQL-rankens ordning ska vara grad fallande och spegla MatchGradeCalculator " +
            "(med isRelated) EXAKT — Related (2) strikt mellan Basic (1) och Good (3); en " +
            "related-only-annons i fel stad ska rangordnas på Related (2), INTE Basic (1) " +
            $"(cap före RB1). SQL-ordnade rank: [{string.Join(", ", sqlOrderedRanks)}].");
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
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), Profile(), grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
        var matchPage = await matchSort.SearchPerUserAsync(
            filter, Profile(), grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var (searchScope, search, _) = NewSearchAndScorer();
        using var __ = searchScope;
        var defaultPage = await search.SearchAsync(
            new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, 1, 100), ct);

        matchPage.TotalCount.ShouldBe(defaultPage.TotalCount,
            "Match-sorten ska reordna, aldrig filtrera bort — TotalCount måste vara " +
            "identisk med default-sortens för samma filter (otaggade inkluderade).");
        matchPage.TotalCount.ShouldBe(5);
        matchPage.Items.Count.ShouldBe(5);
    }

    // ===============================================================
    // 4. #552 grade-gate — FLIPPED. Pre-#552 a STATED-ort NULL shadow read NotAssessed (did NOT
    //    floor), and this test proved the SQL rank did not treat NULL as `not contained` (the
    //    classic `!list.Contains(col)` bug) — a both-NULL-ort ad ranked HIGHER than a contradiction.
    //    #552 INVERTS the guard on a STATED dimension: a both-NULL-ort ad now floors to Basic
    //    exactly LIKE a contradiction. The SQL twin must floor it via an EXPLICIT `== null` disjunct
    //    (three-valued logic: `NOT (col = ANY(...))` is NULL — not TRUE — for a NULL col, so a bare
    //    membership test would NOT floor it; the floor must add `col IS NULL`). This is RED against
    //    current production, which still ranks the both-NULL ad as Good (rank 3).
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_StatedRegionPref_NullRegionShadow_FloorsToBasic_LikeAContradiction()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The profile STATES a region preference (PrefRegion); all three ads share SSYK Match +
        // employment Match, so the grade varies ONLY on the ort dimension.
        //   strong:      region = PrefRegion (hit) → ort Match → Strong (non-vacuity anchor, newest).
        //   contradiction: region = OtherRegion (present, non-preferred) → ort NoMatch → Basic.
        //   bothNullOrt: region NULL + municipality NULL → #552: ort NoMatch (stated pref, no value)
        //                → Basic. Pre-#552 this was NotAssessed → Good (rank 3).
        var strong = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(3), ct);
        var contradiction = await SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(2), ct);
        var bothNullOrt = await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(1), ct);

        var profile = Profile();

        // C# SSOT (the scorer-side of the twin): the both-NULL-ort ad grades Basic under #552.
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync([strong, contradiction, bothNullOrt], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[bothNullOrt]).ShouldBe(MatchGrade.Basic,
            "#552: en ANGIVEN region-preferens mot en annons utan NÅGOT ort-värde (båda shadows " +
            "NULL) golvar till Basic — precis som en motsägelse (inte längre NotAssessed → Good).");
        MatchGradeCalculator.Grade(scores[contradiction]).ShouldBe(MatchGrade.Basic,
            "Motsägande region-shadow (NoMatch) golvar till Basic (oförändrat).");
        MatchGradeCalculator.Grade(scores[strong]).ShouldBe(MatchGrade.Strong);

        var (sortScope, matchSort) = NewMatchSort();
        using var ___ = sortScope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();

        // Non-vacuity anchor: the genuine Strong ranks above the floored both-NULL ad (both states).
        orderedIds.IndexOf(strong.Value).ShouldBeLessThan(orderedIds.IndexOf(bothNullOrt.Value),
            "Den äkta Strong-annonsen ska rangordnas över den golvade both-NULL-ort-annonsen.");

        // THE DISCRIMINATING SQL ASSERTION (the SQL twin's floor): the NEWER contradiction ad (Basic)
        // ranks AT LEAST AS HIGH AS the older both-NULL-ort ad — i.e. it ranks ABOVE it, because under
        // #552 BOTH are Basic (rank 1) and the publishedAt DESC tie-break puts the newer first. Under
        // current production the both-NULL ad is Good (rank 3) and ranks ABOVE the contradiction, so
        // this assertion FAILS (RED) until the SQL floor gains its `== null` disjunct.
        orderedIds.IndexOf(contradiction.Value).ShouldBeLessThan(orderedIds.IndexOf(bothNullOrt.Value),
            "#552: en both-NULL-ort-annons golvas till Basic i SQL-ranken (via en explicit == null-" +
            "disjunkt — NOT (col = ANY(...)) är NULL för en NULL-kolumn), så den nyare motsägelse-" +
            "annonsen (också Basic) rankas via publishedAt-tie-breaken ÖVER den — inte under, vilket " +
            "vore fallet om SQL-ranken fortfarande läste NULL-shadow som NotAssessed → Good.");
    }

    // ===============================================================
    // 4b. Spår 3 PR-C — the ORT-UNION grade-SSOT oracle. The SQL ORDER BY folds municipality
    //     into the ort secondary, EXACTLY mirroring MatchScorer.ScoreOrtUnion: ortConfirmed =
    //     (regions.Contains(adRegion) OR municipalities.Contains(adMunicipality)); ortContradicts
    //     (floors to Basic) = (regionsStated OR municipalitiesStated) AND (adRegion!=null OR
    //     adMunicipality!=null) AND NOT(regionHit OR municipalityHit). The profile states BOTH a
    //     region AND a municipality preference, so the SQL rank must equal
    //     MatchGradeCalculator.Grade(scorer output) over the FULL set of reachable ort-union
    //     tuples — same anti-drift contract as #1, now exercised through the union.
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_OrtUnion_OrderingEqualsGradeSsot_AcrossEveryReachableOrtTuple()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Profile STATES region=[PrefRegion] AND municipality=[PrefMunicipality] (the ort
        // union) plus employment=[PrefEmployment]. Employment is held Match on every tagged ad
        // so the grade varies ONLY on the ort dimension — the cleanest ort-union ladder:
        //   ort Match (any union hit) + employment Match → 2 confirmed → Strong
        //   ort NotAssessed (ad has neither ort value) + employment Match → 1 confirmed → Good
        //   ort NoMatch (ad has ort value(s), no union hit) → RB1 floor → Basic
        // Each seeded with a distinct municipality/region tuple covering every reachable ort
        // verdict reachable through the union.
        var seeded = new List<JobAdId>
        {
            // --- Strong via REGION hit (adRegion=PrefRegion, adMun=null).
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(20), ct,
                municipalityConceptId: null),
            // --- Strong via MUNICIPALITY hit, region NULL (adRegion=null, adMun=PrefMunicipality).
            await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(19), ct,
                municipalityConceptId: PrefMunicipality),
            // --- Strong via MUNICIPALITY hit, region NON-preferred (adRegion=OtherRegion,
            //     adMun=PrefMunicipality) — the union hit carries the Match even though the ad's
            //     region is not preferred (the bare-region sort would WRONGLY miss this).
            await SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(18), ct,
                municipalityConceptId: PrefMunicipality),
            // --- Strong via BOTH hit (adRegion=PrefRegion, adMun=PrefMunicipality).
            await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(17), ct,
                municipalityConceptId: PrefMunicipality),

            // --- Good — ort NotAssessed via #477 containment (län-only ad, region = ContainmentLan,
            //     kommun NULL) + employment Match. Under #552 a stated-ort ad with NEITHER value (both
            //     shadows NULL) is NoMatch → Basic, so the reachable Good is the containment carve-out.
            await SeedJobAdAsync(run, PrefGroup, ContainmentLan, PrefEmployment, t.AddDays(12), ct,
                municipalityConceptId: null),

            // --- Basic — same län different kommun: ad HAS a (non-preferred) region AND a
            //     (non-preferred) municipality, no union hit → ort NoMatch → RB1 floor.
            await SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(8), ct,
                municipalityConceptId: OtherMunicipality),
            // --- Basic — municipality-only NoMatch: ad has a non-preferred municipality, NULL
            //     region, no union hit → ort NoMatch → RB1 floor (the municipality NoMatch must
            //     floor exactly like a region NoMatch).
            await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(7), ct,
                municipalityConceptId: OtherMunicipality),

            // --- Untagged (SSYK NoMatch) — sorts last regardless of ort.
            await SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, t.AddDays(3), ct,
                municipalityConceptId: PrefMunicipality),
        };

        var profile = ProfileWithOrt([PrefRegion], [PrefMunicipality]);
        var filter = FilterFor(run);

        var (sortScope, matchSort) = NewMatchSort();
        using var _ = sortScope;
        var page = await matchSort.SearchPerUserAsync(
            filter, profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        page.Items.Count.ShouldBe(seeded.Count,
            "Match-sorten ska returnera HELA den filtrerade mängden (otaggade inkluderade).");

        // C# grade-SSOT over the SAME (profile.Fast, ad-shadows) — the ort-union RegionFit the
        // scorer computes flows into the UNCHANGED grade ladder.
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync(seeded, profile.Fast, ct);

        var expectedRankById = seeded.ToDictionary(
            id => id.Value,
            id => RankOf(MatchGradeCalculator.Grade(scores[id])));

        // Sanity: the ort-union seed spans Strong / Good / Basic / untagged. PR-4 (#300): the
        // renumbered ranks are {0,1,3,4} (no Related row here — the Fast overload yields no Related).
        var distinctRanks = expectedRankById.Values.Distinct().OrderBy(r => r).ToList();
        distinctRanks.ShouldBe([0, 1, 3, 4],
            "Ort-union-seeden ska täcka otaggad/Basic/Good/Strong (rank 0/1/3/4 i PR-4-schemat).");

        // THE ORT-UNION ORACLE: SQL-ordered ranks non-increasing ≡ MatchGradeCalculator over
        // the ort-union scorer output. If the SQL ORDER BY's ort fold drifts from
        // MatchScorer.ScoreOrtUnion this breaks.
        var sqlOrderedRanks = page.Items
            .Select(dto => expectedRankById[dto.Id])
            .ToList();

        sqlOrderedRanks.ShouldBe(
            sqlOrderedRanks.OrderByDescending(r => r).ToList(),
            "SQL-rankens ort-union-ordning ska vara grad fallande och spegla " +
            "MatchGradeCalculator över ScoreOrtUnion EXAKT (anti-drift-orakel för ort-unionen). " +
            $"SQL-ordnade rank: [{string.Join(", ", sqlOrderedRanks)}].");
    }

    [Fact]
    public async Task SearchByMatch_OrtUnion_MunicipalityHit_RegionNonPreferred_RanksAsStrong_NotFlooredByRegion()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The load-bearing union case: an ad whose REGION is non-preferred but whose
        // MUNICIPALITY is preferred must rank as Strong (ort Match via the municipality leg),
        // NOT be floored to Basic by a bare region-only NoMatch test. Paired against a genuine
        // ort NoMatch (both ort values non-preferred → Basic) so the rank gap is asserted, not
        // just the absolute grade.
        var municipalityHit = await SeedJobAdAsync(
            run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(2), ct,
            municipalityConceptId: PrefMunicipality);
        var ortNoMatch = await SeedJobAdAsync(
            run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(1), ct,
            municipalityConceptId: OtherMunicipality);

        var profile = ProfileWithOrt([PrefRegion], [PrefMunicipality]);

        // C# SSOT proves the seed: municipality hit → ort Match → Strong; both non-preferred →
        // ort NoMatch → Basic.
        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync([municipalityHit, ortNoMatch], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[municipalityHit]).ShouldBe(MatchGrade.Strong,
            "Kommun-träff (region ej föredragen) = ort Match → 2 bekräftade sekundärer → Strong.");
        MatchGradeCalculator.Grade(scores[ortNoMatch]).ShouldBe(MatchGrade.Basic,
            "Ingen union-träff (både region och kommun icke-föredragna) → ort NoMatch → Basic.");

        var (sortScope, matchSort) = NewMatchSort();
        using var ___ = sortScope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.IndexOf(municipalityHit.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(ortNoMatch.Value),
                "Kommun-träffen (Strong) ska rangordnas ÖVER ort-NoMatch (Basic) — SQL ORDER BY:n " +
                "ska läsa kommun-leget i ort-unionen, inte golva på en bar region-NoMatch.");
    }

    // ===============================================================
    // 4c. #552 grade-gate — FLIPPED (the municipality twin of #4). Pre-#552 an ad with BOTH ort
    //     shadows NULL under a STATED municipality preference read ort NotAssessed (did NOT floor)
    //     and ranked HIGHER than a contradicting municipality. #552 makes it NoMatch → Basic,
    //     exactly like the contradiction. The SQL twin must floor it via an explicit
    //     `region IS NULL AND municipality IS NULL` disjunct (three-valued logic). RED against
    //     current production, which ranks the both-NULL ad as Good (rank 3).
    // ===============================================================

    [Fact]
    public async Task SearchByMatch_StatedMunicipalityPref_NullMunicipalityShadow_FloorsToBasic_LikeAContradiction()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The profile states ONLY a municipality ort preference (no region) → the ort union is
        // municipality-only. All three ads share SSYK Match + employment Match.
        //   strong:      municipality = PrefMunicipality (hit), region NULL → ort Match → Strong
        //                (non-vacuity anchor, newest).
        //   contradiction: municipality = OtherMunicipality (present, non-preferred), region NULL →
        //                ad HAS an ort value, no hit → ort NoMatch → Basic.
        //   bothNullOrt: region NULL + municipality NULL → #552: ort NoMatch (stated pref, no value)
        //                → Basic. Pre-#552 this was NotAssessed → Good (rank 3).
        var strong = await SeedJobAdAsync(
            run, PrefGroup, null, PrefEmployment, t.AddDays(3), ct, municipalityConceptId: PrefMunicipality);
        var contradiction = await SeedJobAdAsync(
            run, PrefGroup, null, PrefEmployment, t.AddDays(2), ct, municipalityConceptId: OtherMunicipality);
        var bothNullOrt = await SeedJobAdAsync(
            run, PrefGroup, null, PrefEmployment, t.AddDays(1), ct, municipalityConceptId: null);

        var profile = ProfileWithOrt([], [PrefMunicipality]);

        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync([strong, contradiction, bothNullOrt], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[bothNullOrt]).ShouldBe(MatchGrade.Basic,
            "#552: NULL kommun-shadow (och NULL region) med ANGIVEN kommun-preferens golvar till " +
            "Basic — precis som en motsägelse (inte längre NotAssessed → Good).");
        MatchGradeCalculator.Grade(scores[contradiction]).ShouldBe(MatchGrade.Basic,
            "Motsägande kommun-shadow (present, icke-föredragen) → ort NoMatch → Basic (oförändrat).");
        MatchGradeCalculator.Grade(scores[strong]).ShouldBe(MatchGrade.Strong);

        var (sortScope, matchSort) = NewMatchSort();
        using var ___ = sortScope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();

        orderedIds.IndexOf(strong.Value).ShouldBeLessThan(orderedIds.IndexOf(bothNullOrt.Value),
            "Den äkta Strong-annonsen (kommun-träff) ska rangordnas över den golvade both-NULL-ort-annonsen.");

        // THE DISCRIMINATING SQL ASSERTION: the newer contradiction (Basic) ranks above the older
        // both-NULL ad — both Basic under #552, tie-broken by publishedAt. RED under current
        // production, where the both-NULL ad is Good (rank 3) and ranks above the contradiction.
        orderedIds.IndexOf(contradiction.Value).ShouldBeLessThan(orderedIds.IndexOf(bothNullOrt.Value),
            "#552: en both-NULL-ort-annons golvas till Basic i SQL-ranken (via en explicit " +
            "region IS NULL AND municipality IS NULL-disjunkt), så den nyare motsägelse-annonsen " +
            "(också Basic) rankas via publishedAt-tie-breaken ÖVER den.");
    }

    [Fact]
    public async Task SearchByMatch_StatedMunicipalityPref_RegionHit_NullMunicipality_StillRanksAsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The impl-trap pair: the profile states BOTH a region AND a municipality preference.
        // An ad with a REGION hit but a NULL municipality must NOT be floored by the (NULL)
        // municipality leg — the region hit alone makes the ort union a Match → Strong. Paired
        // against a genuine ort NoMatch so the rank gap is asserted.
        //   regionHitNullMun: region=PrefRegion (hit), municipality NULL → ort Match → Strong.
        //   ortNoMatch:       region=OtherRegion, municipality=OtherMunicipality (both present,
        //                     non-preferred) → ort NoMatch → Basic.
        var regionHitNullMun = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), ct, municipalityConceptId: null);
        var ortNoMatch = await SeedJobAdAsync(
            run, PrefGroup, OtherRegion, PrefEmployment, t.AddDays(1), ct,
            municipalityConceptId: OtherMunicipality);

        var profile = ProfileWithOrt([PrefRegion], [PrefMunicipality]);

        var (scoreScope, _, scorer) = NewSearchAndScorer();
        using var __ = scoreScope;
        var scores = await scorer.ScoreBatchAsync([regionHitNullMun, ortNoMatch], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[regionHitNullMun]).ShouldBe(MatchGrade.Strong,
            "Region-träff + NULL kommun = ort Match (NULL-kommunen får inte golva region-träffen) " +
            "→ 2 bekräftade sekundärer → Strong.");
        MatchGradeCalculator.Grade(scores[ortNoMatch]).ShouldBe(MatchGrade.Basic,
            "Ingen union-träff (båda ort-värden icke-föredragna) → ort NoMatch → Basic.");

        var (sortScope, matchSort) = NewMatchSort();
        using var ___ = sortScope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.IndexOf(regionHitNullMun.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(ortNoMatch.Value),
                "Region-träff med NULL kommun (Strong) ska rangordnas ÖVER ort-NoMatch (Basic) — " +
                "SQL ORDER BY:n får inte låta en NULL-kommun golva en region-träff (impl-trap CTO C: " +
                "golvet är det KOMBINERADE predikatet, aldrig en bar !municipalities.Contains).");
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
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

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

        var gradeMatch = MatchGradeCalculator.Grade(full[mustHaveMatch].Score);
        var gradeNoMatch = MatchGradeCalculator.Grade(full[mustHaveNoMatch].Score);

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
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), profile, grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
