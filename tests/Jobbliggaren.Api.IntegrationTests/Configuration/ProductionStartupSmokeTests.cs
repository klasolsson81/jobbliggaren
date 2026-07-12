using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// Verifierar att <c>Program.cs</c> startar i Production-env utan att tippa över
/// när env-gated config är populerad. Komplement till de övriga integration-
/// testerna som tvingar Development-env via fixtures (TD-37 fix). Skyddar mot
/// regression där en ny env-gated check (HSTS, ForwardedHeaders, etc.) tyst
/// bara körs i Development och därmed bryter Production-deploy först i CI.
/// </summary>
public sealed class ProductionStartupFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public ProductionStartupFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-prodsmoke-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-prodsmoke-public-{Guid.NewGuid()}.pem");
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

            // N-2 hardening (2026-05-11): prod-seedrar (IdempotentAdminRoleSeeder
            // + ADR 0043 TaxonomySnapshotSeeder) bubblar 42P01 i Production-env
            // (CLAUDE.md §3.4 fail-loud). Fixturen kör Services.CreateScope FÖRE
            // MigrateAsync (catch-22) → seedrarna plockas bort här. Prod-defensen
            // verifieras separat via *ProdBubbleTests + *.IsSchemaInitGracePeriod.
            // Delad SPOT (ADR 0043 defekt-triage #3).
            services.RemoveStartupSeeders();
        });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redis.GetConnectionString();

        // ASPNETCORE_ENVIRONMENT sätts FÖRE Services-access. UseEnvironment() i
        // ConfigureWebHost är otillräckligt för minimal API. Production-mode
        // är HELA poängen med denna fixture — verifiera Program.cs-startup-pipeline
        // i prod-läge med populerad config.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", _privateKeyPath);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);

        // Production-defense per ForwardedHeadersConfig.EnsureSafeForEnvironment:
        // KnownNetworks får inte vara tom när Environment != Development/Test.
        // Loopback-CIDR är tillräckligt för smoke-startup (test-host gör direkt-anrop).
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", "127.0.0.1/32");

        // Production-env kräver explicit ConnectionStrings:Postgres + Redis (Development
        // tolererar saknad). ApiFactory replacer DbContext + IDistributedCache via
        // ConfigureServices, men AddInfrastructure läser CS:erna direkt vid registrerings-
        // tid innan replace körs. Sätt till container-CS:erna så registreringen passerar.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisCs);
        // TD-13 (ADR 0049): Production-env hård-validerar FieldEncryption:CmkKeyId
        // (FieldEncryptionOptionsValidator). En Production-smoke MÅSTE förse det
        // Production kräver. KMS-klienten anropas ej i en ren startup-smoke.
        Environment.SetEnvironmentVariable(
            "FieldEncryption__CmkKeyId",
            "arn:aws:kms:eu-north-1:000000000000:key/test-cmk");

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
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", null);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", null);
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);
        Environment.SetEnvironmentVariable("FieldEncryption__CmkKeyId", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

[CollectionDefinition("ProductionStartup")]
public sealed class ProductionStartupFixtureGroup : ICollectionFixture<ProductionStartupFactory>;

[Collection("ProductionStartup")]
public class ProductionStartupSmokeTests(ProductionStartupFactory factory)
{
    private readonly ProductionStartupFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GET_api_ready_returns_200_in_Production_env()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/ready", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // #796 map-gate guardrail (HARD merge gate, CLAUDE.md §12): the whole /api/v1/dev/*
    // group — including the token-free confirmed-login seam — MUST be unmapped outside
    // Development. A 404 (not 401/405) proves the route does not exist in Production; if
    // the Program.cs IsDevelopment() gate ever regressed, these turn red before deploy.

    [Fact]
    public async Task POST_dev_confirm_email_is_unmapped_in_Production_env()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/dev/confirm-email",
            new { email = "x@e2e.jobbliggaren.test" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_dev_reset_my_data_is_unmapped_in_Production_env()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsync("/api/v1/dev/reset-my-data", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // The map-gate 404 above is necessary but not sufficient on its own: the endpoint's
    // own "account not found" branch also returns 404, so if BOTH structural gates ever
    // regressed together the route could answer 404 spuriously (green while the primitive
    // is live). This asserts the SECOND, independent gate directly — the dev-only
    // IDevEmailConfirmer must be ABSENT from the container outside Development
    // (AddDevOnlyTestingSupport). Together the two tests prove both gates fail-closed.
    [Fact]
    public void IDevEmailConfirmer_is_not_registered_in_Production_env()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IDevEmailConfirmer>().ShouldBeNull();
    }
}
