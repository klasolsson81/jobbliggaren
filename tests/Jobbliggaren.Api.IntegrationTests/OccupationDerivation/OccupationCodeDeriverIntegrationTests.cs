using System.Text.Json;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.OccupationDerivation;

/// <summary>
/// Fas 4 STEG 3 (F4-3, ADR 0040 amendment + ADR 0074, CTO Decision 1–5) — the
/// REAL OccupationCodeDeriver against the seeded taxonomy snapshot + the real
/// frozen occupation-name→ssyk-4 map + the real Swedish Snowball analyzer
/// (Testcontainers, ALDRIG EF-InMemory — paritet med
/// TaxonomyReadModelIntegrationTests / SwedishStemmerPostgresParityTests; the
/// seeder's idempotens-transaktion + advisory-lock kräver en relationell motor).
/// Self-contained fixture (egen container) så snapshoten styrs deterministiskt.
///
/// V2 (CTO Decision 1): match the free-text title against the ~2323 occupation-
/// NAME labels, resolve the hit to its ssyk-4 group via the frozen map, return a
/// RANKED candidate list. Two passes (Decision 3): exact normalized
/// (OrdinalIgnoreCase, NO diacritic folding — Decision 4) + stemmed token-overlap
/// via ITextAnalyzer.ToLexemes. No auto-select, no persist; no-match → empty
/// (Decision 5).
///
/// GOLDEN PROVENANCE (derived from the committed data 2026-06-14, NOT guessed —
/// F4-2 lesson). For each hardcoded pair: occupation-name label → its concept-id
/// in taxonomy-snapshot.json → its ssyk-4 id in
/// occupation-name-to-ssyk-level-4.v30.json → that group's label back in the
/// snapshot. The pairs are ALSO re-derived live below (DeriveExpectedGroup) so
/// the assertions can never silently go stale against a future snapshot bump.
///   • "Advokat"          occ-name tQFo_jhD_UXT → ssyk-4 q8wL_kdi_WaW "Advokater"
///   • "Arbetsförmedlare" occ-name XSDj_JZ2_ugu → ssyk-4 fsnw_ZCu_v2U "Arbetsförmedlare"
///   • "Mjukvaruutvecklare" occ-name rQds_YGd_quU → ssyk-4 DJh5_yyF_hEM
///                                       "Mjukvaru- och systemutvecklare m.fl."
///   • "Förskollärare"    occ-name rUcW_z9R_Qsv → ssyk-4 5ek3_Cgq_WeZ "Förskollärare"
/// Live data counts (2026-06-14): 2323 occ-names, 400 groups, 2179 frozen
/// mappings, 2153 mapped / 170 with a snapshot label but no frozen entry
/// (those degrade to manual selection — F4-3 partial-coverage rule).
///
/// RED until OccupationCodeDeriver + IOccupationCodeDeriver + the DTOs/enum ship.
/// </summary>
public sealed class OccupationCodeDeriverIntegrationTests : IAsyncLifetime
{
    // Frozen migration-owned reverse-lookup resource (ADR 0067 C2). F4-3 reads
    // it from the live path (role-widening RATIFIED — CTO Decision 2; bytes
    // never regenerated, read-only second consumer). Same LogicalName the csproj
    // declares; used here ONLY to re-derive expected goldens, not to drive SUT.
    private const string FrozenMapResource =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _provider = default!;
    private IReadOnlyDictionary<string, string> _frozenMap = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_postgres.GetConnectionString(),
                    npgsql => npgsql.MigrationsAssembly(
                        typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();

        await RunSeederAsync(CancellationToken.None);
        _frozenMap = await ReadFrozenMapAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private IServiceScopeFactory ScopeFactory =>
        _provider.GetRequiredService<IServiceScopeFactory>();

    private async Task RunSeederAsync(CancellationToken ct)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test"); // grace-period på, fail-loud i prod
        var seeder = new TaxonomySnapshotSeeder(
            ScopeFactory, env,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaxonomySnapshotSeeder>.Instance);
        await seeder.StartAsync(ct);
    }

    // SUT factory — exact ctor signature CC will create (architect §3.3 /
    // CTO Decision 5): internal sealed, consumes ITaxonomyReadModel + ITextAnalyzer,
    // owns its own lazy frozen-map cache. Construct the real read model + the real
    // Swedish analyzer (paritet SwedishStemmerPostgresParityTests' NewAnalyzer()).
    // Concrete return type (not the IOccupationCodeDeriver port) satisfies CA1859
    // for this local factory — the SUT is the real Infrastructure impl resolved by
    // direct construction (paritet SwedishStemmerPostgresParityTests' NewAnalyzer()).
    private OccupationCodeDeriver NewDeriver()
    {
        var taxonomy = new TaxonomyReadModel(ScopeFactory);
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        return new OccupationCodeDeriver(taxonomy, analyzer);
    }

