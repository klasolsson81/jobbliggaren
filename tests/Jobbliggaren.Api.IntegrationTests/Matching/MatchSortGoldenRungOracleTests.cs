using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6 b-ii) — the GOLDEN-RUNG sort oracle. F4-15
/// gives the per-user match sort (<see cref="IMatchSortedJobAdSearchQuery"/>) a new top
/// tier WITHOUT adding a visible <see cref="Grading.MatchGrade"/> member: an ad that is
/// Strong (occupation + region + employment all confirmed Match) AND shares ≥1 of the
/// profile's <see cref="FullCandidateMatchProfile.CvSkillConceptIds"/> (via the
/// <c>extracted_lexemes ?|</c> GIN overlap) sorts ABOVE a plain Strong. This PROVES the
/// <c>?|</c>-in-ORDER-BY translation against real Postgres (Testcontainers, NEVER
/// EF-InMemory — InMemory hides BOTH the STORED generated <c>extracted_lexemes</c> column
/// AND the jsonb overlap operator; memory ef_strongly_typed_vo_contains).
///
/// <para>
/// The F4-15 signature change under test: <c>SearchByMatchAsync</c> now takes a
/// <see cref="FullCandidateMatchProfile"/> (was <see cref="CandidateMatchProfile"/>).
/// </para>
///
/// <para>
/// <b>BEHAVIOURAL, not SQL-mechanistic (Klas/partner note):</b> if
/// <c>extracted_lexemes ?|</c> does not translate inside <c>OrderBy</c> and the impl falls
/// back to a raw Infra <c>FromSql</c> ORDER fragment, this oracle still passes — it asserts
/// the ORDER of the returned page, never the SQL form.
/// </para>
///
/// Mirrors <see cref="MatchSortOracleTests"/> (the F4-14 ladder oracle) for the shadow-column
/// seeding + the unique-municipality test-run isolation, extended with
/// <c>SetExtractedTerms</c> for the skill-overlap signal (parity
/// <see cref="FullMatchScorerIntegrationTests"/>).
///
/// RED until F4-15 widens <c>SearchByMatchAsync</c> to FullCandidateMatchProfile AND adds
/// the golden top tier to the ORDER BY.
/// </summary>
[Collection("Api")]
public class MatchSortGoldenRungOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string PrefGroup = "grp-golden-pref";
    private const string PrefRegion = "reg-golden-pref";
    private const string PrefEmployment = "emp-golden-pref";

    // The CV skill concept-id the golden ad shares; seeded into that ad's extracted_terms.
    private const string CvSkillConceptId = "skill-golden-0001";
    private const string CvSkillDisplay = "Golden-skill";

    private (IServiceScope Scope, IMatchSortedJobAdSearchQuery Query) NewMatchSort()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IMatchSortedJobAdSearchQuery>();
        return (scope, query);
    }

    // Fast = stated SSYK + region + employment (so Strong is reachable). The CV skill
    // set drives the golden lift; an empty set ⇒ no lift (≡ F4-14).
    private static FullCandidateMatchProfile Profile(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                // Spår 3 PR-A — 5:e dimension; tom (municipality testas i PR-B, ej här).
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    private static JobAdFilterCriteria FilterFor(string runMunicipality) => new(
        OccupationGroup: [],
        Municipality: [runMunicipality],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Q: null);

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    // Seeds an ad with the test-run municipality (isolation), the grade shadows, and an
    // optional skill term (→ STORED extracted_lexemes GIN for the ?| overlap).
    private async Task<JobAdId> SeedJobAdAsync(
        string runMunicipality,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        DateTimeOffset publishedAt,
        ExtractedTerms? terms,
        CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId,
            runMunicipality, employmentTypeConceptId);

        var jobAd = JobAd.Import(
            title: "Golden-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        if (terms is not null)
            jobAd.SetExtractedTerms(terms);

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string municipalityConceptId,
        string? employmentTypeConceptId)
    {
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";

        var regionPart = regionConceptId is null
            ? string.Empty
            : $"\"region_concept_id\":\"{regionConceptId}\",";
        var addressJson =
            $"{{{regionPart}\"municipality_concept_id\":\"{municipalityConceptId}\"}}";

        var employmentJson = employmentTypeConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentTypeConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson}}}";
    }

    private static string NewRunMunicipality() => $"kn-golden-{Guid.NewGuid():N}"[..20];

    // =================================================================
    // 10. The full ladder with the golden top tier:
    //     [Strong+skill] > [Strong no-skill] > [Good] > [Basic] > [untagged]
    // =================================================================

    [Fact]
    public async Task SearchByMatch_WithCvSkills_GoldenStrongSortsAbovePlainStrong_ThenF4_14Ladder()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunMunicipality();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var goldenTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        // S+ : Strong AND shares the CV skill (golden).
        var sPlus = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(10), goldenTerms, ct);
        // S- : Strong, no shared skill (no extracted skill term).
        var sMinus = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(9), terms: null, ct);
        // G  : Good (one confirmed secondary — employment NotAssessed via null shadow).
        var g = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, null, t.AddDays(8), terms: null, ct);
        // B  : Basic (both secondaries NotAssessed — null region + null employment).
        var b = await SeedJobAdAsync(
            run, PrefGroup, null, null, t.AddDays(7), terms: null, ct);
        // U  : untagged (SSYK NotAssessed via null group) → sorts last.
        var u = await SeedJobAdAsync(
            run, null, PrefRegion, PrefEmployment, t.AddDays(6), terms: null, ct);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), Profile(CvSkillConceptId), page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(5, "Hela den filtrerade mängden ska returneras (otaggade inkl.).");

        // The exact expected order: golden-Strong, plain-Strong, Good, Basic, untagged.
        orderedIds.ShouldBe(
            [sPlus.Value, sMinus.Value, g.Value, b.Value, u.Value],
            "Golden (Strong + delad CV-skill) ska ligga ÖVER plain Strong, sedan F4-14-stegen.");
    }

    // =================================================================
    // 11. Empty CvSkillConceptIds → no golden lift (order ≡ F4-14)
    // =================================================================

    [Fact]
    public async Task SearchByMatch_WithEmptyCvSkills_NoGoldenLift_OrderEqualsF4_14()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunMunicipality();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var goldenTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        // The ad that WOULD be golden (has the skill term) but is published OLDER than the
        // plain Strong. With NO CV skills, there is no golden lift → both are plain Strong,
        // so the tie-break (publishedAt desc) puts the NEWER plain Strong first.
        var strongWithSkillOlder = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), goldenTerms, ct);
        var strongNoSkillNewer = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), terms: null, ct);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        // EMPTY CvSkillConceptIds → no golden lift.
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), Profile(), page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.IndexOf(strongNoSkillNewer.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(strongWithSkillOlder.Value),
                "Utan CV-skills får skill-annonsen INGEN golden-lyft — ordningen ≡ F4-14 " +
                "(nyare publishedAt först inom samma grad).");
    }

    // =================================================================
    // 12. Tie-break within a tier — publishedAt desc, then Id
    // =================================================================

    [Fact]
    public async Task SearchByMatch_WithinGoldenTier_OrdersByPublishedAtDescThenId()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunMunicipality();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var goldenTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        // Three golden-Strong ads (all share the CV skill) with DISTINCT publishedAt.
        var older = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), goldenTerms, ct);
        var newest = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(3), goldenTerms, ct);
        var middle = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), goldenTerms, ct);

        // Two golden-Strong ads with EQUAL publishedAt → broken deterministically on Id.
        var equalA = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(5), goldenTerms, ct);
        var equalB = await SeedJobAdAsync(run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(5), goldenTerms, ct);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), Profile(CvSkillConceptId), page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();

        orderedIds.IndexOf(newest.Value).ShouldBeLessThan(orderedIds.IndexOf(middle.Value),
            "Nyare publishedAt först inom golden-tiern.");
        orderedIds.IndexOf(middle.Value).ShouldBeLessThan(orderedIds.IndexOf(older.Value),
            "Nyare publishedAt först inom golden-tiern.");

        // Equal publishedAt → canonical Postgres uuid order (derived empirically, NOT a
        // .NET Guid.CompareTo guess — Postgres uuid byte order differs).
        var canonical = await CanonicalIdOrderAsync(equalA, equalB, ct);
        orderedIds.IndexOf(canonical[0].Value)
            .ShouldBeLessThan(orderedIds.IndexOf(canonical[1].Value),
                "Lika publishedAt bryts deterministiskt på Id (.ThenBy(j => j.Id)).");
    }

    private async Task<List<JobAdId>> CanonicalIdOrderAsync(
        JobAdId first, JobAdId second, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobAds.AsNoTracking()
            .Where(j => j.Id == first || j.Id == second)
            .OrderBy(j => j.Id)
            .Select(j => j.Id)
            .ToListAsync(ct);
    }
}
