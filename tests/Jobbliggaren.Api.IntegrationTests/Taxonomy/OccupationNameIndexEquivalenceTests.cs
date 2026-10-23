using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Taxonomy;

/// <summary>
/// The permanent guard on the shared occupation-name index (#2198, ADR 0180): over the real
/// seeded snapshot, its names and groups equal the committed snapshot joined to the frozen map,
/// computed by <see cref="OccupationNameOracle"/> along a path that shares no code with the index.
/// Both readers of the index (the deriver and the picker search) rest on this equality.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class OccupationNameIndexEquivalenceTests : IAsyncLifetime
{
    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    public OccupationNameIndexEquivalenceTests(SharedPostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        _provider = services.BuildServiceProvider();

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        var seeder = new TaxonomySnapshotSeeder(
            _provider.GetRequiredService<IServiceScopeFactory>(), env,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaxonomySnapshotSeeder>.Instance);
        await seeder.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DropDatabaseAsync(_connectionString);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetAsync_OverTheSeededSnapshot_EqualsTheSnapshotJoinedToTheFrozenMap()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new OccupationNameIndex(new TaxonomyReadModel(_provider.GetRequiredService<IServiceScopeFactory>()));

        var contents = await sut.GetAsync(ct);
        var expected = await OccupationNameOracle.LoadAsync();
        expected.Names.ShouldNotBeEmpty();
        expected.Groups.ShouldNotBeEmpty();

        contents.Names.Select(n => (n.NameLabel, n.GroupConceptId, n.GroupLabel))
            .ShouldBe(expected.Names.Select(n => (n.NameLabel, n.GroupConceptId, n.GroupLabel)));
        contents.Groups.Select(g => (g.ConceptId, g.Label)).ShouldBe(expected.Groups);
    }

    [Fact]
    public async Task GetAsync_CalledTwice_ReturnsTheSameCachedContents()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new OccupationNameIndex(new TaxonomyReadModel(_provider.GetRequiredService<IServiceScopeFactory>()));

        var first = await sut.GetAsync(ct);
        var second = await sut.GetAsync(ct);

        second.ShouldBeSameAs(first);
    }
}
