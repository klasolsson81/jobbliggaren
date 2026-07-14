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
using Jobbliggaren.TestSupport;
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

    // PR-4 (#300, ADR 0084) — the ssyk-4 occupation-group concept-ids for the Related-cap cases.
    // ExactGroup is the user's stated occupation; RelatedGroup is a substitutable neighbour in the
    // profile's related set ONLY; BothGroup sits in BOTH sets (exact precedence); DisjointGroup is
    // in NEITHER (the gate-fail case). We control the seed, so the (exact ∪ related) membership the
    // SsykIsRelated bit reads is exactly what these constants encode.
    private const string ExactGroup = "grp-exact-0001";
    private const string RelatedGroup = "grp-related-0002";
    private const string BothGroup = "grp-both-0003";
    private const string DisjointGroup = "grp-disjoint-0004";

    // Stated ort/employment preference values for the cap-before-RB1 cases. Hoisted to
    // static readonly so the per-test args do not allocate a fresh inline array each call
    // (CA1861). The ad's "wrong city" region (AdWrongRegion) is deliberately NOT in the
    // preferred set, forcing a region NoMatch the Related-cap must override BEFORE RB1.
    private static readonly string[] PrefRegions = ["reg-pref-0001"];
    private static readonly string[] PrefEmployments = ["emp-pref-0001"];
    private const string AdWrongRegion = "reg-wrong-9999";

    // ---------------------------------------------------------------
    // SUT factory — the real Infrastructure scorer (fresh scoped AppDbContext +
    // the real Swedish analyzer), parity MatchScorerIntegrationTests.NewScorer.
    // ---------------------------------------------------------------
    private (IServiceScope Scope, MatchScorer Scorer) NewScorer()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
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

    // Seeds an Imported JobAd whose raw_payload drives the facet columns
    // (occupation_group / region / employment) AND, when terms is non-null, sets
    // the extracted_terms VO (which generates the STORED extracted_lexemes GIN
    // column). null terms → extracted_terms stays NULL (never-extracted path).
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
        {
            jobAd.SetExtractedTerms(terms);
        }

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // occupation_group + employment_type are TOP-LEVEL; region lives under
    // workplace_address (parity MatchScorerIntegrationTests / JobAdFacetsSurvivePurgeTests).
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

    // A FullCandidateMatchProfile with a minimal embedded Fast profile (only the
    // CV-side skill ids vary for the new dims). Title/ssyk/region/employment empty
    // so the Fast dims read NotAssessed and do not interfere with new-dim asserts.
    private static FullCandidateMatchProfile FullProfile(params string[] cvSkillConceptIds) =>
        new(
            Fast: new CandidateMatchProfile(
                Title: "Titel",
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: cvSkillConceptIds);

    // PR-4 (#300, ADR 0084) — a profile that STATES an exact ssyk-4 set { ExactGroup, BothGroup }
    // and a related ssyk-4 set { RelatedGroup, BothGroup } (BothGroup is in both → exact wins).
    // RelatedSsykGroupConceptIds is an additive init-property on the embedded Fast profile (not a
    // positional arg). preferredRegions/preferredEmployments default empty so the cap-before-RB1
    // cases can opt into a stated-but-contradicted secondary. The related set is EMPTY for every
    // pre-PR-4 test (behaviour-inert), so this helper is only used by the B2 Related cases.
    private static FullCandidateMatchProfile RelatedFullProfile(
        IReadOnlyList<string>? preferredRegions = null,
        IReadOnlyList<string>? preferredEmployments = null,
        IReadOnlyList<string>? preferredMunicipalities = null,
        params string[] cvSkillConceptIds) =>
        new(
            Fast: new CandidateMatchProfile(
                Title: "Titel",
                SsykGroupConceptIds: [ExactGroup, BothGroup],
                PreferredRegionConceptIds: preferredRegions ?? [],
                PreferredEmploymentTypeConceptIds: preferredEmployments ?? [],
                PreferredMunicipalityConceptIds: preferredMunicipalities ?? [])
            {
                RelatedSsykGroupConceptIds = [RelatedGroup, BothGroup],
            },
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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Partial);
        score.SkillOverlap.Matched.ShouldBe([CSharpDisplay]);
        // Missing = what the ad wants that the CV lacks (Display).
        score.SkillOverlap.Missing.ShouldBe([DockerDisplay]);
    }

    // #477 Low 2 — the CARRIER surfaces the covered-skill CONCEPT-IDS (the persisted explainability
    // evidence), distinct from the SkillOverlap dimension's Display labels. Only the Skill partition,
    // only ids the CV covers.
    [Fact]
    public async Task ScoreFull_MatchedSkillConceptIds_AreCoveredSkillIds_NotDisplay_NotRequirements()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            SkillTerm(CSharpConceptId, CSharpDisplay),   // a Skill the CV covers
            SkillTerm(DockerConceptId, DockerDisplay),   // a Skill the CV does NOT cover
            // A Requirement term the CV "covers" — but it is NOT a Skill, so it must NOT appear
            // in the skill evidence (the evidence is the SkillOverlap partition only).
            RequirementTerm(KubernetesConceptId, "Kubernetes", ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId, KubernetesConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var scored = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        // Evidence = the covered SKILL concept-ids only: C#. Docker is not covered; Kubernetes is a
        // Requirement term (not a Skill) so it is not skill evidence.
        scored.MatchedSkillConceptIds.ShouldBe([CSharpConceptId]);
        // IDS, not Display labels (the DE-display-1 dimension carries labels; the evidence carries ids).
        scored.MatchedSkillConceptIds.ShouldNotContain(CSharpDisplay);
        scored.MatchedSkillConceptIds.ShouldNotContain(DockerConceptId);
        scored.MatchedSkillConceptIds.ShouldNotContain(KubernetesConceptId);
    }

    [Fact]
    public async Task ScoreFull_MatchedSkillConceptIds_EmptyWhenCvHasNoConfirmedSkills()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(); // no confirmed skills → nothing to cite

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var scored = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        scored.MatchedSkillConceptIds.ShouldBeEmpty();
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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.NotAssessed);
        score.SkillOverlap.Matched.ShouldBeEmpty();
        score.SkillOverlap.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_SkillOverlap_AdHasNoSkillTerms_CvPresent_IsVacuous()
    {
        var ct = TestContext.Current.CancellationToken;
        // PR-B1 (RE-BIND G1-b): ad has ONLY a must_have requirement term, NO Skill terms,
        // but the CV HAS skills → the ad SKILL partition is empty while the CV is present
        // → Vacuous ("we looked; the ad specifies none of this kind"), NOT NotAssessed.
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        score.MustHaveCoverage.Matched.ShouldBeEmpty();
        score.MustHaveCoverage.Missing.ShouldBe([CSharpDisplay]);
    }

    [Fact]
    public async Task ScoreFull_MustHaveCoverage_AdHasNoMustHaveTerms_CvPresent_IsVacuous()
    {
        var ct = TestContext.Current.CancellationToken;
        // PR-B1 (RE-BIND G1-b): ad has a nice_to_have but NO must_have, and the CV HAS
        // skills → the ad must_have partition is empty while the CV is present → Vacuous
        // (the gate-open "ad states no skall-krav" case, never NoMatch). This is the
        // load-bearing distinction that lets a bare ad still reach Strong/Top (Reading 1).
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(DockerConceptId, DockerDisplay, ExtractedTermSource.NiceToHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Partial);
        score.NiceToHaveCoverage.Matched.ShouldBe([DockerDisplay]);
        score.NiceToHaveCoverage.Missing.ShouldBe([KubernetesDisplay]);
    }

    [Fact]
    public async Task ScoreFull_NiceToHaveCoverage_AdHasNoNiceToHaveTerms_CvPresent_IsVacuous()
    {
        var ct = TestContext.Current.CancellationToken;
        // PR-B1 (RE-BIND G1-b): ad has a must_have but NO nice_to_have, and the CV HAS
        // skills → the ad nice_to_have partition is empty while the CV is present →
        // Vacuous (the bonus bucket is empty BUT we looked — never NoMatch). NiceToHave
        // Vacuous is NOT in the Top tie-break set {Match,Partial}.
        var terms = ExtractedTerms.From(
        [
            RequirementTerm(CSharpConceptId, CSharpDisplay, ExtractedTermSource.MustHave),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        score.NiceToHaveCoverage.Matched.ShouldBeEmpty();
        score.NiceToHaveCoverage.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // NULL / empty extracted_terms (ad never extracted) WITH a CV present → all 3 new
    // dims VACUOUS (PR-B1 RE-BIND G1-b: ad partition empty, CV present → "we looked,
    // the ad specifies none"). The no-CV case stays NotAssessed (covered separately by
    // ScoreFull_SkillOverlap_EmptyCvSkills_IsNotAssessed). Never NoMatch.
    // =================================================================

    [Fact]
    public async Task ScoreFull_NullExtractedTerms_CvPresent_AllThreeNewDimensions_AreVacuous()
    {
        var ct = TestContext.Current.CancellationToken;
        // terms null → extracted_terms NULL (never-extracted; ~76% of corpus
        // pre-reingest). The CV HAS skills → each ad partition is empty while the CV is
        // present → Vacuous across the board, never NoMatch, never NotAssessed.
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms: null, ct);
        var profile = FullProfile(CSharpConceptId, DockerConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        score.SkillOverlap.Matched.ShouldBeEmpty();
        score.SkillOverlap.Missing.ShouldBeEmpty();
        score.MustHaveCoverage.Matched.ShouldBeEmpty();
        score.MustHaveCoverage.Missing.ShouldBeEmpty();
        score.NiceToHaveCoverage.Matched.ShouldBeEmpty();
        score.NiceToHaveCoverage.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ScoreFull_EmptyExtractedTerms_CvPresent_AllThreeNewDimensions_AreVacuous()
    {
        var ct = TestContext.Current.CancellationToken;
        // Extracted-to-empty ('[]') is distinct from NULL but still has no Skill /
        // Requirement terms — and the CV HAS skills → all three Vacuous (ad partition
        // empty, CV present).
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, null, null, ExtractedTerms.Empty, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        score.MustHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
        score.NiceToHaveCoverage.Verdict.ShouldBe(MatchDimensionVerdict.Vacuous);
    }

    [Fact]
    public async Task ScoreFull_NullExtractedTerms_NoCv_AllThreeNewDimensions_AreNotAssessed()
    {
        var ct = TestContext.Current.CancellationToken;
        // PR-B1 boundary guard: ad partition empty AND the CV is empty → NotAssessed
        // (we could not assess), NOT Vacuous. This pins that Vacuous is the ad-empty-but-
        // CV-PRESENT case only; the no-CV case stays the honest "not assessed v1".
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms: null, ct);
        var profile = FullProfile(); // empty CV-side skills

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
            PreferredEmploymentTypeConceptIds: [adEmployment],
            PreferredMunicipalityConceptIds: []);
        var full = new FullCandidateMatchProfile(fast, [CSharpConceptId]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var fastScore = await scorer.ScoreAsync(jobAdId, fast, ct);
        var fullScore = (await scorer.ScoreFullAsync(jobAdId, full, ct)).Score;

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
    // Spår 3 PR-B (ADR 0076-amendment 2026-06-21) — the embedded Fast RegionFit in
    // ScoreFullAsync is the ort-union (region ∪ municipality), identical to ScoreAsync's
    // for the same ad + Fast profile. Pinned for a union MUNICIPALITY hit (the new
    // granularity) and a union NoMatch — proving the Full path embeds the SAME union,
    // not the legacy region-only ScoreMembership.
    // =================================================================

    [Fact]
    public async Task ScoreFull_EmbeddedFast_RegionFit_IsOrtUnion_MunicipalityHit_EqualsScoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefMunicipality = $"mun-{Guid.NewGuid():N}"[..12];
        // adReg not preferred, adMun == prefMunicipality → union Match via municipality.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, $"reg-{Guid.NewGuid():N}"[..12], null, terms: null, ct,
            municipalityConceptId: prefMunicipality);

        var fast = new CandidateMatchProfile(
            Title: "Titel",
            SsykGroupConceptIds: [],
            PreferredRegionConceptIds: [],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [prefMunicipality]);
        var full = new FullCandidateMatchProfile(fast, []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var fastScore = await scorer.ScoreAsync(jobAdId, fast, ct);
        var fullScore = (await scorer.ScoreFullAsync(jobAdId, full, ct)).Score;

        // The embedded Fast RegionFit equals the standalone Fast ScoreAsync RegionFit.
        AssertSameDimension(fastScore.RegionFit, fullScore.Fast.RegionFit);
        // And it genuinely scored the union Match via the municipality (not region-only).
        fullScore.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        fullScore.Fast.RegionFit.Matched.ShouldBe([prefMunicipality]);
    }

    [Fact]
    public async Task ScoreFull_EmbeddedFast_RegionFit_IsOrtUnion_NoMatch_EqualsScoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var adRegion = $"reg-{Guid.NewGuid():N}"[..12];
        var adMunicipality = $"mun-{Guid.NewGuid():N}"[..12];
        // ort stated (prefs below), ad has BOTH ort values, neither preferred → union NoMatch.
        var jobAdId = await SeedJobAdAsync(
            "Titel", null, adRegion, null, terms: null, ct,
            municipalityConceptId: adMunicipality);

        var fast = new CandidateMatchProfile(
            Title: "Titel",
            SsykGroupConceptIds: [],
            PreferredRegionConceptIds: [$"reg-{Guid.NewGuid():N}"[..12]],
            PreferredEmploymentTypeConceptIds: [],
            PreferredMunicipalityConceptIds: [$"mun-{Guid.NewGuid():N}"[..12]]);
        var full = new FullCandidateMatchProfile(fast, []);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        var fastScore = await scorer.ScoreAsync(jobAdId, fast, ct);
        var fullScore = (await scorer.ScoreFullAsync(jobAdId, full, ct)).Score;

        AssertSameDimension(fastScore.RegionFit, fullScore.Fast.RegionFit);
        fullScore.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        // Missing = the ad's present ort values [adRegion, adMunicipality] Ordinal-sorted.
        fullScore.Fast.RegionFit.Missing.ShouldBe(
            new[] { adRegion, adMunicipality }.OrderBy(v => v, StringComparer.Ordinal).ToList());
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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

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
        var score = (await scorer.ScoreFullAsync(jobAdId, profile, ct)).Score;

        score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        // ONE entry per concept-id (not two); Title-source Display wins deterministically.
        score.SkillOverlap.Matched.ShouldBe([titleDisplay]);
        score.SkillOverlap.Missing.ShouldBeEmpty();
    }

    // =================================================================
    // #477 Low 2 — MatchedSkillConceptIds evidence determinism
    // =================================================================

    // The evidence is Ordinal-ORDERED — pins CoveredSkillConceptIds' `.OrderBy(Ordinal)`. The
    // single-id / ignoreOrder assertions elsewhere cannot catch a removed OrderBy; this seeds the
    // Skill terms in NON-Ordinal order and asserts order-SENSITIVELY.
    [Fact]
    public async Task ScoreFull_MatchedSkillConceptIds_AreOrdinalOrdered()
    {
        var ct = TestContext.Current.CancellationToken;
        // Term/seed order (K8s, C#, Docker) differs from the Ordinal order of the concept-ids.
        var terms = ExtractedTerms.From(
        [
            SkillTerm(KubernetesConceptId, KubernetesDisplay),
            SkillTerm(CSharpConceptId, CSharpDisplay),
            SkillTerm(DockerConceptId, DockerDisplay),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId, DockerConceptId, KubernetesConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var scored = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        // Ordinal: skill-csharp-0001 < skill-docker-0002 < skill-k8s-0003. ORDER-SENSITIVE (no ignoreOrder).
        scored.MatchedSkillConceptIds.ShouldBe(
            [CSharpConceptId, DockerConceptId, KubernetesConceptId]);
    }

    // The evidence is DEDUPED — pins CoveredSkillConceptIds' `.Distinct`. The same concept-id
    // borne as BOTH a Title- and Description-source Skill term survives the VO's (Lexeme,Kind,Source)
    // dedup (two terms), but appears ONCE in the evidence.
    [Fact]
    public async Task ScoreFull_MatchedSkillConceptIds_DedupesSameConceptIdAcrossSources()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From(
        [
            new ExtractedTerm(
                Lexeme: CSharpConceptId, Display: "C# (beskrivning)",
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Description,
                MatchedOn: "C# (beskrivning)", ConceptId: CSharpConceptId, Weight: 1),
            new ExtractedTerm(
                Lexeme: CSharpConceptId, Display: "C# (titel)",
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Title,
                MatchedOn: "C# (titel)", ConceptId: CSharpConceptId, Weight: 1),
        ]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(CSharpConceptId);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var scored = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        scored.MatchedSkillConceptIds.ShouldBe([CSharpConceptId]); // one entry, not two
    }

    // Empty evidence via the intersection FILTER (CV present but covers none of the ad's Skill
    // terms) — distinct from the empty-CV early return already covered above.
    [Fact]
    public async Task ScoreFull_MatchedSkillConceptIds_EmptyWhenCvCoversNoAdSkill()
    {
        var ct = TestContext.Current.CancellationToken;
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);
        var jobAdId = await SeedJobAdAsync("Titel", null, null, null, terms, ct);
        var profile = FullProfile(DockerConceptId); // covers Docker, not the ad's C#

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var scored = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        scored.MatchedSkillConceptIds.ShouldBeEmpty();
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
            var first = (await scorer1.ScoreFullAsync(jobAdId, profile, ct)).Score;
            var (scope2, scorer2) = NewScorer();
            using (scope2)
            {
                var second = (await scorer2.ScoreFullAsync(jobAdId, profile, ct)).Score;

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

    // =================================================================
    // PR-4 (#300, ADR 0084) — SsykIsRelated on the FullScoredMatch carrier. TRUE iff the ad's
    // occupation-group concept-id ∈ profile.Fast.RelatedSsykGroupConceptIds AND ∉
    // profile.Fast.SsykGroupConceptIds (exact precedence), and only meaningful when the SSYK gate
    // is a Match; FALSE otherwise. The related set is non-empty here (RelatedFullProfile); it is
    // EMPTY in every other test in this file, so SsykIsRelated is bit-for-bit false there (the
    // behaviour-inert v1 invariant, pinned by the last case below).
    // =================================================================

    [Fact]
    public async Task ScoreFull_SsykIsRelated_IsFalse_WhenAdGroupInExactSet()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad's occupation group ∈ the EXACT stated set → an exact hit → SsykIsRelated false.
        var jobAdId = await SeedJobAdAsync("Titel", ExactGroup, null, null, terms: null, ct);
        var profile = RelatedFullProfile();

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        result.SsykIsRelated.ShouldBeFalse(
            "ad-gruppen ligger i det EXAKTA setet → exakt-träff, inte related.");
        // The exact hit makes the SSYK gate a Match (so the bit is meaningful and false, not vacuous).
        result.Score.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    [Fact]
    public async Task ScoreFull_SsykIsRelated_IsTrue_WhenAdGroupInRelatedSetOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad's occupation group ∈ the RELATED set, ∉ the exact set → a related-only hit.
        var jobAdId = await SeedJobAdAsync("Titel", RelatedGroup, null, null, terms: null, ct);
        var profile = RelatedFullProfile();

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        result.SsykIsRelated.ShouldBeTrue(
            "ad-gruppen ligger ENBART i related-setet (∉ exakt) → related-träff.");
        // The broadened gate (exact ∪ related) still reads SSYK Match for a related-only hit.
        result.Score.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    [Fact]
    public async Task ScoreFull_RelatedOnlyHit_GradesRelated_FlatCap_EvenWithBothSecondariesMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        // Related-only occupation hit AND both secondaries Match (region + employment confirmed) —
        // an exact hit here would be Strong. The flat Related-cap must override to Related.
        var jobAdId = await SeedJobAdAsync(
            "Titel", RelatedGroup, PrefRegions[0], PrefEmployments[0], terms: null, ct);
        var profile = RelatedFullProfile(
            preferredRegions: PrefRegions, preferredEmployments: PrefEmployments);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        result.SsykIsRelated.ShouldBeTrue();
        // Both secondaries genuinely Match (so this is NOT vacuously Related on a thin tuple).
        result.Score.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        result.Score.Fast.EmploymentFit.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        // Flat cap: a related occupation is always exactly Related, never Good/Strong/Top.
        MatchGradeCalculator.Grade(result.Score, result.SsykIsRelated).ShouldBe(MatchGrade.Related,
            "en related-only-träff cap:as platt till Related även med båda sekundärerna Match.");
    }

    [Fact]
    public async Task ScoreFull_RelatedOnlyHit_GradesRelated_NotBasic_WhenRegionContradicts_CapBeforeRB1()
    {
        var ct = TestContext.Current.CancellationToken;
        // Related-only occupation hit AND a stated-region the ad CONTRADICTS (wrong city → region
        // NoMatch). For an EXACT hit RB1 would floor to Basic; the Related-cap is evaluated BEFORE
        // RB1, so a related occupation in the wrong city reads Related, NOT Basic.
        var jobAdId = await SeedJobAdAsync(
            "Titel", RelatedGroup, AdWrongRegion, PrefEmployments[0], terms: null, ct);
        var profile = RelatedFullProfile(
            preferredRegions: PrefRegions, preferredEmployments: PrefEmployments);

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        result.SsykIsRelated.ShouldBeTrue();
        // The region genuinely contradicts (the RB1 trigger an exact hit would floor on).
        result.Score.Fast.RegionFit.Verdict.ShouldBe(MatchDimensionVerdict.NoMatch);
        MatchGradeCalculator.Grade(result.Score, result.SsykIsRelated).ShouldBe(MatchGrade.Related,
            "Related-cap före RB1: en related occupation i fel stad ska läsa Related, INTE Basic " +
            "(annars presenteras ett substituerbart yrke som ett exakt-yrkes-utfall).");
    }

    [Fact]
    public async Task ScoreFull_SsykIsRelated_IsFalse_WhenAdGroupInBothExactAndRelated_ExactPrecedence()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad's occupation group ∈ BOTH the exact and related sets → exact precedence → false.
        var jobAdId = await SeedJobAdAsync("Titel", BothGroup, null, null, terms: null, ct);
        var profile = RelatedFullProfile();

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        result.SsykIsRelated.ShouldBeFalse(
            "ad-gruppen ligger i BÅDE exakt och related → exakt vinner (exact-precedence).");
        result.Score.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    [Fact]
    public async Task ScoreFull_SsykIsRelated_IsFalse_AndGradeNull_WhenAdGroupInNeitherSet_GateFail()
    {
        var ct = TestContext.Current.CancellationToken;
        // Ad's occupation group ∈ NEITHER set → the broadened gate (exact ∪ related) still fails →
        // SSYK not a Match → no tag (grade null) and SsykIsRelated false (the bit is not meaningful
        // when the gate is closed).
        var jobAdId = await SeedJobAdAsync("Titel", DisjointGroup, null, null, terms: null, ct);
        var profile = RelatedFullProfile();

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, profile, ct);

        result.SsykIsRelated.ShouldBeFalse("gate stängd → related-biten är inte meningsfull → false.");
        result.Score.Fast.SsykOverlap.Verdict.ShouldNotBe(MatchDimensionVerdict.Match);
        MatchGradeCalculator.Grade(result.Score, result.SsykIsRelated).ShouldBeNull(
            "ad-gruppen ligger i varken exakt eller related → gate-fail → ingen tagg (null).");
    }

    [Fact]
    public async Task ScoreFull_SsykIsRelated_IsFalse_WhenRelatedSetEmpty_BehaviourInert()
    {
        var ct = TestContext.Current.CancellationToken;
        // The behaviour-inert v1 invariant: with an EMPTY related set, a normal exact-occupation ad
        // reads SsykIsRelated false — bit-for-bit today's behaviour. This is the only profile shape
        // every OTHER test in this file uses (related set empty), so this pins they are all false.
        var jobAdId = await SeedJobAdAsync("Titel", ExactGroup, null, null, terms: null, ct);
        var exactOnly = new FullCandidateMatchProfile(
            Fast: new CandidateMatchProfile(
                Title: "Titel",
                SsykGroupConceptIds: [ExactGroup],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []); // RelatedSsykGroupConceptIds defaults to []

        var (scope, scorer) = NewScorer();
        using var _ = scope;
        var result = await scorer.ScoreFullAsync(jobAdId, exactOnly, ct);

        result.SsykIsRelated.ShouldBeFalse(
            "tomt related-set → SsykIsRelated alltid false (PR-4 är beteende-inert i v1).");
        result.Score.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
    }

    // =================================================================
    // #864 (CTO D2/D3, the SINGLE half of the S-split) — ScoreFullAsync does NOT gate on status.
    //
    // This is the scorer-level half of the guard whose wire-level half lives in
    // JobAdMatchDetailEndpointTests (archived → 200 + a grade). Both exist because the "obvious"
    // completion of #864 — add the batch predicate to all four methods — fails SILENTLY at the
    // surface: ScoreFullAsync would throw NotFoundException, the endpoint would 404, and
    // job-ad-match.ts:85-92 swallows a 404 (`if (!res.ok) return null`), so the match section
    // would simply vanish from the modal with nothing looking broken. Pinning it here as well
    // states the contract where the contract lives, not only where the symptom would appear.
    //
    // The grade is TRUE either way: archiving changes none of the inputs the scorer reads. On a
    // detail page the user navigated to deliberately, it is an EXPLANATION ("here is why this was
    // a fit") — which is exactly what #805-3 preserved archived ads for.
    // =================================================================
    [Fact]
    public async Task ScoreFullAsync_ScoresAnArchivedAd_TheSingleFamilyDoesNotGate()
    {
        var ct = TestContext.Current.CancellationToken;
        // Run-unique ids (the Api collection shares one database — a fixed id could collide with a
        // sibling test's seed and make this spec read another test's ads).
        var grp = $"grp-arch-{Guid.NewGuid():N}"[..16];
        var reg = $"reg-arch-{Guid.NewGuid():N}"[..16];
        var terms = ExtractedTerms.From([SkillTerm(CSharpConceptId, CSharpDisplay)]);

        // Identical facets + identical terms; the ONLY difference is the lifecycle status.
        var live = await SeedJobAdAsync("Systemutvecklare", grp, reg, null, terms, ct);
        var archived = await SeedJobAdAsync("Systemutvecklare", grp, reg, null, terms, ct);
        await ArchiveAsync(archived, ct);

        var profile = new FullCandidateMatchProfile(
            new CandidateMatchProfile("Systemutvecklare", [grp], [reg], [], []),
            CvSkillConceptIds: [CSharpConceptId]);

        var (scope, scorer) = NewScorer();
        using var _ = scope;

        // NON-VACUITY: the live twin genuinely scores a Match on both a Fast and a Full dimension.
        var liveScore = await scorer.ScoreFullAsync(live, profile, ct);
        liveScore.Score.Fast.SsykOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);
        liveScore.Score.SkillOverlap.Verdict.ShouldBe(MatchDimensionVerdict.Match);

        // THE SPECIFICATION: the archived ad is fully scored, not thrown away. A gate here throws
        // NotFoundException — this spec is the detector for that mutation at the scorer level.
        var archivedScore = await scorer.ScoreFullAsync(archived, profile, ct);

        // Scored IDENTICALLY, dimension for dimension.
        archivedScore.Score.Fast.SsykOverlap.Verdict.ShouldBe(liveScore.Score.Fast.SsykOverlap.Verdict);
        archivedScore.Score.Fast.RegionFit.Verdict.ShouldBe(liveScore.Score.Fast.RegionFit.Verdict);
        archivedScore.Score.SkillOverlap.Verdict.ShouldBe(liveScore.Score.SkillOverlap.Verdict);
        archivedScore.Score.SkillOverlap.Matched.ShouldBe(liveScore.Score.SkillOverlap.Matched);
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

    // The REAL retraction transition (#821: Archive() is JobAd's only lifecycle method; #843 forbids
    // fabricating a state production cannot reach).
    //
    // THE READ-BACK IS LOAD-BEARING HERE, not belt-and-suspenders. The spec above is an INCLUSION
    // spec (it expects the archived ad to BE scored, identically to its live twin), and an inclusion
    // spec CANNOT detect its own broken seed: a silently-failed Archive() leaves the ad Active, an
    // Active ad scores identically to its live twin, and the test passes GREEN — having degraded
    // into "an active ad is scored", which forty other tests in this file already assert. The
    // detector would detect nothing, precisely on the path it was written to protect (a Status gate
    // added to ScoreFullAsync only throws for an ad that is genuinely NOT Active). The exclusion
    // specs elsewhere fail-safe; this one does not. So the seed's premise is asserted, not assumed.
    private async Task ArchiveAsync(JobAdId id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var ad = await db.JobAds.FindAsync([id], ct);
        ad.ShouldNotBeNull();
        ad!.Archive(clock).IsSuccess.ShouldBeTrue("Archive() ska lyckas — annars är specen vakuös.");
        await db.SaveChangesAsync(ct);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await verifyDb.JobAds.AsNoTracking().FirstAsync(j => j.Id == id, ct);
        stored.Status.ShouldBe(JobAdStatus.Archived,
            "Annonsen MÅSTE vara arkiverad i databasen — annars degraderar specen tyst till " +
            "\"en aktiv annons scoras\" och detekterar ingenting.");
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
