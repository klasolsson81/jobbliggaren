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
/// Custom factory som <strong>inte</strong> override:ar rate-limit-policies via
/// env-vars (TD-21 Sec-Major-2). Används för att verifiera att 429-respons
/// faktiskt returneras vid PermitLimit-överskridning. Reguljär <c>ApiFactory</c>
/// höjer IP-policies till 10 000/min så övriga tester inte krockar — denna
/// factory använder default-värden (20/min auth-write, 30/min auth-loose).
///
/// Egen Postgres + Redis Testcontainer-instans → ~16 s cold-start. Acceptabelt
/// för en isolerad rate-limit-test-suite.
/// </summary>
public sealed class StrictRateLimitApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tvinga Development-env explicit (samma rationale som ApiFactory).
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

            // #714 — force email-confirmation-first OFF (parity with ApiFactory). Development env loads
            // appsettings.Development.json where the flag is ON; without this override the AuthWrite
            // rate-limit test's RegisterAndGetSessionIdAsync gets a 202 (empty body) instead of a session.
            services.PostConfigure<AuthOptions>(o => o.RequireEmailConfirmation = false);

            // ADR 0083 Amendment 2026-08-03 - the kill-switch defaults CLOSED, and this factory
            // registers users (RegisterAndGetSessionIdAsync). Pinned explicitly, like the line
            // above, so the harness never depends on a dev config file it does not own.
            services.PostConfigure<AuthOptions>(o => o.RegistrationsOpen = true);
        });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redis.GetConnectionString();

        // ASPNETCORE_ENVIRONMENT + ConnectionStrings sätts FÖRE Services-access
        // (samma rationale som ApiFactory — IConnectionMultiplexer registreras
        // med string captured vid registration-time, ConfigureServices replacar
        // bara IDistributedCache + DbContexts).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisCs);

        // VIKTIGT: clear:a ev. ApiFactory-overlays som lever i samma process —
        // strikt-factoryn ska se default-värden i RateLimitingOptions.
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__AuthLoose__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__AuthLoose__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", null);

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

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

[CollectionDefinition("StrictRateLimit")]
public sealed class StrictRateLimitFixtureGroup : ICollectionFixture<StrictRateLimitApiFactory>;
