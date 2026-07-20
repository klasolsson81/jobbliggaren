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
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
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
/// <para>
/// <b>The lifecycle axis (#864, CTO G1).</b> Until #864 this oracle seeded ONLY Active ads, so it
/// was silent on the very axis where the two engines diverged: the SQL twin has always filtered
/// <c>Status == Active</c>; the C# scorer carried no gate at all. Test 4 closes that — it seeds an
/// ARCHIVED ad (via the real <c>JobAd.Archive</c> transition, never a fabricated column) and an
/// ERASED ad (the real Art. 17 transition, #842 — the #864-B4 deny-list axis, unlocked by #886)
/// and pins that BOTH engines exclude BOTH. It binds all THREE independently deletable gates —
/// delete <c>ScoreBatchAsync</c>'s, <c>ScoreFullBatchAsync</c>'s, or <c>ApplyFilter</c>'s, and it
/// goes red — and on each gate the erased row additionally kills the flip <c>== Active</c> →
/// <c>!= Archived</c>, which the archived row alone cannot see.
/// </para>
/// <para>
/// <b>It DOES reach one SINGLE scorer method, on purpose.</b> Test 4 calls <c>ScoreAsync</c> — which
/// does not gate <c>Active</c> — to establish the COUNTERFACTUAL for the ARCHIVED ad: that it WOULD
/// have graded into the band had it stayed Active, because absence only evidences a gate if the row
/// would otherwise have been in the set. So an <c>== Active</c> gate added to <c>ScoreAsync</c> ALSO
/// turns this oracle red, and that is correct: the single family's deliberate non-gating on the
/// ACTIVE axis is part of the contract (#864 D2/D3, the detail page still explains an archived ad —
/// #805-3). <b>If you are here because you added an <c>== Active</c> gate to <c>ScoreAsync</c> and
/// this went red, that gate is the defect — not this assertion.</b>
/// </para>
/// <para>
/// <b>The ERASED axis is the exception, and it changed (#885).</b> The single family now DENIES the
/// tombstone (<c>!= Erased</c>) — the match-detail path must not serve an ad whose own detail page
/// answers 410 Gone. That gate is CORRECT, and it is why the erased counterfactual in test 4 no
/// longer runs through the port: no scorer method can grade an erased row any more. Its two claims
/// are asserted off the port instead — see the comment at the site. <b>Do not "fix" a red here by
/// removing the <c>!= Erased</c> gate; that gate is the contract (#885), and its own detectors are
/// <c>Score{,Full}Async_RefusesAnErasedAd_TheTombstoneIsNotServed</c>.</b>
/// </para>
/// <para>
/// <b>What this oracle does NOT reach</b> (its claim is an enumeration, not a vague "matching is
/// coherent"): the persisted-notification surfaces — <c>/matchningar</c>, the two digest emails and
/// the Översikt badges — do not run through either engine here; they are gated and specced
/// separately (PR-A, #864).
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

    // #477 Low 1 / #552 — a containment län (the parent län of the preferred kommun). Under #552 a
    // STATED-ort NULL shadow is NoMatch (floors), so the ONLY reachable Good for a both-stated
    // profile is the #477 containment carve-out (a län-only ad in the preferred kommun's parent
    // län + employment Match → RegionFit NotAssessed → Good). DISTINCT from PrefRegion/OtherRegion
    // so no other seed's grade changes when it is added to the profile.
    private const string ContainmentLan = "reg-matchcount-containment-lan";

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
            // #477 / #552 — set DIRECTLY. Behaviour-inert except for a län-only ContainmentLan ad,
            // which reads NotAssessed → the only reachable Good under this both-stated profile.
            ContainmentRegionConceptIds = [ContainmentLan],
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
        Remote: false,
        Q: null);

    // ---------------------------------------------------------------
    // Seeding — raw_payload drives the facet columns. null group/region/employment →
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
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

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

    // Good: exactly one confirmed secondary. Under #552 the reachable Good for a both-stated
    // profile is the #477 containment carve-out — a LÄN-ONLY ad (region = ContainmentLan, kommun
    // NULL) reads RegionFit NotAssessed (neither floors nor lifts) + employment Match → Good. (Pre-
    // #552 a plain "region Match + employment NULL" ad was also Good, but #552 floors it to Basic:
    // employment NULL under a stated-employment profile is now NoMatch. Containment stays Good in
    // BOTH production states, so every count-oracle Good sanity assertion survives the gate.)
    private Task<JobAdId> SeedGoodAsync(string run, DateTimeOffset publishedAt, CancellationToken ct) =>
        SeedJobAdAsync(run, PrefGroup, ContainmentLan, PrefEmployment, publishedAt, ct);

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

    // #864 — the REAL retraction transition (Archive() is JobAd's only lifecycle method since #821;
    // there is no soft-delete axis, and #843 forbids fabricating one via db.Entry/raw UPDATE). The
    // Result is asserted and the status is read back through a FRESH context: a silently-failed
    // Archive() would leave the ad Active and make the lifecycle oracle below vacuously green — it
    // would "prove" agreement by seeding the wrong thing.
    private async Task ArchiveAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var ad = await db.JobAds.FindAsync([id], ct);
        ad.ShouldNotBeNull();
        ad!.Archive(clock).IsSuccess.ShouldBeTrue("Archive() ska lyckas — annars är orakelet vakuöst.");
        await db.SaveChangesAsync(ct);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await verifyDb.JobAds.AsNoTracking().FirstAsync(j => j.Id == id, ct);
        stored.Status.ShouldBe(JobAdStatus.Archived);
    }

    // #864 follow-up (B4) — the REAL Art. 17 erasure transition (#842). Same fail-loud discipline
    // as ArchiveAsync: Result asserted, status AND the surviving facet read back through a fresh
    // context. The facet read-back is the attribution: Erase() keeps the *_concept_id columns, so
    // the erased row still sits in the run's filter band and its absence below is the status
    // gate's doing alone — a facet that did NOT survive would make the erased leg vacuous.
    private async Task EraseAsync(JobAdId id, string run, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var ad = await db.JobAds.FindAsync([id], ct);
        ad.ShouldNotBeNull();
        ad!.Erase(clock).IsSuccess.ShouldBeTrue("Erase() ska lyckas — annars är orakelet vakuöst.");
        await db.SaveChangesAsync(ct);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await verifyDb.JobAds.AsNoTracking().FirstAsync(j => j.Id == id, ct);
        stored.Status.ShouldBe(JobAdStatus.Erased);
        stored.WorktimeExtentConceptId.ShouldBe(run,
            "run-facetten ska ÖVERLEVA Erase() — annars lämnar tombstonen frågan via " +
            "worktime-filtret och vittnar inte om status-grinden.");
    }

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
    // 4. #864 (CTO G1 / D6-B5) — THE LIFECYCLE AXIS THE COHERENCE ORACLE WAS BLIND TO.
    //
    // THIS IS A REPAIR, NOT A NEW GUARD. This class's own docstring calls it "det bärande
    // koherens-orakelet": it exists to pin that the SQL grade engine (CountPerUserAsync →
    // JobAdSearchComposition.ApplyFilter) and the C# grade engine (MatchScorer.ScoreBatchAsync)
    // never disagree. But EVERY ad in its seed was Active — so it was SILENT on the one axis where
    // the two engines actually HAD diverged: the SQL twin has always filtered Status == Active;
    // the C# scorer carried no gate at all (#864). The load-bearing coherence oracle's claim was
    // broader than its reach. That is the #841 disease sitting inside the guard that exists to
    // prevent divergence.
    //
    // It is TWO-SIDED and behavioural, not a source-string scan. All THREE independently deletable
    // gates are bound (the C# engine has two batch methods, not one):
    //   - delete ScoreBatchAsync's gate     → the C# Fast side includes the archived ad → RED.
    //   - delete ScoreFullBatchAsync's gate → the C# Full side includes it → RED.
    //   - delete ApplyFilter's Status gate  → the SQL side includes it → RED.
    //
    // THE COUNTERFACTUAL IS ASSERTED, NOT ASSUMED. Absence only evidences a gate if the row would
    // otherwise have been in the set. The UNGATED single method (ScoreAsync, the other half of the
    // S-split) supplies that: it says the archived ad WOULD grade Strong. Without it, a retuned
    // grade ladder could drop the archived ad from the band for an unrelated reason and this oracle
    // would go green while an engine had lost its gate.
    //
    // ASYMMETRIC SEED (2 live + 1 archived + 1 erased), and it is load-bearing here, not a habit:
    // with 1 live + 1 archived, an INVERTED C# gate (`== Archived`) returns exactly ONE graded ad —
    // the archived one — and the "expected == count" agreement would read 1 == 1 and pass GREEN
    // while the two engines were grading DISJOINT SETS. With this seed every mutant state
    // separates at every gate: correct → 2 == 2; gate deleted → 4; deny-list (`!= Archived`) → 3;
    // inverted → 1.
    //
    // THE ERASED ROW IS THE DENY-LIST AXIS (#864 follow-up, B4): the archived row binds gate
    // DELETION but is blind to the flip `== Active` → `!= Archived` (excluded by both forms).
    // #864 recorded that survivor; #886 retired Expired and left Erased (#842, real transition,
    // facets survive) as the reachable row where the forms disagree — on ALL THREE gates this
    // oracle binds. A deny-list on any engine grades the tombstone; here that divergence is RED.
    //
    // ABSENCE IS ASSERTED, NEVER INDEXED (the architect's warning): with the gate in place,
    // scores[archivedId] THROWS KeyNotFoundException. A blind index would turn this oracle into an
    // exception instead of a verdict.
    // ===============================================================

    [Fact]
    public async Task Count_AndCsharpScorer_AgreeThatArchivedAndErasedAdsAreNotMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // 2 live Strong + 1 archived Strong + 1 erased Strong. All four would grade Strong on the
        // inputs the scorer reads — archiving changes none of them, and Erase() keeps the facet
        // columns. Only the lifecycle status differs.
        var live1 = await SeedStrongAsync(run, t.AddDays(20), ct);
        var live2 = await SeedStrongAsync(run, t.AddDays(19), ct);
        var archived = await SeedStrongAsync(run, t.AddDays(18), ct);
        await ArchiveAsync(archived, ct);
        var erased = await SeedStrongAsync(run, t.AddDays(17), ct);
        await EraseAsync(erased, run, ct);

        var profile = Profile();
        var filter = FilterFor(run);
        var headline = new[] { MatchGrade.Good, MatchGrade.Strong };

        // ---- The C# engine (MatchScorer.ScoreBatchAsync — the scorer-side SSOT) ----
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync([live1, live2, archived, erased], profile.Fast, ct);

        // NON-VACUITY FIRST (#841): the two ACTIVE ads ARE scored, and they genuinely land in the
        // headline band. Without this, "the archived one is absent" would pass trivially the day
        // the batch query returns nothing at all.
        scores.ShouldContainKey(live1);
        scores.ShouldContainKey(live2);
        MatchGradeCalculator.Grade(scores[live1]).ShouldBe(MatchGrade.Strong);
        MatchGradeCalculator.Grade(scores[live2]).ShouldBe(MatchGrade.Strong);

        // THE COUNTERFACTUAL — asserted, not assumed. "The archived ad is absent" is only evidence
        // of a GATE if the ad would OTHERWISE have been in the set. Nothing above says that: it
        // rests on the seed helper, and the day the grade ladder is retuned the archived ad could
        // fall out of the headline band for an unrelated reason — at which point this oracle would
        // read 2 == 2 and go GREEN while the SQL engine had lost its gate entirely.
        // The S-split supplies its own oracle here: ScoreAsync is the SINGLE method, deliberately
        // UNGATED (#864 D2), so it says what the archived ad WOULD grade. It grades Strong — so its
        // absence from the batch is the gate's doing, and nothing else's.
        var archivedIfItHadStayedActive = await scorer.ScoreAsync(archived, profile.Fast, ct);
        MatchGradeCalculator.Grade(archivedIfItHadStayedActive).ShouldBe(MatchGrade.Strong,
            "Den arkiverade annonsen SKULLE ha graderat Strong (den ogrindade single-metoden säger " +
            "det) — annars mäter detta orakel inte grinden utan seedens råkade gradfall.");

        // #885 — THE ERASED COUNTERFACTUAL NO LONGER GOES THROUGH THE PORT, AND CANNOT.
        //
        // This used to be `ScoreAsync(erased)`, asserting Strong. #885 gated the SINGLE family on
        // `!= Erased` (the match-detail path must not serve a tombstone once /job-ads/{id} answers
        // 410), so no scorer method will ever grade an erased row again — by design. The old line
        // now throws NotFoundException. It is NOT relocated to another scorer method, because there
        // is none: the claim has to be established off the port entirely.
        //
        // It conflated TWO claims in one call, and each is established independently — more
        // directly than the call did:
        //   1. "The facets SURVIVED Erase()" — asserted against the DATABASE ROW itself, in
        //      EraseAsync's fail-loud read-back (`stored.WorktimeExtentConceptId.ShouldBe(run)`).
        //      That is the stronger form: it reads the column rather than inferring its survival
        //      from a grade.
        //   2. "Those facets WOULD have graded Strong" — carried by the ARCHIVED twin asserted
        //      immediately above. `archived` and `erased` are seeded by the SAME SeedStrongAsync
        //      helper with the SAME `run` facet, so they are identical in every scored input and
        //      differ ONLY in which transition ran. A twin seeded here purely to re-prove it would
        //      be redundant with that assertion.
        //
        // So the erased leg still cannot pass by seed decay: if Erase() had eaten the facets,
        // EraseAsync goes red before this test body runs.

        // THE C# SIDE OF THE INVARIANT: the archived ad is MISSING to the batch scorer (#864).
        // Asserted as ABSENCE — never `scores[archived]`, which now throws KeyNotFoundException.
        scores.ShouldNotContainKey(archived,
            "MatchScorer.ScoreBatchAsync grindar på Status == Active (#864) — en arkiverad annons " +
            "är FRÅNVARANDE ur C#-motorns resultat, precis som ett id som inte finns.");

        // THE DENY-LIST AXIS (#864 B4 → killed here): the ERASED tombstone is missing too. The
        // archived assert above stays green under `!= Archived`; this one goes RED — it is the
        // allow-list pin on the Fast engine.
        scores.ShouldNotContainKey(erased,
            "MatchScorer.ScoreBatchAsync grindar ALLOW-LIST (== Active, #864 D4) — en deny-list " +
            "(!= Archived) hade graderat Art. 17-tombstonen (#842).");

        // The C# engine has TWO batch methods with TWO independently deletable gates. Binding only
        // the Fast one would leave this oracle's "both engines" claim wider than its reach — and
        // ScoreFullBatchAsync is the one with production callers. Bind it too.
        var fullScores = await scorer.ScoreFullBatchAsync([live1, live2, archived, erased], profile, ct);
        fullScores.ShouldContainKey(live1);
        fullScores.ShouldContainKey(live2);
        fullScores.ShouldNotContainKey(archived,
            "ScoreFullBatchAsync grindar på Status == Active (#864) — samma kontrakt som Fast-batchen. " +
            "Utan denna rad kunde Full-batchens grind raderas med orakelet GRÖNT.");
        fullScores.ShouldNotContainKey(erased,
            "Full-batchens allow-list-pin: en deny-list (!= Archived) hade graderat tombstonen — " +
            "utan denna rad kunde Full-grindens flip överleva med orakelet GRÖNT.");

        var csharpHeadlineCount = scores.Count(kv =>
            MatchGradeCalculator.Grade(kv.Value) is { } g && headline.Contains(g));

        // ---- The SQL engine (CountPerUserAsync — ApplyFilter's own Status == Active) ----
        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;
        var sqlCount = await query.CountPerUserAsync(filter, profile, headline, ct);

        // ---- THE COHERENCE INVARIANT, now stated on the lifecycle axis ----
        // Both engines must count exactly the two ACTIVE Strong ads. This is the assertion the
        // asymmetric seed protects: 1 live + 1 archived would read 1 == 1 even with the C# gate
        // INVERTED, i.e. with the two engines grading disjoint sets.
        csharpHeadlineCount.ShouldBe(2,
            "C#-motorn ska gradera exakt de TVÅ aktiva annonserna i headline-bandet — inte 4 " +
            "(grinden raderad), inte 3 (deny-list: tombstonen graderad) och inte 1 (grinden " +
            "inverterad).");
        sqlCount.ShouldBe(csharpHeadlineCount,
            "SQL-motorn (ApplyFilter: Status == Active) och C#-motorn (MatchScorer: Status == " +
            "Active, #864) MÅSTE vara eniga om att en ARKIVERAD eller RADERAD annons inte är en " +
            "matchning. Divergens här är exakt det detta orakel finns för att fånga — och exakt " +
            "det den var BLIND för så länge varenda annons i seeden var Active.");

        // And the coherence the class already pins (count == list TotalCount) still holds with an
        // archived ad in the corpus — the archived ad is absent from the list path too.
        var page = await query.SearchPerUserAsync(
            filter, profile, headline, sort: JobAdSortBy.PublishedAtDesc,
            orderByMatchRank: false, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 1, ct);
        sqlCount.ShouldBe(page.TotalCount,
            "Count == list-vägens TotalCount ska hålla även när korpusen innehåller en arkiverad " +
            "och en raderad annons.");
    }

    // ===============================================================
    // 5. PR-4 (#300, ADR 0084 fråga D) — the count is LIST-ONLY: a Related-grade ad does NOT
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

    // ===============================================================
    // 6. #552 grade-gate — the headline count EXCLUDES a STATED-preference-vs-NULL-shadow ad,
    //    because the gate floors it from Good to Basic (below the {Good, Strong} headline band).
    //    This is the count-side twin of the grade-filter oracle's new-arm test: the live-notis
    //    number must SHRINK by exactly the ads the gate demotes. RED against current production,
    //    which grades both new-arm ads Good and counts them in the headline (4, not 2).
    //
    //    The SQL floor must fire via an explicit `col IS NULL` disjunct (three-valued logic:
    //    `NOT (col = ANY(...))` is NULL — not TRUE — for a NULL col), so only Testcontainers proves
    //    it. Non-vacuity: the two genuine Strong ads ARE counted, so "count shrank" is not "count is 0".
    // ===============================================================

    [Fact]
    public async Task Count_HeadlineBand_ExcludesGradeGateFlooredAds_StatedPrefNullShadow()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Two genuine Strong (headline, both production states) + two #552 new-arm ads that grade
        // Good today and Basic after the gate.
        var strong1 = await SeedStrongAsync(run, t.AddDays(20), ct);
        var strong2 = await SeedStrongAsync(run, t.AddDays(19), ct);
        //   bothNullOrt:   SSYK Match + BOTH ort shadows NULL + employment Match → ort NoMatch (#552).
        var bothNullOrt = await SeedJobAdAsync(run, PrefGroup, null, PrefEmployment, t.AddDays(15), ct,
            municipalityConceptId: null);
        //   nullEmployment: SSYK Match + region Match + NULL employment shadow → employment NoMatch (#552).
        var nullEmployment = await SeedJobAdAsync(run, PrefGroup, PrefRegion, null, t.AddDays(14), ct);

        var profile = Profile();
        var filter = FilterFor(run);

        // C# SSOT (the scorer-side of the twin): the new-arm ads grade Basic under #552.
        var (scoreScope, scorer) = NewScorer();
        using var scoreDispose = scoreScope;
        var scores = await scorer.ScoreBatchAsync(
            [strong1, strong2, bothNullOrt, nullEmployment], profile.Fast, ct);
        MatchGradeCalculator.Grade(scores[strong1]).ShouldBe(MatchGrade.Strong);
        MatchGradeCalculator.Grade(scores[strong2]).ShouldBe(MatchGrade.Strong);
        MatchGradeCalculator.Grade(scores[bothNullOrt]).ShouldBe(MatchGrade.Basic,
            "#552: both-NULL-ort + employment Match golvar till Basic (pre-#552 Good).");
        MatchGradeCalculator.Grade(scores[nullEmployment]).ShouldBe(MatchGrade.Basic,
            "#552: region Match + NULL employment (anställning angiven) golvar till Basic (pre-#552 Good).");

        var (queryScope, query) = NewPerUserQuery();
        using var queryDispose = queryScope;

        var headlineCount = await query.CountPerUserAsync(
            filter, profile, [MatchGrade.Good, MatchGrade.Strong], ct);
        headlineCount.ShouldBe(2,
            "#552: headline-counten {Good, Strong} ska vara 2 (bara de två äkta Strong) — de två " +
            "grade-gate-golvade annonserna (both-NULL-ort och NULL-employment) demoteras till Basic " +
            "och räknas INTE. Pre-#552 räknades de som Good (4).");
    }
}
