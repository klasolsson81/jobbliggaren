using System.Security.Cryptography;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
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

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// N-2 anti-regression (security-auditor 2026-05-11 Major 1): bevisar att
/// <c>IdempotentAdminRoleSeeder</c> faktiskt bubblar 42P01 i Production-env
/// när Identity-schemat saknas — inte bara att <c>IsSchemaInitGracePeriod</c>-
/// predicate:n returnerar false isolerat. Komplement till
/// <c>IdempotentAdminRoleSeederTests</c> i Application.UnitTests.
///
/// <para>
/// Fixturen KÖR seedern (skiljer från <see cref="ProductionStartupFactory"/>
/// och <see cref="HttpsRedirectionGateFactoryBase"/> som plockar bort den).
/// AppDbContext migreras men AppIdentityDbContext lämnas otomigrerad — så
/// 42P01 trigger:as när seedern accessar Identity-tabellerna.
/// </para>
///
/// <para>
/// Förväntat beteende: <c>Services.CreateScope()</c> triggar host-start →
/// <c>IdempotentAdminRoleSeeder.StartAsync</c> körs → <c>RoleManager</c>-query
/// kastar <see cref="PostgresException"/> SqlState=42P01 → gate-villkoret
/// <c>IsSchemaInitGracePeriod</c> returnerar false i Production → exception
/// bubblar genom <c>IHost.StartAsync</c> → ECS deployment_circuit_breaker
/// triggar rollback. Test asserterar PostgresException-typ + 42P01-SqlState.
/// </para>
/// </summary>
public sealed class ProdSeederBubbleFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public ProdSeederBubbleFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-prodseederbubble-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-prodseederbubble-public-{Guid.NewGuid()}.pem");
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
                opts.InstanceName = "jobbliggaren:";
            });

            // OBS: medveten frånvaro av seeder-removal — seedern SKA köra
            // i denna fixture för att bevisa 42P01-bubbling i Production.
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
        // ADR 0066 (#802): master-nyckeln (Local-only, krävs i ALLA miljöer) sätts
        // systemiskt av TestSecrets-module-init (process-env-var) före boot.
        Environment.SetEnvironmentVariable("Hsts__MaxAgeDays", "365");
    }

    public new async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", null);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", null);
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("Hsts__MaxAgeDays", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        GC.SuppressFinalize(this);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

public class IdempotentAdminRoleSeederProdBubbleTests : IAsyncLifetime
{
    private readonly ProdSeederBubbleFactory _factory = new();

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Host_start_throws_PostgresException_42P01_when_Identity_schema_missing_in_Production()
    {
        // Services-property-access triggar host-start → IdempotentAdminRoleSeeder.StartAsync
        // körs som IHostedService → kastar PostgresException(42P01) (Identity-schemat saknas)
        // → gate-villkoret IsSchemaInitGracePeriod returnerar false i Production →
        // exception bubblar → host-start failer.
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
            "PostgresException ska ha SqlState 42P01 (undefined_table — Identity-schema saknas).");
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
