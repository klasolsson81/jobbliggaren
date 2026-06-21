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
/// seeding + the unique-worktime-extent test-run isolation, extended with
/// <c>SetExtractedTerms</c> for the skill-overlap signal (parity
/// <see cref="FullMatchScorerIntegrationTests"/>).
///
/// <para>
/// <b>Spår 3 PR-C (ADR 0076-amendment 2026-06-21; architect NOTE-3) — run-isolation moved off
/// municipality:</b> the SQL match-sort now reads the municipality shadow as part of the
/// ort-union (region ∪ municipality). A municipality run-isolation key would therefore become a
/// SPURIOUS ort signal (every isolated ad would share a preferred/non-preferred kommun and skew
/// its grade). The run tag is now the grade-neutral worktime-extent (payload key
/// <c>working_hours_type</c> → <c>worktime_extent_concept_id</c> shadow), which the grade never
/// reads — EXACTLY the move PR-B made to <see cref="MatchSortOracleTests"/>. Municipality is left
/// FREE as a genuine ort signal: the golden-via-municipality-hit case below proves the golden
/// top-skill lift fires when the ort confirmation comes via a kommun hit, not only a region hit.
/// All existing golden-rung semantics are preserved (the golden ads stay Strong-band via
/// region = PrefRegion).
/// </para>
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
                // Region-only ort preference; the municipality-hit golden case states a
                // municipality pref via ProfileWithMunicipality (Spår 3 PR-C).
                PreferredMunicipalityConceptIds: []),
            cvSkillConceptIds);

    // Spår 3 PR-C — the golden-via-municipality-hit profile: states a municipality ort
    // preference (region pref EMPTY) so the Strong-band confirmation comes via the KOMMUN leg
    // of the ort union, not the region leg. The CV skill set still drives the golden lift.
    private static FullCandidateMatchProfile ProfileWithMunicipality(
        string preferredMunicipality, params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: [preferredMunicipality]),
            cvSkillConceptIds);

    // Filter on the unique test-run worktime-extent only (Spår 3 PR-C: moved off municipality —
    // the grade now reads municipality as an ort signal, so the run-isolation key rides the
    // grade-neutral worktime-extent instead; mirrors MatchSortOracleTests.FilterFor).
    private static JobAdFilterCriteria FilterFor(string runWorktimeExtent) => new(
        OccupationGroup: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [runWorktimeExtent],
        Q: null);

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    // Seeds an ad with the test-run worktime-extent (grade-neutral isolation — Spår 3 PR-C),
    // the grade shadows, an OPTIONAL genuine municipality ort value, and an optional skill term
    // (→ STORED extracted_lexemes GIN for the ?| overlap). The run-isolation key rides the
    // TOP-LEVEL working_hours_type → worktime_extent_concept_id shadow (which the grade never
    // reads); municipality is now a FREE ort signal under workplace_address (default null).
    private async Task<JobAdId> SeedJobAdAsync(
        string runWorktimeExtent,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        DateTimeOffset publishedAt,
        ExtractedTerms? terms,
        CancellationToken ct,
        string? municipalityConceptId = null)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId,
            runWorktimeExtent, employmentTypeConceptId, municipalityConceptId);

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

    // occupation_group + employment_type + working_hours_type (the run-isolation worktime-extent)
    // are TOP-LEVEL; region AND municipality (both ort-union grade inputs) live under
    // workplace_address — only the present location key(s). Both location ids null →
    // workplace_address null (both ort shadows NULL). Parity
    // MatchScorerIntegrationTests.BuildWorkplaceAddressJson + MatchSortOracleTests.BuildRawPayload.
    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string runWorktimeExtentConceptId,
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
            + $"\"employment_type\":{employmentJson},"
            + $"\"working_hours_type\":{{\"concept_id\":\"{runWorktimeExtentConceptId}\"}}}}";
    }

    // workplace_address carries only the present location key(s): both null → "null" (both ort
    // shadows NULL); region only → {"region_concept_id":...}; municipality only →
    // {"municipality_concept_id":...}; both → both keys. Parity
    // MatchScorerIntegrationTests.BuildWorkplaceAddressJson.
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

    private static string NewRunWorktimeExtent() => $"wt-golden-{Guid.NewGuid():N}"[..20];

    // =================================================================
    // 10. The full ladder with the golden top tier:
    //     [Strong+skill] > [Strong no-skill] > [Good] > [Basic] > [untagged]
    // =================================================================

    [Fact]
    public async Task SearchByMatch_WithCvSkills_GoldenStrongSortsAbovePlainStrong_ThenF4_14Ladder()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
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
        var run = NewRunWorktimeExtent();
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
        var run = NewRunWorktimeExtent();
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

    // =================================================================
    // 13. Spår 3 PR-C — the golden top-skill lift fires when the ort confirmation comes via a
    //     MUNICIPALITY hit (region null / non-preferred, municipality preferred) rather than a
    //     region hit. PROVES the golden tier's Strong-band precondition reads the ort UNION
    //     (region-hit OR municipality-hit), not a bare region hit — so a kommun-confirmed Strong
    //     ad with a shared CV skill still earns the golden lift above a plain kommun-confirmed
    //     Strong. The profile states a MUNICIPALITY ort pref (region pref empty).
    // =================================================================

    [Fact]
    public async Task SearchByMatch_WithCvSkills_GoldenStrongViaMunicipalityHit_SortsAbovePlainMunicipalityStrong()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var goldenTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        const string prefMunicipality = "kn-golden-pref-0001";

        // Both ads are Strong via the MUNICIPALITY leg of the ort union: region is non-preferred
        // (the profile states NO region pref) but municipality = prefMunicipality (hit) →
        // ort Match → occ + ort + employment all Match → Strong-band. The golden ad ALSO shares
        // the CV skill; the plain ad does not. The golden ad is published OLDER, so if the golden
        // lift did NOT fire (e.g. the precondition read region-only and missed the kommun hit),
        // the NEWER plain ad would sort first. The golden lift must override the tie-break.
        var goldenViaMunicipalityOlder = await SeedJobAdAsync(
            run, PrefGroup, regionConceptId: null, PrefEmployment, t.AddDays(1), goldenTerms, ct,
            municipalityConceptId: prefMunicipality);
        var plainMunicipalityStrongNewer = await SeedJobAdAsync(
            run, PrefGroup, regionConceptId: null, PrefEmployment, t.AddDays(2), terms: null, ct,
            municipalityConceptId: prefMunicipality);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchByMatchAsync(
            FilterFor(run), ProfileWithMunicipality(prefMunicipality, CvSkillConceptId),
            page: 1, pageSize: 100, since: null, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(2, "Båda annonserna ska returneras.");
        orderedIds.IndexOf(goldenViaMunicipalityOlder.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(plainMunicipalityStrongNewer.Value),
                "Golden-lyften ska tända även när Strong-bandet bekräftas via en KOMMUN-träff " +
                "(region ej föredragen) — golden-precondition läser ort-UNIONEN (region-träff " +
                "ELLER kommun-träff), inte en bar region-träff. Den äldre golden-annonsen ska " +
                "därför ligga ÖVER den nyare plain-Strong (golden-lyften överrider tie-breaken).");
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
