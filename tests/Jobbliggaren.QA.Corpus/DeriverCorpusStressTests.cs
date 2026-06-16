using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.QA.Corpus.Generation;
using Jobbliggaren.QA.Corpus.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Fas 4 STEG C, PR 2 — the deriver frontend (CTO Fork 1 = 1D). Runs the seeded title corpus
/// through the REAL <c>IOccupationCodeDeriver</c> (F4-3) against a real seeded taxonomy
/// (Testcontainers Postgres + the real frozen occupation-name→ssyk-4 map + real Swedish
/// Snowball) — the engine is NOT mocked. Self-contained fixture, mirroring
/// <c>OccupationCodeDeriverIntegrationTests</c>.
///
/// <para>Two GATING properties (CTO Fork 4/5 — the only things asserted; derivation hit-rate
/// is observe-only and reported in PR 4):</para>
/// <list type="number">
/// <item><b>Crash-safety = 100%</b> across every stratum (aggregating per-strata sweep, 5C).</item>
/// <item><b>The bearing invariant</b> (ADR 0040 Beslut 4): deriving the WHOLE edge corpus
/// creates ZERO <c>SavedSearch</c> rows and persists nothing — the runtime half of "no
/// SavedSearch is ever created without explicit user confirmation" (the structural half is the
/// arch-test <c>DerivedSavedSearchInvariantTests</c>). Fails HARD with an unambiguous message.</item>
/// </list>
///
/// <para><b>Anti-stale:</b> the title→ssyk-4 ground-truth fed to the generator is derived LIVE
/// from the seeded tree + the frozen map (same data the deriver consumes) — nothing hard-coded.</para>
/// </summary>
public sealed class DeriverCorpusStressTests : IAsyncLifetime
{
    // Same frozen migration-owned resource the deriver loads (ADR 0067 C2); read here ONLY to
    // build the live ground-truth pairs, never to drive the SUT (parity OccupationCodeDeriverIntegrationTests).
    private const string FrozenMapResource =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _provider = default!;
    private IReadOnlyDictionary<string, string> _frozenMap = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_postgres.GetConnectionString(),
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
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

