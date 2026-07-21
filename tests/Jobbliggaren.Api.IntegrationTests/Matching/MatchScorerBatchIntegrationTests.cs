using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Matching.Abstractions;
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
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 Decision A = A1) — the REAL
/// <c>MatchScorer.ScoreBatchAsync</c> against seeded JobAds (raw_payload → STORED generated
/// shadow columns) on real Postgres (Testcontainers, NEVER EF-InMemory — the
/// <c>FromSql("... id = ANY(@p)")</c> translation only exists on the real engine; InMemory
/// hides it, memory ef_strongly_typed_vo_contains). This is the ORACLE for the batch
/// query: the parameterized <c>= ANY</c> over a <c>Guid[]</c> must translate and read the
/// EF.Property shadows.
/// <para>
/// Contract pinned here (the regression + omission rules from the port doc):
/// <list type="bullet">
/// <item>Per-ad <c>MatchScore</c> equals what <see cref="IMatchScorer.ScoreAsync"/> returns
/// for that ad + the same profile (the same four Fast helpers run in-memory).</item>
/// <item>Missing / non-existent ids are SILENTLY OMITTED (no NotFoundException — unlike the
/// single-ad path) so one stale id never fails a page render.</item>
/// <item><b>An ARCHIVED ad IS "missing" to this family</b> (#864, CTO D2 S-split): the batch
/// composes <c>.Where(j => j.Status == JobAdStatus.Active)</c> onto the <c>FromSql</c>, so an
/// archived ad is omitted exactly like a non-existent id. This suite is also the TRANSLATION
/// oracle for that predicate — <c>JobAdStatus</c> is a value-converted record, and only the real
/// engine proves the comparison composes with the <c>= ANY</c>. The SINGLE methods deliberately
/// do NOT gate (the detail page still explains an archived ad, #805-3).</item>
/// </list>
/// </para>
/// </summary>
[Collection("Api")]
public class MatchScorerBatchIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Real Infrastructure scorer (fresh scoped AppDbContext + real Swedish analyzer),
    // parity MatchScorerIntegrationTests.NewScorer.
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        return (scope, new MatchScorer(db, analyzer));
    }

    // Seeds an Imported JobAd whose raw_payload drives the facet columns
    // (parity MatchScorerIntegrationTests.SeedJobAdAsync). null → key omitted → shadow NULL.
    // Spår 3 PR-B: the optional municipalityConceptId (5th-after-ct, default null) folds into
    // workplace_address.municipality_concept_id — every legacy callsite reduces to region-only.
    private async Task<JobAdId> SeedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        CancellationToken ct,
        string? municipalityConceptId = null)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId, employmentTypeConceptId,
            municipalityConceptId);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // The REAL retraction transition (#821: JobAd's only lifecycle method is Archive(),
    // which sets Status — there is no soft-delete axis to stamp).
    private async Task ArchiveAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var ad = await db.JobAds.FindAsync([id], ct);
        ad.ShouldNotBeNull();
        ad!.Archive(clock);
        await db.SaveChangesAsync(ct);
    }

    // The REAL Art. 17 erasure transition (#842) — the tombstone keeps its facet columns, so the
    // erased ad still matches the profile the scorer reads; never a fabricated stamp (#843/AC 4).
    private async Task EraseAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var ad = await db.JobAds.FindAsync([id], ct);
        ad.ShouldNotBeNull();
        ad!.Erase(clock).IsSuccess.ShouldBeTrue("Erase-seeden får inte tyst misslyckas");
        await db.SaveChangesAsync(ct);
    }

    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
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
            + $"\"employment_type\":{employmentJson}}}";
    }

    // workplace_address carries only the present location key(s): both null → "null";
    // region only → legacy single-key shape (NULL municipality shadow); municipality only →
    // NULL region shadow; both → both keys (parity MatchScorerIntegrationTests).
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

    private static string NewConceptId(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..16];

    private static void AssertSameDimension(MatchDimension a, MatchDimension b)
    {
        b.Verdict.ShouldBe(a.Verdict);
        b.Matched.ShouldBe(a.Matched);   // sequence-equal (order included)
        b.Missing.ShouldBe(a.Missing);
    }

    // =================================================================
    // EF translation oracle + per-ad regression contract:
    // ScoreBatchAsync over N ads == ScoreAsync per ad
    // =================================================================

    [Fact]
    public async Task ScoreBatchAsync_ForManyAds_TranslatesAnyQuery_AndEqualsScoreAsyncPerAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");

        var ad1 = await SeedJobAdAsync("Systemutvecklare", grp, reg, emp, ct);
        var ad2 = await SeedJobAdAsync("Sjuksköterska", NewConceptId("grp"), reg, null, ct);
        var ad3 = await SeedJobAdAsync("Lastbilschaufför", null, null, null, ct);

        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [grp],
            PreferredRegionConceptIds: [reg],
            PreferredEmploymentTypeConceptIds: [emp],
            PreferredMunicipalityConceptIds: []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([ad1, ad2, ad3], profile, ct);

        // All three exist → all three present in the batch.
        batch.Count.ShouldBe(3);
        batch.ShouldContainKey(ad1);
        batch.ShouldContainKey(ad2);
        batch.ShouldContainKey(ad3);

        // Per-ad regression: each batched MatchScore equals ScoreAsync for that ad.
        foreach (var id in new[] { ad1, ad2, ad3 })
        {
            var single = await scorer.ScoreAsync(id, profile, ct);
            AssertSameDimension(single.SsykOverlap, batch[id].SsykOverlap);
            AssertSameDimension(single.TitleSimilarity, batch[id].TitleSimilarity);
            AssertSameDimension(single.RegionFit, batch[id].RegionFit);
            AssertSameDimension(single.EmploymentFit, batch[id].EmploymentFit);
        }

        // Sanity: ad1 genuinely scored Match on the three concept-id dims (the seed matches).
        batch[ad1].SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        batch[ad1].RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        batch[ad1].EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    // =================================================================
    // Spår 3 PR-B (ADR 0076-amendment 2026-06-21) — the ort-union RegionFit equals
    // ScoreAsync's for the same ad+profile through the batch path too (the embedded-
    // helper regression contract extended to the union). One ad scores a union Match
    // (municipality hit while the region is not preferred) and one a union NoMatch
    // (ort stated, ad has both ort values, no hit) — both must match the single-ad path.
    // =================================================================

    [Fact]
    public async Task ScoreBatchAsync_OrtUnion_RegionFit_EqualsScoreAsync_ForMatchAndNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var prefRegion = NewConceptId("reg");
        var prefMunicipality = NewConceptId("mun");

        // matchAd: municipality hit (adMun == prefMunicipality) while its region is NOT in the
        // pref set → union Match carried by the municipality.
        var matchAd = await SeedJobAdAsync(
            "Systemutvecklare", grp, NewConceptId("reg"), null, ct,
            municipalityConceptId: prefMunicipality);

        // noMatchAd: ort stated (prefs below), ad has BOTH a region and a municipality, neither
        // in the pref sets → union NoMatch; Missing = both present ad ort values.
        var noMatchAd = await SeedJobAdAsync(
            "Sjuksköterska", grp, NewConceptId("reg"), null, ct,
            municipalityConceptId: NewConceptId("mun"));

        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [grp],
            PreferredRegionConceptIds: [prefRegion],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [prefMunicipality]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([matchAd, noMatchAd], profile, ct);

        batch.Count.ShouldBe(2);
        foreach (var id in new[] { matchAd, noMatchAd })
        {
            var single = await scorer.ScoreAsync(id, profile, ct);
            AssertSameDimension(single.RegionFit, batch[id].RegionFit);
        }

        // Sanity: the batch genuinely exercised both union verdicts.
        batch[matchAd].RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        batch[matchAd].RegionFit.Matched.ShouldBe([prefMunicipality]);
        batch[noMatchAd].RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        batch[noMatchAd].RegionFit.Missing.ShouldNotBeEmpty();
    }

    // =================================================================
    // #552 grade-gate (ADR 0076-amendment) — the new stated-preference-vs-NULL-shadow NoMatch
    // flows through the BATCH path identically to ScoreAsync (the embedded-helper regression
    // contract extended to the gate). One ad has ort stated + BOTH ort shadows NULL (→ RegionFit
    // NoMatch, empty evidence), one has employment stated + NULL employment shadow (→ EmploymentFit
    // NoMatch, empty evidence). RED against current production (both read NotAssessed today).
    // =================================================================

    [Fact]
    public async Task ScoreBatchAsync_GradeGate_StatedPrefNullShadow_IsNoMatch_EqualsScoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var prefRegion = NewConceptId("reg");
        var prefEmployment = NewConceptId("emp");

        // ortNullAd: ort stated (region + municipality below) but the ad carries NEITHER ort
        // value (both shadows NULL) → RegionFit NoMatch, empty evidence (#552).
        var ortNullAd = await SeedJobAdAsync(
            "Systemutvecklare", grp, null, prefEmployment, ct, municipalityConceptId: null);

        // employmentNullAd: employment stated but the ad's employment shadow is NULL → EmploymentFit
        // NoMatch, empty evidence (#552). Region present + preferred so RegionFit is a clean Match.
        var employmentNullAd = await SeedJobAdAsync(
            "Sjuksköterska", grp, prefRegion, null, ct);

        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [grp],
            PreferredRegionConceptIds: [prefRegion],
            PreferredEmploymentTypeConceptIds: [prefEmployment],
            PreferredMunicipalityConceptIds: [NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([ortNullAd, employmentNullAd], profile, ct);

        batch.Count.ShouldBe(2);
        foreach (var id in new[] { ortNullAd, employmentNullAd })
        {
            var single = await scorer.ScoreAsync(id, profile, ct);
            AssertSameDimension(single.RegionFit, batch[id].RegionFit);
            AssertSameDimension(single.EmploymentFit, batch[id].EmploymentFit);
        }

        // The new #552 verdicts, via the batch path.
        batch[ortNullAd].RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "Ort angiven + annons utan ort-värde → NoMatch via batch-vägen (#552).");
        batch[ortNullAd].RegionFit.Matched.ShouldBeEmpty();
        batch[ortNullAd].RegionFit.Missing.ShouldBeEmpty();

        batch[employmentNullAd].EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "Anställning angiven + NULL employment-shadow → NoMatch via batch-vägen (#552).");
        batch[employmentNullAd].EmploymentFit.Matched.ShouldBeEmpty();
        batch[employmentNullAd].EmploymentFit.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // Missing / non-existent ids are OMITTED (no NotFoundException)
    // =================================================================

    [Fact]
    public async Task ScoreBatchAsync_WithNonExistentId_OmitsIt_WithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var existing = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var ghost = JobAdId.New(); // never seeded

        var profile = new CandidateMatchProfile("Titel", [grp], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        // Single-ad ScoreAsync would throw NotFoundException for the ghost — the batch
        // must NOT: it silently omits the missing id.
        var batch = await scorer.ScoreBatchAsync([existing, ghost], profile, ct);

        batch.Count.ShouldBe(1);
        batch.ShouldContainKey(existing);
        batch.ShouldNotContainKey(ghost);
    }

    // =================================================================
    // SPECIFICATION (#864) — an ARCHIVED ad is MISSING to the batch family
    // =================================================================

    // =================================================================
    // This was a CHARACTERIZATION test (Feathers 2004, ch. 13): it asserted that an archived
    // ad WAS scored, so the gap could not be forgotten, and it said "when #864 lands, this
    // goes RED — rewrite it as a specification." #864 landed. This is that specification.
    //
    // The contract (CTO D2, S-split): on the BATCH family "missing" means the row does not
    // exist OR the ad is not Active. A batch is a decoration of a LIST; an ad the product may
    // no longer present must not carry a grade in one. The SINGLE family deliberately keeps
    // scoring archived ads (the detail page explains WHY an ad was a fit, #805-3) — that spec
    // lives in JobAdMatchDetailEndpointTests and is the inverse mutation's detector.
    //
    // ASYMMETRIC SEED (2 live + 1 archived), not 1+1: a cardinality assertion over a 1+1 seed
    // reads 1 whether the gate is CORRECT or INVERTED (`== Archived`) — blind to polarity. With
    // 2+1 the three states separate: correct → 2, gate deleted → 3, gate inverted → 1.
    // =================================================================
    [Fact]
    public async Task ScoreBatchAsync_OmitsArchivedAd_ScoringOnlyTheActiveOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var live1 = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var live2 = await SeedJobAdAsync("Backendutvecklare", grp, null, null, ct);
        var archived = await SeedJobAdAsync("Arkitekt", grp, null, null, ct);
        await ArchiveAsync(archived, ct);

        var profile = new CandidateMatchProfile("Titel", [grp], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([live1, live2, archived], profile, ct);

        // NON-VACUITY FIRST (#841): the ACTIVE ads ARE scored. Without this, "the archived one is
        // absent" would pass trivially the day the query returns nothing at all.
        batch.ShouldContainKey(live1);
        batch.ShouldContainKey(live2);

        // THE SPECIFICATION: the archived ad is omitted exactly like a non-existent id — the port's
        // definition of "missing" (row absent OR not Active).
        batch.ShouldNotContainKey(archived,
            "ScoreBatchAsync gates on Status == Active (#864): an archived ad is MISSING to the " +
            "batch family, omitted exactly like a non-existent id.");

        // Polarity: 2, not 1 (inverted gate) and not 3 (gate deleted).
        batch.Count.ShouldBe(2);
    }

    // =================================================================
    // SPECIFICATION (#864 follow-up, B4) — an ERASED ad is MISSING to the batch family, and it is
    // the row that pins the ALLOW-LIST as an allow-list.
    //
    // The archived spec above cannot see the deny-list mutation `== Active` → `!= Archived`
    // (Archived is excluded by both forms) — #864 recorded that survivor, and #886 unlocked its
    // kill by retiring Expired and leaving Erased (#842, a real Art. 17 transition) as the
    // reachable row where the two forms disagree. The gate's own comment names the harm: a
    // deny-list would grade the tombstone, and its '[raderad]' company name would be EMAILED.
    // Erase() keeps the facet columns, so the erased ad still matches the profile — its omission
    // is the status gate's doing alone.
    //
    // ASYMMETRIC SEED (2 live + 1 erased): correct → 2 · deny-list OR gate deleted → 3 (the
    // tombstone graded) · inverted (== Archived) → 0. Every mutant state separates.
    // =================================================================
    [Fact]
    public async Task ScoreBatchAsync_OmitsErasedAd_TheAllowListPin()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var live1 = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var live2 = await SeedJobAdAsync("Backendutvecklare", grp, null, null, ct);
        var erased = await SeedJobAdAsync("Raderad", grp, null, null, ct);
        await EraseAsync(erased, ct);

        var profile = new CandidateMatchProfile("Titel", [grp], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([live1, live2, erased], profile, ct);

        // NON-VACUITY FIRST (#841): the ACTIVE ads ARE scored.
        batch.ShouldContainKey(live1);
        batch.ShouldContainKey(live2);

        batch.ShouldNotContainKey(erased,
            "ScoreBatchAsync grindar ALLOW-LIST (== Active, #864 D4): en raderad annons " +
            "(Art. 17-tombstone, #842) är MISSING — en deny-list (!= Archived) hade graderat " +
            "tombstonen och mejlat '[raderad]' som match.");

        // Polarity: 2, not 3 (deny-list/deleted) and not 0 (inverted).
        batch.Count.ShouldBe(2);
    }

    // =================================================================
    // Empty id list → empty result (the no-ids fast-path, no query)
    // =================================================================

    [Fact]
    public async Task ScoreBatchAsync_WithEmptyIdList_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = new CandidateMatchProfile("Titel", [NewConceptId("grp")], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([], profile, ct);

        batch.ShouldBeEmpty();
    }
}
