using System.Net;
using System.Security.Cryptography;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
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
/// Anti-regression för UseHttpsRedirection + UseHsts env-gates per ADR 0026 + ADR 0027
/// (TD-31 + TD-44).
///
/// <para>
/// Sec-Major-2 STEG 13b: om <c>UseHttpsRedirection</c>-gaten tas bort skulle redirect → port 443
/// mot HTTP-only-ALB trigga ALB-health-check fail → ECS deployment_circuit_breaker rollback.
/// Strukturellt regression-skydd, inte docs-disciplin. Tre redirect-testfall:
/// </para>
///
/// <list type="number">
/// <item>Production + Alb:HttpsEnabled=false → 200 (UseHttpsRedirection EJ registrerad)</item>
/// <item>Production + Alb:HttpsEnabled=true  → 307 (UseHttpsRedirection registrerad)</item>
/// <item>Development (oavsett Alb-flag)      → 307 (Development gör default-redirect via dev-cert)</item>
/// </list>
///
/// <para>
/// TD-44 (dotnet-architect Mindre 4, Fas 1 Block A3): <c>UseHsts</c>-gaten på rad 150
/// (<c>!IsDevelopment() &amp;&amp; albOptions.HttpsEnabled</c>) har egen regression-yta —
/// HSTS-header-aktivering. Tre HSTS-testfall återanvänder samma factories
/// (X-Forwarded-Proto-mock så Request.IsHttps==true post-ForwardedHeaders → HSTS-middleware
/// triggar och returnerar 200 i stället för 307):
/// </para>
///
/// <list type="number">
/// <item>Production + Alb:HttpsEnabled=true  → HSTS-header satt (max-age=31536000; includeSubDomains)</item>
/// <item>Production + Alb:HttpsEnabled=false → HSTS-header EJ satt (gate skip:ar UseHsts)</item>
/// <item>Development (oavsett Alb-flag)      → HSTS-header EJ satt (Development-asymmetri vs HttpsRedirection)</item>
/// </list>
///
/// <para>
/// HSTS-Development-asymmetrin är medveten per Program.cs:144 — HSTS-policy persistar i
/// MaxAgeDays-fönstret i browsern, så aktivering på localhost skulle bryta framtida
/// <c>dotnet run</c>-sessioner efter dev-cert-rotation.
/// </para>
/// </summary>
public abstract class HttpsRedirectionGateFactoryBase : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// TD-44 Host-header för HSTS-tester. ASP.NET-default <c>HstsOptions.ExcludedHosts</c>
    /// inkluderar "localhost", "127.0.0.1", "[::1]" — HSTS-header sätts ALDRIG på dessa
    /// hosts (dev-loop-skydd). För att verifiera UseHsts()-gaten i WebApplicationFactory
    /// utan att kompromissa det skyddet skickar HSTS-testerna explicit Host-header som
    /// matchar verklig prod-DNS. Pattern: simulera prod-trafik, inte override prod-defense.
    /// </summary>
    public const string ProdLikeHost = "dev.jobbliggaren.se";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    /// <summary>Värdet som sätts på <c>Alb__HttpsEnabled</c> env-var i InitializeAsync.</summary>
    protected abstract bool HttpsEnabled { get; }

    /// <summary>ASP.NET environment-name. Override:s av Development-factory; default Production.</summary>
    protected virtual string EnvironmentName => "Production";

    protected HttpsRedirectionGateFactoryBase()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-httpsgate-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-httpsgate-public-{Guid.NewGuid()}.pem");
        File.WriteAllText(_privateKeyPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ASPNETCORE_ENVIRONMENT sätts i InitializeAsync FÖRE host-build (TD-37-
        // läxa: builder.UseEnvironment() är no-op för minimal API där
        // WebApplication.CreateBuilder() läser env-varen direkt vid host-bygge,
        // innan denna ConfigureWebHost-callback körs).
        builder.ConfigureServices(services =>
        {
            // UseHttpsRedirection-middleware behöver veta vilken port att redirecta TILL.
            // Default-resolver kollar ASPNETCORE_URLS / ASPNETCORE_HTTPS_PORTS / HTTPS_PORT —
            // ingen är satt i WebApplicationFactory-test-host → middleware loggar varning
            // "Failed to determine the https port for redirect" och returnerar 200 utan
            // redirect. Sätt explicit 443 så testet kan asserta 307 + Location-scheme.
            services.PostConfigure<HttpsRedirectionOptions>(opts => opts.HttpsPort = 443);

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

        // Env-vars sätts FÖRE Services-access (triggar host-build). Production-env
        // kräver populerad ConnectionStrings + KnownNetworks (per ForwardedHeadersConfig.
        // EnsureSafeForEnvironment) + IConnectionMultiplexer-string (TD-37-läxa).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", EnvironmentName);
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", _privateKeyPath);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);
        Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", "127.0.0.1/32");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisCs);
        // TD-13 (ADR 0049): Production-env hård-validerar FieldEncryption:CmkKeyId
        // (FieldEncryptionOptionsValidator). Dessa gate-tester booter Production.
        Environment.SetEnvironmentVariable(
            "FieldEncryption__CmkKeyId",
            "arn:aws:kms:eu-north-1:000000000000:key/test-cmk");
        Environment.SetEnvironmentVariable("Alb__HttpsEnabled", HttpsEnabled ? "true" : "false");

        // TD-44: deterministisk HSTS-konfig så HSTS-header-asserts inte beror på vilken
        // appsettings.*.json som råkar hamna i test-output-directory. Default i HstsOptions
        // är redan 365 men explicit env-var gör testet self-documenting + skyddar mot
        // future-default-flip. EnsureSafeForEnvironment kräver MaxAgeDays>=365 i Production.
        Environment.SetEnvironmentVariable("Hsts__MaxAgeDays", "365");
        Environment.SetEnvironmentVariable("Hsts__IncludeSubDomains", "true");

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
        Environment.SetEnvironmentVariable("Alb__HttpsEnabled", null);
        Environment.SetEnvironmentVariable("Hsts__MaxAgeDays", null);
        Environment.SetEnvironmentVariable("Hsts__IncludeSubDomains", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        // SuppressFinalize FÖRE base.DisposeAsync (idempotent, men rätt ordning enligt
        // CA1816 — undviker dubbel-anrop då base själv anropar SuppressFinalize internt).
        GC.SuppressFinalize(this);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}

public sealed class HttpsRedirectionDisabledProductionFactory : HttpsRedirectionGateFactoryBase
{
    protected override bool HttpsEnabled => false;
}

public sealed class HttpsRedirectionEnabledProductionFactory : HttpsRedirectionGateFactoryBase
{
    protected override bool HttpsEnabled => true;
}

public sealed class HttpsRedirectionDevelopmentFactory : HttpsRedirectionGateFactoryBase
{
    // Development triggar UseHttpsRedirection oavsett Alb-flag (Program.cs:155
    // första halvan: builder.Environment.IsDevelopment()).
    protected override bool HttpsEnabled => false;
    protected override string EnvironmentName => "Development";
}

[CollectionDefinition("HttpsRedirectionDisabled")]
public sealed class HttpsRedirectionDisabledFixtureGroup
    : ICollectionFixture<HttpsRedirectionDisabledProductionFactory>;

[CollectionDefinition("HttpsRedirectionEnabled")]
public sealed class HttpsRedirectionEnabledFixtureGroup
    : ICollectionFixture<HttpsRedirectionEnabledProductionFactory>;

[CollectionDefinition("HttpsRedirectionDevelopment")]
public sealed class HttpsRedirectionDevelopmentFixtureGroup
    : ICollectionFixture<HttpsRedirectionDevelopmentFactory>;

[Collection("HttpsRedirectionDisabled")]
public class HttpsRedirectionGateDisabledTests(HttpsRedirectionDisabledProductionFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task GET_api_ready_returns_200_when_HttpsEnabled_false_in_Production()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/ready", ct);

        // Med Alb:HttpsEnabled=false (default fram till ADR 0026-trigger) registreras
        // INTE UseHttpsRedirection — HTTP-request returnerar direkt utan redirect.
        // Skydd mot regression där gaten tas bort och redirect → port 443 mot HTTP-only-
        // ALB → ALB-health-check fail → ECS deployment_circuit_breaker rollback.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HSTS_header_not_set_when_HttpsEnabled_false_in_Production()
    {
        // TD-44 anti-regression: även om Request faktiskt är HTTPS (X-Forwarded-Proto-mock)
        // och Host matchar prod-DNS (HstsOptions.ExcludedHosts-skyddet eliminerat) ska
        // HSTS-headern INTE sättas när Alb:HttpsEnabled=false. Program.cs:150-gate
        // (`!IsDevelopment() && albOptions.HttpsEnabled`) skip:ar UseHsts() då. Skydd mot
        // regression där HSTS-gaten flyttas/tas bort och browser cache:ar HTTPS-only-
        // policy i 365 dagar — kan inte återgå till HTTP utan att rensa browser-state.
        var ct = TestContext.Current.CancellationToken;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ready");
        request.Headers.Host = HttpsRedirectionGateFactoryBase.ProdLikeHost;
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse(
            "UseHsts() ska INTE registreras när Alb:HttpsEnabled=false (Program.cs:150-gate).");
    }
}

[Collection("HttpsRedirectionEnabled")]
public class HttpsRedirectionGateEnabledTests(HttpsRedirectionEnabledProductionFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task GET_api_ready_returns_307_when_HttpsEnabled_true_in_Production()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/ready", ct);

        // Med Alb:HttpsEnabled=true (post-STEG 13c HTTPS-flip) registreras
        // UseHttpsRedirection — HTTP-request returnerar 307 mot https://.
        response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.Scheme.ShouldBe("https");
    }

    [Fact]
    public async Task HSTS_header_set_when_HttpsEnabled_true_in_Production()
    {
        // TD-44 anti-regression: när Request är HTTPS (X-Forwarded-Proto-mock så
        // UseForwardedHeaders sätter Request.Scheme=https → Request.IsHttps=true) OCH Host
        // matchar prod-DNS (inte i HstsOptions.ExcludedHosts-default-listan) ska UseHsts()
        // sätta Strict-Transport-Security-headern på svaret. Verifierar att
        // Program.cs:150-gate (`!IsDevelopment() && albOptions.HttpsEnabled`) är intakt
        // — om gaten flippas eller tas bort tappar prod sin HSTS-policy tyst.
        //
        // Host-header = "dev.jobbliggaren.se" simulerar verklig prod-trafik (DNS-host).
        // Default HstsOptions.ExcludedHosts ("localhost"/"127.0.0.1"/"[::1]") bevaras —
        // pattern: simulera prod-trafik, inte override prod-defense (dotnet-architect
        // Major-fynd TD-44 review).
        //
        // Request blir 200 (inte 307) eftersom UseHttpsRedirection ser IsHttps=true och
        // skippar redirect. Det är OK — vi verifierar HSTS, inte redirect (det gör
        // redirect-testet ovan utan X-Forwarded-Proto).
        var ct = TestContext.Current.CancellationToken;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ready");
        request.Headers.Host = HttpsRedirectionGateFactoryBase.ProdLikeHost;
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue(
            "UseHsts() ska sätta header på HTTPS-svar när Alb:HttpsEnabled=true i Production.");

        var hstsValue = response.Headers.GetValues("Strict-Transport-Security").Single();
        hstsValue.ShouldContain("max-age=31536000",
            customMessage: "MaxAgeDays=365 → 365*24*3600 = 31536000 sekunder (HSTS-spec).");
        hstsValue.ShouldContain("includeSubDomains",
            customMessage: "IncludeSubDomains=true skyddar alla *.jobbliggaren.se-subdomäner.");
        hstsValue.ShouldNotContain("preload",
            customMessage: "Preload=false initialt — submit till hstspreload.org är prod-launch-step (HstsOptions.cs:47).");
    }
}

[Collection("HttpsRedirectionDevelopment")]
public class HttpsRedirectionGateDevelopmentTests(HttpsRedirectionDevelopmentFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    [Fact]
    public async Task GET_api_ready_returns_307_in_Development_regardless_of_Alb_flag()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/ready", ct);

        // Program.cs:155 — Development triggar UseHttpsRedirection oavsett Alb-flag
        // (dev-cert via Kestrel). Skydd mot regression där env-gate omdesignas så
        // Development-läge tappar redirect.
        response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.Scheme.ShouldBe("https");
    }

    [Fact]
    public async Task HSTS_header_not_set_in_Development()
    {
        // TD-44 asymmetri-skydd: även när Request är HTTPS (X-Forwarded-Proto-mock) och
        // Host matchar prod-DNS (ExcludedHosts-default-skyddet eliminerat som confound)
        // ska HSTS-headern INTE sättas i Development. Program.cs:150-gate
        // (`!IsDevelopment() && albOptions.HttpsEnabled`) skip:ar UseHsts() i Development
        // för att undvika browser-HTTPS-lock på localhost (HSTS-policy persistar i 365
        // dagar i browsern → bryter framtida `dotnet run` efter dev-cert-rotation,
        // per HstsOptions.cs:15-19 + Program.cs:144).
        //
        // Asymmetri vs UseHttpsRedirection: Development triggar redirect (rad 155 första
        // halvan) men INTE HSTS (rad 150 första halvan). Detta test verifierar att
        // asymmetrin är intakt — regression skulle bryta lokal dev-loop.
        var ct = TestContext.Current.CancellationToken;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ready");
        request.Headers.Host = HttpsRedirectionGateFactoryBase.ProdLikeHost;
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse(
            "UseHsts() ska ALDRIG registreras i Development (Program.cs:150-gate + HstsOptions.cs:15-19).");
    }
}
