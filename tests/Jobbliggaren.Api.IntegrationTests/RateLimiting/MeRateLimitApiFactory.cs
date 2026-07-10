using System.Security.Cryptography;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// Dedikerad factory för MeRateLimitTests (Pre-4 STEG 5 — TD-87 + TD-92).
/// Behöver:
/// - Aggressiva limits (3/60s) på de tre nya "me"-policyerna (MeListRead,
///   JobAdStatusBatch, MeWrite) för test-snabbhet — annars krävs 40-60+
///   sekventiella anrop för att trigga 429.
/// - Höjd AuthWrite (10000/min) så registrerings-flödet inte själv rate-
///   limit:as på den delade 127.0.0.1-IP-bucketen.
///
/// Egen Postgres + Redis Testcontainer (cold-start ~16s) — acceptabelt för
/// isolerat test-flöde. Speglar ListReadRateLimitApiFactory exakt.
/// </summary>
public sealed class MeRateLimitApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public MeRateLimitApiFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-me-rl-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-me-rl-public-{Guid.NewGuid()}.pem");
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
                opts.InstanceName = "jobbliggaren:";
            });

            // #714 — force email-confirmation-first OFF (parity with ApiFactory). This factory forces
            // Development env, which loads appsettings.Development.json where the flag is ON; without
            // this override the rate-limit tests' RegisterAndGetSessionIdAsync gets a 202 (empty body)
            // instead of 200 + sessionId. PostConfigure runs after config binding (and beats a dev's
            // gitignored appsettings.Local.json, which an env var would not).
            services.PostConfigure<AuthOptions>(o => o.RequireEmailConfirmation = false);
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

        // De tre nya "me"-policyerna aggressiva för test-snabbhet (TD-87 + TD-92).
        // MeListRead: GET /api/v1/me/profile m.fl. — partition UserId (claim "sub").
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__WindowSeconds", "60");
        // JobAdStatusBatch: POST /api/v1/me/job-ad-status — dual partition
        // (sub→user:-bucket, annars→ip:-bucket). TD-87 kritisk anonym-skydds-yta.
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__WindowSeconds", "60");
        // MeWrite: POST/DELETE saved-job-ads + DELETE recent-searches —
        // partition UserId (claim "sub").
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__WindowSeconds", "60");

        using var scope = Services.CreateScope();
        // F6 P4 — pg_trgm krävs av F6P4aJobAdTrigramIndexes (se ApiFactory).
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();
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
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__WindowSeconds", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

[CollectionDefinition("MeRateLimit")]
public sealed class MeRateLimitFixtureGroup : ICollectionFixture<MeRateLimitApiFactory>;
