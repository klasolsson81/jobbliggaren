using System.Security.Cryptography;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace JobbPilot.Api.IntegrationTests.RateLimiting;

/// <summary>
/// Dedikerad factory för ListReadRateLimitTests. Behöver:
/// - Aggressiv ListRead (3/60s) för test-snabbhet
/// - Höjd AuthWrite (10000/min) så registrerings-flödet inte krockar med
///   StrictRateLimitApiFactory:s AuthWriteRateLimitTests-budget
///
/// Egen Postgres + Redis Testcontainer (cold-start ~16s) — acceptabelt för
/// isolerad test-flöde. Per CTO-rond 2026-05-13 F2-P9 + security-auditor
/// Major-fynd.
/// </summary>
public sealed class ListReadRateLimitApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public ListReadRateLimitApiFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbpilot-listread-rl-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbpilot-listread-rl-public-{Guid.NewGuid()}.pem");
        File.WriteAllText(_privateKeyPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

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
        });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redis.GetConnectionString();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisCs);

        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", _privateKeyPath);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);

        // AuthWrite höjs så registration-flödet inte rate-limit:as (delade
        // 127.0.0.1-bucket med övriga tester).
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__WindowSeconds", "60");
        // ListRead aggressiv för test-snabbhet (default 60/min skulle kräva
        // 61+ sequential requests).
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", "60");

        Environment.SetEnvironmentVariable("FeatureFlags__RegistrationsOpen", "true");

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", null);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", null);
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", null);
        Environment.SetEnvironmentVariable("FeatureFlags__RegistrationsOpen", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

[CollectionDefinition("ListReadRateLimit")]
public sealed class ListReadRateLimitFixtureGroup : ICollectionFixture<ListReadRateLimitApiFactory>;
