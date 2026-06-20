using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// F4-16 (ADR 0076 Amendment (b) §5, ADR 0053 Beslut 5 amendment) — the single-ad
/// match-detail endpoint <c>GET /api/v1/me/job-ad-match-tags/{jobAdId:guid}</c> for the job
/// modal, end-to-end on Testcontainers Postgres. This is the WIRE oracle: it proves the
/// auth-gated endpoint, the REAL <c>MatchProfileBuilder.BuildFullFromCvSkillsAsync</c>
/// (encrypted CV skills via the DEK pipeline, fail-closed) and the REAL
/// <c>MatchScorer.ScoreFullAsync</c> compose through the full Mediator pipeline, and that the
/// modal DTO's grade + per-dimension verdict + matched/missing STRINGS round-trip as
/// camelCase JSON with enums by NAME (<c>[JsonStringEnumConverter]</c>). The unit
/// handler/scorer/grade tests already cover the logic exhaustively (FullMatchScorer-,
/// MatchGradeCalculator-, GetJobAdMatchDetailQueryHandler-suites); these ~4 high-value tests
/// prove the wire + DEK + owner-scope. NO AI/LLM (ADR 0071/0076).
/// <para>
/// Seeding reuses the proven fixtures: the preference→shadow-column path
/// (<see cref="MatchTagBatchEndpointsTests"/>), the primary-CV-with-skills + DEK-warm path
/// (<see cref="MatchProfileBuilderFullCvIntegrationTests"/>), and the ad
/// <c>extracted_terms</c> Skill-term path (<see cref="FullMatchScorerIntegrationTests"/>).
/// </para>
/// <para>
/// <b>Provenance-safe skill overlap (F4-2/F4-3 lesson — derive, never guess):</b> to make
/// <c>SkillOverlap == Match</c>, the CV's skill LABEL and the ad's extracted Skill term must
/// resolve to the SAME concept-id. We resolve a real committed-taxonomy skill label LIVE via
/// the real <see cref="SkillResolver"/> (the SAME shared index the CV builder uses), then seed
/// BOTH the CV (with that label) AND the ad (with a Skill term carrying that exact resolved
/// concept-id) — so the overlap is guaranteed by construction, not by a magic token.
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdMatchDetailEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // ---------------------------------------------------------------
    // Auth + preference helpers (parity MatchTagBatchEndpointsTests).
    // ---------------------------------------------------------------

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task SetPreferencesAsync(
        string[] occupationGroups, string[] regions, string[] employmentTypes,
        CancellationToken ct)
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            new
            {
                preferredOccupationGroups = occupationGroups,
                preferredRegions = regions,
                preferredEmploymentTypes = employmentTypes,
            },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<HttpResponseMessage> GetDetailAsync(Guid jobAdId, CancellationToken ct) =>
        await _client.GetAsync($"/api/v1/me/job-ad-match-tags/{jobAdId}", ct);

    // Unique-but-regex-valid concept-id (^[A-Za-z0-9_-]{1,32}$). 16 chars.
    private static string NewConceptId(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..16];

    // ---------------------------------------------------------------
    // Ad seeding — raw_payload drives the STORED shadow columns
    // (occupation_group / region / employment); optional extracted_terms drives
    // the STORED extracted_lexemes GIN (parity FullMatchScorerIntegrationTests).
    // ---------------------------------------------------------------

    private async Task<Guid> SeedJobAdAsync(
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
            jobAd.SetExtractedTerms(terms);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
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

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    // ---------------------------------------------------------------
    // Primary-CV seeding for the AUTHENTICATED user — the endpoint reads the
    // current user's primary CV, so the CV must hang off the REGISTERED user's
    // JobSeeker (RegisterCommandHandler created it). We locate that seeker by the
    // unique occupation-group we stated, warm its DEK, then attach a primary
    // Resume whose Master content carries the given skill LABELS (encrypted).
    // ---------------------------------------------------------------

    private async Task SeedPrimaryCvForStatedSeekerAsync(
        string statedOccupationGroup, string[] skillLabels, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await FindSeekerByStatedOccupationAsync(db, statedOccupationGroup, ct);

        // Resume.Content is encrypted (ADR 0049) → warm the owner DEK FÖRE the encrypted
        // entity is added (direct seed bypasses the Mediator prefetch behavior).
        await EncryptionKeyTestSeed.WarmAsync(scope, seekerId, ct);

        var resume = Resume.Create(seekerId, "Mitt CV", "Test User", clock).Value;
        var content = new ResumeContent(
            new PersonalInfo("Test User", null, null, null),
            skills: skillLabels.Select(n => new Skill(n, null)).ToList());
        resume.UpdateMasterContent(content, clock);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        var seeker = await db.JobSeekers.FirstAsync(s => s.Id == seekerId, ct);
        seeker.SetPrimaryResume(resume.Id, clock);
        await db.SaveChangesAsync(ct);
    }

    // The registered user's JobSeeker, located by the unique occupation-group we just
    // stated. MatchPreferences is a jsonb VO list → strongly-typed Contains does not
    // translate (MEMORY ef_strongly_typed_vo_contains_translation); load the bounded set
    // and filter client-side.
    private static async Task<JobSeekerId> FindSeekerByStatedOccupationAsync(
        AppDbContext db, string statedOccupationGroup, CancellationToken ct)
    {
        var seekers = await db.JobSeekers.AsNoTracking().ToListAsync(ct);
        var seeker = seekers.SingleOrDefault(
            s => s.MatchPreferences.PreferredOccupationGroups.Contains(
                statedOccupationGroup, StringComparer.Ordinal));
        seeker.ShouldNotBeNull(
            "Den registrerade användarens JobSeeker (med den unika yrkesgruppen) ska hittas.");
        return seeker!.Id;
    }

    // ---------------------------------------------------------------
    // Provenance: resolve a real committed-taxonomy skill label LIVE to its
    // concept-id via the real shared-index resolver (the SAME index the CV builder
    // uses, ADR 0076 Decision 6). Pick a single-token label whose lexeme is unique
    // to one concept (unambiguous), parity SkillResolverIntegrationTests' derivation.
    // ---------------------------------------------------------------

    private const string SkillTaxonomyResource =
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";

    private static (string Label, string ConceptId) ResolveGoldenSkill()
    {
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        var resolver = new SkillResolver(new SkillTaxonomyIndex(analyzer));

        var concepts = ReadSkillConcepts();
        var lexemeOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var candidates = new List<(string Label, string ConceptId, string Lexeme)>();

        foreach (var c in concepts)
        {
            var label = c.PreferredLabel.Trim();
            if (label.Length is < 7 or > 14 || label.Any(ch => !char.IsLetter(ch)))
                continue;

            var lexemes = analyzer.ToLexemes(label, TextLanguage.Swedish);
            if (lexemes.Count != 1)
                continue;
            var lex = lexemes[0];

            if (!lexemeOwners.TryGetValue(lex, out var owners))
                lexemeOwners[lex] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(c.ConceptId);
            candidates.Add((label, c.ConceptId, lex));
        }

        // Among unambiguous single-token labels, pick the first that the resolver actually
        // resolves to its own concept-id (verify LIVE before relying on it).
        foreach (var cand in candidates
                     .Where(x => lexemeOwners[x.Lexeme].Count == 1)
                     .OrderBy(x => x.ConceptId, StringComparer.Ordinal))
        {
            var resolved = resolver.Resolve([cand.Label], TestContext.Current.CancellationToken);
            if (resolved.Contains(cand.ConceptId))
                return (cand.Label, cand.ConceptId);
        }

        throw new InvalidOperationException(
            "Ingen entydig single-token skill-golden kunde resolvas ur " +
            $"{SkillTaxonomyResource} — assetens form har ändrats (härled, gissa aldrig).");
    }

    private static List<(string ConceptId, string PreferredLabel)> ReadSkillConcepts()
    {
        var asm = typeof(LocalTextAnalyzer).Assembly; // Infrastructure assembly
        using var stream = asm.GetManifestResourceStream(SkillTaxonomyResource);
        stream.ShouldNotBeNull(
            $"Skill-taxonomi-resursen '{SkillTaxonomyResource}' ska vara en " +
            "<EmbeddedResource> i Infrastructure-assemblyn.");

        using var doc = JsonDocument.Parse(stream!);
        var skills = doc.RootElement.GetProperty("skills");
        var list = new List<(string, string)>(skills.GetArrayLength());
        foreach (var el in skills.EnumerateArray())
        {
            list.Add((
                el.GetProperty("conceptId").GetString()!,
                el.GetProperty("preferredLabel").GetString()!));
        }
        return list;
    }

    // ---------------------------------------------------------------
    // Wire helpers — enums serialize by NAME ([JsonStringEnumConverter]); assert
    // against the LIVE enum name so a future rename is caught here (no magic strings).
    // ---------------------------------------------------------------

    private static string Wire(MatchGrade grade) => grade.ToString();
    private static string Wire(MatchDimensionVerdict verdict) => verdict.ToString();

    private static string DimVerdict(JsonElement dto, string dimension) =>
        dto.GetProperty(dimension).GetProperty("verdict").GetString()!;

    private static string?[] DimMatched(JsonElement dto, string dimension) =>
        dto.GetProperty(dimension).GetProperty("matched")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

    // =================================================================
    // 1. Golden happy path on the wire — Top, SkillOverlap Match, strings
    //    round-trip, a Fast dimension also Match.
    // =================================================================

    [Fact]
    public async Task GET_match_detail_returns_top_with_skill_overlap_strings_when_cv_skills_match_strong_ad()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");
        await SetPreferencesAsync([grp], [reg], [emp], ct);

        // CV skill LABEL + the ad Skill term share the SAME resolved concept-id (derived
        // live from the committed taxonomy) → SkillOverlap == Match.
        var (skillLabel, skillConceptId) = ResolveGoldenSkill();
        await SeedPrimaryCvForStatedSeekerAsync(grp, [skillLabel], ct);

        // Strong Fast ad (occupation + region + employment all Match) WITH the shared skill
        // term → Strong + SkillOverlap Match → Top ("Toppmatch").
        var terms = ExtractedTerms.From([SkillTerm(skillConceptId, skillLabel)]);
        var adId = await SeedJobAdAsync("Systemutvecklare", grp, reg, emp, terms, ct);

        var response = await GetDetailAsync(adId, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // The golden grade — Top, by NAME on the wire.
        dto.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Top));

        // SkillOverlap Match + the matched STRING round-trips (the whole point of the modal DTO).
        DimVerdict(dto, "skillOverlap").ShouldBe(Wire(MatchDimensionVerdict.Match));
        var matched = DimMatched(dto, "skillOverlap");
        matched.ShouldNotBeEmpty();
        matched.ShouldContain(skillLabel); // the Display label, not the opaque concept-id
        matched.ShouldNotContain(skillConceptId);

        // A Fast dimension genuinely scored Match (the preference path is live on the wire).
        DimVerdict(dto, "ssykOverlap").ShouldBe(Wire(MatchDimensionVerdict.Match));
        DimVerdict(dto, "regionFit").ShouldBe(Wire(MatchDimensionVerdict.Match));
        DimVerdict(dto, "employmentFit").ShouldBe(Wire(MatchDimensionVerdict.Match));
    }

    // =================================================================
    // 2. Strong WITHOUT skill overlap, but with must-have VACUOUS — grade Strong.
    //    PR-B1 (RE-BIND G1-b): the ad has no extracted terms (no must-have, no skill)
    //    while the CV is present → all three FULL dims are Vacuous (gate-open Reading 1),
    //    so the must-have gate is OPEN and the Strong Fast grade holds (confirmed==2, no
    //    skill/nice {Match,Partial} signal → Strong, not Top). The skillOverlap verdict on
    //    the wire is Vacuous, never NotAssessed (the CV WAS assessed; the ad specifies none).
    // =================================================================

    [Fact]
    public async Task GET_match_detail_returns_strong_with_skill_overlap_vacuous_when_ad_has_no_skill_terms_and_cv_present()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");
        await SetPreferencesAsync([grp], [reg], [emp], ct);

        // The user HAS a CV skill, but the ad has NO extracted terms → the ad-side skill /
        // must-have / nice-to-have partitions are all empty while the CV is present →
        // Vacuous. must-have Vacuous = gate-open → the Strong Fast grade is preserved.
        var (skillLabel, _) = ResolveGoldenSkill();
        await SeedPrimaryCvForStatedSeekerAsync(grp, [skillLabel], ct);

        // Strong Fast ad, but terms: null → no extracted Skill / Requirement terms.
        var adId = await SeedJobAdAsync("Systemutvecklare", grp, reg, emp, terms: null, ct);

        var response = await GetDetailAsync(adId, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Vacuous must-have is gate-open → Strong holds; no Top (no skill/nice signal).
        dto.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Strong));
        // The ad specifies no skills BUT we DID look (CV present) → Vacuous, never NotAssessed.
        DimVerdict(dto, "skillOverlap").ShouldBe(Wire(MatchDimensionVerdict.Vacuous));
        DimVerdict(dto, "mustHaveCoverage").ShouldBe(Wire(MatchDimensionVerdict.Vacuous));
        DimMatched(dto, "skillOverlap").ShouldBeEmpty();
    }

    // =================================================================
    // 3. Honest breakdown / no short-circuit — authed user with NO stated
    //    occupation → 200, non-null body, grade null, ssykOverlap NotAssessed.
    // =================================================================

    [Fact]
    public async Task GET_match_detail_returns_null_grade_and_honest_breakdown_when_user_has_no_stated_occupation()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // Freshly-registered user has empty MatchPreferences (no stated occupation) — we set
        // none, so the handler must NOT short-circuit: it renders the honest breakdown.

        var grp = NewConceptId("grp");
        var adId = await SeedJobAdAsync("Systemutvecklare", grp, null, null, terms: null, ct);

        var response = await GetDetailAsync(adId, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Non-null body (the modal renders the rows + signpost), grade null (no positive tag).
        var raw = await response.Content.ReadAsStringAsync(ct);
        raw.ShouldNotBe("null", "200 ska ha en icke-null body — modalen renderar den ärliga uppdelningen.");
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        dto.GetProperty("grade").ValueKind.ShouldBe(JsonValueKind.Null);
        DimVerdict(dto, "ssykOverlap").ShouldBe(Wire(MatchDimensionVerdict.NotAssessed));
    }

    // =================================================================
    // 4. Not found — a non-existent ad → 404 (the scorer's NotFoundException
    //    propagates through the pipeline).
    // =================================================================

    [Fact]
    public async Task GET_match_detail_returns_404_when_ad_does_not_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        await SetPreferencesAsync([grp], [], [], ct);

        var response = await GetDetailAsync(Guid.NewGuid(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // =================================================================
    // 5. Anonymous (no auth) → 401 (.RequireAuthorization()). Low-value but cheap.
    // =================================================================

    [Fact]
    public async Task GET_match_detail_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header — the endpoint is RequireAuthorization()-gated.
        var response = await _client.GetAsync(
            $"/api/v1/me/job-ad-match-tags/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
