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
/// <item><b>ARCHIVED ads are NOT absent — they are scored.</b> The old claim ("soft-deleted ads
/// are absent, the DeletedAt==null filter composes with the FromSql") rested on a filter that
/// never had a writer and is now retired (#821). MatchScorer has no status gate: known gap
/// <b>#864</b>, pinned below as a characterization test.</item>
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

    // Seeds an Imported JobAd whose raw_payload drives the STORED shadow columns
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
            clock: clock).Value;

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
    // ARCHIVED ads are NOT absent -- they are scored (no status gate; known gap #864)
    // with the FromSql `= ANY`)
    // =================================================================

    // =================================================================
    // CHARACTERIZATION TEST (#864) — NOT a specification. It asserts what the code
    // ACTUALLY DOES today, so the gap cannot be forgotten (Feathers 2004, ch. 13).
    //
    // MatchScorer has NO Status gate. Its exclusion story was delegated entirely to
    // JobAd's global soft-delete query filter — which was VACUOUS (DeletedAt never
    // had a writer) and is now retired (#821). So an ARCHIVED ad is scored and tagged
    // in production, today. Its siblings DO gate (PerUserJobAdSearchQuery:307/:368),
    // which is what proves this is a gap, not a design choice.
    //
    // The predecessor of this test fabricated DeletedAt via db.Entry(...) — a state
    // production could never reach — and asserted the ad was omitted. Green forever,
    // proving nothing (#843 test fiction). #821 removed the tool that made the
    // fabrication possible.
    //
    // WHEN #864 IS FIXED, THIS TEST GOES RED. That is the signal to rewrite it into a
    // specification, not to patch it back to green.
    // =================================================================
    [Fact]
    public async Task ScoreBatchAsync_WithArchivedAd_StillScoresIt_KnownGap_Issue864()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var live = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var archived = await SeedJobAdAsync("Arkitekt", grp, null, null, ct);
        await ArchiveAsync(archived, ct);

        var profile = new CandidateMatchProfile("Titel", [grp], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([live, archived], profile, ct);

        batch.ShouldContainKey(live);

        // THE GAP (#864): ScoreBatchAsync carries no Status predicate, so the archived ad is
        // scored exactly like the live one. This assertion documents the defect; it does not
        // bless it. Fix #864 and this line flips to ShouldNotContainKey.
        batch.ShouldContainKey(archived,
            "MatchScorer has no Status gate (#864) — an archived ad is still scored. When #864 " +
            "lands, this characterization test must be rewritten as a specification.");
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