    private IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    private async Task RunSeederAsync(CancellationToken ct)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        var seeder = new TaxonomySnapshotSeeder(
            ScopeFactory, env,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaxonomySnapshotSeeder>.Instance);
        await seeder.StartAsync(ct);
    }

    private OccupationCodeDeriver NewDeriver() =>
        new(new TaxonomyReadModel(ScopeFactory), new LocalTextAnalyzer(new SnowballStemmer()));

    // ===============================================================
    // GATE 1 — Crash-safety MUST be 100% across every stratum (CTO Fork 5 = 5C)
    // ===============================================================

    [Fact]
    public async Task DeriverCorpus_IsCrashSafe_AcrossEveryStratum()
    {
        var ct = TestContext.Current.CancellationToken;
        var corpus = new CorpusGenerator().GenerateTitleCorpus(await BuildGroundTruthAsync(ct));
        corpus.Count.ShouldBeGreaterThan(200, "the stress corpus must be non-trivial.");

        var deriver = NewDeriver();
        var outcomes = await CrashSweep.RunAsync(
            corpus,
            c => c.Label,
            c => c.Stratum,
            async (c, t) => _ = await deriver.DeriveAsync(c.Title, t),
            ct);

        var crashes = outcomes.Crashes();
        crashes.ShouldBeEmpty(
            "KRASCH-SÄKERHET BRUTEN — derivern kastade på: " +
            string.Join(", ", crashes.Select(x => $"{x.Label} ({x.ExceptionType})")));

        // Per-strata 100% (so an adversarial-class regression is pinpointed).
        foreach (var stratumGroup in outcomes.GroupBy(o => o.Stratum))
            stratumGroup.Count(o => o.Threw).ShouldBe(0,
                $"strata {stratumGroup.Key} ska vara 100% krasch-fri.");
    }

    // ===============================================================
    // GATE 2 — The bearing invariant (ADR 0040 Beslut 4), runtime half (CTO Fork 4 = 4C/4B)
    // ===============================================================

    [Fact]
    public async Task BearingInvariant_DerivingTheWholeCorpus_CreatesZeroSavedSearches()
    {
        var ct = TestContext.Current.CancellationToken;
        var corpus = new CorpusGenerator().GenerateTitleCorpus(await BuildGroundTruthAsync(ct));
        var deriver = NewDeriver();

        (await CountSavedSearchesAsync(ct)).ShouldBe(0, "baseline: greenfield DB has no SavedSearch.");
        var conceptsBefore = await ConceptRowCountAsync(ct);

        foreach (var c in corpus)
            _ = await deriver.DeriveAsync(c.Title, ct);

        var savedSearchesAfter = await CountSavedSearchesAsync(ct);
        savedSearchesAfter.ShouldBe(0,
            $"BÄRANDE INVARIANT BRUTEN (ADR 0040 Beslut 4): {savedSearchesAfter} SavedSearch skapades av " +
            "derivering över hela korpusen — derivering FÖRESLÅR, men en SavedSearch får ALDRIG skapas " +
            "utan explicit användarbekräftelse.");

        (await ConceptRowCountAsync(ct)).ShouldBe(conceptsBefore,
            "derivern är read-only och får inte persistera något (paritet DeriveAsync_DoesNotPersistAnything).");
    }

    // ===============================================================
    // Wiring anchor — the harness exercises REAL derivation, not a no-op (known golden)
    // ===============================================================

    [Fact]
    public async Task Deriver_IsWiredToRealTaxonomy_ViaKnownGolden()
    {
        // A guaranteed-true fact (not a corpus-wide hit-rate, which is observe-only): the
        // golden "Advokat" → ssyk-4 q8wL_kdi_WaW must resolve, proving the harness runs the
        // REAL deriver against REAL seeded data. Provenance: OccupationCodeDeriverIntegrationTests.
        var ct = TestContext.Current.CancellationToken;
        var result = await NewDeriver().DeriveAsync("Advokat", ct);

        result.Candidates.ShouldContain(c =>
            c.OccupationGroupConceptId == "q8wL_kdi_WaW"
            && c.MatchKind == OccupationMatchKind.ExactOccupationName);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OccupationGroundTruth>> BuildGroundTruthAsync(CancellationToken ct)
    {
        var tree = await new TaxonomyReadModel(ScopeFactory).GetTreeAsync(ct);
        var occLabelById = tree.OccupationFields
            .SelectMany(f => f.Occupations)
            .ToDictionary(o => o.ConceptId, o => o.Label, StringComparer.Ordinal);
        var groupIds = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .Select(g => g.ConceptId)
            .ToHashSet(StringComparer.Ordinal);

        var pairs = new List<OccupationGroundTruth>();
        foreach (var (occId, groupId) in _frozenMap)
            if (occLabelById.TryGetValue(occId, out var label) && groupIds.Contains(groupId))
                pairs.Add(new OccupationGroundTruth(label, groupId));

        pairs.Count.ShouldBeGreaterThan(1000,
            "the frozen map should yield the full mapped occupation-name→ssyk-4 ground-truth (~2153).");
        return pairs;
    }

    private async Task<int> CountSavedSearchesAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // IgnoreQueryFilters so a soft-deleted row would still count — "0 created" means 0 total.
        return await db.Set<SavedSearch>().IgnoreQueryFilters().CountAsync(ct);
    }

    private async Task<int> ConceptRowCountAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TaxonomyConcept>().CountAsync(ct);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadFrozenMapAsync()
    {
        var asm = typeof(TaxonomyReadModel).Assembly;
        await using var stream = asm.GetManifestResourceStream(FrozenMapResource);
        stream.ShouldNotBeNull($"Frozen-map-resursen '{FrozenMapResource}' ska vara en EmbeddedResource.");

        using var doc = await JsonDocument.ParseAsync(stream!);
        var mappings = doc.RootElement.GetProperty("mappings");
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in mappings.EnumerateObject())
            dict[entry.Name] = entry.Value.GetString()!;
        return dict;
    }
}
