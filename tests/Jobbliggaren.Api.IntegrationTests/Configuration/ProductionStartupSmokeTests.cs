using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Application.Dev.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
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

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

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
        // ADR 0066 (#802): fält-krypteringen är Local-only och validatorn kräver en
        // giltig master-nyckel i ALLA miljöer (även Production-smoke) — den sätts
        // systemiskt av TestSecrets-module-init (process-env-var) före boot.

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
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", null);

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

    // #796 map-gate guardrail (HARD merge gate, CLAUDE.md §12). The invariant is now NARROWER
    // than "the whole group is unmapped", and the narrowing is measured rather than assumed:
    //
    //   * /api/v1/dev/confirm-email is UNCONDITIONALLY unmapped outside Development. It force-
    //     confirms an address without authentication, so no configuration may widen it — and
    //     the flag-ON arm below is what turns that from a code-reading into a measurement.
    //   * /api/v1/dev/reset-my-data is unmapped outside Development UNLESS
    //     DevTools:EnableResetMyData is explicitly true (Klas-direktiv 2026-08-27). It is
    //     owner-scoped, authenticated, and refused a second time inside the handler.
    //
    // A 404 (not 401/405) proves the route does not exist; if either gate regressed, these
    // turn red before deploy.

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
    public async Task POST_dev_reset_my_data_is_unmapped_in_Production_env_when_the_flag_is_absent()
    {
        // The fail-closed DEFAULT, measured rather than assumed: this host sets no DevTools
        // section at all, which is exactly how a deployed environment that never heard of the
        // flag binds it.
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsync("/api/v1/dev/reset-my-data", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_dev_reset_my_data_is_mapped_in_Production_env_when_the_flag_is_on()
    {
        // The arm the flag exists for. 404 here would mean the box still cannot re-test
        // onboarding, which is the whole point of the change.
        var ct = TestContext.Current.CancellationToken;
        using var host = _factory.WithWebHostBuilder(
            b => b.UseSetting("DevTools:EnableResetMyData", "true"));
        using var client = host.CreateClient();

        var response = await client.PostAsync("/api/v1/dev/reset-my-data", content: null, ct);

        // 401, not 204: the route now EXISTS, and RequireAuthorization answers first. Any
        // non-404 proves the mapping; asserting 401 additionally pins that turning the flag on
        // did not also drop the auth requirement.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Only_reset_my_data_is_mapped_under_api_v1_dev_in_Production_env_when_the_flag_is_on()
    {
        // The three route tests above are ENUMERATED — they each name a route. That is fine for
        // the two routes that exist and blind to a third: a new endpoint added to
        // MapDevResetMyDataEndpoint, or a new method called under the same flag, would reach
        // Production with the flag on and nothing would go red. This assertion is universally
        // quantified over the route table instead, so it fails on arrival rather than on
        // someone remembering to add a case.
        using var host = _factory.WithWebHostBuilder(
            b => b.UseSetting("DevTools:EnableResetMyData", "true"));
        _ = host.CreateClient(); // forces the host to build so the route table exists

        var devRoutes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(p => p.Contains("api/v1/dev", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        devRoutes.ShouldBe(["/api/v1/dev/reset-my-data"]);
    }

    [Fact]
    public async Task POST_dev_confirm_email_stays_unmapped_in_Production_env_even_when_the_reset_flag_is_on()
    {
        // THE load-bearing test of this whole change. The reset flag must never be one || away
        // from re-arming the unauthenticated confirm-email seam in a deployed environment. That
        // is why the two routes are mapped by two different extension methods rather than one
        // call behind one condition — and this is the measurement that keeps it true.
        var ct = TestContext.Current.CancellationToken;
        using var host = _factory.WithWebHostBuilder(
            b => b.UseSetting("DevTools:EnableResetMyData", "true"));
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/dev/confirm-email",
            new { email = "x@e2e.jobbliggaren.test" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the second, independent gate is still shut too: the dev-only confirmer is not in
        // the container. Both must hold — a 404 alone could also come from the endpoint's own
        // not-found branch if both structural gates ever regressed together.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetService<IDevEmailConfirmer>().ShouldBeNull();
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