    // =================================================================
    // (a) Exact occupation-name title → its real ssyk-4 group, ExactOccupationName
    // =================================================================

    [Theory]
    // title, expected ssyk-4 id, expected ssyk-4 label (provenance above).
    [InlineData("Advokat", "q8wL_kdi_WaW", "Advokater")]
    [InlineData("Arbetsförmedlare", "fsnw_ZCu_v2U", "Arbetsförmedlare")]
    [InlineData("Mjukvaruutvecklare", "DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl.")]
    [InlineData("Förskollärare", "5ek3_Cgq_WeZ", "Förskollärare")]
    public async Task DeriveAsync_ExactOccupationNameTitle_ResolvesToRealSsyk4Group(
        string title, string expectedGroupId, string expectedGroupLabel)
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync(title, ct);

        result.Title.ShouldBe(title);
        // The exact hit must be present, carry the real ssyk-4 id+label, be
        // marked ExactOccupationName, and cite the occupation-name label it
        // matched on (explainable-by-design — CLAUDE.md §5).
        result.Candidates.ShouldContain(c =>
            c.OccupationGroupConceptId == expectedGroupId
            && c.MatchKind == OccupationMatchKind.ExactOccupationName);
        var exact = result.Candidates.Single(c =>
            c.OccupationGroupConceptId == expectedGroupId
            && c.MatchKind == OccupationMatchKind.ExactOccupationName);
        exact.OccupationGroupLabel.ShouldBe(expectedGroupLabel);
        exact.MatchedOn.ShouldNotBeNullOrWhiteSpace();
        // MatchedOn is the occupation-name label span that grounded the match —
        // an exact hit cites the title's own label (case-insensitive equality,
        // OrdinalIgnoreCase — Decision 4: no diacritic folding).
        exact.MatchedOn.Equals(title, StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
            $"Exact-träffens MatchedOn ('{exact.MatchedOn}') ska vara titelns " +
            $"yrkesnamn-label ('{title}', OrdinalIgnoreCase).");
    }

    [Fact]
    public async Task DeriveAsync_ExactHit_RanksExactBeforeAnyStemmedCandidate()
    {
        // Deterministic ordering (CTO Decision 3): MatchKind exact before stemmed.
        // If a title yields both an exact and stemmed candidates, the exact one
        // must sort first. "Advokat" is a clean exact hit.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync("Advokat", ct);

        result.Candidates.ShouldNotBeEmpty();
        result.Candidates[0].MatchKind.ShouldBe(OccupationMatchKind.ExactOccupationName);
        // The first candidate is the exact "Advokater" group (provenance above).
        result.Candidates[0].OccupationGroupConceptId.ShouldBe("q8wL_kdi_WaW");
    }

    [Fact]
    public async Task DeriveAsync_ExactHit_MatchesLiveDerivedFrozenMapGroup()
    {
        // Anti-stale guard: re-derive the expected group LIVE from the seeded
        // tree + the frozen map (the same data the deriver loads) and assert the
        // deriver agrees. If a future snapshot bump changes the mapping, this
        // updates automatically instead of asserting a stale magic id.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        var expected = await DeriveExpectedGroupAsync("Arbetsförmedlare", ct);
        expected.ShouldNotBeNull(
            "Golden-titeln 'Arbetsförmedlare' borde finnas som occupation-name " +
            "i snapshoten OCH i frozen-mapen (provenance-verifierad).");

        var result = await sut.DeriveAsync("Arbetsförmedlare", ct);

        result.Candidates.ShouldContain(c =>
            c.OccupationGroupConceptId == expected!.Value.GroupId
            && c.OccupationGroupLabel == expected.Value.GroupLabel
            && c.MatchKind == OccupationMatchKind.ExactOccupationName);
    }

    // =================================================================
    // (b) Stemmed/inflected title → a StemmedTokenOverlap candidate
    // =================================================================

    [Fact]
    public async Task DeriveAsync_InflectedTitle_YieldsStemmedTokenOverlapCandidate()
    {
        // "mjukvaruutvecklaren" (bestämd form) is NOT an exact occupation-name
        // label, but its Snowball stem overlaps the label "Mjukvaruutvecklare"
        // (occ-name rQds_YGd_quU → ssyk-4 DJh5_yyF_hEM). The stemmed pass must
        // surface a StemmedTokenOverlap candidate for that group, citing the
        // occupation-name label it overlapped on. Verified: the inflected form is
        // not itself a label, so this can only come from the stemmed pass.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync("mjukvaruutvecklaren", ct);

        result.Candidates.ShouldNotBeEmpty(
            "En böjd yrkestitel ('mjukvaruutvecklaren') ska ge minst en " +
            "StemmedTokenOverlap-kandidat (stemmen delar token med ett yrkesnamn).");
        result.Candidates.ShouldContain(c =>
            c.MatchKind == OccupationMatchKind.StemmedTokenOverlap);
        var stemmed = result.Candidates.First(c =>
            c.MatchKind == OccupationMatchKind.StemmedTokenOverlap);
        stemmed.OccupationGroupConceptId.ShouldNotBeNullOrWhiteSpace();
        stemmed.OccupationGroupLabel.ShouldNotBeNullOrWhiteSpace();
        // Cited evidence is an occupation-name label that shares the stem.
        stemmed.MatchedOn.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeriveAsync_StemmedCandidates_AllResolveToRealSsyk4GroupsInSnapshot()
    {
        // Every candidate's ssyk-4 id must be a REAL group id present in the
        // seeded snapshot (the deriver resolves via the frozen map, whose targets
        // are all snapshot groups — 0 dangling, verified in derivation). Guards
        // against a candidate carrying an occupation-name id by mistake.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        var groupIds = await SeededGroupIdsAsync(ct);

        var result = await sut.DeriveAsync("sjuksköterskan", ct);

        result.Candidates.ShouldAllBe(c => groupIds.Contains(c.OccupationGroupConceptId));
    }

    // =================================================================
    // (c) Nonsense title → empty candidates (never throws, never auto-selects)
    // =================================================================

    [Theory]
    [InlineData("xyzzy qwerty")]
    [InlineData("zzzzqqqq vvvvwwww")]
    public async Task DeriveAsync_NonsenseTitle_ReturnsEmptyCandidates(string title)
    {
        // No-match contract (ADR 0040 Beslut 4): empty Candidates → UX falls to
        // manual SSYK selection. Must NOT throw and must NOT auto-select.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync(title, ct);

        result.Title.ShouldBe(title);
        result.Candidates.ShouldBeEmpty();
    }

    // =================================================================
    // (d) Determinism — same input twice → identical ordered result
    // =================================================================

    // A union of confirmed-DISTINCT occupation titles that survives the group-spread
    // gate and yields several deduped candidates — re-pointed from the old "chef"/
    // "Operatör" probes, which are now correctly filtered to nothing in the stemmed
    // pass (their bare generic tokens fan out across many ssyk-4 groups). Each title
    // here resolves to a focused group; the union is a strong multi-candidate probe.
    private static readonly string[] DistinctiveTitleUnion =
        ["Advokat", "Förskollärare", "Arbetsförmedlare", "Mjukvaruutvecklare"];

    [Fact]
    public async Task DeriveManyAsync_SameTitlesTwice_ReturnsIdenticalOrderedResult()
    {
        // Determinism (CTO Decision 3, Invariant-grade). A union of distinctive titles
        // yields several deduped candidates; the same input twice must produce an
        // identical ordered sequence (not just set-equal).
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var first = await sut.DeriveManyAsync(DistinctiveTitleUnion, ct);
        var second = await sut.DeriveManyAsync(DistinctiveTitleUnion, ct);

        first.Candidates.Count.ShouldBeGreaterThan(1,
            "Unionen av distinktiva titlar ska ge flera kandidater (annars är " +
            "determinism-proben urvattnad — peka om titlarna).");
        var firstKeys = first.Candidates
            .Select(c => (c.OccupationGroupConceptId, c.MatchKind)).ToList();
        var secondKeys = second.Candidates
            .Select(c => (c.OccupationGroupConceptId, c.MatchKind)).ToList();

        // Identical sequence (order included) — not just set-equal.
        secondKeys.ShouldBe(firstKeys);
    }

    [Fact]
    public async Task DeriveManyAsync_Candidates_ExactBeforeStemmed()
    {
        // The ordering rule's stable, observable invariant (CTO Decision 3): all Exact
        // candidates precede all Stemmed candidates. A union of an EXACT title
        // ("Advokat") and an inflected STEMMED-only title ("förskollärarna", bestämd
        // plural — not an exact label, distinctive stem) yields both kinds in distinct
        // groups. (Re-pointed from "chef": the old probe relied on a generic 1-token
        // title fanning out, which the group-spread gate now filters; and the old
        // "labels Ordinal within a kind" sub-assertion was an artefact of a single
        // title's constant SourceOrder — SourceOrder now governs intra-kind order, so
        // we assert only the kind-ordering invariant.)
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveManyAsync(["Advokat", "förskollärarna"], ct);
        var candidates = result.Candidates;

        candidates.ShouldContain(c => c.MatchKind == OccupationMatchKind.ExactOccupationName,
            "'Advokat' ska ge en exakt kandidat.");
        candidates.ShouldContain(c => c.MatchKind == OccupationMatchKind.StemmedTokenOverlap,
            "'förskollärarna' (böjd, distinkt stam) ska ge en stammad kandidat.");

        // Exact (enum value 0) before Stemmed (enum value 1) — the MatchKind sequence
        // must be non-decreasing by enum value.
        var kindSequence = candidates.Select(c => (int)c.MatchKind).ToList();
        kindSequence.ShouldBe(kindSequence.OrderBy(k => k).ToList(),
            "Exact-kandidater ska sorteras före Stemmed-kandidater.");
    }

    // =================================================================
    // (e) Bounded — a high-overlap title cannot return an unbounded list
    // =================================================================

    [Fact]
    public async Task DeriveManyAsync_DistinctiveUnion_ReturnsBoundedDeduplicatedCandidates()
    {
        // Bounded candidate cap (CTO/architect §2 sub-axis 6): the union must not
        // return hundreds. We assert the result is BOUNDED and DEDUPED by ssyk-4 group
        // id (not an exact magic number — impl const). (Re-pointed from "chef": a bare
        // generic title now yields nothing in the stemmed pass; a distinctive union is
        // the probe.)
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveManyAsync(DistinctiveTitleUnion, ct);

        result.Candidates.ShouldNotBeEmpty();
        // Bounded: never the whole taxonomy. 50 is a generous ceiling — the impl
        // cap is expected to be far smaller; this only falsifies an unbounded fan-out.
        result.Candidates.Count.ShouldBeLessThanOrEqualTo(50,
            "Unionen får inte returnera en obegränsad kandidat-lista " +
            "(bounded cap, DoS-/UX-skydd).");

        // Deduped by ssyk-4 group id — many occupation-names map to one group,
        // but each group appears at most once (strongest evidence kept).
        var distinctGroupIds = result.Candidates
            .Select(c => c.OccupationGroupConceptId).Distinct().Count();
        distinctGroupIds.ShouldBe(result.Candidates.Count,
            "Kandidater ska vara deduplicerade per ssyk-4-grupp-id.");
    }

    [Fact]
    public async Task DeriveAsync_DoesNotPersistAnything_WhenCalled()
    {
        // ADR 0040 Beslut 4 — derivation persists NOTHING (the SavedSearch is
        // created downstream of the user's confirm). Row count is unchanged.
        var ct = TestContext.Current.CancellationToken;
        var before = await ConceptRowCountAsync(ct);
        var sut = NewDeriver();

        await sut.DeriveAsync("Advokat", ct);
        await sut.DeriveAsync("xyzzy qwerty", ct);

        var after = await ConceptRowCountAsync(ct);
        after.ShouldBe(before);
    }

    // =================================================================
    // (f) Tier 1 multi-signal — DeriveManyAsync over a UNION of source titles
    //     (Klas 2026-06-21, CTO Decision 6). The caller passes titles in PRIORITY
    //     order (education FIRST, then work history). Candidates from earlier titles
    //     rank first WITHIN a match kind; dedupe per ssyk-4 group; companies/schools
    //     self-filter; total cap = MaxCandidates (25). All against the REAL snapshot.
    // =================================================================

    // The ssyk-4 group "Mjukvaru- och systemutvecklare m.fl." — the GOLDEN target of
    // the Swedish education degree "Systemutvecklare" (it is not itself an exact
    // occupation-name label, but "Systemutvecklare/Programmerare" occ-name fg7B_yov_smw
    // → ssyk-4 DJh5_yyF_hEM, and the degree's stem overlaps it). Provenance re-derived
    // LIVE below (SystemutvecklareGroupAsync) so the magic id can never go stale.
    private const string SystemutvecklareGroupId = "DJh5_yyF_hEM";

    [Fact]
    public async Task DeriveManyAsync_KlasCareerChangerSignals_SurfacesEducationGroup_CompanyAndSchoolContributeNothing()
    {
        // Klas's exact career-changer case (load-bearing): the CV says
        // "Plasman — Operatör" (what they DID) but the current studies say
        // "Systemutvecklare .NET" / "NBI-Handelsakademin" (what they WANT). The union
        // must surface the systemutvecklare group (the desired-occupation signal),
        // while the company "Plasman" and the school "NBI-Handelsakademin" — neither an
        // occupation-name — contribute nothing on their own.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        var (expectedGroupId, expectedGroupLabel) = await SystemutvecklareGroupAsync(ct);

        var result = await sut.DeriveManyAsync(
            ["Systemutvecklare .NET", "NBI-Handelsakademin", "Plasman", "Operatör"], ct);

        // Title echo is the first non-blank source (parity with the single-title shape).
        result.Title.ShouldBe("Systemutvecklare .NET");
        result.Candidates.ShouldNotBeEmpty(
            "Career-changer-unionen ska ge minst en kandidat (utbildningssignalen).");
        // The systemutvecklare group surfaces (live-derived id/label — anti-stale).
        result.Candidates.ShouldContain(c => c.OccupationGroupConceptId == expectedGroupId);
        var hit = result.Candidates.First(c => c.OccupationGroupConceptId == expectedGroupId);
        hit.OccupationGroupLabel.ShouldBe(expectedGroupLabel);
        hit.MatchedOn.ShouldNotBeNullOrWhiteSpace(); // cited occupation-name span (§5)

        // The company and the school carry nothing of their own: every candidate must be
        // a group reachable from one of the two OCCUPATION strings, never a group that
        // only "Plasman" or "NBI-Handelsakademin" could have produced (they produce none).
        var companyGroups = await GroupsReachedByAsync("Plasman", ct);
        var schoolGroups = await GroupsReachedByAsync("NBI-Handelsakademin", ct);
        companyGroups.ShouldBeEmpty("'Plasman' (ett företag) är inget yrkesnamn.");
        schoolGroups.ShouldBeEmpty("'NBI-Handelsakademin' (en skola) är inget yrkesnamn.");

        // The diagnosed noise (Klas 2026-06-21): the bare experience title "Operatör"
        // used to fan out across 7 unrelated ssyk-4 groups (Fartygsbefäl, SOS-operatörer,
        // Maskinoperatörer, sekreterare, …). The group-spread gate now drops every one —
        // bare "Operatör" reaches NO group on its own. This is the living regression for
        // the whole fix: the career-changer's wanted (education) signal survives while
        // the work-history noise is filtered.
        var operatorGroups = await GroupsReachedByAsync("Operatör", ct);
        operatorGroups.ShouldBeEmpty(
            "Bara generiska 'Operatör' ska inte längre nå någon yrkesgrupp " +
            "(alla överlapp vilar på det scattered tokenet 'operatör', spread ≥ G).");
    }

    [Fact]
    public async Task DeriveManyAsync_EducationBeforeExperience_RanksEducationGroupFirstWithinSameKind()
    {
        // Education-first ordering (SourceOrder, CTO Decision 6.2). Both titles yield
        // STEMMED candidates via a DISTINCTIVE stem (so both survive the group-spread
        // gate): "Systemutvecklare" (index 0, education) and "Sjuksköterska" (index 1,
        // experience — spread 2 < G). Within the StemmedTokenOverlap run, the index-0
        // (education) systemutvecklare group must precede every sjuksköterska group.
        // (Re-pointed from "Operatör": its bare generic token now fans out across too
        // many groups and is correctly filtered to nothing — so it can no longer be the
        // experience probe.)
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        var (sysGroupId, _) = await SystemutvecklareGroupAsync(ct);
        var experienceGroups = await GroupsReachedByAsync("Sjuksköterska", ct);
        experienceGroups.ShouldNotBeEmpty(
            "'Sjuksköterska' (distinkt stam) ska ge minst en stammad grupp — om den " +
            "blir tom efter en snapshot-bump, peka om experiens-proben.");

        var result = await sut.DeriveManyAsync(["Systemutvecklare", "Sjuksköterska"], ct);

        var sysIndex = IndexOfGroup(result, sysGroupId);
        sysIndex.ShouldBeGreaterThanOrEqualTo(0,
            "Utbildningstiteln 'Systemutvecklare' ska ge systemutvecklar-gruppen.");
        // The systemutvecklare group (from index 0) precedes EVERY sjuksköterska group
        // (from index 1) — SourceOrder governs the order within the same match kind.
        foreach (var expGroupId in experienceGroups.Where(g => g != sysGroupId))
        {
            var expIndex = IndexOfGroup(result, expGroupId);
            if (expIndex >= 0)
                sysIndex.ShouldBeLessThan(expIndex,
                    $"Utbildningsgruppen (index 0) ska ranka före experiens-gruppen " +
                    $"'{expGroupId}' (index 1) inom samma MatchKind.");
        }
    }

    [Fact]
    public async Task DeriveManyAsync_TwoTitlesSameSsyk4Group_YieldsExactlyOneCandidate()
    {
        // Union + dedupe (CTO Decision 6.3): two source titles that both map to the SAME
        // ssyk-4 group produce ONE candidate for that group, not two. "Systemutvecklare"
        // and "Mjukvaruutvecklare" both resolve to DJh5_yyF_hEM (systemutvecklar-gruppen).
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        var (sysGroupId, _) = await SystemutvecklareGroupAsync(ct);

        var result = await sut.DeriveManyAsync(
            ["Systemutvecklare", "Mjukvaruutvecklare"], ct);

        var forGroup = result.Candidates
            .Count(c => c.OccupationGroupConceptId == sysGroupId);
        forGroup.ShouldBe(1,
            "Två titlar mot samma ssyk-4-grupp ska ge EXAKT en kandidat (dedupe, " +
            "starkaste evidensen behålls).");
        // And the whole list stays deduped per group (no group id appears twice).
        var distinct = result.Candidates
            .Select(c => c.OccupationGroupConceptId).Distinct().Count();
        distinct.ShouldBe(result.Candidates.Count);
    }

    [Fact]
    public async Task DeriveManyAsync_OnlyNonOccupationStrings_ReturnsEmptyCandidates()
    {
        // Non-occupation strings self-filter (CTO Decision 6.4): a company, a generic
        // brand, and a school are none of them occupation-name labels → empty union.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveManyAsync(
            ["Plasman", "Acme AB", "NBI-Handelsakademin"], ct);

        result.Candidates.ShouldBeEmpty(
            "Inget av strängarna är ett yrkesnamn → tom kandidatlista → manuellt val.");
    }

    [Fact]
    public async Task DeriveManyAsync_SingleElement_ReturnsSameCandidatesAsDeriveAsync()
    {
        // Single-element parity (CTO Decision 6.5): DeriveManyAsync([t]) ≡ DeriveAsync(t)
        // — DeriveAsync delegates to DeriveManyAsync([t]), so the surfaced candidate set
        // (group id + kind, in order) must be identical for a representative title.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        const string title = "Systemutvecklare";

        var many = await sut.DeriveManyAsync([title], ct);
        var single = await sut.DeriveAsync(title, ct);

        var manyKeys = many.Candidates
            .Select(c => (c.OccupationGroupConceptId, c.MatchKind)).ToList();
        var singleKeys = single.Candidates
            .Select(c => (c.OccupationGroupConceptId, c.MatchKind)).ToList();
        manyKeys.ShouldBe(singleKeys); // identical sequence, order included
        many.Title.ShouldBe(single.Title);
    }

    [Fact]
    public async Task DeriveManyAsync_EmptyInput_ReturnsEmptyCandidatesAndEmptyTitle_NoThrow()
    {
        // Empty input (CTO Decision 6.6): an empty list yields empty candidates and an
        // empty Title echo — never throws.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveManyAsync([], ct);

        result.Title.ShouldBe(string.Empty);
        result.Candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeriveManyAsync_AllBlankInput_ReturnsEmptyCandidatesAndEmptyTitle_NoThrow()
    {
        // Blank/whitespace input (CTO Decision 6.6): all-blank is treated as no input —
        // empty candidates, empty Title echo, no throw.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveManyAsync(["", "  "], ct);

        result.Title.ShouldBe(string.Empty);
        result.Candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeriveManyAsync_ManyMatchingTitles_NeverExceedsTotalCap()
    {
        // Total cap (CTO Decision 6.7): the union across MANY titles is still bounded by
        // MaxCandidates (25) — a long CV cannot fan out the taxonomy. Re-pointed from a
        // list of generic 1-token titles (now filtered to nothing — the test would pass
        // vacuously as 0 ≤ 25) to many DISTINCTIVE profession titles that genuinely
        // produce groups, so the cap is really stressed.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        string[] titles =
        [
            "Advokat", "Förskollärare", "Arbetsförmedlare", "Mjukvaruutvecklare",
            "Sjuksköterska", "Barnskötare", "Frisör", "Psykolog", "Tandläkare",
            "Elektriker", "Snickare", "Svetsare", "Kock", "Veterinär", "Bibliotekarie",
        ];

        var result = await sut.DeriveManyAsync(titles, ct);

        result.Candidates.ShouldNotBeEmpty(
            "Unionen av distinktiva professionstitlar ska ge kandidater (annars är " +
            "cap-stressen vakuös — peka om titlarna).");
        result.Candidates.Count.ShouldBeLessThanOrEqualTo(25,
            "Unionen över många titlar får aldrig överskrida " +
            "MaxCandidates (25) — bounded cap, DoS-/UX-skydd.");
        // Still deduped per group even at the cap.
        var distinct = result.Candidates
            .Select(c => c.OccupationGroupConceptId).Distinct().Count();
        distinct.ShouldBe(result.Candidates.Count);
    }

    // =================================================================
    // (g) Group-spread generic-token gate (Klas 2026-06-21, senior-cto-advisor
    //     Variant C). A stemmed overlap survives only if grounded on a DISTINCTIVE
    //     occupation-name token — one whose occupation-names stay within fewer than
    //     MaxGroupSpread (G=4) distinct ssyk-4 groups. Drops the diagnosed bare-
    //     "operatör" cross-group fan-out; keeps qualified titles + the education signal.
    // =================================================================

    // MUST mirror OccupationCodeDeriver.MaxGroupSpread (the gate drops spread >= G).
    private const int GroupSpreadCutoff = 4;

    [Fact]
    public async Task DeriveAsync_QualifiedCncOperator_StillMatches_WhileBareOperatorIsFiltered()
    {
        // The per-shared-token gate is fine-grained (Variant C over the coarser
        // whole-query Variant B): a QUALIFIED "CNC-operatör" survives on its distinctive
        // "cnc" token (spread ~2 < G) even though it also shares the generic "operatör"
        // (spread 7 >= G); the bare "Operatör", with only the generic token, reaches
        // nothing. This is exactly Klas's diagnosed noise removed without collateral.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var qualified = await sut.DeriveAsync("CNC-operatör", ct);
        qualified.Candidates.ShouldNotBeEmpty(
            "Kvalificerad 'CNC-operatör' (distinkt 'cnc'-stam) ska fortfarande matcha.");

        var bare = await sut.DeriveAsync("Operatör", ct);
        bare.Candidates.ShouldBeEmpty(
            "Bara generiska 'Operatör' (scattered token, spread ≥ G) ska filtreras bort.");
    }

    [Fact]
    public async Task GroupSpreadGate_AnchorsBracketTheThreshold_AgainstLiveSnapshot()
    {
        // Pin G inside the empty band against the LIVE seeded snapshot (no hardcoded
        // counts) so a snapshot bump that moved a token across G fails loud rather than
        // silently re-leaking. Reads the real per-lexeme group-spread the gate uses and
        // stems each anchor with the SAME analyzer the cache was built with — so the
        // lookup is consistent. Asserts both the far anchors (operatör/systemutveckl)
        // and the band edge (tekniker last-distinctive < G <= ingenjör first-generic).
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();
        var analyzer = new LocalTextAnalyzer(new SnowballStemmer());
        var spread = await sut.GetGroupSpreadAsync(ct);

        int SpreadOf(string word)
        {
            var lexemes = analyzer.ToLexemes(word, TextLanguage.Swedish).ToList();
            lexemes.Count.ShouldBe(1,
                $"'{word}' förväntas tokeniseras till exakt ETT lexem (band-ankare).");
            return spread.GetValueOrDefault(lexemes[0]);
        }

        // Live-measured group-spreads against the seeded snapshot (2026-06-21):
        //   distinctive (< G): systemutveckl 1, advokat 1, jurist 1, cnc 2,
        //                      sjuksköterska 2, utvecklare 2, lärare 3
        //   generic (>= G):    ingenjör 4, assistent 5, tekniker 5, operatör 7,
        //                      chef 8, projektledare 14, maskinoperatör 17
        // The empty band sits between lärare (3, last distinctive) and ingenjör
        // (4, first generic) — G = 4 cuts there.

        // Generic side — must be filtered (spread >= G). operatör is the diagnosed
        // noise; ingenjör is the first-generic band witness.
        SpreadOf("operatör").ShouldBeGreaterThanOrEqualTo(GroupSpreadCutoff,
            "'operatör' (diagnostiserad brus, spread ~7) ska ligga på generisk sida.");
        SpreadOf("maskinoperatör").ShouldBeGreaterThanOrEqualTo(GroupSpreadCutoff,
            "'maskinoperatör' (scattered komposit, spread ~17) ska vara generisk.");
        SpreadOf("ingenjör").ShouldBeGreaterThanOrEqualTo(GroupSpreadCutoff,
            "'ingenjör' (första generiska, spread ~4) ska ligga på/över bandet.");
        // Distinctive side — must survive (spread < G). systemutveckl is the wanted
        // education signal; lärare is the last-distinctive band witness.
        SpreadOf("systemutvecklare").ShouldBeLessThan(GroupSpreadCutoff,
            "'systemutveckl' (utbildningssignalen, spread ~1) ska vara distinkt.");
        SpreadOf("cnc").ShouldBeLessThan(GroupSpreadCutoff,
            "'cnc' (kvalificerare, spread ~2) ska vara distinkt.");
        SpreadOf("lärare").ShouldBeLessThan(GroupSpreadCutoff,
            "'lärare' (sista distinktiva, spread ~3) ska ligga under bandet.");
    }

    // ---------------------------------------------------------------
    // Helpers — live golden re-derivation + snapshot/frozen-map readers.
    // ---------------------------------------------------------------

    // Live-derives the systemutvecklare ssyk-4 group (id + current snapshot label) so
    // the goldens for the Tier 1 tests never go stale against a snapshot bump. Asserts
    // the constant id is still the live target.
    private async Task<(string GroupId, string GroupLabel)> SystemutvecklareGroupAsync(
        CancellationToken ct)
    {
        var taxonomy = new TaxonomyReadModel(ScopeFactory);
        var tree = await taxonomy.GetTreeAsync(ct);
        var label = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .FirstOrDefault(g => g.ConceptId == SystemutvecklareGroupId)?.Label;
        label.ShouldNotBeNull(
            $"Golden-gruppen '{SystemutvecklareGroupId}' (Mjukvaru- och " +
            "systemutvecklare m.fl.) ska finnas i snapshoten.");
        return (SystemutvecklareGroupId, label!);
    }

    // The set of ssyk-4 group ids a SINGLE title surfaces (via DeriveAsync against the
    // real deriver) — used to prove a company/school self-filters (empty set) and to
    // identify the operatör-only groups for the ordering assertion. The deriver is the
    // source of truth here (parity with the impl the SUT exercises), not a re-impl.
    private async Task<HashSet<string>> GroupsReachedByAsync(
        string title, CancellationToken ct)
    {
        var sut = NewDeriver();
        var result = await sut.DeriveAsync(title, ct);
        return result.Candidates
            .Select(c => c.OccupationGroupConceptId)
            .ToHashSet(StringComparer.Ordinal);
    }

    // First index of a candidate carrying the given ssyk-4 group id, or -1 if absent.
    private static int IndexOfGroup(OccupationDerivationResult result, string groupId)
    {
        for (var i = 0; i < result.Candidates.Count; i++)
        {
            if (result.Candidates[i].OccupationGroupConceptId == groupId)
                return i;
        }
        return -1;
    }

    // Re-derives the expected (ssyk-4 id, label) for a title that is expected to
    // be an EXACT occupation-name hit, straight from the seeded tree + frozen map
    // (the same data the deriver itself consumes) — so the golden never goes stale.
    private async Task<(string GroupId, string GroupLabel)?> DeriveExpectedGroupAsync(
        string title, CancellationToken ct)
    {
        var taxonomy = new TaxonomyReadModel(ScopeFactory);
        var tree = await taxonomy.GetTreeAsync(ct);

        var occName = tree.OccupationFields
            .SelectMany(f => f.Occupations)
            .FirstOrDefault(o => string.Equals(
                o.Label, title, StringComparison.OrdinalIgnoreCase));
        if (occName is null)
            return null;
        if (!_frozenMap.TryGetValue(occName.ConceptId, out var groupId))
            return null;

        var groupLabel = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .FirstOrDefault(g => g.ConceptId == groupId)?.Label;
        return groupLabel is null ? null : (groupId, groupLabel);
    }

    private async Task<HashSet<string>> SeededGroupIdsAsync(CancellationToken ct)
    {
        var taxonomy = new TaxonomyReadModel(ScopeFactory);
        var tree = await taxonomy.GetTreeAsync(ct);
        return tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .Select(g => g.ConceptId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<int> ConceptRowCountAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TaxonomyConcept>().CountAsync(ct);
    }

    // Reads the SAME frozen embedded resource the deriver loads (occupation-name
    // id → ssyk-4 id), via the Infrastructure assembly manifest stream. Used ONLY
    // to re-derive expected goldens — the deriver owns its own loader in prod.
    private static async Task<IReadOnlyDictionary<string, string>> ReadFrozenMapAsync()
    {
        var asm = typeof(TaxonomyReadModel).Assembly;
        await using var stream = asm.GetManifestResourceStream(FrozenMapResource);
        stream.ShouldNotBeNull(
            $"Frozen-map-resursen '{FrozenMapResource}' ska vara en " +
            "<EmbeddedResource> i Infrastructure-assemblyn (csproj LogicalName).");

        using var doc = await JsonDocument.ParseAsync(stream!);
        var mappings = doc.RootElement.GetProperty("mappings");
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in mappings.EnumerateObject())
        {
            dict[entry.Name] = entry.Value.GetString()!;
        }
        return dict;
    }
}
