using System.Text.Json;
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

    [Fact]
    public async Task DeriveAsync_SameTitleTwice_ReturnsIdenticalOrderedResult()
    {
        // Determinism (CTO Decision 3, Invariant-grade). "chef" is a high-overlap
        // 1-token title (12 occ-name hits → 8 distinct ssyk-4 groups in the live
        // data) — a strong determinism probe across many stemmed candidates.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var first = await sut.DeriveAsync("chef", ct);
        var second = await sut.DeriveAsync("chef", ct);

        var firstKeys = first.Candidates
            .Select(c => (c.OccupationGroupConceptId, c.MatchKind)).ToList();
        var secondKeys = second.Candidates
            .Select(c => (c.OccupationGroupConceptId, c.MatchKind)).ToList();

        // Identical sequence (order included) — not just set-equal.
        secondKeys.ShouldBe(firstKeys);
    }

    [Fact]
    public async Task DeriveAsync_Candidates_AreDeterministicallyOrdered()
    {
        // The ordering rule (CTO Decision 3, parity with TaxonomyReadModel's
        // OrderBy(Kind).ThenBy(Label, Ordinal)): MatchKind (Exact before Stemmed)
        // → … → OccupationGroupLabel Ordinal asc. We assert the two stable,
        // observable invariants without pinning the impl's internal overlap score:
        //   (i) all Exact candidates precede all Stemmed candidates;
        //   (ii) within a single MatchKind run, labels are Ordinal non-decreasing.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync("chef", ct);
        var candidates = result.Candidates;
        candidates.Count.ShouldBeGreaterThan(1, "'chef' ska ge flera kandidater.");

        // (i) Exact (enum value 0) before Stemmed (enum value 1) — the MatchKind
        // sequence must be non-decreasing by enum value.
        var kindSequence = candidates.Select(c => (int)c.MatchKind).ToList();
        kindSequence.ShouldBe(kindSequence.OrderBy(k => k).ToList(),
            "Exact-kandidater ska sorteras före Stemmed-kandidater.");

        // (ii) Within each MatchKind run, labels are Ordinal non-decreasing.
        foreach (var run in candidates.GroupBy(c => c.MatchKind))
        {
            var labels = run.Select(c => c.OccupationGroupLabel).ToList();
            labels.ShouldBe(labels.OrderBy(l => l, StringComparer.Ordinal).ToList(),
                $"Labels inom MatchKind={run.Key} ska vara Ordinal-sorterade.");
        }
    }

    // =================================================================
    // (e) Bounded — a high-overlap title cannot return an unbounded list
    // =================================================================

    [Fact]
    public async Task DeriveAsync_HighOverlapTitle_ReturnsBoundedDeduplicatedCandidates()
    {
        // Bounded candidate cap (CTO/architect §2 sub-axis 6): a 1-token title
        // ("chef") must not return hundreds. We assert the result is BOUNDED and
        // DEDUPED by ssyk-4 group id (not an exact magic number — impl const).
        // Live data: "chef" touches 8 distinct mapped groups, so a sane bound is
        // comfortably below the ~400-group ceiling; we assert a generous upper
        // bound that any reasonable cap satisfies, plus strict dedupe.
        var ct = TestContext.Current.CancellationToken;
        var sut = NewDeriver();

        var result = await sut.DeriveAsync("chef", ct);

        result.Candidates.ShouldNotBeEmpty();
        // Bounded: never the whole taxonomy. 50 is a generous ceiling — the impl
        // cap is expected to be far smaller; this only falsifies an unbounded fan-out.
        result.Candidates.Count.ShouldBeLessThanOrEqualTo(50,
            "En 1-token-titel får inte returnera en obegränsad kandidat-lista " +
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

    // ---------------------------------------------------------------
    // Helpers — live golden re-derivation + snapshot/frozen-map readers.
    // ---------------------------------------------------------------

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
