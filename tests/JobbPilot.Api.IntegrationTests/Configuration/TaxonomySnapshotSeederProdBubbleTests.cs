using System.Security.Cryptography;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace JobbPilot.Api.IntegrationTests.Configuration;

/// <summary>
/// ADR 0043 defekt-triage #3 (CTO Variant A) anti-regression: bevisar att
/// <c>TaxonomySnapshotSeeder</c> faktiskt bubblar 42P01 i Production-env när
/// <c>taxonomy_snapshot_meta</c>/<c>taxonomy_concepts</c>-schemat saknas
/// (host-start utan att <c>JobbPilot.Migrate</c> kört DDL först) — inte bara
/// att <c>IsSchemaInitGracePeriod</c>-predicate:n returnerar false isolerat
/// (det täcks av <c>TaxonomySnapshotSeederTests</c> i Application.UnitTests).
/// Speglar <see cref="IdempotentAdminRoleSeederProdBubbleTests"/> exakt.
///
/// <para>
/// Fixturen KÖR seedern (skiljer från <see cref="ProductionStartupFactory"/>
/// och <see cref="HttpsRedirectionGateFactoryBase"/> som plockar bort den via
/// <c>RemoveStartupSeeders()</c>). Medveten frånvaro av seeder-removal.
/// </para>
///
/// <para>
/// Ingen DbContext migreras (speglar <see cref="IdempotentAdminRoleSeederProdBubbleTests"/>
/// exakt). <c>TaxonomySnapshotSeeder</c> registreras som <c>IHostedService</c>
/// FÖRE <see cref="IdempotentAdminRoleSeeder"/> (DependencyInjection.cs rad 140
/// vs 375) → dess <c>StartAsync</c> körs först och bubblar 42P01 på
/// <c>taxonomy_snapshot_meta</c> innan admin-seedern ens nås, så ingen
/// Identity-migration behövs för att isolera taxonomi-seederns fel.
/// </para>
///
/// <para>
/// Förväntat beteende: <c>Services.CreateScope()</c> triggar host-start →
/// <c>TaxonomySnapshotSeeder.StartAsync</c> körs → <c>taxonomy_snapshot_meta</c>-
/// query kastar <see cref="PostgresException"/> SqlState=42P01 → gate-villkoret
/// <c>IsSchemaInitGracePeriod</c> returnerar false i Production → exception
/// bubblar genom <c>IHost.StartAsync</c> → ECS deployment_circuit_breaker
/// triggar rollback. Test asserterar PostgresException-typ + 42P01-SqlState.
/// Grace-gaten gäller alltså ENDAST Dev/Test (CLAUDE.md §3.4 fail-loud).
/// </para>
/// </summary>
public sealed class TaxonomyProdSeederBubbleFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public TaxonomyProdSeederBubbleFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbpilot-taxprodseederbubble-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbpilot-taxprodseederbubble-public-{Guid.NewGuid()}.pem");
        File.WriteAllText(_privateKeyPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options
                    .UseNpgsql(_postgresCs,
                        npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                    .UseSnakeCaseNamingConvention());

            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseNpgsql(_postgresCs, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                }));

            services.RemoveAll<IDistributedCache>();
            services.AddStackExchangeRedisCache(opts =>
            {
                opts.Configuration = _redisCs;
                opts.InstanceName = "jobbpilot:";
            });

            // OBS: medveten frånvaro av RemoveStartupSeeders() — taxonomi-seedern
            // SKA köra i denna fixture för att bevisa 42P01-bubbling i Production.
        });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redis.GetConnectionString();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", _privateKeyPath);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", "127.0.0.1/32");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisCs);
        // TD-13 (ADR 0049): Production-env hård-validerar FieldEncryption:CmkKeyId.
        Environment.SetEnvironmentVariable(
            "FieldEncryption__CmkKeyId",
            "arn:aws:kms:eu-north-1:000000000000:key/test-cmk");
        Environment.SetEnvironmentVariable("Hsts__MaxAgeDays", "365");

        // Ingen migration — speglar IdempotentAdminRoleSeederProdBubbleTests.
        // TaxonomySnapshotSeeder registreras FÖRE IdempotentAdminRoleSeeder
        // (DependencyInjection.cs rad 140 vs 375) → host-start kör dess
        // StartAsync först och bubblar 42P01 på taxonomy_snapshot_meta innan
        // admin-seedern nås. Testet triggar host-start via _factory.Services.
        await Task.CompletedTask;
    }

    public new async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", null);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", null);
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("FieldEncryption__CmkKeyId", null);
        Environment.SetEnvironmentVariable("Hsts__MaxAgeDays", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        GC.SuppressFinalize(this);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

public class TaxonomySnapshotSeederProdBubbleTests : IAsyncLifetime
{
    private readonly TaxonomyProdSeederBubbleFactory _factory = new();

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Host_start_throws_PostgresException_42P01_when_taxonomy_schema_missing_in_Production()
    {
        // Services-property-access triggar host-start → TaxonomySnapshotSeeder.StartAsync
        // körs som IHostedService → taxonomy_snapshot_meta-query kastar
        // PostgresException(42P01) (taxonomi-schemat saknas) → gate-villkoret
        // IsSchemaInitGracePeriod returnerar false i Production → exception bubblar
        // → host-start failer.
        //
        // WebApplicationFactory wrappar inre exceptions från StartAsync — fångar därför
        // generic Exception och borrar ner till första PostgresException(42P01) i kedjan.
        var ex = Should.Throw<Exception>(() =>
        {
            using var scope = _factory.Services.CreateScope();
        });

        var postgresEx = ExtractInnermost<PostgresException>(ex);
        postgresEx.ShouldNotBeNull(
            "Host-start ska kasta exception som innehåller PostgresException i kedjan.");
        postgresEx.SqlState.ShouldBe(
            "42P01",
            "PostgresException ska ha SqlState 42P01 (undefined_table — taxonomi-schema saknas; " +
            "grace-gaten gäller endast Dev/Test, inte Production).");
    }

    private static T? ExtractInnermost<T>(Exception ex) where T : Exception
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is T match) return match;
            current = current.InnerException;
        }
        return null;
    }
}
