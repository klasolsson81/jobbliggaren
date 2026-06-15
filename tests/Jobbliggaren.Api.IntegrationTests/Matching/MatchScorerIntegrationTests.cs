using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Common.Exceptions;
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
///     is a dormant forward-compat guard (fires only when a non-Swedish language is
///     requested, F4-8/9). The analyzer DOES throw for TextLanguage.English (proven
///     below) — that is the guard's future trigger, not F4-5's active path.
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
        var analyzer = new SwedishTextAnalyzer(new SnowballSwedishStemmer());
        return (scope, new MatchScorer(db, analyzer));
    }

    // Re-derives the expected dim-2 lexemes LIVE via the same analyzer the scorer
    // uses — so Matched/Missing assertions never go stale against a stemmer bump
    // (anti-stale, F4-2/F4-3 lesson). Returns Ordinal-distinct lexemes.
    private static List<string> SwedishLexemes(string text)
    {
        var analyzer = new SwedishTextAnalyzer(new SnowballSwedishStemmer());
        return analyzer.ToLexemes(text, TextLanguage.Swedish)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // Seeds an Imported JobAd whose raw_payload drives the STORED shadow columns:
    //   occupation_group.concept_id          → occupation_group_concept_id
    //   workplace_address.region_concept_id  → region_concept_id
    //   employment_type.concept_id           → employment_type_concept_id
    // null → key omitted → that shadow column is NULL (the NotAssessed-by-NULL path).
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

    // occupation_group + employment_type are TOP-LEVEL; region lives under
    // workplace_address (paritet JobAdGeneratedColumnsTests.BuildRawPayload).
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
            PreferredEmploymentTypeConceptIds: []);

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
            PreferredEmploymentTypeConceptIds: []);

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
            PreferredEmploymentTypeConceptIds: []);

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
            PreferredEmploymentTypeConceptIds: []);

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
            cvTitle, [], [], []);

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
        var profile = new CandidateMatchProfile(cvTitle, [], [], []);

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
        var profile = new CandidateMatchProfile(cvTitle, [], [], []);

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
        var profile = new CandidateMatchProfile(string.Empty, [], [], []);

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
        var profile = new CandidateMatchProfile(cvTitle, [], [], []);

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
            PreferredEmploymentTypeConceptIds: [adEmployment]);

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
    public async Task MatchScorer_RealAnalyzer_ThrowsNotSupportedException_ForEnglish()
    {
        // Proves the precondition the narrow-catch test relies on: the real
        // analyzer genuinely throws for English (F4-2 amendment) — so the
        // NotAssessed above can ONLY come from the scorer catching it, not from
        // the analyzer silently degrading.
        var analyzer = new SwedishTextAnalyzer(new SnowballSwedishStemmer());

        Should.Throw<NotSupportedException>(
            () => analyzer.ToLexemes("Software Engineer", TextLanguage.English));
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
            "Titel", [], [adRegion, NewConceptId("reg")], []);

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
            "Titel", [], [NewConceptId("reg"), NewConceptId("reg")], []);

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
        var profile = new CandidateMatchProfile("Titel", [], [], []);

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
            "Titel", [], [NewConceptId("reg")], []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreAsync(jobAdId, profile, ct);

        score.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.RegionFit.Matched.ShouldBeEmpty();
        score.RegionFit.Missing.ShouldBeEmpty();
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
            "Titel", [], [], [adEmployment, NewConceptId("emp")]);

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
            "Titel", [], [], [NewConceptId("emp"), NewConceptId("emp")]);

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
        var profile = new CandidateMatchProfile("Titel", [], [], []);

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
            "Titel", [], [], [NewConceptId("emp")]);

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
        var profile = new CandidateMatchProfile("Titel", [], [], []);

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
            PreferredEmploymentTypeConceptIds: [NewConceptId("emp")]);

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

    private static void AssertSameDimension(MatchDimension a, MatchDimension b)
    {
        b.Verdict.ShouldBe(a.Verdict);
        b.Matched.ShouldBe(a.Matched);   // sequence-equal (order included)
        b.Missing.ShouldBe(a.Missing);
    }
}
