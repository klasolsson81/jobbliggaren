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
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the REAL
/// <c>MatchScorer.ScoreFullBatchAsync</c> against seeded JobAds (raw_payload → STORED
/// shadow columns; <c>extracted_terms</c> jsonb VO → STORED <c>extracted_lexemes</c> GIN)
/// on real Postgres (Testcontainers, NEVER EF-InMemory — the <c>FromSql("... id = ANY(@p)")</c>
/// translation, the jsonb VO materialisation, and the generated column only exist on the
/// real engine; InMemory hides all three, memory ef_strongly_typed_vo_contains). This is
/// the ORACLE for the FULL batch query — the zero-N+1 form of <c>ScoreFullAsync</c>.
/// <para>
/// Contract pinned here (the regression + omission rules):
/// <list type="bullet">
/// <item>Per-key <see cref="FullMatchScore"/> EQUALS <see cref="IMatchScorer.ScoreFullAsync"/>
/// for that ad + the same profile — the four embedded Fast dims AND the three new dims
/// (SkillOverlap / MustHaveCoverage / NiceToHaveCoverage).</item>
/// <item>Missing / non-existent ids are SILENTLY OMITTED (an ARCHIVED ad is NOT missing: it is
/// scored -- known gap #864). (no
/// NotFoundException — parity <c>ScoreBatchAsync</c>).</item>
/// <item>Empty id list → empty dict (no query).</item>
/// </list>
/// </para>
/// Mirrors <see cref="FullMatchScorerIntegrationTests"/> (the single-ad sibling +
/// SetExtractedTerms/GIN seeding) and <see cref="MatchScorerBatchIntegrationTests"/> (the
/// Fast batch oracle + omission rules).
/// </summary>
[Collection("Api")]
public class FullMatchScorerBatchIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string CSharpConceptId = "skill-csharp-0001";
    private const string CSharpDisplay = "C#";
    private const string DockerConceptId = "skill-docker-0002";
    private const string DockerDisplay = "Docker";
    private const string KubernetesConceptId = "skill-k8s-0003";
    private const string KubernetesDisplay = "Kubernetes";

    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        return (scope, new MatchScorer(db, analyzer));
    }

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    private static ExtractedTerm RequirementTerm(
        string conceptId, string display, ExtractedTermSource source) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Requirement,
            Source: source, MatchedOn: display, ConceptId: conceptId, Weight: 1);

    // Seeds an Imported JobAd; null terms → extracted_terms stays NULL.
    // Spår 3 PR-B: optional municipalityConceptId folds into workplace_address (default null
    // → every legacy callsite reduces to region-only).
    private async Task<JobAdId> SeedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        ExtractedTerms? terms,
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
            clock: clock).Value;

        if (terms is not null)
            jobAd.SetExtractedTerms(terms);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // The REAL retraction transition (#821: Archive() is JobAd's only lifecycle method -
    // there is no soft-delete axis to stamp).
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

    // workplace_address carries only the present location key(s) (parity
    // MatchScorerIntegrationTests): both null → "null"; region only → legacy single-key
    // shape; municipality only → NULL region shadow; both → both keys.
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

    private static string NewConceptId(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private static FullCandidateMatchProfile FullProfile(
        CandidateMatchProfile fast, params string[] cvSkillConceptIds) =>
        new(fast, cvSkillConceptIds);

    private static void AssertSameDimension(MatchDimension a, MatchDimension b)
    {
        b.Verdict.ShouldBe(a.Verdict);
        b.Matched.ShouldBe(a.Matched);   // sequence-equal (order included)
        b.Missing.ShouldBe(a.Missing);
    }

    private static void AssertSameFull(FullMatchScore single, FullMatchScore batched)
    {
        AssertSameDimension(single.Fast.SsykOverlap, batched.Fast.SsykOverlap);
        AssertSameDimension(single.Fast.TitleSimilarity, batched.Fast.TitleSimilarity);
        AssertSameDimension(single.Fast.RegionFit, batched.Fast.RegionFit);
        AssertSameDimension(single.Fast.EmploymentFit, batched.Fast.EmploymentFit);
        AssertSameDimension(single.SkillOverlap, batched.SkillOverlap);
        AssertSameDimension(single.MustHaveCoverage, batched.MustHaveCoverage);
        AssertSameDimension(single.NiceToHaveCoverage, batched.NiceToHaveCoverage);
    }

    // =================================================================
    // 8. Per-key regression contract: ScoreFullBatchAsync == ScoreFullAsync per ad
    //    (the four Fast dims AND the three new dims), incl. the jsonb materialisation.
    // =================================================================

    [Fact]
    public async Task ScoreFullBatch_ForManyAds_EachKeyEqualsScoreFullAsync_AndSurfacesNewVerdicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");

        // ad1: full signal — Skill + must_have covered, nice_to_have partly covered.
        var ad1Terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
            RequirementTerm(KubernetesConceptId, KubernetesDisplay, ExtractedTermSource.NiceToHave),
        ]);
        var ad1 = await SeedJobAdAsync("Systemutvecklare", grp, reg, emp, ad1Terms, ct);

        // ad2: only a Skill term, different secondary signals.
        var ad2Terms = ExtractedTerms.From([SkillTerm(KubernetesConceptId, KubernetesDisplay)]);
        var ad2 = await SeedJobAdAsync("Plattformsingenjör", grp, reg, null, ad2Terms, ct);

        // ad3: never-extracted (terms NULL). The CV HAS skills → all three new dims
        // Vacuous (PR-B1 RE-BIND G1-b: ad partition empty, CV present → "we looked").
        var ad3 = await SeedJobAdAsync("Lastbilschaufför", null, null, null, terms: null, ct);

        var fast = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [grp],
            PreferredRegionConceptIds: [reg],
            PreferredEmploymentTypeConceptIds: [emp],
            PreferredMunicipalityConceptIds: []);
        var profile = FullProfile(fast, CSharpConceptId, DockerConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreFullBatchAsync([ad1, ad2, ad3], profile, ct);

        batch.Count.ShouldBe(3);
        foreach (var id in new[] { ad1, ad2, ad3 })
        {
            batch.ShouldContainKey(id);
            var single = await scorer.ScoreFullAsync(id, profile, ct);
            AssertSameFull(single.Score, batch[id].Score);
            // #477 Low 2 — the carrier's covered-skill evidence is identical batch-vs-single (same
            // CoveredSkillConceptIds helper on both paths).
            batch[id].MatchedSkillConceptIds.ShouldBe(single.MatchedSkillConceptIds,
                "MatchedSkillConceptIds batch==single (per-key regression contract)");
        }

        // Sanity that the seed genuinely exercised the new dims on ad1.
        batch[ad1].Score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match,
            "CV täcker båda ad-skills → SkillOverlap Match.");
        batch[ad1].Score.SkillOverlap.Matched.ShouldBe([CSharpDisplay, DockerDisplay], ignoreOrder: true);
        // #477 Low 2 — ad1's covered-skill EVIDENCE = the ids the CV covers among ad1's Skill terms
        // (C# + Docker), as concept-ids (not Display labels).
        batch[ad1].MatchedSkillConceptIds.ShouldBe(
            [CSharpConceptId, DockerConceptId], ignoreOrder: true);
        batch[ad1].Score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        batch[ad1].Score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "Enda nice_to_have (Kubernetes) saknas i CV-skill-setet → NoMatch.");

        // ad3 (NULL extracted_terms, CV present): the three new dims are Vacuous, never
        // NoMatch, never NotAssessed (PR-B1 RE-BIND G1-b — ad partition empty, CV present).
        batch[ad3].Score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        batch[ad3].Score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        batch[ad3].Score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
    }

    [Fact]
    public async Task ScoreFullBatch_EmbeddedFast_EqualsScoreAsync_ForSameAdAndFastProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);
        var ad = await SeedJobAdAsync("Arkitekt utvecklare", grp, reg, emp, terms, ct);

        var fast = new CandidateMatchProfile(
            Title: "Utvecklare arkitekt",
            SsykGroupConceptIds: [grp],
            PreferredRegionConceptIds: [reg],
            PreferredEmploymentTypeConceptIds: [emp],
            PreferredMunicipalityConceptIds: []);
        var profile = FullProfile(fast, CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var fastScore = await scorer.ScoreAsync(ad, fast, ct);
        var batch = await scorer.ScoreFullBatchAsync([ad], profile, ct);

        // The embedded Fast in the batch result equals the standalone Fast ScoreAsync.
        AssertSameDimension(fastScore.SsykOverlap, batch[ad].Score.Fast.SsykOverlap);
        AssertSameDimension(fastScore.TitleSimilarity, batch[ad].Score.Fast.TitleSimilarity);
        AssertSameDimension(fastScore.RegionFit, batch[ad].Score.Fast.RegionFit);
        AssertSameDimension(fastScore.EmploymentFit, batch[ad].Score.Fast.EmploymentFit);
        batch[ad].Score.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    // =================================================================
    // Spår 3 PR-B (ADR 0076-amendment 2026-06-21) — the embedded Fast RegionFit in
    // ScoreFullBatchAsync is the ort-union (region ∪ municipality), identical to ScoreAsync's
    // for the same ad + Fast profile through the batch path. One ad scores a union MUNICIPALITY
    // Match (the new granularity) and one a union NoMatch — both equal the single-ad path,
    // proving the Full batch embeds the SAME union, not the legacy region-only ScoreMembership.
    // =================================================================

    [Fact]
    public async Task ScoreFullBatch_EmbeddedFast_RegionFit_IsOrtUnion_EqualsScoreAsync_ForMatchAndNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var prefRegion = NewConceptId("reg");
        var prefMunicipality = NewConceptId("mun");
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);

        // matchAd: municipality hit while its region is NOT preferred → union Match via municipality.
        var matchAd = await SeedJobAdAsync(
            "Systemutvecklare", grp, NewConceptId("reg"), null, terms, ct,
            municipalityConceptId: prefMunicipality);

        // noMatchAd: ort stated, ad has BOTH ort values, neither preferred → union NoMatch.
        var noMatchAd = await SeedJobAdAsync(
            "Sjuksköterska", grp, NewConceptId("reg"), null, terms, ct,
            municipalityConceptId: NewConceptId("mun"));

        var fast = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [grp],
            PreferredRegionConceptIds: [prefRegion],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [prefMunicipality]);
        var profile = FullProfile(fast, CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreFullBatchAsync([matchAd, noMatchAd], profile, ct);

        batch.Count.ShouldBe(2);
        foreach (var id in new[] { matchAd, noMatchAd })
        {
            var single = await scorer.ScoreAsync(id, fast, ct);
            AssertSameDimension(single.RegionFit, batch[id].Score.Fast.RegionFit);
        }

        // Sanity: both union verdicts genuinely scored through the batch.
        batch[matchAd].Score.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        batch[matchAd].Score.Fast.RegionFit.Matched.ShouldBe([prefMunicipality]);
        batch[noMatchAd].Score.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        batch[noMatchAd].Score.Fast.RegionFit.Missing.ShouldNotBeEmpty();
    }

    // =================================================================
    // 9. Missing ids omitted -- and an ARCHIVED ad IS "missing" to this family (#864);
    //    empty ids → empty dict
    // =================================================================

    [Fact]
    public async Task ScoreFullBatch_WithNonExistentId_OmitsIt_WithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var existing = await SeedJobAdAsync(
            "Systemutvecklare", grp, null, null,
            ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]), ct);
        var ghost = JobAdId.New();

        var profile = FullProfile(new CandidateMatchProfile("Titel", [grp], [], [], []), CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreFullBatchAsync([existing, ghost], profile, ct);

        batch.Count.ShouldBe(1);
        batch.ShouldContainKey(existing);
        batch.ShouldNotContainKey(ghost);
    }

    // =================================================================
    // SPECIFICATION (#864) - an ARCHIVED ad is MISSING to the batch family.
    //
    // This was a CHARACTERIZATION test (Feathers 2004, ch. 13) asserting the opposite - that an
    // archived ad WAS fully scored - and it said: "when #864 is fixed, this goes RED; rewrite it
    // as a specification." #864 landed (CTO D2, S-split). This is that specification.
    //
    // The contract: on the BATCH family "missing" = the row does not exist OR the ad is not
    // Active. This is the batch the client-supplied-id endpoint (POST /me/job-ad-match-tags)
    // feeds - the surface where the gap was actually reachable. The SINGLE family deliberately
    // still scores archived ads (the detail page, #805-3).
    //
    // ASYMMETRIC SEED (2 live + 1 archived): a 1+1 seed's cardinality reads 1 under BOTH the
    // correct gate and an INVERTED one (`== Archived`) - blind to polarity. With 2+1: correct
    // → 2, deleted → 3, inverted → 1.
    // =================================================================
    [Fact]
    public async Task ScoreFullBatch_OmitsArchivedAd_ScoringOnlyTheActiveOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);
        var live1 = await SeedJobAdAsync("Systemutvecklare", grp, null, null, terms, ct);
        var live2 = await SeedJobAdAsync("Backendutvecklare", grp, null, null, terms, ct);
        var archived = await SeedJobAdAsync("Arkitekt", grp, null, null, terms, ct);
        await ArchiveAsync(archived, ct);

        var profile = FullProfile(new CandidateMatchProfile("Titel", [grp], [], [], []), CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreFullBatchAsync([live1, live2, archived], profile, ct);

        // NON-VACUITY FIRST (#841): the ACTIVE ads ARE fully scored - otherwise "the archived one
        // is absent" passes trivially the day the query returns nothing at all.
        batch.ShouldContainKey(live1);
        batch.ShouldContainKey(live2);

        // THE SPECIFICATION: the archived ad is omitted exactly like a non-existent id.
        batch.ShouldNotContainKey(archived,
            "ScoreFullBatchAsync gates on Status == Active (#864): an archived ad is MISSING to " +
            "the batch family, omitted exactly like a non-existent id.");

        // Polarity: 2, not 1 (inverted gate) and not 3 (gate deleted).
        batch.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ScoreFullBatch_WithEmptyIdList_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = FullProfile(
            new CandidateMatchProfile("Titel", [NewConceptId("grp")], [], [], []), CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreFullBatchAsync([], profile, ct);

        batch.ShouldBeEmpty();
    }
}
