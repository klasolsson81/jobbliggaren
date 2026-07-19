using System.Security.Cryptography;
using Jobbliggaren.Application.Admin.BackgroundJobs;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;

    // #241 — last-wins IEmailSender override so the host never composes the real Resend provider.
    // Held as a field (not just type-registered) so tests can read the recorded sends via Emails.
    private readonly RecordingEmailSender _emailSender = new();

    /// <summary>
    /// #241 — the recording <see cref="Jobbliggaren.Application.Common.Abstractions.IEmailSender"/>
    /// the host resolves. Lets a test positively assert an email side-effect (e.g. "a waitlist
    /// confirmation was queued to X") without the network, and locks out the real Resend provider.
    /// </summary>
    internal RecordingEmailSender Emails => _emailSender;

    // #204 / TD-83 PR2 — last-wins IBackgroundJobController override so the host never composes the
    // real HangfireBackgroundJobController. Held as a field so audit/outcome tests can read recorded
    // trigger calls and drive the next requeue outcome via Jobs.
    private readonly RecordingBackgroundJobController _backgroundJobs = new();

    /// <summary>
    /// #204 / TD-83 PR2 — the recording <see cref="Jobbliggaren.Application.Admin.BackgroundJobs.IBackgroundJobController"/>
    /// the host resolves. Lets the admin trigger/retry audit + outcome-mapping tests run the full
    /// Mediator pipeline WITHOUT a bootstrapped Hangfire schema (ApiFactory does not bootstrap it).
    /// </summary>
    internal RecordingBackgroundJobController Jobs => _backgroundJobs;

    // #616 — last-wins IBreachedPasswordChecker override so the host never composes the real HIBP
    // client (no network egress from tests). Held as a field so tests can steer verdicts per
    // password via BreachChecks.
    private readonly StubBreachedPasswordChecker _breachChecker = new();

    /// <summary>
    /// #616 — the stub <see cref="Jobbliggaren.Application.Common.Abstractions.IBreachedPasswordChecker"/>
    /// the host resolves. Defaults every password to NotBreached; a test opts a password into
    /// Breached/Unavailable via <c>SetVerdict</c> to exercise the rejection and fail-open paths.
    /// </summary>
    internal StubBreachedPasswordChecker BreachChecks => _breachChecker;

    // Set in InitializeAsync before Services is accessed (triggers host creation)
    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;

    public ApiFactory()
    {
        var rsa = RSA.Create(2048);
        _privateKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-test-private-{Guid.NewGuid()}.pem");
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jobbliggaren-test-public-{Guid.NewGuid()}.pem");
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

        // ADR 0066 (#802) — fält-krypteringen är Local-only. Provider läses via
        // configuration[...] vid DI-tid i AddPersistence, så det MÅSTE vara ett
        // config-värde (inte en service-override). In-memory-källan läggs sist i
        // ConfigureWebHost ⇒ vinner över Program.cs alla källor, inkl. en dev:s
        // gitignored appsettings.Local.json (som annars kan bära en stale
        // FieldEncryption-sektion). CI saknar Local.json → master-nyckeln nedan
        // är den enda källan och krävs (validatorn hård-failar på tom nyckel i
        // ALLA miljöer). Nyckeln är en deterministisk 32-byte test-nyckel —
        // round-trip kräver bara SAMMA nyckel inom host-livstiden.
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FieldEncryption:Provider"] = "Local",
                ["FieldEncryption:LocalMasterKeyBase64"] = TestSecrets.MasterKeyBase64,

                // #842 — Art. 17 audit pepper (fail-closed at startup, all environments).
                ["AuditPseudonymization:PepperBase64"] = TestSecrets.AuditPepperBase64,

                // #544 — separate company-watch pepper (fail-closed at startup, all environments).
                ["CompanyWatchPseudonymization:PepperBase64"] = TestSecrets.WatchPepperBase64,

                // #692 — separate CV-review finding-fingerprint pepper (AddCvReview, Api-only;
                // fail-closed at startup via ValidateOnStart in all environments).
                ["CvReviewFingerprintPseudonymization:PepperBase64"] = TestSecrets.FingerprintPepperBase64,
            }));

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
                    // #714 — a flag-ON test class builds an extra host via WithWebHostBuilder
                    // (CreateEmailConfirmationClient). Each derived host that re-AddDbContext's spins a
                    // fresh EF internal service provider; across the shared [Collection("Api")] that
                    // trips EF's process-wide ManyServiceProvidersCreatedWarning (>20 providers), which
                    // is thrown-by-default and cascades to unrelated tests. Ignoring it is the
                    // EF-team-sanctioned accommodation for WebApplicationFactory suites (test-only; prod
                    // DbContext config is separate and unaffected).
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .AddInterceptors(
                        sp.GetRequiredService<Jobbliggaren.Infrastructure.Security.FieldEncryptionSaveChangesInterceptor>(),
                        sp.GetRequiredService<Jobbliggaren.Infrastructure.Security.FieldDecryptionMaterializationInterceptor>()));

            // Replace AppIdentityDbContext
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseNpgsql(_postgresCs, npgsql =>
                    {
                        npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                    })
                    // #714 — same rationale as AppDbContext above.
                    .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

            // Replace Redis cache
            services.RemoveAll<IDistributedCache>();
            services.AddStackExchangeRedisCache(opts =>
            {
                opts.Configuration = _redisCs;
                opts.InstanceName = "jobbliggaren:";
            });

            // ADR 0066 (#802) — fält-krypteringen kör den riktiga
            // LocalDataKeyProvider (Provider=Local + master-nyckel injiceras via
            // ConfigureAppConfiguration ovan). Interceptor-paret anropar
            // Create/Unwrap i full Mediator-pipeline; round-trip +
            // per-användare-isolering bevaras end-to-end utan någon KMS-fake.

            // #241 — replace the configured IEmailSender (Console/Null/Resend per Email:Provider)
            // with a recording fake. Integration tests must never depend on a real external email
            // provider: locally a gitignored appsettings.Local.json carries Email:Provider=Resend +
            // a live key, so the host would resolve ResendEmailSender → 403 test-mode → 500 on any
            // email-success path (the #220 residual; green in CI, which has no Local.json). Forcing
            // Email__Provider=Console via env var does NOT win (Local.json is layered after env
            // vars); this last-wins singleton in ConfigureServices does. RemoveAll first so nothing
            // resolves the real sender even via GetServices<IEmailSender>().
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_emailSender);

            // #204 / TD-83 PR2 — replace the real HangfireBackgroundJobController (composed in the Api
            // root, wrapping Hangfire's IRecurringJobManager/IBackgroundJobClient/IMonitoringApi) with
            // a recording fake. The integration host bootstraps NO hangfire schema (Api runs
            // PrepareSchemaIfNecessary=false; the Worker owns bootstrap), so the real adapter would hit
            // a missing schema. The fake lets the trigger/retry audit + auth + outcome-mapping tests
            // exercise the full Mediator pipeline (validation/authorization/AuditBehavior/UnitOfWork).
            // RemoveAll first so nothing resolves the real adapter via GetServices<IBackgroundJobController>().
            services.RemoveAll<IBackgroundJobController>();
            services.AddSingleton<IBackgroundJobController>(_backgroundJobs);

            // #616 — replace the real HIBP typed client (AddBreachedPasswordCheck) with the stub.
            // Every register/change-password test funnels through PwnedPasswordValidator inside
            // UserManager, so without this override each of those tests would make a live call to
            // api.pwnedpasswords.com. RemoveAll first so nothing resolves the real client even via
            // GetServices<IBreachedPasswordChecker>().
            services.RemoveAll<IBreachedPasswordChecker>();
            services.AddSingleton<IBreachedPasswordChecker>(_breachChecker);

            // #714 — pin email-confirmation-first OFF for the base host. The factory forces
            // Development env (above), which loads appsettings.Development.json where the flag is ON
            // (dev runs the confirmation-first flow). Without this override every [Collection("Api")]
            // test would inherit the flag → RegisterAndGetSessionIdAsync (142 sites) would get a 202
            // with no sessionId and break. PostConfigure runs after config binding and wins; the
            // flag-ON test classes re-flip it ON per class via WithWebHostBuilder + PostConfigure(true),
            // which registers AFTER this and therefore takes precedence (CTO-bind Risk 3).
            services.PostConfigure<AuthOptions>(o => o.RequireEmailConfirmation = false);
        });
    }

    private WebApplicationFactory<Program>? _emailConfirmationHost;
    private readonly object _emailConfirmationLock = new();

    /// <summary>
    /// #714 — an <see cref="HttpClient"/> against a host with email-confirmation-first registration
    /// forced ON (<c>Auth:RequireEmailConfirmation</c>). The derived host is built ONCE and cached, so
    /// all flag-ON test classes SHARE it: building one per class would each spin a fresh EF internal
    /// service provider and, across the shared <c>[Collection("Api")]</c>, trip EF's process-wide
    /// <c>ManyServiceProvidersCreatedWarning</c> (&gt;20 providers) → cascade failures. It reuses this
    /// factory's Testcontainers + the shared <c>RecordingEmailSender</c> (so <c>factory.Emails</c> still
    /// captures its sends). The base host pins the flag OFF (PostConfigure above); this
    /// PostConfigure(true) is registered after it and therefore wins.
    /// </summary>
    internal HttpClient CreateEmailConfirmationClient()
    {
        lock (_emailConfirmationLock)
        {
            _emailConfirmationHost ??= WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.PostConfigure<AuthOptions>(o => o.RequireEmailConfirmation = true)));
        }

        return _emailConfirmationHost.CreateClient();
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
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", "60");
        // Pre-4 STEG 5 (TD-87 + TD-92): höj de tre nya /me-policyerna så delade
        // [Collection("Api")]-tester inte rate-limit:as. MeListRead/MeWrite är
        // UserId-partitionerade (unik user per test → unik bucket, vanligen säkra)
        // men JobAdStatusBatch:s anonyma ip:-fallback delar 127.0.0.1-bucketen över
        // alla tester som anonymt träffar POST /me/job-ad-status → måste höjas.
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__WindowSeconds", "60");
        // F4-13 (ADR 0076 Decision 5): POST /me/job-ad-match-tags har samma anonym-toleranta
        // dual-partition (JobAdMatchBatchPolicy) som status-batchen — den anonyma ip:-fallbacken
        // delar 127.0.0.1-bucketen över ALLA tester som anonymt träffar /me/job-ad-match-tags,
        // så även denna policy måste höjas för att inte rate-limit:a den delade [Collection("Api")].
        Environment.SetEnvironmentVariable("RateLimiting__JobAdMatchBatch__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__JobAdMatchBatch__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__WindowSeconds", "60");

        using var scope = Services.CreateScope();
        // F6 P4 — pg_trgm krävs av F6P4aJobAdTrigramIndexes-migrationen. I prod
        // skapas extensionen av Jobbliggaren.Migrate `ensure-extensions`-mode
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
        // (Jobbliggaren.Migrate kör DDL före Api-trafik, ADR 0043 Beslut B).
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
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__ListRead__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeListRead__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__JobAdStatusBatch__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__JobAdMatchBatch__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__JobAdMatchBatch__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__MeWrite__WindowSeconds", null);

        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
        if (File.Exists(_publicKeyPath)) File.Delete(_publicKeyPath);

        await Task.WhenAll(_postgres.StopAsync(), _redis.StopAsync());
        await base.DisposeAsync();
    }
}
