using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 Decision A = A1) — the REAL
/// <c>MatchScorer.ScoreBatchAsync</c> against seeded JobAds (raw_payload → STORED generated
/// shadow columns) on real Postgres (Testcontainers, NEVER EF-InMemory — the
/// <c>FromSql("... id = ANY(@p)")</c> translation only exists on the real engine; InMemory
/// hides it, memory ef_strongly_typed_vo_contains). This is the ORACLE for the batch
/// query: the parameterized <c>= ANY</c> over a <c>Guid[]</c> must translate, compose with
/// the soft-delete query filter, and read the EF.Property shadows.
/// <para>
/// Contract pinned here (the regression + omission rules from the port doc):
/// <list type="bullet">
/// <item>Per-ad <c>MatchScore</c> equals what <see cref="IMatchScorer.ScoreAsync"/> returns
/// for that ad + the same profile (the same four Fast helpers run in-memory).</item>
/// <item>Missing / non-existent ids are SILENTLY OMITTED (no NotFoundException — unlike the
/// single-ad path) so one stale id never fails a page render.</item>
/// <item>Soft-deleted ads are absent (the global DeletedAt==null filter composes with the
/// FromSql).</item>
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
    private async Task<JobAdId> SeedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId, employmentTypeConceptId);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // Sets DeletedAt directly (JobAd has no domain SoftDelete — Archive sets Status, not
    // DeletedAt). The global query filter is DeletedAt == null. Parity
    // ManualPostingPersistenceTests' EF-direct soft-delete (architect-fix-rapport 2026-05-17).
    private async Task SoftDeleteAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var ad = await db.JobAds.FindAsync([id], ct);
        ad.ShouldNotBeNull();
        db.Entry(ad!).Property(nameof(JobAd.DeletedAt)).CurrentValue = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string BuildRawPayload(
        string externalId,
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
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson}}}";
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
    // Soft-deleted ads are absent (the global DeletedAt==null filter composes
    // with the FromSql `= ANY`)
    // =================================================================

    [Fact]
    public async Task ScoreBatchAsync_WithSoftDeletedAd_OmitsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = NewConceptId("grp");
        var live = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var deleted = await SeedJobAdAsync("Arkitekt", grp, null, null, ct);
        await SoftDeleteAsync(deleted, ct);

        var profile = new CandidateMatchProfile("Titel", [grp], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var batch = await scorer.ScoreBatchAsync([live, deleted], profile, ct);

        batch.ShouldContainKey(live);
        batch.ShouldNotContainKey(deleted);
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
