using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6 b-ii) — the GOLDEN-RUNG sort oracle. F4-15
/// gives the per-user match sort (<see cref="IPerUserJobAdSearchQuery"/>) a new top
/// tier WITHOUT adding a visible <see cref="Grading.MatchGrade"/> member: an ad that is
/// Strong (occupation + region + employment all confirmed Match) AND shares ≥1 of the
/// profile's <see cref="FullCandidateMatchProfile.CvSkillConceptIds"/> (via the
/// <c>extracted_lexemes ?|</c> GIN overlap) sorts ABOVE a plain Strong. This PROVES the
/// <c>?|</c>-in-ORDER-BY translation against real Postgres (Testcontainers, NEVER
/// EF-InMemory — InMemory hides BOTH the STORED generated <c>extracted_lexemes</c> column
/// AND the jsonb overlap operator; memory ef_strongly_typed_vo_contains).
///
/// <para>
/// The F4-15 signature change under test: <c>SearchPerUserAsync</c> now takes a
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
/// RED until F4-15 widens <c>SearchPerUserAsync</c> to FullCandidateMatchProfile AND adds
/// the golden top tier to the ORDER BY.
///
/// <para>
/// <b>#268 audit C3 — the lift fires on ANY extracted lexeme, not only Skill-kind terms.</b>
/// The golden precondition is <c>extracted_lexemes ?| cvSkillIds</c>, and
/// <c>extracted_lexemes</c> is the STORED <c>jsonb_path_query_array(extracted_terms,
/// '$[*].Lexeme')</c> — it harvests EVERY term's Lexeme regardless of Kind (Skill ∪
/// must_have ∪ nice_to_have ∪ keyword; for Skill/Requirement terms Lexeme == ConceptId).
/// So this sort lift is a STRICT SUPERSET of the badge's skill signal
/// (<c>MatchGradeCalculator.HasSkillOrNiceSignal</c>, which counts only <c>Kind==Skill</c>
/// or <c>Source==NiceToHave</c>): a CV skill that matches an ad's <c>must_have</c>-only
/// concept-id (not echoed in the free-text description, so no Skill-kind term) lifts the
/// SORT but badges only "Stark match", never "Toppmatch". This is deliberately one-directional
/// (the sort is never less-informed than the badge — a must_have hit IS requirement evidence);
/// <see cref="SearchByMatch_GoldenLift_FiresOnMustHaveOnlyLexeme_NotJustSkillKind"/> pins it.
/// </para>
/// </summary>
[Collection("Api")]
public class MatchSortGoldenRungOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string PrefGroup = "grp-golden-pref";
    private const string PrefRegion = "reg-golden-pref";
    private const string PrefEmployment = "emp-golden-pref";

    // PR-4 (#300, ADR 0084) — a ssyk-4 in the RELATED (substitutable) set only (∉ exact). A
    // related-only ad caps flat at the Related rung; the golden top-skill lift reads the EXACT
    // ssyk set, so a shared CV skill must NOT lift a related-only ad above an exact positive.
    private const string RelatedGroup = "grp-golden-related";
    private static readonly string[] ExactGroups = [PrefGroup];
    private static readonly string[] RelatedGroups = [RelatedGroup];

    // The CV skill concept-id the golden ad shares; seeded into that ad's extracted_terms.
    private const string CvSkillConceptId = "skill-golden-0001";
    private const string CvSkillDisplay = "Golden-skill";

    private (IServiceScope Scope, IPerUserJobAdSearchQuery Query) NewMatchSort()
    {
        var scope = _factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPerUserJobAdSearchQuery>();
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

    // PR-4 (#300, ADR 0084) — a profile that STATES the related set { RelatedGroup } alongside the
    // exact set { PrefGroup }, with the CV skill set driving the (exact-only) golden lift. Used to
    // prove a related-only ad sharing the CV skill earns NO golden lift and stays at the Related rung.
    private static FullCandidateMatchProfile ProfileWithRelated(params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ExactGroups,
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: [])
            {
                RelatedSsykGroupConceptIds = RelatedGroups,
            },
            cvSkillConceptIds);

    // #477 Low 1 — a profile that states a region + MUNICIPALITY ort preference PLUS the derived
    // CONTAINMENT län set (the parent län of the preferred kommun). A LÄN-ONLY ad in the containment
    // län reads RegionFit NotAssessed (neither floors nor lifts) → it can NEVER be Fast-Strong and
    // NEVER earns the golden top-skill lift (which is Strong-band gated). Set DIRECTLY (the taxonomy
    // derivation is tested in the profile-builder unit tests).
    private static FullCandidateMatchProfile ProfileWithContainment(
        string preferredMunicipality, string containmentRegion, params string[] cvSkillConceptIds) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [PrefGroup],
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: [preferredMunicipality])
            {
                ContainmentRegionConceptIds = [containmentRegion],
            },
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
        Employer: [],
        Q: null);

    private static ExtractedTerm SkillTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Skill,
            Source: ExtractedTermSource.Description, MatchedOn: display,
            ConceptId: conceptId, Weight: 1);

    // A structured employer MUST-HAVE requirement term (NOT a Skill-kind term). Its Lexeme is
    // the ConceptId (parity JobAdKeywordExtractor's requirement pass), so it lands in the STORED
    // extracted_lexemes companion exactly like a Skill term — used by the #268-C3 oracle to prove
    // the golden sort-lift fires on a non-Skill lexeme while the badge would stay Strong.
    private static ExtractedTerm MustHaveTerm(string conceptId, string display) =>
        new(
            Lexeme: conceptId, Display: display, Kind: ExtractedTermKind.Requirement,
            Source: ExtractedTermSource.MustHave, MatchedOn: display,
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
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: publishedAt,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

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
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), Profile(CvSkillConceptId), grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), Profile(), grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), Profile(CvSkillConceptId), grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

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
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), ProfileWithMunicipality(prefMunicipality, CvSkillConceptId),
            grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(2, "Båda annonserna ska returneras.");
        orderedIds.IndexOf(goldenViaMunicipalityOlder.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(plainMunicipalityStrongNewer.Value),
                "Golden-lyften ska tända även när Strong-bandet bekräftas via en KOMMUN-träff " +
                "(region ej föredragen) — golden-precondition läser ort-UNIONEN (region-träff " +
                "ELLER kommun-träff), inte en bar region-träff. Den äldre golden-annonsen ska " +
                "därför ligga ÖVER den nyare plain-Strong (golden-lyften överrider tie-breaken).");
    }

    // =================================================================
    // 14. #268 audit C3 — the golden lift fires on a MUST-HAVE-only lexeme.
    //     An ad whose ONLY extracted term carrying the CV skill concept-id is a
    //     Requirement/MustHave term (NO Skill-kind term — the skill is stated only in
    //     JobTech's structured must_have, not echoed in the free-text description) still
    //     earns the golden top-sort lift, because extracted_lexemes harvests $[*].Lexeme
    //     across ALL Kinds. PROVES the sort lift is a strict SUPERSET of the badge's
    //     HasSkillOrNiceSignal (which counts only Skill/NiceToHave). This ad sorts in the
    //     golden tier yet would badge "Stark match", never "Toppmatch" — the documented,
    //     one-directional divergence.
    // =================================================================

    [Fact]
    public async Task SearchByMatch_GoldenLift_FiresOnMustHaveOnlyLexeme_NotJustSkillKind()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The CV skill concept-id appears ONLY as a MUST-HAVE requirement term (no Skill term).
        var mustHaveOnlyTerms = ExtractedTerms.From([MustHaveTerm(CvSkillConceptId, CvSkillDisplay)]);

        // golden-via-must-have: Strong (occ+region+employment all Match) AND the CV skill
        // concept-id is present via a must_have term. Published OLDER than the plain Strong, so
        // if the lift did NOT fire (e.g. it only read Skill-kind lexemes), the NEWER plain Strong
        // would sort first. The golden lift must override the publishedAt tie-break.
        var goldenViaMustHaveOlder = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), mustHaveOnlyTerms, ct);
        var plainStrongNewer = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), terms: null, ct);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), Profile(CvSkillConceptId),
            grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(2, "Båda annonserna ska returneras.");
        orderedIds.IndexOf(goldenViaMustHaveOlder.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(plainStrongNewer.Value),
                "Golden-lyften ska tända även när CV-skill-concept-id:t bara finns som en " +
                "must_have-term (ingen Skill-kind-term) — extracted_lexemes skördar $[*].Lexeme " +
                "över ALLA Kinds. Den äldre golden-annonsen ska därför ligga ÖVER den nyare " +
                "plain-Strong (lyften överrider tie-breaken). Sort-lyften är ett strikt superset " +
                "av badgens HasSkillOrNiceSignal; annonsen badgar Stark, aldrig Topp (#268 C3).");
    }

    // =================================================================
    // 15. #268 audit C3 (negative) — a must_have lexeme that does NOT overlap the CV
    //     gives NO lift. Proves the golden lift on a must_have term is OVERLAP-gated
    //     (JsonExistAny is symmetric set overlap), not triggered by the mere presence of
    //     a Requirement term. Pairs with #14: the lift fires iff the must_have concept-id
    //     is in the CV skill set. (#11's negative runs through a Skill term + empty CV;
    //     this one keeps the CV non-empty and uses a non-matching must_have concept-id.)
    // =================================================================

    [Fact]
    public async Task SearchByMatch_GoldenLift_DoesNotFire_WhenMustHaveLexemeNotInCvSkills()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // A must_have term whose concept-id is NOT in the CV skill set (CvSkillConceptId).
        const string otherConceptId = "skill-golden-9999";
        var nonMatchingMustHave = ExtractedTerms.From([MustHaveTerm(otherConceptId, "Other-skill")]);

        // The ad with the non-matching must_have term is published OLDER; the plain Strong is
        // NEWER. With NO overlap there is no golden lift → both are plain Strong → publishedAt
        // desc puts the NEWER plain ad first. (If a must_have term lifted on mere presence, the
        // OLDER ad would wrongly sort first.)
        var mustHaveNoOverlapOlder = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(1), nonMatchingMustHave, ct);
        var plainStrongNewer = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(2), terms: null, ct);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), Profile(CvSkillConceptId),
            grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(2, "Båda annonserna ska returneras.");
        orderedIds.IndexOf(plainStrongNewer.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(mustHaveNoOverlapOlder.Value),
                "En must_have-term vars concept-id INTE finns i CV-skill-mängden ger INGEN " +
                "golden-lyft (overlap-gated, ej blotta närvaron av en Requirement-term) — " +
                "ordningen ≡ F4-14 (nyare plain-Strong först).");
    }

    // =================================================================
    // 16. PR-4 (#300, ADR 0084) — a RELATED-only ad carrying a shared CV skill earns NO golden lift
    //     and sorts at the Related rung (below exact Good/Strong). The golden top-skill lift reads
    //     the EXACT ssyk set only — a substitutable occupation is capped flat at Related regardless
    //     of any skill overlap, so the shared skill must NOT lift it above an exact positive. Pairs
    //     a related-only Strong-SHAPED ad (region+employment Match) holding the shared skill against
    //     an exact Good ad (no skill): the exact Good (rung 3) must sort ABOVE the related ad
    //     (rung 2), even though only the related ad carries the golden skill.
    // =================================================================

    [Fact]
    public async Task SearchByMatch_RelatedOnlyAdWithSharedSkill_GetsNoGoldenLift_SortsBelowExactGood()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var goldenTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        // #552 grade gate: a stated-but-NULL secondary now floors to Basic, so the old
        // "employment null → NotAssessed → Good" seed no longer expresses Good. The remaining
        // stable Good path under a both-stated profile is the #477 containment carve-out —
        // Good in BOTH production epochs (containment reads NotAssessed before and after).
        const string prefKommunRel = "mun-golden-rel-pref";
        const string containmentLanRel = "reg-golden-rel-containment-lan";

        // relatedWithSkill: occupation ∈ related-only, region+employment Match (a would-be Strong),
        // AND carries the shared CV skill term — the golden precondition for an EXACT ad. Published
        // NEWER, so if the related rung wrongly took a golden lift (or any exact-positive rank), it
        // would sort first. The flat related-cap (rung 2) must place it BELOW the exact Good.
        var relatedWithSkill = await SeedJobAdAsync(
            run, RelatedGroup, PrefRegion, PrefEmployment, t.AddDays(2), goldenTerms, ct);
        // exactGood: exact occupation + one confirmed secondary (employment Match; ort via the
        // län-only containment ad → RegionFit NotAssessed, #477) + NO skill term → plain Good
        // (rung 3). Published OLDER.
        var exactGood = await SeedJobAdAsync(
            run, PrefGroup, containmentLanRel, PrefEmployment, t.AddDays(1), terms: null, ct);

        // ProfileWithRelated + the kommun preference whose derived containment län admits the
        // län-only exactGood ad (set directly — the taxonomy derivation is profile-builder-tested).
        var profile = new FullCandidateMatchProfile(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ExactGroups,
                PreferredRegionConceptIds: [PrefRegion],
                PreferredEmploymentTypeConceptIds: [PrefEmployment],
                PreferredMunicipalityConceptIds: [prefKommunRel])
            {
                RelatedSsykGroupConceptIds = RelatedGroups,
                ContainmentRegionConceptIds = [containmentLanRel],
            },
            [CvSkillConceptId]);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run), profile,
            grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true, status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(2, "Båda annonserna ska returneras.");
        orderedIds.IndexOf(exactGood.Value)
            .ShouldBeLessThan(orderedIds.IndexOf(relatedWithSkill.Value),
                "Den exakta Good-annonsen (rung 3) ska rangordnas ÖVER den related-only-annonsen " +
                "(rung 2) — den delade CV-skill:en ger INGEN golden-lyft på related-rungen " +
                "(golden-lyften läser det EXAKTA ssyk-setet; ett substituerbart yrke cap:as platt " +
                "till Related oavsett skill-överlapp). Den nyare related-annonsen får alltså INTE " +
                "ligga först.");
    }

    // =================================================================
    // 17. #477 Low 1 — a CONTAINMENT län-only ad does NOT perturb the golden rung. Such an ad
    //     reads RegionFit NotAssessed (via the containment branch), so it can never be Fast-Strong
    //     and never earns the golden top-skill lift — even when it carries the shared CV skill. It
    //     sorts at its REAL Good rank (rank 3): BELOW the two golden/plain Strong ads and ABOVE a
    //     plain Basic ad. The golden rung is UNTOUCHED by #477; this pins that invariant.
    //
    //     Revert-detection: the containment ad is published OLDER than the plain Basic ad, so if the
    //     containment branch were reverted (RegionFit → NoMatch → RB1 floor → Basic), the ad would
    //     TIE the plain Basic at rank 1 and — broken by publishedAt desc — sink BELOW the newer plain
    //     Basic, breaking the asserted order. So this test genuinely fails if containment is reverted.
    // =================================================================

    [Fact]
    public async Task SearchByMatch_ContainmentLanOnlyAd_SortsAtGoodRankBelowGolden_NeverGoldenNorBasic()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = NewRunWorktimeExtent();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // The preferred kommun and its parent län (containment). containmentLan ≠ PrefRegion so the
        // containment ad is NOT a direct region hit — only the containment branch can neutralise it.
        const string prefMunicipality = "kn-golden-containment-pref";
        const string containmentLan = "reg-golden-containment-lan";

        var goldenTerms = ExtractedTerms.From([SkillTerm(CvSkillConceptId, CvSkillDisplay)]);

        // goldenStrong: Strong (region hit) + shared skill → golden top lift.
        var goldenStrong = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(10), goldenTerms, ct);
        // plainStrong: Strong (region hit), no skill → plain Strong (below golden).
        var plainStrong = await SeedJobAdAsync(
            run, PrefGroup, PrefRegion, PrefEmployment, t.AddDays(9), terms: null, ct);
        // containmentGood: LÄN-ONLY ad in the containment län (region = containmentLan, municipality
        // NULL) + employment Match → RegionFit NotAssessed (containment) → Good (rank 3). It ALSO
        // carries the shared CV skill — but it is NOT Strong-band, so it earns NO golden lift (the
        // golden precondition requires an ort hit, which a containment NotAssessed is not). Published
        // OLDER than plainBasic so a reverted containment (→ Basic floor) would sink it below the
        // newer plainBasic — making this assertion fail loud on revert.
        var containmentGood = await SeedJobAdAsync(
            run, PrefGroup, containmentLan, PrefEmployment, t.AddDays(3), goldenTerms, ct);
        // plainBasic: SSYK Match only, both secondaries NotAssessed (region + employment NULL) →
        // Basic (rank 1). Published NEWER than containmentGood — the contrast that makes Good-vs-Basic
        // observable in the sort order (revert-detection).
        var plainBasic = await SeedJobAdAsync(
            run, PrefGroup, null, null, t.AddDays(8), terms: null, ct);

        var (scope, matchSort) = NewMatchSort();
        using var _ = scope;
        var page = await matchSort.SearchPerUserAsync(
            FilterFor(run),
            ProfileWithContainment(prefMunicipality, containmentLan, CvSkillConceptId),
            grades: [], sort: JobAdSortBy.PublishedAtDesc, orderByMatchRank: true,
            status: JobAdStatusFilter.None, seekerId: default, page: 1, pageSize: 100, ct);

        var orderedIds = page.Items.Select(i => i.Id).ToList();
        orderedIds.Count.ShouldBe(4, "Hela den filtrerade mängden ska returneras.");

        orderedIds.ShouldBe(
            [goldenStrong.Value, plainStrong.Value, containmentGood.Value, plainBasic.Value],
            "Den gyllene rungen är ORÖRD av #477: golden-Strong > plain-Strong (golden-lyft), sedan " +
            "containment-annonsen på sin RIKTIGA Good-rank (under båda Strong-annonserna — INGEN " +
            "golden-lyft trots delad CV-skill, den är inte Strong-band), sedan Basic sist. Vore " +
            "containment reverterad skulle annonsen golvas till Basic och sjunka UNDER den nyare " +
            "plain-Basic (denna assertion failar då).");
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
