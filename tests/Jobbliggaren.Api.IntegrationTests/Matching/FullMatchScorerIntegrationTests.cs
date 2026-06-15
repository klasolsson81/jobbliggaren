using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Fas 4 STEG 6 (F4-6, ADR 0074 row U5b; senior-cto-advisor Decision A=A2 / B=B2 /
/// C=C1 / D=DD-shape-1+DD-verdict-A / E=DE-combine-2(skill-only)+DE-display-1 /
/// F=F1) — the REAL deterministic FULL <c>MatchScorer.ScoreFullAsync</c> against a
/// seeded JobAd whose <c>extracted_terms</c> jsonb VO drives the STORED generated
/// <c>extracted_lexemes</c> GIN column, on real Postgres (Testcontainers, ALDRIG
/// EF-InMemory — the generated column + jsonb overlap only exist on the real
/// engine) + the real Swedish Snowball <c>ITextAnalyzer</c> for the embedded Fast
/// title dimension. Mirrors MatchScorerIntegrationTests (the F4-5 sibling) and
/// JobAdExtractedTermsPersistenceTests (the extracted_terms round-trip).
///
/// SUT contract (CTO bound shapes):
///   internal sealed class MatchScorer(AppDbContext db, ITextAnalyzer analyzer) : IMatchScorer
///   ValueTask&lt;FullMatchScore&gt; ScoreFullAsync(JobAdId, FullCandidateMatchProfile, CancellationToken)
///
/// Per-dimension semantics for the three NEW dims (CTO Decision D/E — set-emptiness
/// only, NO ratio/Jaccard threshold; parity F4-5 ScoreTitle):
///   • SkillOverlap: ad terms where Kind==Skill (Lexeme==ConceptId, Display=label)
///     vs profile.CvSkillConceptIds. Matched/Missing = Display labels of ad skills
///     whose ConceptId ∈ / ∉ the CV set. Match (all ad skills covered) / Partial
///     (some) / NoMatch (none, both non-empty) / NotAssessed (CV empty OR ad has
///     no Skill terms).
///   • MustHaveCoverage:  ad terms where Kind==Requirement && Source==MustHave.
///   • NiceToHaveCoverage: ad terms where Kind==Requirement && Source==NiceToHave
///     (the bonus bucket — same set-emptiness verdict logic, absence never penalises).
///   • All three: NotAssessed when the CV has no skill ids OR the ad has no terms
///     of that kind/source (NULL/empty extracted_terms) — NEVER NoMatch.
///   • Matched/Missing surface Display labels, NOT raw concept-ids (DE-display-1),
///     and are Ordinal-stable.
///   • Embedded Fast == ScoreAsync(ad, profile.Fast) for the same ad (regression).
///   • JobAd not found → NotFoundException.
///
/// RED until ScoreFullAsync is implemented (the SUT throws NotImplementedException;
/// FullMatchScore/FullCandidateMatchProfile already ship as the RED contract surface).
/// </summary>
[Collection("Api")]
public class FullMatchScorerIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // ---------------------------------------------------------------
    // Deterministic skill/requirement concept-ids + Display labels — WE control
    // the seed, so we assert against these exact values (never guessed taxonomy
    // data). Concept-ids are opaque tokens (the overlap key); Display labels are
    // what the result must surface (DE-display-1).
    // ---------------------------------------------------------------
    private const string CSharpConceptId = "skill-csharp-0001";
    private const string CSharpDisplay = "C#";
    private const string DockerConceptId = "skill-docker-0002";
    private const string DockerDisplay = "Docker";
    private const string KubernetesConceptId = "skill-k8s-0003";
    private const string KubernetesDisplay = "Kubernetes";

    // ---------------------------------------------------------------
    // SUT factory — the real Infrastructure scorer (fresh scoped AppDbContext +
    // the real Swedish analyzer), parity MatchScorerIntegrationTests.NewScorer.
    // ---------------------------------------------------------------
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyzer = new SwedishTextAnalyzer(new SnowballSwedishStemmer());
        return (scope, new MatchScorer(db, analyzer));
    }

    // Builds a Skill ExtractedTerm (Lexeme == ConceptId; Source must be Title or
    // Description per the VO invariant; Display is the human label we assert on).
    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId,
            Display: display,
            Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description,
            MatchedOn: display,
            ConceptId: conceptId,
            Weight: 1);

    // Builds a Requirement ExtractedTerm (Lexeme == ConceptId; Source MustHave or
    // NiceToHave per the VO invariant; Display is the human label).
    private static ExtractedTerm RequirementTerm(
        string conceptId, string display, ExtractedTermSource source) =>
        new(
            Lexeme: conceptId,
            Display: display,
            Kind: ExtractedTermKind.Requirement,
            Source: source,
            MatchedOn: display,
            ConceptId: conceptId,
            Weight: 1);

    // Seeds an Imported JobAd whose raw_payload drives the STORED shadow columns
    // (occupation_group / region / employment) AND, when terms is non-null, sets
    // the extracted_terms VO (which generates the STORED extracted_lexemes GIN
    // column). null terms → extracted_terms stays NULL (never-extracted path).
    private async Task<JobAdId> SeedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        ExtractedTerms? terms,
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

        if (terms is not null)
        {
            jobAd.SetExtractedTerms(terms);
        }

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // occupation_group + employment_type are TOP-LEVEL; region lives under
    // workplace_address (parity MatchScorerIntegrationTests / JobAdGeneratedColumnsTests).
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

    // A FullCandidateMatchProfile with a minimal embedded Fast profile (only the
    // CV-side skill ids vary for the new dims). Title/ssyk/region/employment empty
    // so the Fast dims read NotAssessed and do not interfere with new-dim asserts.
    private static FullCandidateMatchProfile FullProfile(params string[] cvSkillConceptIds) =>
        new(
            Fast: new CandidateMatchProfile(
                Title: "Titel",
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: []),
            CvSkillConceptIds: cvSkillConceptIds);

    // =================================================================
    // SkillOverlap — Match / Partial / NoMatch / NotAssessed
    // =================================================================

    [Fact]
    public async Task ScoreFull_SkillOverlap_AllAdSkillsCovered_IsMatch_SurfacesDisplayLabels()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        // CV covers both ad skills (and an extra the ad does not want).
        var profile = FullProfile(CSharpConceptId, DockerConceptId, KubernetesConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        // Display labels, NOT raw concept-ids (DE-display-1).
        score.SkillOverlap.Matched.ShouldBe([CSharpDisplay, DockerDisplay], ignoreOrder: true);
        score.SkillOverlap.Matched.ShouldNotContain(CSharpConceptId);
        score.SkillOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_SkillOverlap_SomeAdSkillsCovered_IsPartial()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        // CV covers C# but not Docker → Partial.
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Partial);
        score.SkillOverlap.Matched.ShouldBe([CSharpDisplay]);
        // Missing = what the ad wants that the CV lacks (Display).
        score.SkillOverlap.Missing.ShouldBe([DockerDisplay]);
    }

    [Fact]
    public async Task ScoreFull_SkillOverlap_NoAdSkillsCovered_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        // CV has skills, but none the ad wants → NoMatch (both sides non-empty, disjoint).
        var profile = FullProfile(KubernetesConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.SkillOverlap.Matched.ShouldBeEmpty();
        score.SkillOverlap.Missing.ShouldBe([CSharpDisplay, DockerDisplay], ignoreOrder: true);
    }

    [Fact]
    public async Task ScoreFull_SkillOverlap_EmptyCvSkills_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        // Empty CV-side skill ids → NotAssessed, never NoMatch.
        var profile = FullProfile();

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.SkillOverlap.Matched.ShouldBeEmpty();
        score.SkillOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_SkillOverlap_AdHasNoSkillTerms_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad has ONLY a must_have requirement term, no Skill terms → NotAssessed.
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.SkillOverlap.Matched.ShouldBeEmpty();
        score.SkillOverlap.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // MustHaveCoverage — Match / Partial / NoMatch / NotAssessed
    // =================================================================

    [Fact]
    public async Task ScoreFull_MustHaveCoverage_AllMustHavesCovered_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId, DockerConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.MustHaveCoverage.Matched.ShouldBe([CSharpDisplay, DockerDisplay], ignoreOrder: true);
        score.MustHaveCoverage.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_MustHaveCoverage_SomeMustHavesCovered_IsPartial()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Partial);
        score.MustHaveCoverage.Matched.ShouldBe([CSharpDisplay]);
        score.MustHaveCoverage.Missing.ShouldBe([DockerDisplay]);
    }

    [Fact]
    public async Task ScoreFull_MustHaveCoverage_NoMustHavesCovered_IsNoMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        // CV has a skill, but not the must_have → NoMatch.
        var profile = FullProfile(KubernetesConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.MustHaveCoverage.Matched.ShouldBeEmpty();
        score.MustHaveCoverage.Missing.ShouldBe([CSharpDisplay]);
    }

    [Fact]
    public async Task ScoreFull_MustHaveCoverage_AdHasNoMustHaveTerms_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad has a nice_to_have but no must_have → MustHaveCoverage NotAssessed
        // (no must_have to cover — never NoMatch; honest "not assessed v1").
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.NiceToHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.MustHaveCoverage.Matched.ShouldBeEmpty();
        score.MustHaveCoverage.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // NiceToHaveCoverage — Match / Partial / NotAssessed (bonus bucket)
    // =================================================================

    [Fact]
    public async Task ScoreFull_NiceToHaveCoverage_AllNiceToHavesCovered_IsMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.NiceToHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(DockerConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        score.NiceToHaveCoverage.Matched.ShouldBe([DockerDisplay]);
        score.NiceToHaveCoverage.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_NiceToHaveCoverage_SomeNiceToHavesCovered_IsPartial()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.NiceToHave),
            RequirementTerm(KubernetesConceptId, KubernetesDisplay, ExtractedTermSource.NiceToHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(DockerConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Partial);
        score.NiceToHaveCoverage.Matched.ShouldBe([DockerDisplay]);
        score.NiceToHaveCoverage.Missing.ShouldBe([KubernetesDisplay]);
    }

    [Fact]
    public async Task ScoreFull_NiceToHaveCoverage_AdHasNoNiceToHaveTerms_IsNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad has a must_have but no nice_to_have → NiceToHaveCoverage NotAssessed
        // (the bonus bucket is empty — absence never penalises; never NoMatch).
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.NiceToHaveCoverage.Matched.ShouldBeEmpty();
        score.NiceToHaveCoverage.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // NULL extracted_terms (ad never extracted) → all 3 new dims NotAssessed
    // =================================================================

    [Fact]
    public async Task ScoreFull_NullExtractedTerms_AllThreeNewDimensions_AreNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // terms null → extracted_terms NULL (never-extracted; ~76% of corpus
        // pre-reingest). NotAssessed across the board, never NoMatch.
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms: null, ct);
        var profile = FullProfile(CSharpConceptId, DockerConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.SkillOverlap.Matched.ShouldBeEmpty();
        score.SkillOverlap.Missing.ShouldBeEmpty();
        score.MustHaveCoverage.Matched.ShouldBeEmpty();
        score.MustHaveCoverage.Missing.ShouldBeEmpty();
        score.NiceToHaveCoverage.Matched.ShouldBeEmpty();
        score.NiceToHaveCoverage.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_EmptyExtractedTerms_AllThreeNewDimensions_AreNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Extracted-to-empty ('[]') is distinct from NULL but still has no Skill /
        // Requirement terms → all three NotAssessed.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, null, null, ExtractedTerms.Empty, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
    }

    // =================================================================
    // Embedded Fast — ScoreFullAsync(...).Fast equals ScoreAsync(...) for the
    // same ad + Fast profile (regression: the four Fast dims keep working)
    // =================================================================

    [Fact]
    public async Task ScoreFull_EmbeddedFast_EqualsScoreAsync_ForSameAdAndFastProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var adGroup = $"grp-{Guid.NewGuid():N}"[..12];
        var adRegion = $"reg-{Guid.NewGuid():N}"[..12];
        var adEmployment = $"emp-{Guid.NewGuid():N}"[..12];
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);
        var jobAdId = await SeedJobAdAsync(
            "Utvecklare arkitekt", adGroup, adRegion, adEmployment, terms, ct);

        var fast = new CandidateMatchProfile(
            Title: "Arkitekt utvecklare",
            SsykGroupConceptIds: [adGroup],
            PreferredRegionConceptIds: [adRegion],
            PreferredEmploymentTypeConceptIds: [adEmployment]);
        var full = new FullCandidateMatchProfile(fast, [CSharpConceptId]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var fastScore = await scorer.ScoreAsync(jobAdId, fast, ct);
        var fullScore = await scorer.ScoreFullAsync(jobAdId, full, ct);

        // The four Fast dims must be identical (verdicts + ordered evidence lists).
        AssertSameDimension(fastScore.SsykOverlap, fullScore.Fast.SsykOverlap);
        AssertSameDimension(fastScore.TitleSimilarity, fullScore.Fast.TitleSimilarity);
        AssertSameDimension(fastScore.RegionFit, fullScore.Fast.RegionFit);
        AssertSameDimension(fastScore.EmploymentFit, fullScore.Fast.EmploymentFit);

        // And the Fast dims genuinely scored (Match), proving the embedding is live.
        fullScore.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        fullScore.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        fullScore.Fast.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    // =================================================================
    // Ordinal-stable ordering of matched/missing (determinism)
    // =================================================================

    [Fact]
    public async Task ScoreFull_SkillOverlap_MatchedAndMissing_AreOrdinalSorted()
    {
        var ct = TestContext.Current.CancellationToken;
        // Multiple ad skills so ordering is observable. Display labels chosen so
        // their Ordinal order is non-trivial ("C#" < "Docker" < "Kubernetes").
        var terms = ExtractedTerms.From(
        [
            SkillTerm(KubernetesConceptId, KubernetesDisplay),
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        // CV covers C# only → Matched=[C#], Missing=[Docker, Kubernetes] sorted.
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Matched.ShouldBe(
            score.SkillOverlap.Matched.OrderBy(d => d, StringComparer.Ordinal).ToList(),
            "Matched Display-labels ska vara Ordinal-sorterade (determinism).");
        score.SkillOverlap.Missing.ShouldBe(
            score.SkillOverlap.Missing.OrderBy(d => d, StringComparer.Ordinal).ToList(),
            "Missing Display-labels ska vara Ordinal-sorterade (determinism).");
        // Concrete expected order (we control the seed).
        score.SkillOverlap.Missing.ShouldBe([DockerDisplay, KubernetesDisplay]);
    }

    // =================================================================
    // Determinism hardening (code-reviewer Minor 2026-06-15) — the SAME skill
    // concept-id present as BOTH a Title- and a Description-source Skill survives
    // the VO's (Lexeme, Kind, Source) dedup as two terms; the overlap must surface
    // its Display exactly ONCE and deterministically. ExtractedTerms.From sorts
    // Source ascending (Title=0 before Description=1), so the Title Display wins via
    // the in-memory dictionary TryAdd (MatchScorer.ScoreConceptCoverage).
    // =================================================================

    [Fact]
    public async Task ScoreFull_SkillOverlap_SameConceptIdTwoSources_SurfacesTitleDisplayOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        const string titleDisplay = "C# (titel)";
        const string descriptionDisplay = "C# (beskrivning)";
        // Same concept-id, two sources → both survive dedup (distinct (Lexeme,Kind,Source)).
        var terms = ExtractedTerms.From(
        [
            new ExtractedTerm(
                Lexeme: CSharpConceptId, Display: descriptionDisplay,
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Description,
                MatchedOn: descriptionDisplay, ConceptId: CSharpConceptId, Weight: 1),
            new ExtractedTerm(
                Lexeme: CSharpConceptId, Display: titleDisplay,
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Title,
                MatchedOn: titleDisplay, ConceptId: CSharpConceptId, Weight: 1),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        // ONE entry per concept-id (not two); Title-source Display wins deterministically.
        score.SkillOverlap.Matched.ShouldBe([titleDisplay]);
        score.SkillOverlap.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // NotFoundException on missing ad (parity ScoreAsync)
    // =================================================================

    [Fact]
    public async Task ScoreFull_UnknownJobAdId_ThrowsNotFoundException()
    {
        var ct = TestContext.Current.CancellationToken;
        var unknownId = JobAdId.New();
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        await Should.ThrowAsync<NotFoundException>(
            async () => await scorer.ScoreFullAsync(unknownId, profile, ct));
    }

    // =================================================================
    // Determinism — same inputs twice → equal FullMatchScore
    // =================================================================

    [Fact]
    public async Task ScoreFull_SameInputsTwice_ProduceEqualScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
            RequirementTerm(KubernetesConceptId, KubernetesDisplay, ExtractedTermSource.MustHave),
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.NiceToHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId, DockerConceptId);

        var (scope1, scorer1) = NewScorer();
        using (scope1)
        {
            var first = await scorer1.ScoreFullAsync(jobAdId, profile, ct);
            var (scope2, scorer2) = NewScorer();
            using (scope2)
            {
                var second = await scorer2.ScoreFullAsync(jobAdId, profile, ct);

                AssertSameDimension(first.SkillOverlap, second.SkillOverlap);
                AssertSameDimension(first.MustHaveCoverage, second.MustHaveCoverage);
                AssertSameDimension(first.NiceToHaveCoverage, second.NiceToHaveCoverage);
                AssertSameDimension(first.Fast.SsykOverlap, second.Fast.SsykOverlap);
                AssertSameDimension(first.Fast.TitleSimilarity, second.Fast.TitleSimilarity);
                AssertSameDimension(first.Fast.RegionFit, second.Fast.RegionFit);
                AssertSameDimension(first.Fast.EmploymentFit, second.Fast.EmploymentFit);
            }
        }
    }

    // =================================================================
    // GIN surface proof — the seeded ad's extracted_lexemes column is populated
    // (parity JobAdExtractedTermsPersistenceTests' jsonb_exists_any pattern).
    // Single-ad scoring uses in-memory overlap (CTO Decision C1); this proves the
    // STORED GIN column still populates so the deferred multi-ad search has its
    // index ready (no-silent-cap — the GIN is real, just unused for single-ad).
    // =================================================================

    [Fact]
    public async Task ScoreFull_SeededAd_ExtractedLexemesGinColumn_ContainsTheSkillConceptIds()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);

        var connectionString = GetConnectionString();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // The lexemes are the concept-ids (Lexeme == ConceptId for Skill/Requirement).
        (await OverlapMatchExistsAsync(conn, jobAdId.Value, [CSharpConceptId, DockerConceptId], ct))
            .ShouldBeTrue(
                "extracted_lexemes ?| array[seed-concept-ids] ska returnera annonsen " +
                "(STORED GIN-kolumnen är populerad även om single-ad använder " +
                "in-memory overlap, CTO Decision C1).");
        (await OverlapMatchExistsAsync(conn, jobAdId.Value, ["no-match-lexeme-xyz"], ct))
            .ShouldBeFalse("icke-överlappande lexem-set ska inte returnera annonsen.");
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------

    private static void AssertSameDimension(MatchDimension a, MatchDimension b)
    {
        b.Verdict.ShouldBe(a.Verdict);
        b.Matched.ShouldBe(a.Matched);   // sequence-equal (order included)
        b.Missing.ShouldBe(a.Missing);
    }

    // Reads the live connection string from the DbContext the factory configured
    // (so the raw Npgsql probe hits the same Testcontainers Postgres).
    private string GetConnectionString()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("AppDbContext har ingen connection string.");
    }

    // Raw overlap probe — the FUNCTION form jsonb_exists_any(target, text[]) is
    // EXACTLY equivalent to the `?|` operator the GIN index serves, avoiding
    // Npgsql's `?`→positional-parameter escaping (parity
    // JobAdExtractedTermsPersistenceTests.OverlapMatchExistsAsync). Parameterized.
    private static async Task<bool> OverlapMatchExistsAsync(
        NpgsqlConnection conn, Guid id, string[] lexemes, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT EXISTS (SELECT 1 FROM job_ads " +
            "WHERE id = @id AND jsonb_exists_any(extracted_lexemes, @lexemes));";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("lexemes", lexemes);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }
}
