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
/// Dedikerad factory för ListReadRateLimitTests. Behöver:
/// - Aggressiv ListRead (3/60s) för test-snabbhet
///
/// Egen Postgres + Redis Testcontainer (cold-start ~16s) — acceptabelt för
/// isolerad test-flöde. Per CTO-rond 2026-05-13 F2-P9 + security-auditor
/// Major-fynd.
/// </summary>
public sealed class ListReadRateLimitApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

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

        // ListRead aggressiv för test-snabbhet (default 60/min skulle kräva
        // 61+ sequential requests).
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", "3");
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", "60");

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
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", null);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

[CollectionDefinition("ListReadRateLimit")]
public sealed class ListReadRateLimitFixtureGroup : ICollectionFixture<ListReadRateLimitApiFactory>;
