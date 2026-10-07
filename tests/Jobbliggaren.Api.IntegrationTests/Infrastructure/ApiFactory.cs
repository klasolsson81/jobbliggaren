using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Security;
using Jobbliggaren.Application.Admin.BackgroundJobs;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Access;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private readonly RedisBoundaryFixture _redisBoundary = new();

    // Real ACL identities and separate stores; placement is checked by VolatileRedisPlacementTests.

    // #241 — last-wins IEmailSender override so the host never composes the real transactional provider.
    // Held as a field (not just type-registered) so tests can read the recorded sends via Emails.
    private readonly RecordingEmailSender _emailSender = new();

    /// <summary>
    /// #241 — the recording <see cref="Jobbliggaren.Application.Common.Abstractions.IEmailSender"/>
    /// the host resolves. Lets a test positively assert an email side-effect (e.g. "a login code was
    /// queued to X") without the network, and locks out the real provider.
    /// </summary>
    internal RecordingEmailSender Emails => _emailSender;
    internal RedisBoundaryFixture RedisBoundary => _redisBoundary;

    private readonly LoginChallengeFaults _loginChallengeFaults = new();

    // ADR 0146 — disarmed unless a race test arms it; registered on the host's own AppDbContext.
    private readonly JobSeekerSaveRace _jobSeekerSaveRace = new();

    /// <summary>ADR 0146 — holds one user's job_seekers write until a second request has committed.</summary>
    internal JobSeekerSaveRace JobSeekerSaveRace => _jobSeekerSaveRace;

    // #1975 — disarmed unless a test arms them for one account: the address change's teardown and its audit row.
    private readonly SessionTeardownFaults _sessionTeardownFaults = new();
    private readonly AuditRowSaveFailure _auditRowSaveFailure = new();
    private readonly AccountEmailChangeStoreFaults _accountEmailChangeStoreFaults = new();
    internal AccountEmailChangeStoreFaults AccountEmailChangeStoreFaults => _accountEmailChangeStoreFaults;
    private readonly AccountAccessFlowGates _accountAccessFlowGates = new();
    internal AccountAccessFlowGates AccountAccessFlowGates => _accountAccessFlowGates;
    private readonly CommitAcknowledgementLoss _commitAcknowledgementLoss = new();
    internal CommitAcknowledgementLoss CommitAcknowledgementLoss => _commitAcknowledgementLoss;
    private readonly EmailChangeActivationFaults _emailChangeActivationFaults = new();
    internal EmailChangeActivationFaults EmailChangeActivationFaults => _emailChangeActivationFaults;

    /// <summary>#1975 — fails the session store's invalidation of one account, so the completion's teardown fails.</summary>
    internal SessionTeardownFaults SessionTeardownFaults => _sessionTeardownFaults;

    /// <summary>#1975 — refuses the save carrying one account's audit row of one event type.</summary>
    internal AuditRowSaveFailure AuditRowSaveFailure => _auditRowSaveFailure;

    // #1744 — Google's two server-side endpoints, scripted. The only thing the external-login path stubs.
    private readonly ScriptedGoogle _google = new();

    /// <summary>#1744 — the scripted token and userinfo endpoints the host's Google adapter calls.</summary>
    internal ScriptedGoogle Google => _google;

    /// <summary>#1744 — the host's client id at Google, as the test adapter sends it.</summary>
    internal const string GoogleClientId = "test-client-id.apps.googleusercontent.com";

    /// <summary>#1745 — the host's client id at GitHub, as the test adapter sends it.</summary>
    internal const string GitHubClientId = "Iv23-test-client-id";

    // Not shaped like a real secret. gitleaks:allow
    private const string GitHubClientSecret = "test-github-client-secret"; // gitleaks:allow

    // #1745 — GitHub's token, REST and revocation endpoints, scripted. The only thing the GitHub login path stubs.
    private readonly ScriptedGitHub _github = new(GitHubClientId, GitHubClientSecret);

    /// <summary>#1745 — the scripted endpoints the host's GitHub adapter calls.</summary>
    internal ScriptedGitHub GitHub => _github;

    /// <summary>#1746 — the host's client id at LinkedIn, as the test adapter sends it.</summary>
    internal const string LinkedInClientId = "li-test-client-id";

    // Not shaped like a real secret. gitleaks:allow
    private const string LinkedInClientSecret = "test-linkedin-client-secret"; // gitleaks:allow

    // #1746 — LinkedIn's token and userinfo endpoints, scripted. The only thing the LinkedIn login path stubs.
    private readonly ScriptedLinkedIn _linkedin = new(LinkedInClientId, LinkedInClientSecret);

    /// <summary>#1746 — the scripted endpoints the host's LinkedIn adapter calls.</summary>
    internal ScriptedLinkedIn LinkedIn => _linkedin;

    /// <summary>#1735 — puts the login challenge's Redis stores out of reach for a scope (the 503 rows).</summary>
    internal LoginChallengeFaults LoginChallengeFaults => _loginChallengeFaults;

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

    // Set in InitializeAsync before Services is accessed (triggers host creation)
    private string _postgresCs = string.Empty;
    private string _redisCs = string.Empty;
    private RedisTestEnvironment? _redisEnvironment;
    private string _volatileRedisCs = string.Empty;

    /// <summary>#1735 — the durable instance (sessions, cooldowns, caches), for a test that scans its keyspace.</summary>
    internal string DurableRedisConnectionString => _redisBoundary.OptionsFor(_redisBoundary.Persistent, RedisBoundaryFixture.Admin).ToString(true);

    /// <summary>#1735 — the non-persisted instance (login challenges, rate budgets), for the same purpose.</summary>
    internal string VolatileRedisConnectionString => _redisBoundary.OptionsFor(_redisBoundary.Volatile, RedisBoundaryFixture.Admin).ToString(true);

    // Replaces DbContext registrations (which are registered before ConfigureWebHost runs)
    // with Testcontainer connection strings. Redis is replaced the same way.
    // Everything Program.cs reads from builder.Configuration at service-registration time —
    // ASPNETCORE_ENVIRONMENT, ConnectionStrings, rate-limit overrides — is set as an
    // environment variable in InitializeAsync instead, because that read happens before
    // this ConfigureWebHost callback runs.
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

        // #1744 — a full Google client, as a developer's appsettings.Local.json carries one.
        builder.UseSetting("Auth:OAuth:Google:ClientId", GoogleClientId);
        builder.UseSetting("Auth:OAuth:Google:ClientSecret", "test-google-client-secret"); // gitleaks:allow

        // #1745 — a full GitHub client too, as a developer's appsettings.Local.json can carry one.
        builder.UseSetting("Auth:OAuth:GitHub:ClientId", GitHubClientId);
        builder.UseSetting("Auth:OAuth:GitHub:ClientSecret", GitHubClientSecret);

        // #1746 — and LinkedIn's. Without it the composition row would read a developer's own LinkedIn client from
        // appsettings.Local.json on that machine, and nothing in CI.
        builder.UseSetting("Auth:OAuth:LinkedIn:ClientId", LinkedInClientId);
        builder.UseSetting("Auth:OAuth:LinkedIn:ClientSecret", LinkedInClientSecret);

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

                // #692 — separate CV-review finding-fingerprint pepper (AddCvReview, reached by the
                // dual-host AddJobSources; fail-closed at startup via ValidateOnStart in all environments).
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
                    .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .AddInterceptors(
                        _jobSeekerSaveRace,
                        _auditRowSaveFailure,
                        _commitAcknowledgementLoss,
                        _emailChangeActivationFaults.AuditSaveFailure,
                        _emailChangeActivationFaults.CommitAcknowledgementLoss,
                        sp.GetRequiredService<ProtectedAccountTransactionInterceptor>(),
                        sp.GetRequiredService<Jobbliggaren.Infrastructure.Security.FieldEncryptionSaveChangesInterceptor>(),
                        sp.GetRequiredService<Jobbliggaren.Infrastructure.Security.FieldDecryptionMaterializationInterceptor>()));

            // Replace AppIdentityDbContext
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>((sp, options) =>
                options.UseNpgsql(_postgresCs, npgsql =>
                    {
                        npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                    })
                    .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .AddInterceptors(sp.GetRequiredService<ProtectedAccountTransactionInterceptor>()));


            // ADR 0066 (#802) — fält-krypteringen kör den riktiga
            // LocalDataKeyProvider (Provider=Local + master-nyckel injiceras via
            // ConfigureAppConfiguration ovan). Interceptor-paret anropar
            // Create/Unwrap i full Mediator-pipeline; round-trip +
            // per-användare-isolering bevaras end-to-end utan någon KMS-fake.

            // #241 — replace the configured IEmailSender (Console/Null/Scaleway per Email:Provider)
            // with a recording fake. Integration tests must never depend on a real external email
            // provider: if a gitignored appsettings.Local.json carries Email:Provider=Scaleway + live
            // API keys, the host would resolve ScalewayEmailSender and attempt a real send on any
            // email-success path — billed, delivered, or rejected against an @example.com recipient,
            // and green in CI, which has no Local.json. The same shape #220 measured against an
            // earlier provider. Forcing
            // Email__Provider=Console via env var does NOT win (Local.json is layered after env
            // vars); this last-wins singleton in ConfigureServices does. RemoveAll first so nothing
            // resolves the real sender even via GetServices<IEmailSender>().
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sp => new ActivationEmailSender(_emailSender,
                _emailChangeActivationFaults, sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()));

            // #1735 — the swap above removed the Development composition's login-code capture with the sender it
            // wrapped; wrap the recording sender the same way, or /dev/login-code is only ever tested on its 404.
            services.AddDevLoginCodeCapture();

            // #204 / TD-83 PR2 — replace the real HangfireBackgroundJobController (composed in the Api
            // root, wrapping Hangfire's IRecurringJobManager/IBackgroundJobClient/IMonitoringApi) with
            // a recording fake. The integration host bootstraps NO hangfire schema (Api runs
            // PrepareSchemaIfNecessary=false; the Worker owns bootstrap), so the real adapter would hit
            // a missing schema. The fake lets the trigger/retry audit + auth + outcome-mapping tests
            // exercise the full Mediator pipeline (validation/authorization/AuditBehavior/UnitOfWork).
            // RemoveAll first so nothing resolves the real adapter via GetServices<IBackgroundJobController>().
            services.RemoveAll<IBackgroundJobController>();
            services.AddSingleton<IBackgroundJobController>(_backgroundJobs);

            // #1735 — the login challenge's stores stay the real Redis adapters, wrapped so a test can put
            // them out of reach in place (LoginChallengeFaults).
            services.RemoveAll<IRateBudget>();
            services.AddSingleton<IRateBudget>(sp => new FaultableRateBudget(
                ActivatorUtilities.CreateInstance<RedisRateBudget>(sp), _loginChallengeFaults));
            services.RemoveAll<ILoginChallengeStore>();
            services.AddSingleton<ILoginChallengeStore>(sp => new ActivationLoginChallengeStore(
                new FaultableLoginChallengeStore(ActivatorUtilities.CreateInstance<RedisLoginChallengeStore>(sp),
                    _loginChallengeFaults), _emailChangeActivationFaults));
            services.RemoveAll<IGrantStore>();
            services.AddSingleton<IGrantStore>(sp => new FaultableGrantStore(
                ActivatorUtilities.CreateInstance<RedisGrantStore>(sp), _loginChallengeFaults, _accountAccessFlowGates));
            services.RemoveAll<IOAuthStateStore>();
            services.AddSingleton<IOAuthStateStore>(sp => new FaultableOAuthStateStore(
                ActivatorUtilities.CreateInstance<RedisOAuthStateStore>(sp), _loginChallengeFaults));
            services.RemoveAll<IAccountEmailChangeStore>();
            services.AddSingleton<IAccountEmailChangeStore>(sp => new FaultableAccountEmailChangeStore(
                ActivatorUtilities.CreateInstance<RedisAccountEmailChangeStore>(sp), _loginChallengeFaults,
                _accountEmailChangeStoreFaults));

            services.RemoveAll<IAccountAccessCoordinator>();
            services.AddScoped<IAccountAccessCoordinator>(sp => new GatedAccountAccessCoordinator(
                sp.GetRequiredService<SqlAccountAccess>(), _accountAccessFlowGates));

            // #1975 — the session store as production composes it, with one account's invalidation failable.
            services.RemoveAll<ISessionStore>();
            services.AddScoped<ISessionStore>(sp => new FaultableSessionStore(
                new AccessControlledSessionStore(
                    new SessionStoreResilienceDecorator(sp.GetRequiredService<RedisSessionStore>()),
                    sp.GetRequiredService<IAccountAccessReader>(),
                    sp.GetRequiredService<IAccountAccessCoordinator>()),
                _sessionTeardownFaults, _accountAccessFlowGates));

            // #1744 — the REAL Google adapter over ScriptedGoogle, handed to the handlers through RegisteredProviders
            // alone: the composition's own IExternalIdentityProvider registrations stay what they are, and no test
            // reaches Google. The redirect base is the host's own Email:BaseUrl.
            //
            // #1745 — and the REAL GitHub adapter over ScriptedGitHub, and #1746 the REAL LinkedIn adapter over
            // ScriptedLinkedIn. They stand in for what the gates compose on this host from its client ids
            // (ExternalLoginEndpointsTests pins that composition).
            services.RemoveAll<RegisteredProviders>();
            services.AddSingleton(sp =>
            {
                var callbacks = new ExternalLoginCallbacks(
                    new Uri(sp.GetRequiredService<IOptions<EmailOptions>>().Value.BaseUrl));
                return new RegisteredProviders(
                [
                    new GoogleIdentityProvider(
                        new NamedClientFactory(GoogleIdentityProvider.HttpClientName, _google),
                        Options.Create(new GoogleOAuthOptions
                        {
                            ClientId = GoogleClientId,
                            ClientSecret = "test-google-client-secret", // gitleaks:allow
                        }),
                        callbacks,
                        sp.GetRequiredService<ILogger<GoogleIdentityProvider>>()),
                    new LinkedInIdentityProvider(
                        new NamedClientFactory(LinkedInIdentityProvider.HttpClientName, _linkedin),
                        Options.Create(new LinkedInOAuthOptions
                        {
                            ClientId = LinkedInClientId,
                            ClientSecret = LinkedInClientSecret,
                        }),
                        callbacks,
                        sp.GetRequiredService<ILogger<LinkedInIdentityProvider>>()),
                    new GitHubIdentityProvider(
                        new NamedClientFactory(GitHubIdentityProvider.HttpClientName, _github),
                        Options.Create(new GitHubOAuthOptions
                        {
                            ClientId = GitHubClientId,
                            ClientSecret = GitHubClientSecret,
                        }),
                        callbacks,
                        sp.GetRequiredService<ILogger<GitHubIdentityProvider>>()),
                ]);
            });

            // ADR 0083 Amendment 2026-08-03 — the registration kill-switch defaults to CLOSED, so the
            // base host must pin it OPEN or every new account would be refused before it is created.
            // Pinned here rather than inherited from appsettings.Development.json: the harness must not
            // depend on a dev config file it does not own. The closed-registration host below registers
            // its PostConfigure AFTER this one and re-flips it.
            services.PostConfigure<AuthOptions>(o => o.RegistrationsOpen = true);
        });
    }

    private WebApplicationFactory<Program>? _registrationsClosedHost;
    private readonly object _registrationsClosedLock = new();

    /// <summary>
    /// ADR 0083 Amendment 2026-08-03 — an <see cref="HttpClient"/> against a host with the
    /// public-registration kill-switch forced CLOSED. This is the counterfactual: without a host that
    /// actually refuses, the base host's 200/202 assertions cannot tell a working gate from an absent
    /// one.
    /// </summary>
    internal HttpClient CreateRegistrationsClosedClient() => GetRegistrationsClosedHost().CreateClient();

    /// <summary>The cached registrations-CLOSED host behind <see cref="CreateRegistrationsClosedClient"/>.</summary>
    internal WebApplicationFactory<Program> GetRegistrationsClosedHost()
    {
        lock (_registrationsClosedLock)
        {
            _registrationsClosedHost ??= WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.PostConfigure<AuthOptions>(o => o.RegistrationsOpen = false);
                // Capture THIS host's boot records so the gate's announcement can be pinned against
                // the behaviour of the same host.
                services.AddSingleton<ILoggerProvider>(_closedHostLogs);
            }));
        }

        return _registrationsClosedHost;
    }

    private readonly CapturingLoggerProvider _closedHostLogs = new();

    /// <summary>
    /// Boot records from the closed-registration host. Per-host, not shared: the base
    /// <c>ConfigureWebHost</c> re-runs for every derived host, so one sink would mix every host's
    /// announcements into a single queue and an "announced once" assertion would read whichever
    /// host happened to boot first.
    /// </summary>
    internal IEnumerable<CapturedLog> ClosedHostLogs => _closedHostLogs.Logs;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redisBoundary.InitializeAsync().AsTask());

        _postgresCs = _postgres.GetConnectionString();
        _redisCs = _redisBoundary.OptionsFor(_redisBoundary.Persistent, RedisBoundaryFixture.ApiPersistent).ToString(true);
        _volatileRedisCs = _redisBoundary.OptionsFor(_redisBoundary.Volatile, RedisBoundaryFixture.ApiVolatile).ToString(true);

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
        _redisEnvironment = new RedisTestEnvironment(_redisCs, _volatileRedisCs);

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
        // #1681 del 2: GET /me/company-watch-criteria lämnade MeListRead och har en egen, medvetet
        // SNÄV budget (5 burst / 3 per minut uthålligt — se RateLimitingOptions.CompanyWatchCriteriaList).
        // Den är UserId-partitionerad, men flera tester i CompanyWatchCriteriaEndpointsTests träffar
        // rutten mer än fem gånger med SAMMA användare, så utan höjningen 429:ar sviten på sin egen
        // rate limit i stället för att mäta endpointen. Höjningen görs HÄR och inte genom att välja ett
        // rundare produktionstal: talet är härlett, och ett test får inte forma det.
        Environment.SetEnvironmentVariable(
            "RateLimiting__CompanyWatchCriteriaList__PermitLimit", "10000");
        Environment.SetEnvironmentVariable(
            "RateLimiting__CompanyWatchCriteriaList__WindowSeconds", "60");
        // #483 — HealthCheck is IP-partitioned FixedWindow like the anonymous policies above; the
        // shared [Collection("Api")] motions /api/ready (HealthCheckEndpointsTests + AdminRole*
        // readiness probes) through the same 127.0.0.1 bucket, so raise it too — else a future test
        // that polls readiness or higher parallelism could silently trip a 429 in an unrelated class.
        Environment.SetEnvironmentVariable("RateLimiting__HealthCheck__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__HealthCheck__WindowSeconds", "60");
        // #1974 — the admin account directory's reads share one bucket per admin, and a paging or sorting
        // test calls them more often than the burst allows.
        Environment.SetEnvironmentVariable("RateLimiting__AdminRead__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__AdminRead__WindowSeconds", "60");
        // #1979 — feedback: the two buckets raised for the shared collection, and the feature opened with
        // a reserved-domain recipient, so the gate reads Open while RecordingEmailSender can deliver.
        Environment.SetEnvironmentVariable("RateLimiting__FeedbackSubmit__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__FeedbackSubmit__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__FeedbackPromptState__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__FeedbackPromptState__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("Feedback__Enabled", "true");
        Environment.SetEnvironmentVariable("Feedback__NotificationRecipient", "feedback-operator@example.test");

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
        _accountEmailChangeStoreFaults.Dispose();
        _accountAccessFlowGates.Dispose();
        _emailChangeActivationFaults.Dispose();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        _redisEnvironment?.Dispose();
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
        Environment.SetEnvironmentVariable(
            "RateLimiting__CompanyWatchCriteriaList__PermitLimit", null);
        Environment.SetEnvironmentVariable(
            "RateLimiting__CompanyWatchCriteriaList__WindowSeconds", null);
        Environment.SetEnvironmentVariable("RateLimiting__HealthCheck__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__HealthCheck__WindowSeconds", null);

        await Task.WhenAll(_postgres.StopAsync(), _redisBoundary.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }
}
