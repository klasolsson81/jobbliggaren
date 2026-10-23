using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.OccupationDerivation;

/// <summary>
/// One-off characterization test for #2198 PR 1 (Feathers 2004): it pins the deriver's whole
/// output over every mapped occupation-name and every head word as one hash, so extracting the
/// shared occupation-name index cannot change what the deriver answers. A later commit in the
/// same PR deletes it; the permanent guard is the index-equivalence test.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class OccupationCodeDeriverCharacterizationTests : IAsyncLifetime
{
    private const string FrozenMapResource =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    private const string ExpectedHash = "68749D8B89777BBDE3E6286F259F0DE7EAB180410DD5498CAD42BA0A0BADC00B";

    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    public OccupationCodeDeriverCharacterizationTests(SharedPostgresFixture postgres) => _postgres = postgres;

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
    public async Task DeriveAsync_OverEveryMappedNameAndHeadWord_IsUnchangedByTheIndexExtraction()
    {
        var ct = TestContext.Current.CancellationToken;
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var taxonomy = new TaxonomyReadModel(scopeFactory);
        var sut = new OccupationCodeDeriver(
            new OccupationNameIndex(taxonomy), new LocalTextAnalyzer(new SnowballStemmer()));

        var frozenMap = await ReadFrozenMapAsync();
        var tree = await taxonomy.GetTreeAsync(ct);
        var mappedLabels = tree.OccupationFields
            .SelectMany(f => f.Occupations)
            .Where(o => frozenMap.ContainsKey(o.ConceptId))
            .Select(o => o.Label)
            .ToList();
        var headWords = mappedLabels
            .Select(l => l.Normalize(NormalizationForm.FormC).ToLowerInvariant()
                .Split(HeadWordSeparators, StringSplitOptions.RemoveEmptyEntries)[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        foreach (var title in mappedLabels.Concat(headWords))
        {
            var result = await sut.DeriveAsync(title, ct);
            builder.Append(result.Title).Append('\u001f');
            foreach (var c in result.Candidates)
            {
                builder.Append(c.OccupationGroupConceptId).Append('|')
                    .Append(c.OccupationGroupLabel).Append('|')
                    .Append(c.MatchKind).Append('|')
                    .Append(c.MatchedOn).Append('\u001e');
            }
            builder.Append('\n');
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        mappedLabels.Count.ShouldBe(2153);
        hash.ShouldBe(ExpectedHash);
    }

    private static readonly char[] HeadWordSeparators =
        [' ', ',', '/', '(', ')', '.', ':', '&'];

    private static async Task<IReadOnlyDictionary<string, string>> ReadFrozenMapAsync()
    {
        var asm = typeof(TaxonomyReadModel).Assembly;
        await using var stream = asm.GetManifestResourceStream(FrozenMapResource);
        stream.ShouldNotBeNull();
        using var doc = await JsonDocument.ParseAsync(stream!);
        return doc.RootElement.GetProperty("mappings").EnumerateObject()
            .ToDictionary(e => e.Name, e => e.Value.GetString()!, StringComparer.Ordinal);
    }
}
