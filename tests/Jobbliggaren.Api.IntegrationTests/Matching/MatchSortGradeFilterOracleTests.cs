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
/// for the raw_payload → facet column path; the helpers are copied (kept
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
/// <para>
/// <b>The UNIFIED Fast/Full rule (#371/#382, senior-cto-advisor bind):</b> the /jobb list's
/// SORT + grade-FILTER + headline-COUNT all pin to the FAST band
/// (<see cref="MatchGradeCalculator.Grade(MatchScore)"/>, mirrored in SQL); the card BADGE is the
/// Full requirement-aware grade (<see cref="MatchGradeCalculator.Grade(FullMatchScore)"/>, F1(b)-gated).
/// The Fast/Full divergence is bound + bidirectional + oracle-pinned (G3-OPT-A) — a Fast-Strong ad in
/// the <c>{Strong}</c> filter bucket can carry a Full BADGE of Top (up) OR Good (down) and that is by
/// design, never drift. The sort-side twin is pinned in <see cref="MatchSortOracleTests"/>; the
/// filter-side pin is the divergence test below. See ADR 0076 (amendment) and issues #371/#382.
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

    // #552 review-pin — a municipality the profile does NOT prefer, for the mirror
    // asymmetric shape (region NULL + municipality present-not-preferred). Distinct from
    // every preferred/containment id so no seed accidentally hits the union.
    private const string OtherMunicipality = "mun-gradefilter-other";

    // #477 Low 1 — the parent län of PrefMunicipality (kommun→län containment). DISTINCT from
    // PrefRegion AND OtherRegion so a län-only ad carrying it is neither a direct region hit nor an
    // arbitrary contradiction: the profile's ContainmentRegionConceptIds = [PrefMunicipalityRegion]
    // makes ScoreOrtUnion read such a län-only ad as NotAssessed (neutral), and the SQL grade-WHERE
    // mirrors it. Because no OTHER seeded ad uses this as its region, adding it to the profile leaves
    // every existing seed's grade byte-for-byte unchanged.
    private const string PrefMunicipalityRegion = "reg-gradefilter-containment-lan";

    // PR-4 (#300, ADR 0084) — a ssyk-4 in the RELATED (substitutable) set, NOT the exact set. The
    // profile states RelatedSsykGroupConceptIds = [RelatedGroup] so the grade-WHERE can select the
    // Related rung. Hoisted to a static readonly array (CA1861) for the profile builder + the
    // unioned scoring profile that opens the Fast gate for a related-only ad's grade SSOT.
    private const string RelatedGroup = "grp-gradefilter-related";
    private static readonly string[] ExactGroups = [PrefGroup];
    private static readonly string[] RelatedGroups = [RelatedGroup];

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
            SsykGroupConceptIds: ExactGroups,
            PreferredRegionConceptIds: [PrefRegion],
            PreferredEmploymentTypeConceptIds: [PrefEmployment],
            PreferredMunicipalityConceptIds: [PrefMunicipality])
        {
            // PR-4 (#300, ADR 0084): the related set the broadened grade-WHERE selects the Related
            // rung from. Empty in the pre-PR-4 era → behaviour-inert; non-empty here so the {Related}
            // band is reachable. Exact-precedence: a group in BOTH would tag exact, not Related.
            RelatedSsykGroupConceptIds = RelatedGroups,
            // #477 Low 1 — the containment län set (parent län of PrefMunicipality). Set DIRECTLY
            // here (the oracle constructs the profile — the taxonomy-derivation is tested in
            // MatchProfileBuilderTests, NOT relied on here). Drives BOTH the scorer's containment
            // NotAssessed branch AND the SQL grade-WHERE's containment disjunct, so the per-subset
            // set-equality below proves scorer ≡ SQL for a containment ad.
            ContainmentRegionConceptIds = [PrefMunicipalityRegion],
        },
        CvSkillConceptIds: []);

    // Filter on the unique test-run worktime-extent only → exactly the seeded ads,
    // untagged included (the grade-WHERE then gallrar within that mass).
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
    // Seeding — raw_payload drives the facet columns (parity
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
        string? municipalityConceptId = null,
        bool remote = false)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId,
            runWorktimeExtent, employmentTypeConceptId, municipalityConceptId);

        // #551 — remote is AF's harvested classification, not a raw_payload key: state it on the facets
        // explicitly for a remote seed (parity the ACL's MapFacets). Non-remote seeds keep FromPayload.
        var facets = remote
            ? TestFacets.From(
                occupationGroup: occupationGroupConceptId,
                region: regionConceptId,
                employmentType: employmentTypeConceptId,
                municipality: municipalityConceptId,
                worktimeExtent: runWorktimeExtent,
                remote: true)
            : TestFacets.FromPayload(rawPayload);

        var jobAd = JobAd.Import(
            title: "Gradefilter-orakel-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: facets,
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

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

    // Basic (rank 1) via the MIRROR asymmetric contradiction (#552 review-pin, code-review
    // Minor 2): region shadow NULL + a PRESENT-but-not-preferred municipality + employment
    // Match. In-memory: no union hit, ad HAS an ort value → NoMatch → RB1 floor. The SQL
    // floor's negated OR crosses regions.Contains(NULL-region) — this seed makes the
    // comprehensive set-equality below adjudicate that EF's C#-null-semantics compensation
    // keeps SQL ≡ scorer on this leg too (the claimed Good-vs-Basic divergence must not exist).
    private Task<JobAdId> SeedBasicContradictionViaMunicipalityAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, regionConceptId: null, PrefEmployment, publishedAt, ct,
            municipalityConceptId: OtherMunicipality);

    // Strong via the ORT-UNION municipality leg — region non-preferred but municipality
    // preferred → ort Match via the union; with employment Match → 2 confirmed → Strong.
    private Task<JobAdId> SeedStrongViaMunicipalityAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, OtherRegion, PrefEmployment, publishedAt, ct,
            municipalityConceptId: PrefMunicipality);

    // Good (rank 3) via #477 CONTAINMENT — a LÄN-ONLY ad (region = PrefMunicipalityRegion, the
    // parent län of the preferred kommun; municipality NULL) + employment Match. RegionFit reads
    // NotAssessed (containment: neither floors nor lifts), so the single confirmed secondary
    // (employment) grades Good. Before #477 this RB1-floored to Basic; the SQL grade-WHERE now
    // mirrors the scorer's NotAssessed via the containment disjunct. This is the tuple the
    // scorer≡SQL parity oracle proves for the containment case.
    private Task<JobAdId> SeedContainmentGoodAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, PrefMunicipalityRegion, PrefEmployment, publishedAt, ct);

    // #551 Strong via the REMOTE OVERRIDE — a location-LESS ad (region + municipality NULL) with a MATCHING
    // employment. WITHOUT the override this is the #552 ort contradiction (locationless vs a stated ort) →
    // region NoMatch → RB1 floor → Basic, even though employment Matches. Because it is remote, the ORT leg
    // reads Match (a location-match for the stated-ort user) → region Match + employment Match = two
    // confirmed secondaries → Strong. (The override lifts ONLY the ort leg, NOT the employment gate — so
    // employment must Match here; a null employment against Profile()'s stated PrefEmployment would floor
    // on the INDEPENDENT employment contradiction regardless of remote.) The discriminator: it grades Strong
    // ONLY if the remote welds fire; without them it is Basic. The per-subset set-equality then AUTOMATICALLY
    // proves scorer ≡ SQL on the remote leg (parity the containment seed) — a wrong `!remote` /
    // `ortStated && remote` weld would floor it while the scorer lifts it, and the set-equality fails loud.
    private Task<JobAdId> SeedRemoteStrongAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, regionConceptId: null, PrefEmployment, publishedAt, ct,
            remote: true);

    // Related (rank 2, PR-4 #300): occupation group ∈ the RELATED set only (∉ exact). The flat cap
    // makes it Related regardless of secondaries — region + employment are stated Match here, but an
    // exact hit's Strong is capped to Related (the load-bearing flat-cap proof).
    private Task<JobAdId> SeedRelatedAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, RelatedGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Untagged (rank 0): SSYK NoMatch (ad group present, not in profile) → no tag.
    private Task<JobAdId> SeedUntaggedSsykNoMatchAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, OtherGroup, PrefRegion, PrefEmployment, publishedAt, ct);

    // Untagged (rank 0): SSYK NotAssessed (null group) → no tag.
    private Task<JobAdId> SeedUntaggedSsykNullAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, null, PrefRegion, PrefEmployment, publishedAt, ct);

    // A representative family of non-empty subsets of the FOUR-grade filterable band
    // {Basic, Related, Good, Strong} (PR-4 #300, ADR 0084). Each singleton (incl. {Related}), the
    // Related-combined pairs {Basic,Related} and {Related,Good}, the legacy Fast-only pairs/triple,
    // and the all-four set — enough to pin Related both alone and combined without enumerating all
    // 15 subsets (the oracle is per-subset set-equality, so the family is the load-bearing cover).
    private static IReadOnlyList<IReadOnlyList<MatchGrade>> AllNonEmptyBandSubsets() =>
    [
        [MatchGrade.Basic],
        [MatchGrade.Related],
        [MatchGrade.Good],
        [MatchGrade.Strong],
        [MatchGrade.Basic, MatchGrade.Related],
        [MatchGrade.Related, MatchGrade.Good],
        [MatchGrade.Basic, MatchGrade.Good],
        [MatchGrade.Basic, MatchGrade.Strong],
        [MatchGrade.Good, MatchGrade.Strong],
        [MatchGrade.Basic, MatchGrade.Good, MatchGrade.Strong],
        [MatchGrade.Basic, MatchGrade.Related, MatchGrade.Good, MatchGrade.Strong],
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
        // floored Basic + an ort-union Strong + a #477 containment Good. publishedAt is
        // monotonically decreasing so recency does not accidentally coincide with grade order.
        // The containment Good ad is captured so its SSOT grade can be sanity-checked below.
        var containmentGood = await SeedContainmentGoodAsync(run, t.AddDays(13), ct);
        // #551 — a remote locationless ad (employment matches): Strong via the override (WITHOUT it, the
        // ort contradiction floors it to Basic). Captured so its SSOT grade is sanity-checked.
        var remoteStrong = await SeedRemoteStrongAsync(run, t.AddDays(16), ct);
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
            // --- Good via #477 containment (län-only ad in the preferred kommun's parent län) →
            //     RegionFit NotAssessed + employment Match → Good. The per-subset set-equality then
            //     AUTOMATICALLY proves scorer ≡ SQL for the containment neutralisation.
            containmentGood,
            // --- #551 Strong via the REMOTE OVERRIDE (locationless remote ad, employment Match; Basic
            //     without the override — the ort contradiction floors it).
            remoteStrong,

            // --- Related (≥2, PR-4 #300): occupation ∈ related-only → flat cap Related, even with
            //     both secondaries Match (a would-be Strong capped to Related).
            await SeedRelatedAsync(run, t.AddDays(12), ct),
            await SeedRelatedAsync(run, t.AddDays(11), ct),

            // --- Basic (≥2): one neutral, one contradiction-floored
            await SeedBasicNeutralAsync(run, t.AddDays(10), ct),
            await SeedBasicContradictionAsync(run, t.AddDays(9), ct),
            // --- Basic via the mirror asymmetric shape (#552 review-pin): region NULL +
            //     municipality present-not-preferred; SQL ≡ scorer proven by set-equality.
            await SeedBasicContradictionViaMunicipalityAsync(run, t.AddDays(8), ct),

            // --- Untagged (rank 0) — never selectable by any grade
            await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct),
            await SeedUntaggedSsykNullAsync(run, t.AddDays(4), ct),
        };

        var profile = Profile();
        var filter = FilterFor(run);

        // C# grade-SSOT for the grade-WHERE (the FAST band + the Related cap). The grade-WHERE ranks
        // on the Fast band, so the SSOT is Grade(MatchScore, isRelated) — NOT the requirement-aware
        // Full overload. The broadened full scorer opens the gate for a related-only ad (its Fast
        // SsykOverlap reads Match) AND surfaces SsykIsRelated, so .Score.Fast + .SsykIsRelated feed
        // the Fast Grade overload EXACTLY as the broadened SQL grade-WHERE tags. null grade
        // (untagged) maps to "no band".
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scored = await scorer.ScoreFullBatchAsync(seeded, profile, ct);
        var gradeById = seeded.ToDictionary(
            id => id.Value,
            id => MatchGradeCalculator.Grade(scored[id].Score.Fast, scored[id].SsykIsRelated));

        // Sanity: the seed genuinely spans Basic, Related, Good, Strong AND untagged (so no subset
        // assertion is vacuously green on a degenerate set).
        var distinctGrades = gradeById.Values.Distinct().ToList();
        distinctGrades.ShouldContain(MatchGrade.Basic);
        distinctGrades.ShouldContain(MatchGrade.Related,
            "Seeden ska innehålla minst en Related-annons så {Related}-bandet testas på riktigt.");
        distinctGrades.ShouldContain(MatchGrade.Good);
        distinctGrades.ShouldContain(MatchGrade.Strong);
        distinctGrades.ShouldContain((MatchGrade?)null,
            "Seeden ska innehålla minst en otaggad annons (rank 0) så positiv-only-" +
            "exkluderingen testas på riktigt.");

        // #477 Low 1 — the containment län-only ad genuinely grades Good via the SCORER (RegionFit
        // NotAssessed via containment + employment Match). The per-subset set-equality below then
        // AUTOMATICALLY proves scorer ≡ SQL for it: it must appear in EXACTLY the subsets containing
        // Good (with the recomputed count matching) and NONE other. Had the SQL grade-WHERE and the
        // scorer diverged on containment (one flooring to Basic, the other NotAssessed→Good), that
        // set-equality would fail loud — the whole point of the oracle.
        gradeById[containmentGood.Value].ShouldBe(MatchGrade.Good,
            "Containment-läns-only-annonsen ska grada Good via scorern (RegionFit NotAssessed + " +
            "anställning Match) — annars är seeden inte den #477-fixen påstår.");

        // #551 — the remote locationless ad must grade STRONG via the SCORER (RegionFit=Match via the
        // override + EmploymentFit=Match = two confirmed secondaries). The per-subset set-equality below then
        // AUTOMATICALLY proves scorer ≡ SQL on the remote leg: it must appear in EXACTLY the Strong-containing
        // subsets and none other. Had the SQL grade-WHERE floored it to Basic (a wrong `!remote`/
        // `ortStated && remote` weld), that set-equality would fail loud — the whole point of the oracle.
        gradeById[remoteStrong.Value].ShouldBe(MatchGrade.Strong,
            "Den remote lokationslösa annonsen (anställning Match) ska grada Strong via override:n " +
            "(RegionFit=Match + EmploymentFit=Match) — utan remote-armen golvas ort-motsägelsen till Basic.");

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
                orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
    // 1b. PR-4 (#300, ADR 0084) — the {Related} band selects EXACTLY the related-only ads, with the
    //     recomputed count == in-band size, and Related is EXCLUDED when not selected (positive-only
    //     per band). Self-contained: seeds Related + each Fast rung + untagged, derives the expected
    //     Related id-set from the seed group membership (the related ads are the SeedRelatedAsync ones).
    // ===============================================================

    [Fact]
    public async Task GradeFilter_RelatedBand_ReturnsExactlyRelatedAds_AndExcludesThemOtherwise()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Two Related ads + one of each Fast rung + an untagged. We track the Related ids directly
        // from the seed so the expected {Related}-set is unambiguous (no SSOT round-trip needed for
        // identity — the grade SSOT is still cross-checked below).
        var related1 = await SeedRelatedAsync(run, t.AddDays(20), ct);
        var related2 = await SeedRelatedAsync(run, t.AddDays(19), ct);
        var strong = await SeedStrongAsync(run, t.AddDays(15), ct);
        var good = await SeedGoodAsync(run, t.AddDays(13), ct);
        var basic = await SeedBasicNeutralAsync(run, t.AddDays(10), ct);
        var untagged = await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct);

        var relatedIds = new HashSet<Guid> { related1.Value, related2.Value };

        var profile = Profile();
        var filter = FilterFor(run);

        // Cross-check the SSOT: each Related ad grades to Related (Fast band + isRelated cap), each
        // non-related ad does not. Uses the broadened full scorer (.Score.Fast + .SsykIsRelated).
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scored = await scorer.ScoreFullBatchAsync(
            [related1, related2, strong, good, basic, untagged], profile, ct);
        MatchGradeCalculator.Grade(scored[related1].Score.Fast, scored[related1].SsykIsRelated)
            .ShouldBe(MatchGrade.Related);
        MatchGradeCalculator.Grade(scored[related2].Score.Fast, scored[related2].SsykIsRelated)
            .ShouldBe(MatchGrade.Related);
        scored[strong].SsykIsRelated.ShouldBeFalse("en exakt-yrke-annons är aldrig related.");

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        // The {Related} band returns EXACTLY the two related-only ads, count == in-band size (2).
        var relatedPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Related], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        relatedPage.Items.Select(i => i.Id).ToHashSet().ShouldBe(relatedIds, ignoreOrder: true,
            "{Related}-bandet ska returnera EXAKT de related-only-annonserna (∈ related ∧ ∉ exakt).");
        relatedPage.TotalCount.ShouldBe(2,
            "TotalCount för {Related} ska vara antalet related-annonser (2), inte hela korpusen.");

        // Related is EXCLUDED when not selected: a {Good, Strong} band must contain neither related ad.
        var fastBandPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Good, MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var fastBandIds = fastBandPage.Items.Select(i => i.Id).ToHashSet();
        fastBandIds.ShouldNotContain(related1.Value,
            "en related-annons får aldrig dyka upp i {Good, Strong} (Related är sin egen rung).");
        fastBandIds.ShouldNotContain(related2.Value);
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
                orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        fullPage.TotalCount.ShouldBe(4,
            "TotalCount ska vara antalet annonser i Strong-bandet (4), inte hela korpusen (9) " +
            "— grad-WHERE:ts count måste räknas om (rad-86-fixen).");
        fullPage.Items.Count.ShouldBe(4);

        // Pagination over the filtered band: pageSize 2 < 4 in-band → page 1 has 2 items,
        // TotalCount still 4, and no phantom items leak onto a page beyond the band.
        var firstPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 2, ct);

        firstPage.Items.Count.ShouldBe(2,
            "Sida 1 med pageSize 2 ska ha exakt 2 träffar ur det grad-filtrerade bandet.");
        firstPage.TotalCount.ShouldBe(4,
            "TotalCount ska vara bandets storlek (4) oavsett pageSize — paginering över den " +
            "grad-filtrerade mängden, ingen spök-sida.");
        firstPage.TotalPages.ShouldBe(2);

        var secondPage = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 2, pageSize: 2, ct);

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
        // #552: a plain "region Match + employment NULL" ad no longer grades Good under a stated-
        // employment profile (employment NULL → NoMatch → Basic). The reachable Good is the #477
        // containment carve-out (län-only ad in the preferred kommun's parent län + employment Match).
        var laterGood = await SeedContainmentGoodAsync(run, t.AddDays(10), ct); // higher recency, lower grade
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
            sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: false, status: JobAdStatusFilter.None, seekerId: default,
            page: 1, pageSize: 100, ct);

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
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var returnedIds = page.Items.Select(i => i.Id).ToList();
        returnedIds.ShouldBe([strong.Value],
            "Strong-bandet är taket: grad-filtret returnerar den starkaste Fast-annonsen " +
            "(och bara den) — det finns ingen Top-nivå att filtrera på i Fast-bandet.");
        page.TotalCount.ShouldBe(1);
    }

    // ===============================================================
    // 6. #371/#382 (RE-BIND G3-OPT-A — the FILTER-side twin of MatchSortOracleTests'
    //    SearchByMatch_DoesNotSeparateSameFastTupleByMustHave_WhileGradeWould_DivergenceG3OptA).
    //    The UNIFIED rule: SORT + grade-FILTER + headline-COUNT all rank on the Fast band
    //    (Grade(MatchScore)); the card BADGE is the Full requirement-aware grade
    //    (Grade(FullMatchScore), F1(b)-gated). Fast is an honest COARSENING of Full (no
    //    Kind-separated must-have-lexeme column → Fast can compute neither Top nor the F1(b)
    //    degrade). So an ad in the ?matchGrades=Strong bucket (Fast-Strong) can carry a Full
    //    BADGE of Top (UP) or Good (DOWN, F1(b) degrade). This is the #371 manifestation, and
    //    it is BOUND (Fast filter ≡ Fast band; badge = Full; they diverge by design), never drift.
    //
    //    Provenance-safe overlap (F4-2/F4-3 lesson): BOTH ads carry the SAME skill concept-id in
    //    extracted_terms and that id sits in the CV-skill set; the fullTop ad ALSO puts it in its
    //    must_have partition (covered → must-have Match → Top), while the fullGood ad's must_have is
    //    a DISJOINT term the CV does not cover (must-have NoMatch/Partial → not requirement-backed →
    //    Good). The CV top-5 plaintext set (filter/Fast path) is identical for both → SAME Fast-Strong
    //    bucket; the full CV-skill set (badge/Full path) sees different must-have coverage → Top vs Good.
    //
    //    Helpers below (SkillTerm / MustHaveTerm / SeedJobAdWithTermsAsync / ProfileWithCvSkill +
    //    the SharedSkill/OtherMustHave consts) are deliberately COPIED from MatchSortOracleTests
    //    (kept self-contained per the scaffold convention — this oracle never shares mutable state
    //    with the sort oracle).
    // ===============================================================

    private const string SharedSkillConceptId = "skill-shared-gradefilter-1";
    private const string SharedSkillDisplay = "Delad-skill";
    private const string OtherMustHaveConceptId = "skill-other-musthave-gradefilter-1";
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

    // Same seeding as SeedJobAdAsync, plus an extracted_terms VO (→ STORED extracted_lexemes GIN
    // for the Fast top-5 skill overlap AND the in-memory must-have partition the Full grade reads).
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
            title: "Gradefilter-divergence-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

        jobAd.SetExtractedTerms(terms);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // A FULL profile whose CV-skill set contains the shared skill (so BOTH ads overlap on skill
    // for the Fast/filter path AND the Full grade's must-have set-difference is computable).
    private static FullCandidateMatchProfile ProfileWithCvSkill(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ExactGroups,
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    [Fact]
    public async Task GradeFilter_StrongBand_ContainsAds_WhoseFullBadgeDiffers_BoundDivergenceG3OptA()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // BOTH ads: SAME Fast-Strong tuple (occ + region + employment all Match → Fast rank Strong)
        // and BOTH carry the shared skill term (equal Fast top-5 skill overlap — no Fast split).
        // fullTop:  must_have = the shared skill (CV covers it) + a Skill term → Full grade Top.
        // fullGood: must_have = a DISJOINT must-have (CV does NOT cover) + a Skill term → Full grade
        //           Good (F1(b) degrade — not requirement-backed, but both secondaries confirmed).
        var fullTopTerms = ExtractedTerms.From(
        [
            SkillTerm(SharedSkillConceptId, SharedSkillDisplay),
            MustHaveTerm(SharedSkillConceptId, SharedSkillDisplay),
        ]);
        var fullGoodTerms = ExtractedTerms.From(
        [
            SkillTerm(SharedSkillConceptId, SharedSkillDisplay),
            MustHaveTerm(OtherMustHaveConceptId, OtherMustHaveDisplay),
        ]);

        var fullTop = await SeedJobAdWithTermsAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), fullTopTerms, ct);
        var fullGood = await SeedJobAdWithTermsAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), fullGoodTerms, ct);

        var profile = ProfileWithCvSkill(SharedSkillConceptId);
        var filter = FilterFor(run);

        // ---- Cross-check the SSOT both ways. ----
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var full = await scorer.ScoreFullBatchAsync([fullTop, fullGood], profile, ct);

        // (a) The FAST band (filter/sort/count SSOT) reads Strong for BOTH — same Fast-Strong bucket.
        MatchGradeCalculator.Grade(full[fullTop].Score.Fast, full[fullTop].SsykIsRelated)
            .ShouldBe(MatchGrade.Strong,
                "fullTop ligger i Fast-Strong-bandet (yrke+ort+anställning Match) — det är " +
                "den bucket grad-filtret gallrar på.");
        MatchGradeCalculator.Grade(full[fullGood].Score.Fast, full[fullGood].SsykIsRelated)
            .ShouldBe(MatchGrade.Strong,
                "fullGood ligger i SAMMA Fast-Strong-band — Fast ser inte must-have, så båda " +
                "annonserna hamnar i {Strong}-bucketen oavsett sina olika Full-badgar.");

        // (b) The FULL overload (the card BADGE) DIFFERS — Top (up) vs Good (down, F1(b) degrade).
        var gradeFull_top = MatchGradeCalculator.Grade(full[fullTop].Score);
        var gradeFull_good = MatchGradeCalculator.Grade(full[fullGood].Score);
        gradeFull_top.ShouldBe(MatchGrade.Top,
            "fullTops BADGE = Full requirement-aware Top (must-have Match + båda sekundärer + " +
            "skill-signal) — \"Toppmatch\".");
        gradeFull_good.ShouldBe(MatchGrade.Good,
            "fullGoods BADGE = Full Good (disjunkt must-have ej täckt → F1(b)-degrade under Strong) " +
            "— \"Bra match\".");
        gradeFull_top.ShouldNotBe(gradeFull_good,
            "BADGEN (Full-graden) skiljer de två annonserna åt fastän de delar Fast-Strong-bucket.");

        // ---- The REAL filter: grades:[Strong] (the Fast filter bucket) returns BOTH. ----
        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        var page = await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Strong], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var returnedIds = page.Items.Select(i => i.Id).ToHashSet();

        // The load-bearing assertion: the {Strong} filter bucket contains BOTH ads — the one whose
        // Full BADGE reads "Bra match" (Good) AND the one whose badge reads "Toppmatch" (Top) — because
        // the filter gates on the Fast band (≡ Fast-Strong for both), REGARDLESS of their differing
        // Full badges. This IS the #371 manifestation, and it is BOUND (Fast filter ≡ Fast band; badge =
        // Full; they diverge by design, G3-OPT-A), not drift. The sort-side twin is pinned by
        // MatchSortOracleTests.SearchByMatch_DoesNotSeparateSameFastTupleByMustHave_WhileGradeWould_DivergenceG3OptA.
        returnedIds.ShouldBe([fullTop.Value, fullGood.Value], ignoreOrder: true,
            "{Strong}-grad-filtret ska returnera BÅDA Fast-Strong-annonserna — den vars Full-BADGE " +
            "är \"Bra match\" (Good) OCH den vars badge är \"Toppmatch\" (Top) — eftersom filtret " +
            "gallrar på FAST-bandet (≡ Fast-Strong för båda), oberoende av deras olika Full-badgar. " +
            "Detta är #371-manifestationen: grad-FILTRET pinnar Fast, BADGEN är Full, och de divergerar " +
            "med avsikt (G3-OPT-A) — bundet, aldrig drift (jfr sort-sidans pin i MatchSortOracleTests).");
        page.TotalCount.ShouldBe(2,
            "TotalCount för {Strong} ska vara 2 — räknat över Fast-Strong-bandet (båda annonserna), " +
            "inte över Full-badgen.");
    }

    // ===============================================================
    // 7. #552 grade-gate — the SQL twin floors a STATED-preference-vs-NULL-shadow ad to Basic.
    //
    //    THE THREE-VALUED-LOGIC TRAP (why this is an SQL oracle, not just a C# test): the SQL RB1
    //    floor must fire via an EXPLICIT `col IS NULL` disjunct. `NOT (col = ANY(@prefs))` evaluates
    //    to NULL — not TRUE — when `col` is NULL (Postgres three-valued logic), so a bare membership
    //    negation does NOT floor a NULL shadow; the floor must add
    //    `(Region IS NULL AND Municipality IS NULL)` for ort and `(EmploymentType IS NULL)` for
    //    employment. In-memory C# checks are BLIND to this (null-safe .Contains) — only Testcontainers
    //    proves the SQL disjunct fires. This is RED against current production, which grades both
    //    new-arm ads Good (rank 3) and returns them in the {Good} band, not {Basic}.
    //
    //    ASYMMETRIC SEED (the count-only-oracle rule): ≥2 in-band Basic (the two new-arm ads once the
    //    gate lands, plus a contradiction Basic that is Basic in BOTH production states) + out-of-band
    //    Good (containment) + Strong + untagged, so the {Basic}/{Good} band membership separates the
    //    correct gate from a missing / mis-polarised one.
    // ===============================================================

    [Fact]
    public async Task GradeFilter_GradeGate_StatedPrefNullShadowAds_AreInBasicBand_NotGoodBand()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The two NEW-ARM ads (#552): under the stated-ort+employment Profile() each grades Good
        // TODAY (NULL shadow → NotAssessed) and Basic AFTER the gate (NULL shadow → NoMatch → RB1 floor).
        //   bothNullOrt:   SSYK Match + BOTH ort shadows NULL + employment Match → ort NoMatch (#552).
        //   nullEmployment: SSYK Match + region Match + employment shadow NULL → employment NoMatch (#552).
        var bothNullOrt = await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(20), ct,
            municipalityConceptId: null);
        var nullEmployment = await SeedJobAdAsync(run, PrefGroup, PrefRegion, null, t.AddDays(19), ct);

        // In-band Basic counterfactual (Basic in BOTH production states): region NoMatch contradiction.
        var contradictionBasic = await SeedBasicContradictionAsync(run, t.AddDays(15), ct);

        // Out-of-band anchors (non-vacuity): a genuine containment Good, a Strong, an untagged.
        var good = await SeedContainmentGoodAsync(run, t.AddDays(12), ct);
        var strong = await SeedStrongAsync(run, t.AddDays(11), ct);
        var untagged = await SeedUntaggedSsykNoMatchAsync(run, t.AddDays(5), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        // ---- C# SSOT (the scorer-side of the twin): the new-arm ads grade Basic under #552. ----
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync(
            [bothNullOrt, nullEmployment, contradictionBasic, good, strong], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[bothNullOrt]).ShouldBe(MatchGrade.Basic,
            "#552: SSYK Match + båda ort-shadows NULL (ort angiven) + employment Match → ort NoMatch " +
            "→ RB1-golv → Basic (pre-#552 var detta Good).");
        MatchGradeCalculator.Grade(scores[nullEmployment]).ShouldBe(MatchGrade.Basic,
            "#552: SSYK Match + region Match + NULL employment-shadow (anställning angiven) → employment " +
            "NoMatch → RB1-golv → Basic (pre-#552 var detta Good).");
        // Non-vacuity: the anchors grade as intended.
        MatchGradeCalculator.Grade(scores[good]).ShouldBe(MatchGrade.Good);
        MatchGradeCalculator.Grade(scores[strong]).ShouldBe(MatchGrade.Strong);

        // ---- The SQL twin (grade-WHERE band membership). ----
        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        var goodBandIds = (await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Good], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct))
            .Items.Select(i => i.Id).ToHashSet();

        // Non-vacuity: the containment Good IS in {Good} (the band is reachable + the query works).
        goodBandIds.ShouldContain(good.Value,
            "Containment-Good ska ligga i {Good}-bandet (non-vacuity — bandet är nåbart).");
        // THE RED CORE (ort arm): the both-NULL-ort ad must NOT be in {Good} — it floors to Basic.
        goodBandIds.ShouldNotContain(bothNullOrt.Value,
            "#552: en both-NULL-ort-annons golvas till Basic i SQL-grad-WHERE:t (via en explicit " +
            "Region IS NULL AND Municipality IS NULL-disjunkt) → den får INTE ligga i {Good}-bandet.");
        // THE RED CORE (employment arm): the NULL-employment ad must NOT be in {Good}.
        goodBandIds.ShouldNotContain(nullEmployment.Value,
            "#552: en NULL-employment-annons (anställning angiven) golvas till Basic → inte i {Good}.");

        var basicBandIds = (await query.SearchPerUserAsync(
            filter, profile, grades: [MatchGrade.Basic], sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct))
            .Items.Select(i => i.Id).ToHashSet();

        // Non-vacuity: the genuine contradiction Basic IS in {Basic} (both production states).
        basicBandIds.ShouldContain(contradictionBasic.Value,
            "Motsägelse-Basic ska ligga i {Basic}-bandet (non-vacuity).");
        // THE RED CORE: both new-arm ads land in {Basic} under #552.
        basicBandIds.ShouldContain(bothNullOrt.Value,
            "#552: both-NULL-ort-annonsen ska ligga i {Basic}-bandet (grad-WHERE:ts == null-disjunkt).");
        basicBandIds.ShouldContain(nullEmployment.Value,
            "#552: NULL-employment-annonsen ska ligga i {Basic}-bandet (grad-WHERE:ts == null-disjunkt).");
    }
}
