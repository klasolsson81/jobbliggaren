using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Fas 4 STEG 5 (F4-5, ADR 0074 row U5a; senior-cto-advisor + dotnet-architect
/// 2026-06-15) — the REAL deterministic "Fast mode" <c>MatchScorer</c> against a
/// seeded JobAd (raw_payload → STORED generated shadow columns) on real Postgres
/// (Testcontainers, ALDRIG EF-InMemory — InMemory ignorerar
/// <c>HasComputedColumnSql(stored: true)</c> så NULL/Match-distinktionen skulle ge
/// falska gröna, jfr JobAdGeneratedColumnsTests) + the real Swedish Snowball
/// <c>ITextAnalyzer</c> (dim-2 lexem måste matcha to_tsvector's stem). Mirrors
/// OccupationCodeDeriverIntegrationTests' real-analyzer construction and
/// JobAdGeneratedColumnsTests' raw_payload seeding (occupation_group /
/// workplace_address.region_concept_id / employment_type top-level paths).
///
/// SUT contract (CTO Decision 3/4/5, bound shapes):
///   internal sealed class MatchScorer(AppDbContext db, ITextAnalyzer analyzer)
///       : IMatchScorer
///   ValueTask&lt;MatchScore&gt; ScoreAsync(JobAdId, CandidateMatchProfile, CancellationToken)
/// Per-dimension semantics (CTO Decision 3 — LOCKED):
///   • NotAssessed when the CV-side input is EMPTY, OR the ad's shadow column is
///     NULL. NoMatch ONLY when data present on BOTH sides and disjoint.
///   • dim-1 SSYK / dim-3 Region+Employment: Match / NoMatch / NotAssessed (no Partial).
///   • dim-2 Title: Match (all ad lexemes covered) / Partial (overlap + leftover) /
///     NoMatch (disjoint) / NotAssessed (empty title or NotSupportedException).
///     Matched = cv∩ad lexemes; Missing = ad\cv lexemes; both Ordinal-sorted.
///   • English CV title → no language signal in F4-5 (CTO re-ruling 2026-06-15,
///     Resolution B) → Swedish-stemmed → disjoint → TitleSimilarity.NoMatch while
///     the concept-id dims still score. The NotSupportedException→NotAssessed catch
///     is a dormant forward-compat guard that never fires (the scorer always passes
///     TextLanguage.Swedish). Since F4-9 wired the English analyzer (it no longer
///     throws), the guard is now harmless dead-defense, not a live trigger.
///   • JobAd not found → NotFoundException.
///
/// RED until IMatchScorer + MatchScore/MatchDimension/CandidateMatchProfile/
/// MatchDimensionVerdict ship in Application and MatchScorer ships internal sealed
/// in Infrastructure.Matching.
/// </summary>
[Collection("Api")]
public class MatchScorerIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // ---------------------------------------------------------------
    // SUT factory + seeding helpers
    // ---------------------------------------------------------------

    // Constructs the real Infrastructure scorer directly (paritet
    // OccupationCodeDeriverIntegrationTests.NewDeriver / SwedishStemmerPostgres
    // ParityTests.NewAnalyzer): a fresh scoped AppDbContext + the real Swedish
    // analyzer. The DbContext is held by the returned disposable scope.
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        return (scope, new MatchScorer(db, analyzer));
    }

    // Re-derives the expected dim-2 lexemes LIVE via the same analyzer the scorer
    // uses — so Matched/Missing assertions never go stale against a stemmer bump
    // (anti-stale, F4-2/F4-3 lesson). Returns Ordinal-distinct lexemes.
    private static List<string> SwedishLexemes(string text)
    {
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        return analyzer.ToLexemes(text, TextLanguage.Swedish)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // Seeds an Imported JobAd whose raw_payload drives the STORED shadow columns:
    //   occupation_group.concept_id               → occupation_group_concept_id
    //   workplace_address.region_concept_id       → region_concept_id
    //   workplace_address.municipality_concept_id → municipality_concept_id (Spår 3 PR-B)
    //   employment_type.concept_id                → employment_type_concept_id
    // null → key omitted → that shadow column is NULL (the NotAssessed-by-NULL path).
    //
    // Spår 3 (ADR 0076-amendment 2026-06-21): the municipality shadow folds into the SAME
    // "ort" dimension (RegionFit) as the region shadow — see ScoreOrtUnion. The municipality
    // parameter is OPTIONAL (defaults to null) so every pre-existing callsite reduces EXACTLY
    // to the old region-only behaviour: with municipalityConceptId == null AND regionConceptId
    // present, the payload is byte-for-byte the old single-key workplace_address; with both
    // null, workplace_address stays null (both shadows NULL).
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
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // occupation_group + employment_type are TOP-LEVEL; region AND municipality live
    // under workplace_address (paritet JobAdGeneratedColumnsTests.BuildRawPayload +
    // JobAdConfiguration: region_concept_id / municipality_concept_id both read from
    // raw_payload->'workplace_address'->>...). workplace_address is null ONLY when BOTH
    // location ids are null (both shadows NULL); otherwise it carries exactly the present
    // key(s) — so a region-present + municipality-null seed is byte-for-byte the legacy
    // single-key payload (the old region-only tests are unaffected).
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

    // workplace_address carries only the present location key(s): both null → "null" (both
    // shadows NULL); region only → {"region_concept_id":...} (legacy shape, NULL municipality
    // shadow — the impl-trap NULL-municipality case); municipality only → {"municipality_
    // concept_id":...} (NULL region shadow); both → both keys.
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

    // =================================================================
    // dim 1 — SSYK overlap (Match / NoMatch / NotAssessed)
    // =================================================================

    [Fact]
    public async Task MatchScorer_SsykOverlap_AdGroupInCvList_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [adGroup, NewConceptId("grp")],
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.SsykOverlap.Matched.ShouldBe([adGroup]);
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_BothPresentDisjoint_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [NewConceptId("grp"), NewConceptId("grp")],
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.SsykOverlap.Matched.ShouldBeEmpty();
        // Missing = the ad's group the CV lacks (civic direction).
        score.SsykOverlap.Missing.ShouldBe([adGroup]);
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_EmptyCvList_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [], // empty CV-side input → NotAssessed, never NoMatch
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.SsykOverlap.Matched.ShouldBeEmpty();
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_AdShadowColumnNull_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // occupation_group omitted from payload → occupation_group_concept_id NULL.
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", null, null, null, ct);
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [NewConceptId("grp")], // CV present, ad NULL → NotAssessed
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.SsykOverlap.Matched.ShouldBeEmpty();
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // dim 2 — Title similarity (Match / Partial / NoMatch / NotAssessed)
    // =================================================================

    [Fact]
    public async Task MatchScorer_TitleSimilarity_CvLexemesCoverAllAdLexemes_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad title "Systemutvecklare"; CV title is a superset ("Senior
        // systemutvecklare") so every ad lexeme is covered → Match, Missing empty.
        const string adTitle = "Systemutvecklare";
        const string cvTitle = "Senior systemutvecklare";
        var jobAdId = await SeedJobAdAsync(adTitle, null, null, null, ct);
        var profile = new CandidateMatchProfile(
            cvTitle, [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var adLex = SwedishLexemes(adTitle);
        var cvLex = SwedishLexemes(cvTitle);
        var expectedMatched = adLex.Where(l => cvLex.Contains(l))
            .OrderBy(l => l, StringComparer.Ordinal).ToList();

        score.TitleSimilarity.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.TitleSimilarity.Matched.ShouldBe(expectedMatched);
        score.TitleSimilarity.Matched.ShouldNotBeEmpty();
        score.TitleSimilarity.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_TitleSimilarity_OverlapWithLeftover_IsPartial()
    {
        var ct = TestContext.Current.CancellationToken;
        // Shared "utvecklare" stem (utveckl), but the ad also wants "backend" which
        // the CV lacks → Partial; Matched non-empty, Missing non-empty. NB: Swedish
        // Snowball does NOT decompose compounds, so "Systemutvecklare" stems to
        // "systemutveckl" (≠ "utveckl") — verified via to_tsvector('swedish'); the
        // fixture uses the simple word "Utvecklare" so the shared stem is real.
        const string adTitle = "Utvecklare backend";
        const string cvTitle = "Utvecklare frontend";
        var jobAdId = await SeedJobAdAsync(adTitle, null, null, null, ct);
        var profile = new CandidateMatchProfile(cvTitle, [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var adLex = SwedishLexemes(adTitle);
        var cvLex = SwedishLexemes(cvTitle);
        var expectedMatched = adLex.Where(l => cvLex.Contains(l))
            .OrderBy(l => l, StringComparer.Ordinal).ToList();
        var expectedMissing = adLex.Where(l => !cvLex.Contains(l))
            .OrderBy(l => l, StringComparer.Ordinal).ToList();

        // Sanity: this fixture is genuinely Partial (overlap + leftover).
        expectedMatched.ShouldNotBeEmpty();
        expectedMissing.ShouldNotBeEmpty();

        score.TitleSimilarity.Verdict.ShouldBe(MatchDimensionVerdict.Partial);
        score.TitleSimilarity.Matched.ShouldBe(expectedMatched);
        score.TitleSimilarity.Missing.ShouldBe(expectedMissing);
    }

    [Fact]
    public async Task MatchScorer_TitleSimilarity_DisjointLexemes_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        // Both titles yield lexemes but they share none → NoMatch.
        const string adTitle = "Sjuksköterska";
        const string cvTitle = "Lastbilschaufför";
        var jobAdId = await SeedJobAdAsync(adTitle, null, null, null, ct);
        var profile = new CandidateMatchProfile(cvTitle, [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var adLex = SwedishLexemes(adTitle);
        var cvLex = SwedishLexemes(cvTitle);
        // Sanity: genuinely disjoint, both non-empty.
        adLex.ShouldNotBeEmpty();
        cvLex.ShouldNotBeEmpty();
        adLex.Any(l => cvLex.Contains(l)).ShouldBeFalse();

        score.TitleSimilarity.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.TitleSimilarity.Matched.ShouldBeEmpty();
        // Missing = all ad lexemes (the CV covers none of them).
        score.TitleSimilarity.Missing.ShouldBe(
            adLex.OrderBy(l => l, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task MatchScorer_TitleSimilarity_EmptyCvTitle_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", null, null, null, ct);
        // Empty CV title yields no lexemes → NotAssessed (CTO Decision 3 rule 1).
        var profile = new CandidateMatchProfile(string.Empty, [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.TitleSimilarity.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.TitleSimilarity.Matched.ShouldBeEmpty();
        score.TitleSimilarity.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_TitleSimilarity_MatchedAndMissing_AreOrdinalSorted()
    {
        var ct = TestContext.Current.CancellationToken;
        // Multi-token titles so ordering is observable; assert lists are Ordinal
        // non-decreasing (determinism — CTO Decision 3).
        const string adTitle = "Utvecklare arkitekt projektledare";
        const string cvTitle = "Arkitekt utvecklare designer";
        var jobAdId = await SeedJobAdAsync(adTitle, null, null, null, ct);
        var profile = new CandidateMatchProfile(cvTitle, [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var matched = score.TitleSimilarity.Matched;
        var missing = score.TitleSimilarity.Missing;
        matched.ShouldBe(matched.OrderBy(l => l, StringComparer.Ordinal).ToList(),
            "Matched-lexem ska vara Ordinal-sorterade (determinism).");
        missing.ShouldBe(missing.OrderBy(l => l, StringComparer.Ordinal).ToList(),
            "Missing-lexem ska vara Ordinal-sorterade (determinism).");
    }

    // =================================================================
    // dim 2 — English title → NotSupportedException caught NARROWLY →
    //         TitleSimilarity.NotAssessed while the OTHER three dims still score
    // =================================================================

    [Fact]
    public async Task MatchScorer_EnglishCvTitle_DegradesToNoMatch_OtherDimensionsStillScore()
    {
        var ct = TestContext.Current.CancellationToken;
        // CTO re-ruling 2026-06-15 (Resolution B): F4-5 has no language signal
        // (CandidateMatchProfile is 4-field, no detector — detection is F4-8/9,
        // ADR 0074 amendment). The scorer always analyzes both titles as Swedish;
        // an English CV title is Swedish-stemmed → disjoint lexemes from the Swedish
        // ad title → TitleSimilarity.NoMatch (graceful degradation: the
        // language-agnostic concept-id dims — SSYK/region/employment — carry the
        // cross-lingual match). The NotSupportedException→NotAssessed catch in the
        // scorer is a dormant forward-compat guard (it does NOT fire here because
        // the language parameter is always Swedish). NotAssessed for non-Swedish
        // titles is deferred to F4-8/9 where the language is known.
        var adGroup = NewConceptId("grp");
        var adRegion = NewConceptId("reg");
        var adEmployment = NewConceptId("emp");
        const string adTitle = "Mjukvaruutvecklare";
        var jobAdId = await SeedJobAdAsync(
            adTitle, adGroup, adRegion, adEmployment, ct);

        var profile = new CandidateMatchProfile(
            Title: "Software Engineer", // English → Swedish-stemmed → disjoint
            SsykGroupConceptIds: [adGroup],
            PreferredRegionConceptIds: [adRegion],
            PreferredEmploymentTypeConceptIds: [adEmployment],
            PreferredMunicipalityConceptIds: []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        // Must NOT crash the whole scoring.
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        // Sanity: the English CV lexemes genuinely share no Swedish stem with the ad.
        var adLex = SwedishLexemes(adTitle);
        var cvLex = SwedishLexemes("Software Engineer");
        adLex.ShouldNotBeEmpty();
        cvLex.ShouldNotBeEmpty();
        adLex.Any(l => cvLex.Contains(l)).ShouldBeFalse();

        score.TitleSimilarity.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.TitleSimilarity.Matched.ShouldBeEmpty();
        // Missing = the ad's Swedish title lexemes (the CV covers none of them).
        score.TitleSimilarity.Missing.ShouldBe(
            adLex.OrderBy(l => l, StringComparer.Ordinal).ToList());

        // The three concept-id dimensions are unaffected (language-agnostic).
        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    [Fact]
    public async Task MatchScorer_RealAnalyzer_HandlesEnglish_F49()
    {
        // F4-9 wired the English analyzer (ADR 0074 F4-2 amendment); ToLexemes no longer
        // throws for English. The scorer still passes Swedish hardcoded (F4-5 has no
        // language signal), so the dormant NotSupportedException→NotAssessed guard simply
        // never fires — harmless forward-compat — and the English-CV-title degradation above
        // (Swedish-stemmed → disjoint → NoMatch) is unchanged.
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());

        var lexemes = analyzer.ToLexemes("Software Engineer", TextLanguage.English);

        lexemes.ShouldNotBeEmpty();
        await Task.CompletedTask;
    }

    // =================================================================
    // dim 3a — Region fit (Match / NoMatch / NotAssessed)
    // =================================================================

    [Fact]
    public async Task MatchScorer_RegionFit_AdRegionInPreferred_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var jobAdId = await SeedJobAdAsync("Titel", null, adRegion, null, ct);
        var profile = new CandidateMatchProfile(
            "Titel", [], [adRegion, NewConceptId("reg")], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe([adRegion]);
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_RegionFit_AdRegionNotInPreferred_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var jobAdId = await SeedJobAdAsync("Titel", null, adRegion, null, ct);
        var profile = new CandidateMatchProfile(
            "Titel", [], [NewConceptId("reg"), NewConceptId("reg")], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBe([adRegion]);
    }

    [Fact]
    public async Task MatchScorer_RegionFit_EmptyPreferred_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var jobAdId = await SeedJobAdAsync("Titel", null, adRegion, null, ct);
        var profile = new CandidateMatchProfile("Titel", [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_RegionFit_AdShadowColumnNull_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // region omitted from payload → region_concept_id NULL.
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, ct);
        var profile = new CandidateMatchProfile(
            "Titel", [], [NewConceptId("reg")], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // dim 3 (ort-union) — Spår 3 PR-B (ADR 0076-amendment 2026-06-21;
    // senior-cto-advisor verdict C). The RegionFit dimension becomes a
    // region ∪ municipality UNION: two location granularities fold into ONE
    // dimension (the property keeps the name RegionFit). The verdict is pure
    // set logic over { preferredRegions ∪ preferredMunicipalities } vs
    // { adRegion ∪ adMunicipality } — NO threshold:
    //   • Match    iff (adRegion ∈ prefRegions) OR (adMun ∈ prefMunicipalities).
    //               Matched = the hit value(s), Ordinal-sorted; Missing = [].
    //   • NoMatch  iff an ort pref IS stated AND the ad HAS ≥1 ort value AND no
    //               union hit. Matched = []; Missing = the ad's PRESENT ort
    //               value(s), Ordinal-sorted.
    //   • NotAssessed iff NO ort pref stated (BOTH pref lists empty) OR the ad
    //               carries NEITHER ort value (BOTH shadows NULL).
    // CRITICAL impl-trap (CTO C): NoMatch is `stated AND ad-has-some-ort AND
    // no-union-hit`, NEVER a bare `!prefMun.Contains(adMun)`. A NULL municipality
    // shadow on an ad that HAS a region must NOT read as a municipality-NoMatch
    // and must NOT appear in Missing.
    //
    // RED until ScoreOrtUnion replaces ScoreMembership for RegionFit in all four
    // score paths and the AdShadowRow projects the MunicipalityConceptId shadow.
    // =================================================================

    [Fact]
    public async Task MatchScorer_OrtUnion_RegionHitOnly_MunicipalityNull_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        // adReg=R, adMun=null; prefReg=[R], prefMun=[] → region hit → Match, Matched=[R].
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile(
            "Titel", [], [adRegion, NewConceptId("reg")], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe([adRegion]);
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_MunicipalityHitOnly_RegionNotPreferred_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        // prefReg=[] (region not preferred), prefMun=[M]; adReg=L, adMun=M → municipality
        // hit carries the Match even though the ad's region is not in the (empty) pref set.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            "Titel", [], [], [], [adMunicipality, NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe([adMunicipality]);
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_BothHit_MatchedIsRegionAndMunicipalityOrdinalSorted()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        // prefReg=[L], prefMun=[M]; adReg=L, adMun=M → BOTH hit → Match, Matched=[M,L]
        // Ordinal-sorted (we assert the Ordinal-sorted union, not a fixed order).
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            "Titel", [], [adRegion], [], [adMunicipality]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var expectedMatched = new[] { adRegion, adMunicipality }
            .OrderBy(v => v, StringComparer.Ordinal).ToList();

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe(expectedMatched);
        score.RegionFit.Matched.ShouldBe(
            score.RegionFit.Matched.OrderBy(v => v, StringComparer.Ordinal).ToList(),
            "Matched-orterna ska vara Ordinal-sorterade (determinism).");
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_RegionHit_AdHasNonPreferredMunicipality_IsMatch_NoMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        // prefReg=[L], prefMun=[]; adReg=L, adMun=M (M not preferred — prefMun empty).
        // The region hit makes it a Match; the ad's non-preferred municipality must NOT
        // surface in Missing (Match has Missing=[] by rule 2).
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            "Titel", [], [adRegion], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe([adRegion]);
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_StatedButNoHit_BothAdOrtValuesPresent_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        // prefReg=[X], prefMun=[K] (X≠L, K≠M); adReg=L, adMun=M → ort stated, ad has both,
        // no union hit → NoMatch; Missing = the ad's present ort values [L,M] Ordinal-sorted.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            "Titel", [], [NewConceptId("reg")], [], [NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var expectedMissing = new[] { adRegion, adMunicipality }
            .OrderBy(v => v, StringComparer.Ordinal).ToList();

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBe(expectedMissing);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_SameLanDifferentKommun_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        // CTO same-län-different-kommun edge: prefReg=[] (no region stated), prefMun=[K];
        // adReg=L, adMun=K2 (K2≠K). The municipality differs and no region is preferred →
        // NoMatch; Missing = the ad's present ort values [L,K2] Ordinal-sorted. This is the
        // case that a bare `!prefMun.Contains(adMun)` would get RIGHT — but it is pinned to
        // prove the union still surfaces BOTH present ad ort values in Missing, not just the
        // municipality.
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            "Titel", [], [], [], [NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        var expectedMissing = new[] { adRegion, adMunicipality }
            .OrderBy(v => v, StringComparer.Ordinal).ToList();

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBe(expectedMissing);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_NoOrtPreferenceStated_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        // prefReg=[], prefMun=[] → NO ort preference stated → NotAssessed even though the
        // ad carries both ort values.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile("Titel", [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_AdHasNeitherOrtValue_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // adReg=null, adMun=null (workplace_address omitted → both shadows NULL); prefReg=[R],
        // prefMun=[K] → ort stated but the ad carries NEITHER value → NotAssessed.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, null, null, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile(
            "Titel", [], [NewConceptId("reg")], [], [NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_NullMunicipality_RegionMiss_DoesNotFabricateMunicipalityMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        // impl-trap A (CTO C): adReg=R, adMun=NULL; prefReg=[X] (X≠R), prefMun=[K]. The ad
        // HAS a region (so it is assessed) but no municipality → NoMatch, and Missing must be
        // [R] ONLY — a bare `!prefMun.Contains(adMun)` on a NULL adMun would (wrongly) add a
        // municipality entry to Missing or even treat NULL as a disjoint value. The NULL
        // municipality must contribute NOTHING to Missing.
        var adRegion = NewConceptId("reg");
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile(
            "Titel", [], [NewConceptId("reg")], [], [NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBe([adRegion]);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_NullMunicipality_RegionHit_StillMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        // impl-trap B (CTO C): adReg=R, adMun=NULL; prefReg=[R], prefMun=[K]. The NULL
        // municipality must NOT break the region Match — a bare municipality test on NULL
        // could short-circuit to NoMatch. Region hit → Match, Matched=[R].
        var adRegion = NewConceptId("reg");
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile(
            "Titel", [], [adRegion], [], [NewConceptId("mun")]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe([adRegion]);
        score.RegionFit.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // dim 3 (ort-union) — #477 Low 1: kommun→län-containment. ScoreOrtUnion gains a new
    // param (containmentRegions = the län that CONTAIN the user's preferred kommuner, derived by
    // the profile builder from ParentConceptId) and ONE new branch, ONE direction: an ad that is
    // LÄN-ONLY (region shadow present, municipality shadow NULL) whose region is in
    // containmentRegions reads NotAssessed (empty Matched/Missing), NOT NoMatch — a län-only ad in
    // the user's OWN kommun's län is not a location contradiction, so a kommun-only preference must
    // not RB1-floor it to Basic. Evaluated AFTER the direct region/municipality hit and ONLY for
    // län-only ads: a kommun-SPECIFIC ad in a non-preferred kommun of the same län stays NoMatch.
    // NotAssessed (not Match) is the honest verdict — it neither floors nor lifts. Every case pairs
    // a WITH-containment assertion against a WITHOUT-containment contrast, so a reverted containment
    // branch fails loud.
    // =================================================================

    [Fact]
    public async Task MatchScorer_OrtUnion_Containment_LanOnlyAdInContainmentLan_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var containmentLan = NewConceptId("reg");
        var prefMunicipality = NewConceptId("mun");
        // Län-only ad: region = the containment län, municipality NULL. The profile states a kommun
        // preference (ort stated) whose parent län IS the ad's region (ContainmentRegionConceptIds =
        // [containmentLan]) and NO region preference. #477: NotAssessed, not NoMatch.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, containmentLan, null, ct, municipalityConceptId: null);

        var withContainment = new CandidateMatchProfile(
            "Titel", [], [], [], [prefMunicipality])
        {
            ContainmentRegionConceptIds = [containmentLan],
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, withContainment, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed,
            "En län-only-annons (kommun NULL) vars län INNEHÅLLER en föredragen kommun läses som " +
            "NotAssessed via containment, aldrig NoMatch — den golvar inte graden (#477 Low 1).");
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBeEmpty();

        // CONTRAST — identical seed + preference, but WITHOUT the containment set: the SAME ad reads
        // NoMatch (Missing = the ad's län). This proves the containment branch is exactly what flips
        // the verdict — a reverted branch would make the WITH-containment assertion above fail.
        var withoutContainment = new CandidateMatchProfile(
            "Titel", [], [], [], [prefMunicipality]); // ContainmentRegionConceptIds defaults to []

        var contrast = await scorer.ScoreAsync(jobAdId, withoutContainment, ct);
        contrast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "Utan containment-mängden golvas SAMMA annons till NoMatch (kontrasten som bevisar att " +
            "det är containment-grenen som vänder verdiktet).");
        contrast.RegionFit.Missing.ShouldBe([containmentLan]);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_Containment_LanOnlyAdInNonContainmentLan_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = NewConceptId("reg");        // the ad's län
        var containmentLan = NewConceptId("reg");  // a DIFFERENT län that IS in the containment set
        var prefMunicipality = NewConceptId("mun");
        // A län-only ad in län L, but the containment set contains a DIFFERENT län → containment does
        // NOT rescue an arbitrary län → NoMatch (Missing = the ad's län). Pins that the branch is
        // membership-gated on the ad's own region, not a blanket "any län-only ad is NotAssessed".
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile("Titel", [], [], [], [prefMunicipality])
        {
            ContainmentRegionConceptIds = [containmentLan],
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "Containment-mängden räddar bara en län-only-annons vars EGEN region ligger i mängden — " +
            "en annons i ett annat län golvas fortsatt (#477).");
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBe([adRegion]);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_Containment_KommunSpecificAdInContainmentLan_StillNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var containmentLan = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");   // the ad's SPECIFIC (non-preferred) kommun
        var prefMunicipality = NewConceptId("mun"); // the user's preferred kommun (different)
        // Kommun-SPECIFIC ad (municipality present, non-preferred) in a containment län. Containment
        // rescues ONLY län-only ads (municipality NULL) — the !hasAdMunicipality guard — so a
        // kommun-specific ad in a DIFFERENT kommun of the same län stays NoMatch (the user narrowed
        // to their own kommun; mirrors search).
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, containmentLan, null, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile("Titel", [], [], [], [prefMunicipality])
        {
            ContainmentRegionConceptIds = [containmentLan],
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "Containment räddar ENDAST län-only-annonser (kommun NULL). En kommun-specifik annons i " +
            "en icke-föredragen kommun i samma län golvas fortsatt (kommun-NULL-grinden, #477).");
        score.RegionFit.Matched.ShouldBeEmpty();
        // Missing = the ad's present ort values (län + kommun), Ordinal-sorted.
        var expectedMissing = new[] { containmentLan, adMunicipality }
            .OrderBy(v => v, StringComparer.Ordinal).ToList();
        score.RegionFit.Missing.ShouldBe(expectedMissing);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_Containment_LanOnlyAd_WithSsykAndEmploymentMatch_YieldsGood()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var containmentLan = NewConceptId("reg");
        var adEmployment = NewConceptId("emp");
        var prefMunicipality = NewConceptId("mun");
        // SSYK Match + employment Match + a containment län-only ad → RegionFit NotAssessed (neither
        // floors nor lifts) → grades on its merits: one confirmed secondary (employment) → Good.
        // Before #477 the same ad RB1-floored to Basic; the fix's payoff is Good. End-to-end: real
        // scorer → real grade.
        var jobAdId = await SeedJobAdAsync(
            "Titel", adGroup, containmentLan, adEmployment, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile(
            "Titel", [adGroup], [], [adEmployment], [prefMunicipality])
        {
            ContainmentRegionConceptIds = [containmentLan],
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Good,
            "Containment-läns-only-annons + yrke Match + anställning Match → RegionFit NotAssessed " +
            "(neutral) → en bekräftad sekundär → Good (INTE Basic — #477-fixens payoff).");

        // CONTRAST — without the containment set the SAME ad RB1-floors to Basic (RegionFit NoMatch).
        var withoutContainment = new CandidateMatchProfile(
            "Titel", [adGroup], [], [adEmployment], [prefMunicipality]);
        var contrast = await scorer.ScoreAsync(jobAdId, withoutContainment, ct);
        contrast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        MatchGradeCalculator.Grade(contrast).ShouldBe(MatchGrade.Basic,
            "Utan containment golvas SAMMA annons till Basic (RB1) — kontrasten som bevisar fixens " +
            "värde (NoMatch → floor).");
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_Containment_LanOnlyAd_NoOtherSecondary_YieldsBasic()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var containmentLan = NewConceptId("reg");
        var prefMunicipality = NewConceptId("mun");
        // Containment län-only ad + SSYK Match, but NO other confirmed secondary (employment NULL →
        // NotAssessed) → RegionFit NotAssessed + no secondary → Basic. Containment neither floors NOR
        // lifts: it does not, on its own, invent a Good — the honest floor when nothing else confirms.
        var jobAdId = await SeedJobAdAsync(
            "Titel", adGroup, containmentLan, null, ct, municipalityConceptId: null);
        var profile = new CandidateMatchProfile(
            "Titel", [adGroup], [], [], [prefMunicipality])
        {
            ContainmentRegionConceptIds = [containmentLan],
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic,
            "Containment-läns-only-annons + yrke Match, inga andra bekräftade sekundärer → RegionFit " +
            "NotAssessed (neutral) → Basic (containment lyfter inte på egen hand, #477).");
    }

    // =================================================================
    // dim 3b — Employment fit (Match / NoMatch / NotAssessed)
    // =================================================================

    [Fact]
    public async Task MatchScorer_EmploymentFit_AdTypeInPreferred_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adEmployment = NewConceptId("emp");
        var jobAdId = await SeedJobAdAsync("Titel", null, null, adEmployment, ct);
        var profile = new CandidateMatchProfile(
            "Titel", [], [], [adEmployment, NewConceptId("emp")], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.EmploymentFit.Matched.ShouldBe([adEmployment]);
        score.EmploymentFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_EmploymentFit_AdTypeNotInPreferred_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adEmployment = NewConceptId("emp");
        var jobAdId = await SeedJobAdAsync("Titel", null, null, adEmployment, ct);
        var profile = new CandidateMatchProfile(
            "Titel", [], [], [NewConceptId("emp"), NewConceptId("emp")], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.EmploymentFit.Matched.ShouldBeEmpty();
        score.EmploymentFit.Missing.ShouldBe([adEmployment]);
    }

    [Fact]
    public async Task MatchScorer_EmploymentFit_EmptyPreferred_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var adEmployment = NewConceptId("emp");
        var jobAdId = await SeedJobAdAsync("Titel", null, null, adEmployment, ct);
        var profile = new CandidateMatchProfile("Titel", [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.EmploymentFit.Matched.ShouldBeEmpty();
        score.EmploymentFit.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_EmploymentFit_AdShadowColumnNull_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // employment_type omitted → employment_type_concept_id NULL.
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, ct);
        var profile = new CandidateMatchProfile(
            "Titel", [], [], [NewConceptId("emp")], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.EmploymentFit.Matched.ShouldBeEmpty();
        score.EmploymentFit.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // Not-found JobAdId → NotFoundException
    // =================================================================

    [Fact]
    public async Task MatchScorer_UnknownJobAdId_ThrowsNotFoundException()
    {
        var ct = TestContext.Current.CancellationToken;
        var unknownId = JobAdId.New();
        var profile = new CandidateMatchProfile("Titel", [], [], [], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        await Should.ThrowAsync<NotFoundException>(
            async () => await scorer.ScoreAsync(unknownId, profile, ct));
    }

    // =================================================================
    // Determinism — same inputs → equal MatchScore (incl. list order)
    // =================================================================

    [Fact]
    public async Task MatchScorer_SameInputsTwice_ProduceEqualScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var adRegion = NewConceptId("reg");
        var adEmployment = NewConceptId("emp");
        var jobAdId = await SeedJobAdAsync(
            "Utvecklare arkitekt projektledare", adGroup, adRegion, adEmployment, ct);

        var profile = new CandidateMatchProfile(
            Title: "Arkitekt utvecklare designer",
            SsykGroupConceptIds: [adGroup],
            PreferredRegionConceptIds: [adRegion],
            PreferredEmploymentTypeConceptIds: [NewConceptId("emp")],
            PreferredMunicipalityConceptIds: []);

        var (scope1, scorer1) = NewScorer();
        using (scope1)
        {
            var first = await scorer1.ScoreAsync(jobAdId, profile, ct);
            var (scope2, scorer2) = NewScorer();
            using (scope2)
            {
                var second = await scorer2.ScoreAsync(jobAdId, profile, ct);

                // Records compare by value, incl. the IReadOnlyList contents?
                // List equality is reference-based on records, so assert the
                // observable per-dimension shape (verdicts + ordered lists) is
                // identical — the determinism contract.
                AssertSameDimension(first.SsykOverlap, second.SsykOverlap);
                AssertSameDimension(first.TitleSimilarity, second.TitleSimilarity);
                AssertSameDimension(first.RegionFit, second.RegionFit);
                AssertSameDimension(first.EmploymentFit, second.EmploymentFit);
            }
        }
    }

    // =================================================================
    // Spår 3 PR-B (ADR 0076-amendment 2026-06-21) — grade flow-through. The ort-union
    // RegionFit verdict flows into the UNCHANGED MatchGradeCalculator.Grade(MatchScore):
    //   • an ort-union NoMatch RegionFit floors the grade to Basic (RB1, the
    //     stated-region-the-ad-contradicts floor — a municipality-only NoMatch must
    //     trigger the SAME floor as a region NoMatch);
    //   • an ort-union Match RegionFit (via municipality) + SSYK Match + employment Match
    //     yields Strong (both secondaries confirmed).
    // The calculator is NOT changed by PR-B — these prove the union verdict it already
    // reads is produced correctly by the new ScoreOrtUnion. End-to-end: real scorer → real
    // grade. (The page/modal use the requirement-aware Grade(FullMatchScore) overload; this
    // pins the Fast ladder the union feeds.)
    // =================================================================

    [Fact]
    public async Task MatchScorer_OrtUnion_MunicipalityNoMatch_FloorsGradeToBasic()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var adRegion = NewConceptId("reg");
        var adMunicipality = NewConceptId("mun");
        var adEmployment = NewConceptId("emp");
        // SSYK Match + employment Match, but ort stated (municipality) with no union hit and
        // the ad carries both ort values → ort-union NoMatch → RB1 floor → Basic, even though
        // employment is confirmed.
        var jobAdId = await SeedJobAdAsync(
            "Titel", adGroup, adRegion, adEmployment, ct, municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            Title: "Titel",
            SsykGroupConceptIds: [adGroup],
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [adEmployment],
            PreferredMunicipalityConceptIds: [NewConceptId("mun")]); // stated, no hit

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        // The union produced a NoMatch RegionFit ...
        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        // ... which floors the (unchanged) Fast grade to Basic (RB1).
        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Basic);
    }

    [Fact]
    public async Task MatchScorer_OrtUnion_MunicipalityMatch_WithSsykAndEmploymentMatch_YieldsStrong()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var adMunicipality = NewConceptId("mun");
        var adEmployment = NewConceptId("emp");
        // SSYK Match + employment Match + ort-union Match via MUNICIPALITY (the ad's region is
        // not preferred, the municipality is) → both secondaries confirmed → Strong.
        var jobAdId = await SeedJobAdAsync(
            "Titel", adGroup, NewConceptId("reg"), adEmployment, ct,
            municipalityConceptId: adMunicipality);
        var profile = new CandidateMatchProfile(
            Title: "Titel",
            SsykGroupConceptIds: [adGroup],
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [adEmployment],
            PreferredMunicipalityConceptIds: [adMunicipality]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.RegionFit.Matched.ShouldBe([adMunicipality]);
        score.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        MatchGradeCalculator.Grade(score).ShouldBe(MatchGrade.Strong);
    }

    // =================================================================
    // #300 PR-2 — SSYK gate broadened to exact ∪ related (ADR 0084 §5; senior-cto-advisor
    // + dotnet-architect bound contract). The new CandidateMatchProfile.RelatedSsykGroupConceptIds
    // init-property carries the RELATED occupation-group concept-ids (the neighbouring SSYK
    // groups derived from the user's confirmed occupation). The SsykOverlap dimension's gate
    // broadens from "ad group ∈ exact" to "ad group ∈ (exact ∪ related)":
    //   • ad group ∈ RELATED only (NOT in exact) → Match   (the gate-broadening; today NoMatch)
    //   • ad group ∈ EXACT                       → Match   (exact precedence, unchanged)
    //   • ad group ∈ NEITHER (both non-empty)    → NoMatch
    //   • exact EMPTY AND related EMPTY          → NotAssessed (behavior-inert: existing
    //                                              5-arg callers default related to [] and
    //                                              see NO change — PR-2 changes nothing for them)
    // The related set is constructed DIRECTLY in the test via the init-property (the profile
    // builder that POPULATES it is NOT wired until PR-3 — out of scope here). Existing test
    // constructions of CandidateMatchProfile are UNCHANGED (related defaults to []).
    //
    // RED until CandidateMatchProfile gains the RelatedSsykGroupConceptIds init-property AND
    // MatchScorer's SsykOverlap gate reads the exact ∪ related union.
    // =================================================================

    [Fact]
    public async Task MatchScorer_SsykOverlap_AdGroupInRelatedOnly_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        // The ad's group is NOT in the exact set, but IS in the related set → the broadened
        // gate makes it a Match (today, with related unsupported, it would be NoMatch).
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [NewConceptId("grp"), NewConceptId("grp")], // exact: disjoint
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [])
        {
            RelatedSsykGroupConceptIds = [adGroup, NewConceptId("grp")], // related: contains the ad group
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match,
            "En ad-grupp som ligger i RELATED-mängden (men inte exact) ska ge SsykOverlap " +
            "Match — den breddade grinden exact ∪ related (ADR 0084 §5).");
        score.SsykOverlap.Matched.ShouldBe([adGroup]);
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_AdGroupInExact_StillMatch_WhenRelatedAlsoPresent()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        // The ad's group is in the EXACT set (and a non-empty related set is also supplied) →
        // exact precedence: still Match, unchanged from today.
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [adGroup, NewConceptId("grp")], // exact: contains the ad group
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [])
        {
            RelatedSsykGroupConceptIds = [NewConceptId("grp")], // related: non-empty, irrelevant here
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match,
            "En exact-träff ska förbli Match även när en related-mängd också finns " +
            "(exact precedence, oförändrat — ADR 0084 §5).");
        score.SsykOverlap.Matched.ShouldBe([adGroup]);
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_AdGroupInNeitherSet_BothNonEmpty_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        // The ad's group is in NEITHER the exact nor the related set (both non-empty, both
        // disjoint from the ad) → NoMatch (the union still misses).
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [NewConceptId("grp"), NewConceptId("grp")], // exact: disjoint
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [])
        {
            RelatedSsykGroupConceptIds = [NewConceptId("grp"), NewConceptId("grp")], // related: disjoint
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch,
            "En ad-grupp utanför BÅDE exact och related (båda icke-tomma) ska ge NoMatch " +
            "(unionen missar fortfarande — ADR 0084 §5).");
        score.SsykOverlap.Matched.ShouldBeEmpty();
        // Missing = the ad's group the union lacks (civic direction, parity the exact-only path).
        score.SsykOverlap.Missing.ShouldBe([adGroup]);
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_BothExactAndRelatedEmpty_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        // Empty exact AND empty related (the related default) → NotAssessed, never NoMatch.
        // This is the behavior-inert regression: every existing 5-arg caller (related defaults
        // to []) sees EXACTLY today's behaviour — PR-2 changes nothing when no related set is
        // supplied. We construct the profile the legacy way (no init-property) ON PURPOSE.
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [], // exact empty
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: []); // related defaults to [] (init-property)

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed,
            "Tom exact OCH tom related (default) ska ge NotAssessed, aldrig NoMatch — " +
            "PR-2 är beteende-inert för befintliga anropare (ADR 0084 §5).");
        score.SsykOverlap.Matched.ShouldBeEmpty();
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task MatchScorer_SsykOverlap_ExactEmptyButRelatedHits_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = NewConceptId("grp");
        var jobAdId = await SeedJobAdAsync("Systemutvecklare", adGroup, null, null, ct);
        // Edge: exact EMPTY but related NON-empty and hits → the union is non-empty (the CV
        // side is assessable), so the gate is Match, not NotAssessed. Pins that an empty exact
        // does NOT short-circuit to NotAssessed when a related hit exists.
        var profile = new CandidateMatchProfile(
            Title: "Systemutvecklare",
            SsykGroupConceptIds: [], // exact empty
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [])
        {
            RelatedSsykGroupConceptIds = [adGroup], // related hits the ad group
        };

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match,
            "Tom exact men en related-träff ska ge Match — unionen är icke-tom och träffar " +
            "(ADR 0084 §5).");
        score.SsykOverlap.Matched.ShouldBe([adGroup]);
        score.SsykOverlap.Missing.ShouldBeEmpty();
    }

    private static void AssertSameDimension(MatchDimension a, MatchDimension b)
    {
        b.Verdict.ShouldBe(a.Verdict);
        b.Matched.ShouldBe(a.Matched);   // sequence-equal (order included)
        b.Missing.ShouldBe(a.Missing);
    }
}
