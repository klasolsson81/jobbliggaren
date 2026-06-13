using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace JobbPilot.Api.IntegrationTests.Taxonomy;

/// <summary>
/// ADR 0043 — TaxonomyReadModel + TaxonomySnapshotSeeder mot riktig Postgres
/// (Testcontainers, ALDRIG EF-InMemory: query-filter/sortering/idempotens-
/// transaktion + advisory-lock måste verifieras mot relationell motor).
/// Kör seedern direkt (Test-env grace-period) och exercerar porten.
/// Self-contained fixture (egen container) så idempotens/version-bump kan
/// styras deterministiskt.
/// </summary>
public sealed class TaxonomyReadModelIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _provider = default!;

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
        // F6 P4 — pg_trgm krävs av F6P4aJobAdTrigramIndexes (se ApiFactory).
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();
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
            NullLogger<TaxonomySnapshotSeeder>.Instance);
        await seeder.StartAsync(ct);
    }

    private async Task<int> ConceptRowCountAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<TaxonomyConcept>().CountAsync(ct);
    }

    [Fact]
    public async Task GetTreeAsync_ShouldReturnRegionsAndNestedOccupations_WhenSnapshotSeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var tree = await sut.GetTreeAsync(ct);

        tree.Regions.ShouldNotBeEmpty();
        tree.OccupationFields.ShouldNotBeEmpty();
        // Yrken är nästlade per yrkesområde (parent_concept_id-gruppering).
        tree.OccupationFields.ShouldContain(f => f.Occupations.Count > 0);
        tree.OccupationFields.SelectMany(f => f.Occupations)
            .ShouldAllBe(o => !string.IsNullOrWhiteSpace(o.ConceptId));
    }

    [Fact]
    public async Task GetTreeAsync_ShouldSortRegionsAndOccupationsByLabelOrdinal_WhenSeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var tree = await sut.GetTreeAsync(ct);

        var regionLabels = tree.Regions.Select(r => r.Label).ToList();
        regionLabels.ShouldBe(
            regionLabels.OrderBy(l => l, StringComparer.Ordinal).ToList());

        var fieldLabels = tree.OccupationFields.Select(f => f.Label).ToList();
        fieldLabels.ShouldBe(
            fieldLabels.OrderBy(l => l, StringComparer.Ordinal).ToList());

        foreach (var field in tree.OccupationFields)
        {
            var occLabels = field.Occupations.Select(o => o.Label).ToList();
            occLabels.ShouldBe(
                occLabels.OrderBy(l => l, StringComparer.Ordinal).ToList());
        }
    }

    [Fact]
    public async Task ResolveLabelsAsync_ShouldReturnLabelForKnownId_WhenSeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);
        var tree = await sut.GetTreeAsync(ct);
        var known = tree.Regions[0];

        var result = await sut.ResolveLabelsAsync([known.ConceptId], ct);

        var row = result.ShouldHaveSingleItem();
        row.ConceptId.ShouldBe(known.ConceptId);
        row.Label.ShouldBe(known.Label);
    }

    [Fact]
    public async Task ResolveLabelsAsync_ShouldReturnFallbackNotThrow_WhenIdUnknown()
    {
        // Kärninvariant (ADR 0043 graceful degradation): okänt concept-id
        // → "Okänd kod (<id>)", ALDRIG throw/null. Stale snapshot kraschar inte.
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var result = await sut.ResolveLabelsAsync(["definitivt-okand-99"], ct);

        var row = result.ShouldHaveSingleItem();
        row.ConceptId.ShouldBe("definitivt-okand-99");
        row.Label.ShouldBe("Okänd kod (definitivt-okand-99)");
    }

    [Fact]
    public async Task ResolveLabelsAsync_ShouldResolveMixedKnownAndUnknownInOrder_WhenSeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);
        var tree = await sut.GetTreeAsync(ct);
        var known = tree.Regions[0];

        var result = await sut.ResolveLabelsAsync(
            [known.ConceptId, "saknas-abc", known.ConceptId], ct);

        result.Count.ShouldBe(3);
        result[0].Label.ShouldBe(known.Label);
        result[1].Label.ShouldBe("Okänd kod (saknas-abc)");
        result[2].Label.ShouldBe(known.Label);
    }

    [Fact]
    public async Task GetTreeAsync_ShouldReadDatabaseOnce_WhenCalledTwice()
    {
        // Lat in-memory-cache: snapshot-tabellen läses EN gång per process.
        // Bevis: töm tabellen efter första anropet — andra anropet returnerar
        // fortfarande cachat träd (skulle vara tomt vid per-anrop-DB-läsning).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var first = await sut.GetTreeAsync(ct);
        first.Regions.ShouldNotBeEmpty();

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<TaxonomyConcept>().ExecuteDeleteAsync(ct);
        }

        var second = await sut.GetTreeAsync(ct);
        second.Regions.Count.ShouldBe(first.Regions.Count);
        second.ShouldBeSameAs(first); // samma cachade instans
    }

    [Fact]
    public async Task Seeder_ShouldPersistMunicipalityRowsWithRegionParent_WhenSnapshotSeeded()
    {
        // Fas B1 (Platsbanken sök-paritet, Klass 1): nya Kind=Municipality-rader
        // ska landa i taxonomy_concepts med ParentConceptId = region-concept-id.
        // Verifierar att seederns utökade MapRows persisterar mot riktig Postgres
        // (string-konvertering av enum + parent-relation).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var municipalities = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == TaxonomyConceptKind.Municipality)
            .ToListAsync(ct);

        municipalities.ShouldNotBeEmpty();
        municipalities.ShouldAllBe(m => m.ParentConceptId != null);

        // Varje kommuns parent ska vara ett seedat Region-concept-id (1:1-relation).
        var regionIds = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == TaxonomyConceptKind.Region)
            .Select(c => c.ConceptId)
            .ToListAsync(ct);
        var regionIdSet = regionIds.ToHashSet();
        municipalities.ShouldAllBe(m => regionIdSet.Contains(m.ParentConceptId!));
    }

    [Fact]
    public async Task Seeder_ShouldPersistOccupationGroupRowsWithFieldParent_WhenSnapshotSeeded()
    {
        // Fas B1: nya Kind=OccupationGroup-rader ska landa med ParentConceptId =
        // yrkesområdets concept-id (ssyk-level-4→occupation-field 1:1).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var groups = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == TaxonomyConceptKind.OccupationGroup)
            .ToListAsync(ct);

        groups.ShouldNotBeEmpty();
        groups.ShouldAllBe(g => g.ParentConceptId != null);

        var fieldIds = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == TaxonomyConceptKind.OccupationField)
            .Select(c => c.ConceptId)
            .ToListAsync(ct);
        var fieldIdSet = fieldIds.ToHashSet();
        groups.ShouldAllBe(g => fieldIdSet.Contains(g.ParentConceptId!));
    }

    [Fact]
    public async Task GetTreeAsync_ShouldNestMunicipalitiesUnderRegion_WhenSnapshotSeeded()
    {
        // C1 (ADR 0067 Platsbanken sök-paritet) — additiv kaskad: kommun-DTOs
        // nästlas under rätt Region (parent_concept_id-gruppering, 1:1).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var tree = await sut.GetTreeAsync(ct);

        // Minst ett län ska ha underordnade kommuner.
        tree.Regions.ShouldContain(r => r.Municipalities.Count > 0);
        tree.Regions.SelectMany(r => r.Municipalities)
            .ShouldAllBe(m => !string.IsNullOrWhiteSpace(m.ConceptId)
                              && !string.IsNullOrWhiteSpace(m.Label));
    }

    [Fact]
    public async Task GetTreeAsync_ShouldNestOccupationGroupsUnderOccupationField_WhenSnapshotSeeded()
    {
        // C1 — yrkesgrupp-DTOs (ssyk-level-4) nästlas under rätt yrkesområde.
        // Occupations (occupation-name) BEHÅLLS parallellt (recall-substrat).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var tree = await sut.GetTreeAsync(ct);

        tree.OccupationFields.ShouldContain(f => f.OccupationGroups.Count > 0);
        tree.OccupationFields.SelectMany(f => f.OccupationGroups)
            .ShouldAllBe(g => !string.IsNullOrWhiteSpace(g.ConceptId)
                              && !string.IsNullOrWhiteSpace(g.Label));

        // Occupations får ej tappas av kaskad-tillägget (additiv, ej ersättande).
        tree.OccupationFields.ShouldContain(f => f.Occupations.Count > 0);
    }

    [Fact]
    public async Task ResolveLabelsAsync_ShouldResolveMunicipalityAndOccupationGroupLabels_WhenSeeded()
    {
        // C1 — reverse-lookup måste täcka ALLA Kinds (labelByConceptId byggs över
        // hela snapshoten). Hämta ett kommun- + ett yrkesgrupp-concept-id ur
        // trädet och verifiera att de resolvar till riktiga namn (ej fallback).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);
        var tree = await sut.GetTreeAsync(ct);

        var knownMunicipality = tree.Regions
            .SelectMany(r => r.Municipalities).First();
        var knownGroup = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups).First();

        var result = await sut.ResolveLabelsAsync(
            [knownMunicipality.ConceptId, knownGroup.ConceptId], ct);

        result.Count.ShouldBe(2);
        result[0].Label.ShouldBe(knownMunicipality.Label);
        result[0].Label.ShouldNotStartWith("Okänd kod");
        result[1].Label.ShouldBe(knownGroup.Label);
        result[1].Label.ShouldNotStartWith("Okänd kod");
    }

    [Fact]
    public async Task GetTreeAsync_ShouldExposeEmploymentTypesAndWorktimeExtents_WhenKlass2Seeded()
    {
        // Fas E Klass 2 (ADR 0043-amendment 2026-06-13) — platta, föräldralösa
        // dimensioner exponeras som topp-nivå-listor i trädet (ingen syntetisk
        // rot). Frusen embedded källa (klass2-taxonomy.json) seedas av samma
        // seeder. Mot riktig Postgres (ALDRIG InMemory — enum→string + read-model-
        // projektion måste verifieras relationellt).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var tree = await sut.GetTreeAsync(ct);

        // Frusen mängd: 8 anställningsformer + 2 omfattningar.
        tree.EmploymentTypes.Count.ShouldBe(8);
        tree.WorktimeExtents.Count.ShouldBe(2);
        tree.EmploymentTypes.ShouldAllBe(e =>
            !string.IsNullOrWhiteSpace(e.ConceptId)
            && !string.IsNullOrWhiteSpace(e.Label));
        tree.WorktimeExtents.ShouldAllBe(w =>
            !string.IsNullOrWhiteSpace(w.ConceptId)
            && !string.IsNullOrWhiteSpace(w.Label));

        // Spot-check kända corpus-värden.
        tree.WorktimeExtents.ShouldContain(w =>
            w.ConceptId == "6YE1_gAC_R2G" && w.Label == "Heltid");
        tree.EmploymentTypes.ShouldContain(e =>
            e.ConceptId == "PFZr_Syz_cUq" && e.Label == "Vanlig anställning");

        // Label Ordinal-sortering (konsekvent läs-modell, samma som övriga dims).
        var employmentLabels = tree.EmploymentTypes.Select(e => e.Label).ToList();
        employmentLabels.ShouldBe(
            employmentLabels.OrderBy(l => l, StringComparer.Ordinal).ToList());
        var worktimeLabels = tree.WorktimeExtents.Select(w => w.Label).ToList();
        worktimeLabels.ShouldBe(
            worktimeLabels.OrderBy(l => l, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Seeder_ShouldPersistKlass2RowsWithoutParent_WhenSnapshotSeeded()
    {
        // Klass 2-rader landar i taxonomy_concepts med Kind=EmploymentType/
        // WorktimeExtent och ParentConceptId = null (platta/föräldralösa, till
        // skillnad mot kommun/yrkesgrupp). Verifierar enum→string-konvertering
        // mot riktig Postgres.
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var employmentTypes = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == TaxonomyConceptKind.EmploymentType)
            .ToListAsync(ct);
        var worktimeExtents = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == TaxonomyConceptKind.WorktimeExtent)
            .ToListAsync(ct);

        employmentTypes.Count.ShouldBe(8);
        worktimeExtents.Count.ShouldBe(2);
        employmentTypes.ShouldAllBe(e => e.ParentConceptId == null);
        worktimeExtents.ShouldAllBe(w => w.ParentConceptId == null);
    }

    [Fact]
    public async Task ResolveLabelsAsync_ShouldResolveKlass2ConceptId_WhenSeeded()
    {
        // HÖGT VÄRDE (PR-direktiv): kind-agnostisk reverse-lookup måste täcka
        // Klass 2 utan resolver-ändring (toolbar-chips + recent-/saved-search-
        // labels). Ett Klass 2-concept-id ska resolva till sitt riktiga namn,
        // ALDRIG fallback "Okänd kod".
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var sut = new TaxonomyReadModel(ScopeFactory);

        var result = await sut.ResolveLabelsAsync(
            ["6YE1_gAC_R2G", "PFZr_Syz_cUq"], ct);

        result.Count.ShouldBe(2);
        result[0].ConceptId.ShouldBe("6YE1_gAC_R2G");
        result[0].Label.ShouldBe("Heltid");
        result[0].Label.ShouldNotStartWith("Okänd kod");
        result[1].ConceptId.ShouldBe("PFZr_Syz_cUq");
        result[1].Label.ShouldBe("Vanlig anställning");
        result[1].Label.ShouldNotStartWith("Okänd kod");
    }

    [Fact]
    public async Task Seeder_ShouldBeIdempotent_WhenRunTwiceWithSameVersion()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunSeederAsync(ct);
        var countAfterFirst = await ConceptRowCountAsync(ct);
        countAfterFirst.ShouldBeGreaterThan(0);

        await RunSeederAsync(ct); // andra körningen: version matchar → skip
        var countAfterSecond = await ConceptRowCountAsync(ct);

        countAfterSecond.ShouldBe(countAfterFirst);
    }

    [Fact]
    public async Task Seeder_ShouldReSeed_WhenMetaVersionDiffersFromSnapshot()
    {
        // Version-bump-simulering: efter första seed sätts meta-versionen
        // till ett gammalt värde → nästa körning ska re-seeda (delete+insert).
        var ct = TestContext.Current.CancellationToken;
        await RunSeederAsync(ct);
        var baseline = await ConceptRowCountAsync(ct);

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var meta = await db.Set<TaxonomySnapshotMeta>().FirstAsync(ct);
            meta.TaxonomyVersion = "0-stale";
            // Lägg en falsk rad som ska försvinna vid re-seed.
            db.Set<TaxonomyConcept>().Add(new TaxonomyConcept
            {
                ConceptId = "stale-row-1",
                Kind = TaxonomyConceptKind.Region,
                Label = "Stale",
            });
            await db.SaveChangesAsync(ct);
        }

        await RunSeederAsync(ct); // meta-version != snapshot → re-seed

        var afterReseed = await ConceptRowCountAsync(ct);
        afterReseed.ShouldBe(baseline); // falsk rad borttagen, snapshot återställd

        using var verifyScope = _provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Set<TaxonomyConcept>()
            .AnyAsync(c => c.ConceptId == "stale-row-1", ct)).ShouldBeFalse();
    }
}
