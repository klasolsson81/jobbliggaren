using JobbPilot.Application.JobAds.Abstractions;
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
/// ADR 0067 Beslut 5a (Fas D1) — TaxonomyReadModel.SuggestByPrefixAsync mot
/// riktig Postgres (Testcontainers, ALDRIG EF-InMemory: query-filter/sortering/
/// idempotens-transaktion + advisory-lock i seedern måste verifieras mot
/// relationell motor). Self-contained fixture (egen container) speglar
/// TaxonomyReadModelIntegrationTests så snapshoten kan styras deterministiskt;
/// prefix-scanen är ren in-memory över den cachade snapshoten.
/// <para>
/// Verifierar: case-insensitiv prefix-match mot Län/Kommun/Yrkesområde/
/// Yrkesgrupp; occupation-name (Occupation-kind) returneras ALDRIG; limit-cap;
/// ordning (Kind enum → Label); korrekt Kind-mappning (Region→Region etc.).
/// </para>
/// </summary>
public sealed class TaxonomyReadModelSuggestTests : IAsyncLifetime
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
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();

        await RunSeederAsync(CancellationToken.None);
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

    // Hämtar en label av angiven Kind direkt ur snapshot-tabellen (oberoende av
    // SUT) så testen kan derivera ett verkligt prefix utan hårdkodad svensk data.
    private async Task<string> SampleLabelAsync(
        TaxonomyConceptKind kind, CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var label = await db.Set<TaxonomyConcept>()
            .Where(c => c.Kind == kind)
            .OrderBy(c => c.Label)
            .Select(c => c.Label)
            .FirstOrDefaultAsync(ct);
        label.ShouldNotBeNull($"Snapshot saknar {kind}-rad — kan ej derivera prefix.");
        return label!;
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldMatchRegionLabel_WhenPrefixMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);
        var regionLabel = await SampleLabelAsync(TaxonomyConceptKind.Region, ct);
        var prefix = regionLabel[..Math.Min(4, regionLabel.Length)];

        var result = await sut.SuggestByPrefixAsync(prefix, 50, ct);

        result.ShouldContain(s =>
            s.Kind == SuggestionKind.Region && s.Label == regionLabel);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldMatchMunicipalityLabel_WhenPrefixMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);
        var label = await SampleLabelAsync(TaxonomyConceptKind.Municipality, ct);
        var prefix = label[..Math.Min(4, label.Length)];

        var result = await sut.SuggestByPrefixAsync(prefix, 100, ct);

        result.ShouldContain(s =>
            s.Kind == SuggestionKind.Municipality && s.Label == label);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldMatchOccupationFieldLabel_WhenPrefixMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);
        var label = await SampleLabelAsync(TaxonomyConceptKind.OccupationField, ct);
        var prefix = label[..Math.Min(4, label.Length)];

        var result = await sut.SuggestByPrefixAsync(prefix, 100, ct);

        result.ShouldContain(s =>
            s.Kind == SuggestionKind.OccupationField && s.Label == label);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldMatchOccupationGroupLabel_WhenPrefixMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);
        var label = await SampleLabelAsync(TaxonomyConceptKind.OccupationGroup, ct);
        var prefix = label[..Math.Min(4, label.Length)];

        var result = await sut.SuggestByPrefixAsync(prefix, 100, ct);

        result.ShouldContain(s =>
            s.Kind == SuggestionKind.OccupationGroup && s.Label == label);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldBeCaseInsensitive_WhenPrefixCaseDiffers()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);
        var label = await SampleLabelAsync(TaxonomyConceptKind.Region, ct);
        var prefix = label[..Math.Min(4, label.Length)];

        // Gement + versalt prefix måste ge samma träff (OrdinalIgnoreCase).
        var lower = await sut.SuggestByPrefixAsync(prefix.ToLowerInvariant(), 50, ct);
        var upper = await sut.SuggestByPrefixAsync(prefix.ToUpperInvariant(), 50, ct);

        lower.ShouldContain(s => s.Label == label);
        upper.ShouldContain(s => s.Label == label);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldNeverReturnOccupationName_WhenOccupationLabelMatches()
    {
        // KÄRNAN (VAL 4): occupation-name (Occupation-kind) saknar filter-dimension
        // → får ALDRIG dyka upp i suggest. Härled ett prefix från en faktisk
        // occupation-name-label och bevisa att ingen träff returneras för den.
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);
        var occupationLabel = await SampleLabelAsync(TaxonomyConceptKind.Occupation, ct);
        var prefix = occupationLabel[..Math.Min(4, occupationLabel.Length)];

        var result = await sut.SuggestByPrefixAsync(prefix, 200, ct);

        // SuggestionKind har ingen Occupation-medlem; verifiera även att den exakta
        // occupation-name-labeln inte läcker via någon annan Kind.
        result.ShouldNotContain(s => s.Label == occupationLabel);
        // Ingen suggestable-rad får vara en occupation-name (alla returnerade Kinds
        // är filtrerbara dimensioner).
        result.ShouldAllBe(s =>
            s.Kind == SuggestionKind.Region
            || s.Kind == SuggestionKind.Municipality
            || s.Kind == SuggestionKind.OccupationField
            || s.Kind == SuggestionKind.OccupationGroup);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldRespectLimitCap_WhenManyMatch()
    {
        // Tom prefix matchar allt (StartsWith("") == true) → limit cappar.
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);

        var result = await sut.SuggestByPrefixAsync(string.Empty, 5, ct);

        result.Count.ShouldBe(5);
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldOrderByKindThenLabel_WhenManyMatch()
    {
        // Deterministisk ordning: Kind enum-ordning (Title < Region < Municipality
        // < OccupationField < OccupationGroup), sedan Label OrdinalIgnoreCase.
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);

        var result = await sut.SuggestByPrefixAsync(string.Empty, 1000, ct);

        result.Count.ShouldBeGreaterThan(1);
        for (var i = 1; i < result.Count; i++)
        {
            var prev = result[i - 1];
            var curr = result[i];
            if (prev.Kind == curr.Kind)
            {
                // Inom samma Kind: Label icke-fallande (OrdinalIgnoreCase).
                string.Compare(prev.Label, curr.Label,
                    StringComparison.OrdinalIgnoreCase).ShouldBeLessThanOrEqualTo(0);
            }
            else
            {
                // Annars: Kind strikt stigande (enum-ordning).
                ((int)prev.Kind).ShouldBeLessThan((int)curr.Kind);
            }
        }
    }

    [Fact]
    public async Task SuggestByPrefixAsync_ShouldReturnEmpty_WhenNoLabelMatchesPrefix()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new TaxonomyReadModel(ScopeFactory);

        var result = await sut.SuggestByPrefixAsync("zzzz-definitivt-ingen-traff", 50, ct);

        result.ShouldBeEmpty();
    }
}
