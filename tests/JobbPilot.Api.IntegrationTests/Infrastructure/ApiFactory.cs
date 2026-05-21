using System.Security.Cryptography;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Infrastructure.Taxonomy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace JobbPilot.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    // Set in InitializeAsync before Services is accessed (triggers host creation)
    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public ApiFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbpilot-test-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbpilot-test-public-{Guid.NewGuid()}.pem");
        File.WriteAllText(_privateKeyPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
    }

    // Replaces DbContext registrations (which are registered before ConfigureWebHost runs)
    // with Testcontainer connection strings. Redis is replaced the same way.
    // JWT key paths + rate-limit overrides är handled via environment variables i
    // InitializeAsync — Program.cs läser dem direkt från builder.Configuration vid
    // service-registration-tid (innan ConfigureWebHost-services körs).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tvinga Development-env. Production-env tripper
        // ForwardedHeadersConfig.EnsureSafeForEnvironment (Sec-Major-1, STEG 12)
        // när KnownNetworks är tom — by design fail-loud i prod, men test-fixturen
        // har inga proxy-CIDR:er. Production-startup verifieras isolerat av
        // ProductionStartupSmokeTests.
        //
        // OBS: builder.UseEnvironment() här är otillräckligt för minimal API +
        // WebApplicationFactory eftersom WebApplication.CreateBuilder() i
        // Program.cs läser ASPNETCORE_ENVIRONMENT INNAN denna callback körs.
        // Verklig env-override sker via env-var i InitializeAsync nedan.
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Replace AppDbContext
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            // TD-13 C3 (Mekanik-not 5c): re-AddDbContext måste spegla
            // produktionens (sp,options).AddInterceptors — annars kör Api-integ
            // utan kryptering (interceptor-paret auto-discoveras EJ).
            services.AddDbContext<AppDbContext>((sp, options) =>
                options
                    .UseNpgsql(_postgresCs,
                        npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                    .UseSnakeCaseNamingConvention()
                    .AddInterceptors(
                        sp.GetRequiredService<JobbPilot.Infrastructure.Security.FieldEncryptionSaveChangesInterceptor>(),
                        sp.GetRequiredService<JobbPilot.Infrastructure.Security.FieldDecryptionMaterializationInterceptor>()));

            // Replace AppIdentityDbContext
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseNpgsql(_postgresCs, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                }));

            // Replace Redis cache
            services.RemoveAll<IDistributedCache>();
            services.AddStackExchangeRedisCache(opts =>
            {
                opts.Configuration = _redisCs;
                opts.InstanceName = "jobbpilot:";
            });

            // TD-13 C3: faka KMS (interceptor-paret anropar GenerateDataKey/
            // Decrypt vid Application-writes/reads i full Mediator-pipeline).
            // Annars riktig AWS-KMS med tom CMK → fail. Deterministisk per
            // owner — round-trip + per-användare-isolering bevaras end-to-end.
            services.RemoveAll<Amazon.KeyManagementService.IAmazonKeyManagementService>();
            services.AddSingleton(ApiKmsFake.Create());
        });
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redis.GetConnectionString();

        // ASPNETCORE_ENVIRONMENT sätts FÖRE Services-access så WebApplication.
        // CreateBuilder() i Program.cs läser rätt värde. UseEnvironment() i
        // ConfigureWebHost är ej effektivt för minimal API (callback körs efter
        // builder är byggd). Tvingar Development för att undvika fail-loud
        // ForwardedHeadersConfig.EnsureSafeForEnvironment med tom KnownNetworks.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        // ConnectionStrings sätts FÖRE Services-access. ConfigureServices replacer
        // bara IDistributedCache + DbContexts; IConnectionMultiplexer (Infrastructure
        // DI line ~131) registreras med string captured vid registration-time.
        // Lokalt på Windows funkar default localhost:6379 via Docker Compose;
        // på Linux-CI utan default Redis kraschar IConnectionMultiplexer.Connect()
        // vid första request → 500 på alla auth-endpoints.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgresCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redisCs);

        // JWT key paths are read at service-registration time in Program.cs via
        // builder.Configuration. Setting env vars here (before Services is accessed, which
        // triggers Program.cs to run) makes them available to WebApplication.CreateBuilder().
        Environment.SetEnvironmentVariable("Jwt__PrivateKeyPath", _privateKeyPath);
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);

        // Höj IP-baserade rate-limits drastiskt för testkörning så befintliga
        // tester (alla från 127.0.0.1) inte rate-limit:as på varandras gemen-
        // samma IP-partition (TD-21). Account-deletion-policy (UserId-baserad)
        // hålls default eftersom varje test skapar unik user → unik partition.
        //
        // OBSERVATION (process-globalt env): xunit.runner.json har
        // parallelizeTestCollections=false så Api-collection och StrictRateLimit-
        // collection inte kör samtidigt → ingen race på dessa env-vars.
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__AuthWrite__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__AuthLoose__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__AuthLoose__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__InvitationRedeem__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__InvitationRedeem__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__WaitlistSignup__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__WaitlistSignup__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", "60");

        // Default öppen registrering i integration-tester. Kill-switch testas
        // isolerat av ClosedRegistrationsApiFactory.
        Environment.SetEnvironmentVariable("FeatureFlags__RegistrationsOpen", "true");

        using var scope = Services.CreateScope();
        // F6 P4 — pg_trgm krävs av F6P4aJobAdTrigramIndexes-migrationen. I prod
        // skapas extensionen av JobbPilot.Migrate `ensure-extensions`-mode
        // (master-creds, Phase A); test-harnessen replikerar det (Testcontainers
        // postgres-superuser kan CREATE EXTENSION). Idempotent.
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();

        // Deterministisk seeder-körning EFTER migrations (senior-cto-advisor
        // 2026-05-17, Approach D/B — fix the cause not the symptom). Bakgrund:
        // Services-property-access (raden ovan) triggar EnsureServer() → host-
        // start → ALLA IHostedService.StartAsync körs FÖRE dessa MigrateAsync
        // (web-verifierad .NET 10-semantik: MS Learn + dotnet/aspnetcore
        // #60370). TaxonomySnapshotSeeder + IdempotentAdminRoleSeeder träffar
        // då ett tomt schema → PostgresException 42P01 → seederns Dev/Test-
        // grace-period-catch → bail UTAN seed. StartAsync körs en gång per
        // host-livstid → taxonomy_concepts / Admin-rollen förblir oseeded för
        // hela den delade [Collection("Api")]-livstiden. Det är en latent
        // fixtur-defekt: fixturen bröt prod-kodens implicita kontrakt "schema
        // migrerat innan host-tjänster konsumerar det". Prod är opåverkad
        // (JobbPilot.Migrate kör DDL före Api-trafik, ADR 0043 Beslut B).
        //
        // Åtgärd: kör de två idempotenta seedrarna explicit EFTER att schemat
        // finns. Båda är idempotenta (TaxonomySnapshotSeeder: version-gate +
        // pg_advisory_xact_lock; IdempotentAdminRoleSeeder: RoleManager check-
        // and-insert) → säkra att re-invoke:a; den tidigare bailade host-
        // körningen är en no-op. RIKTAD på exakt dessa två typer (ej bred
        // GetServices-loop över godtyckliga IHostedService) så ingen annan
        // host-tjänst re-startas oavsiktligt.
        foreach (var hosted in Services.GetServices<IHostedService>())
        {
            if (hosted is TaxonomySnapshotSeeder or IdempotentAdminRoleSeeder)
                await hosted.StartAsync(CancellationToken.None);
        }
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
        Environment.SetEnvironmentVariable("RateLimiting__AuthLoose__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__AuthLoose__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__InvitationRedeem__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__InvitationRedeem__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__WaitlistSignup__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__WaitlistSignup__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", null);
        Environment.SetEnvironmentVariable("FeatureFlags__RegistrationsOpen", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}
