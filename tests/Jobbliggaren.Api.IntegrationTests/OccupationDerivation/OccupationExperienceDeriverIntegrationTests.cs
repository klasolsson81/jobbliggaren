using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
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
/// ADR 0079-amendment (exp-per-occ PR-2) — the import-time per-occupation experience attribution
/// against the REAL seeded taxonomy + the REAL OccupationCodeDeriver (Testcontainers, never
/// EF-InMemory — parity OccupationCodeDeriverIntegrationTests). Proves the load-bearing join: the
/// per-entry re-derivation (DeriveManyAsync over the entry's Title+Organization) reproduces the
/// SAME ssyk-4 group the import union pass would, so the parsed period's year span attributes to
/// the right group. The attribution arithmetic + merged-union are unit-tested with a mocked
/// deriver (OccupationExperienceDeriverTests); THIS proves it works end-to-end on live data.
///
/// GOLDEN PROVENANCE (re-using the verified pairs from OccupationCodeDeriverIntegrationTests):
///   • "Advokat"           → ssyk-4 q8wL_kdi_WaW "Advokater"
///   • "Mjukvaruutvecklare" → ssyk-4 DJh5_yyF_hEM "Mjukvaru- och systemutvecklare m.fl."
/// </summary>
public sealed class OccupationExperienceDeriverIntegrationTests : IAsyncLifetime
{
    private const string AdvokatGroup = "q8wL_kdi_WaW";
    private const string MjukvaraGroup = "DJh5_yyF_hEM";

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider _provider = default!;

    // Fixed clock so an ongoing role's "present" resolves deterministically to 2026.
    private static readonly FakeDateTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

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

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        var seeder = new TaxonomySnapshotSeeder(
            ScopeFactory, env,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaxonomySnapshotSeeder>.Instance);
        await seeder.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    // The real attribution pass over the real deriver (real read model + real Swedish analyzer).
    private OccupationExperienceDeriver NewSut()
    {
        var deriver = new OccupationCodeDeriver(
            new TaxonomyReadModel(ScopeFactory), new LocalTextAnalyzer(new SnowballStemmer()));
        return new OccupationExperienceDeriver(deriver, Clock);
    }

    [Fact]
    public async Task DeriveApproximateYears_RealExperienceEntry_AttributesSpanToItsRealGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = NewSut();

        var years = await sut.DeriveApproximateYearsAsync(
            [new ParsedExperience("Mjukvaruutvecklare", "Acme AB", "2019–2024", "raw")], ct);

        years.ShouldContainKeyAndValue(MjukvaraGroup, 5); // 2024 − 2019, joined on the real group
    }

    [Fact]
    public async Task DeriveApproximateYears_OngoingRole_ResolvesPresentToClockYear()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = NewSut();

        var years = await sut.DeriveApproximateYearsAsync(
            [new ParsedExperience("Advokat", "Byrån AB", "2016 – nu", "raw")], ct);

        years.ShouldContainKeyAndValue(AdvokatGroup, 10); // 2026 (clock) − 2016
    }

    [Fact]
    public async Task DeriveApproximateYears_TwoStintsSameRealGroup_MergedUnion()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = NewSut();

        // Two disjoint advocate stints → 2 + 3 = 5 (no double-count; merged-interval union).
        var years = await sut.DeriveApproximateYearsAsync(
        [
            new ParsedExperience("Advokat", "Byrå A", "2010–2012", "raw"),
            new ParsedExperience("Advokat", "Byrå B", "2018–2021", "raw"),
        ], ct);

        years.ShouldContainKeyAndValue(AdvokatGroup, 5);
    }

    [Fact]
    public async Task DeriveApproximateYears_FreeTextPeriod_NoAttribution()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = NewSut();

        var years = await sut.DeriveApproximateYearsAsync(
            [new ParsedExperience("Advokat", "Byrån AB", "ett tag sedan", "raw")], ct);

        years.ShouldNotContainKey(AdvokatGroup); // honest "not stated"
    }
}
