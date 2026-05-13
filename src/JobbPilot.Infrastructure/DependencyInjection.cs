using System.Security.Cryptography;
using System.Threading.RateLimiting;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Infrastructure.Auditing;
using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Infrastructure.Auth;
using JobbPilot.Infrastructure.Auth.Auditing;
using JobbPilot.Infrastructure.Auth.Sessions;
using JobbPilot.Infrastructure.Email;
using JobbPilot.Infrastructure.FeatureFlags;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Invitations;
using JobbPilot.Infrastructure.JobSources.Platsbanken;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.RateLimiting;
using Refit;
using StackExchange.Redis;

namespace JobbPilot.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Composition-root entry för Api. Registrerar alla Infrastructure-moduler.
    /// Worker använder INTE denna metod — Worker anropar bara <see cref="AddPersistence"/>
    /// + egna stub-implementationer av audit-portarna (per ADR 0022 + ADR 0023 / STEG 9).
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddIdentityAndSessions(configuration);
        services.AddHttpAuditing();
        services.AddInvitationsAndEmail(configuration);
        services.AddJobSources(configuration);
        return services;
    }

    /// <summary>
    /// F2-P8b (ADR 0032). Registrerar Refit-baserad <c>IJobTechSearchClient</c>,
    /// typed <c>IJobTechStreamClient</c>, <see cref="JobTechPayloadSanitizer"/>
    /// (singleton), och <see cref="PlatsbankenJobSource"/> som
    /// <see cref="IJobSource"/>. Resilience-pipelinen (retry+CB) appliceras på
    /// Search-klienten via Microsoft.Extensions.Http.Resilience; Stream-klienten
    /// får custom pipeline (RateLimiter → Retry → CB) per dotnet-architect
    /// 2026-05-12: JobStream:s hårda 1-req/min-gräns kräver proaktiv throttling.
    /// </summary>
    public static IServiceCollection AddJobSources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JobTechOptions>()
            .Bind(configuration.GetSection(JobTechOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Application-ägt retention-kontrakt (JobSourceRetentionOptions) binds
        // mot samma section som JobTechOptions så Application-jobben
        // (PurgeStaleRawPayloadsJob) inte behöver bero på Infrastructure-typen.
        // RawPayloadRetentionDays-keyn matchar mellan typerna (default 30).
        services.AddOptions<JobSourceRetentionOptions>()
            .Bind(configuration.GetSection(JobTechOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // JobSearch (Refit) — klassisk REST/JSON. Standard resilience-pipeline
        // (retry+CB+timeout) räcker här eftersom JobSearch saknar publicerad
        // rate-limit (429 endast vid abuse).
        services.AddRefitClient<IJobTechSearchClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<JobTechOptions>>().Value;
                client.BaseAddress = new Uri(options.JobSearchBaseUrl);
                ApplyApiKey(client, options);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.BackoffType = DelayBackoffType.Exponential;
                o.CircuitBreaker.MinimumThroughput = 5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            });

        // JobStream (typed) — NDJSON snapshot + stream. Custom resilience-pipeline
        // med RateLimiter FÖRE retry så 429 inte eskaleras inom samma minut.
        // ADR 0032 §1 + JobTech 1-req/min-gräns (web-verifierat 2026-05-12).
        services.AddHttpClient<IJobTechStreamClient, JobTechStreamClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<JobTechOptions>>().Value;
            client.BaseAddress = new Uri(options.JobStreamBaseUrl);
            ApplyApiKey(client, options);
            // Snapshot kan vara ~50-100 MB; HttpClient default 100s räcker vid normal
            // hastighet men höjs för säkerhets skull.
            client.Timeout = TimeSpan.FromMinutes(5);
            // sec-Min-3: DoS-skydd mot ondskefullt stor respons (10 GB OOM-attack).
            // 500 MB cap är 5-10× förväntad snapshot-storlek per JobTech-docs.
            client.MaxResponseContentBufferSize = 500_000_000;
        })
        .AddResilienceHandler("jobstream", builder =>
        {
            // Rate-limiter FÖRE retry så retries räknas mot samma 1-req/min-fönster
            // (annars eskaleras 429 vid första försök). Polly v8 wrappar
            // System.Threading.RateLimiting.RateLimiter direkt — async hela vägen.
            builder.AddRateLimiter(_streamRateLimiter);
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
            });
            builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromMinutes(5),
            });
        });

        services.AddScoped<IJobSource, PlatsbankenJobSource>();

        // F2-P8c: Application-orchestrator-jobb. Stream + Purge konsumeras
        // exklusivt av Hangfire (ActivatorUtilities löser konstruktor utan
        // DI-registrering), men Snapshot-jobbet konsumeras även av
        // SyncPlatsbankenSnapshotCommandHandler (admin-trigger) via Mediator →
        // måste vara DI-registrerad. Stream + Purge registreras för symmetri
        // + test-discoverability via IServiceProvider.GetService.
        services.AddScoped<JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken.SyncPlatsbankenStreamJob>();
        services.AddScoped<JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken.SyncPlatsbankenSnapshotJob>();
        services.AddScoped<JobbPilot.Application.JobAds.Jobs.PurgeRawPayloads.PurgeStaleRawPayloadsJob>();

        return services;
    }

    // Process-wide rate-limiter för JobStream (1 req/min, ingen queue). FixedWindow
    // är rätt val per dotnet-architect 2026-05-12 — vi accepterar att burst-anrop
    // misslyckas direkt (QueueLimit=0), bättre än att queueras och eskalera till
    // JobTech:s 429.
    //
    // TESTBARHETSNOT (code-reviewer 2026-05-12 Min-3): static-livscykel betyder att
    // alla tester som använder hela DI-stacken delar samma limiter över hela test-
    // körningen. Resilience-tester (JobTechStreamResilienceTests) bygger därför
    // egen DI-container UTAN denna limiter — de testar bara retry/CB-pipelinen.
    // P8c-Hangfire-jobben kommer dela samma limiter i prod, vilket är den
    // önskade semantiken. IDisposable-warning vid host-shutdown är accepterad
    // bagatell — limitern lever app-lifetime.
    private static readonly FixedWindowRateLimiter _streamRateLimiter = new(
        new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static void ApplyApiKey(HttpClient client, JobTechOptions options)
    {
        // SECURITY-NOTE (security-auditor 2026-05-12 Min-2): api-key skickas via
        // DefaultRequestHeaders.TryAddWithoutValidation. Microsoft.Extensions.Http
        // EventSource-tracing kan teoretiskt logga request-headers vid aktiverad
        // diagnostik — vi aktiverar den inte i prod (Serilog enrichers strippar
        // headers redan). JobTech-api-key ger högre rate-limit på publikt
        // data — låg blast-radius om läckt.
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("api-key", options.ApiKey);

        client.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
    }

    /// <summary>
    /// F2-P0d (ADR 0005 amendment 2026-05-12). Registrerar
    /// <see cref="IInvitationTokenGenerator"/> (HMAC-SHA256) +
    /// <see cref="IEmailSender"/> (Console default; Ses framtida TD-69).
    /// Bindas inte i Worker — invitation-utskick sker bara från Api-pipeline.
    /// </summary>
    public static IServiceCollection AddInvitationsAndEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<InvitationTokenOptions>(
            configuration.GetSection(InvitationTokenOptions.SectionName));
        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));
        services.Configure<FeatureFlagsOptions>(
            configuration.GetSection(FeatureFlagsOptions.SectionName));

        services.AddSingleton<IInvitationTokenGenerator, InvitationTokenGenerator>();
        services.AddSingleton<IFeatureFlags, OptionsFeatureFlags>();

        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Console";
        if (string.Equals(emailProvider, "Console", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        }
        else if (string.Equals(emailProvider, "Ses", StringComparison.OrdinalIgnoreCase))
        {
            var region = configuration[$"{EmailOptions.SectionName}:AwsRegion"] ?? "eu-north-1";
            services.AddSingleton<Amazon.SimpleEmailV2.IAmazonSimpleEmailServiceV2>(
                _ => new Amazon.SimpleEmailV2.AmazonSimpleEmailServiceV2Client(
                    Amazon.RegionEndpoint.GetBySystemName(region)));
            services.AddSingleton<IEmailSender, Email.SesEmailSender>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Email:Provider='{emailProvider}' stöds inte. Använd 'Console' eller 'Ses'.");
        }

        return services;
    }

    /// <summary>
    /// Persistence-modul: <see cref="AppDbContext"/>, <see cref="IAppDbContext"/>,
    /// <see cref="IDateTimeProvider"/>. Ingen HTTP-bagage, ingen Identity, ingen Redis.
    /// Worker registrerar denna modul + egna audit-port-stubs.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // Provider-specifik DbUpdateException-analys (ADR 0032 §5). Singleton —
        // stateless. Konsumeras av UpsertExternalJobAdCommandHandler för
        // Postgres 23505-detection utan att Application får Npgsql-beroende.
        services.AddSingleton<IDbExceptionInspector, DbExceptionInspector>();

        // Audit-bypass-portar (ADR 0024 D1+D3). Båda anropas från Worker
        // (AuditLogRetentionJob + HardDeleteAccountsJob) — registreras därför här
        // i AddPersistence, inte i HTTP-only-extensionerna. Lifetime Scoped:
        // följer IAppDbContext-livscykeln.
        services.AddScoped<IAuditPartitionMaintainer, AuditPartitionMaintainer>();
        services.AddScoped<IAuditTrailEraser, AuditTrailEraser>();

        // IP-anonymisering (ADR 0024 D7). Stateless BCL-baserad helper —
        // singleton. Konsumeras av RequestContextProvider (audit-pipeline) och
        // AuthAuditLogger (app-logg) så samma /24+/48-maskning gäller överallt.
        // Registrerad i AddPersistence eftersom Worker-stub:ar inte använder
        // den men ingen kostnad finns att ha den tillgänglig.
        services.AddSingleton<IIpAnonymizer, IpAnonymizer>();

        // Failed-access-logger (ADR 0031 / TD-67). Strukturerad ILogger-wrapper —
        // stateless, singleton. Konsumeras av Application-handlers vid
        // ownership-mismatch för CloudWatch-baserad anomaly-detection (TD-68).
        services.AddSingleton<IFailedAccessLogger, FailedAccessLogger>();

        return services;
    }

    /// <summary>
    /// Identity, sessions, JWT-rester, Redis, HTTP-baserad <see cref="ICurrentUser"/>,
    /// auth audit logger. HTTP-only. Worker laddar inte denna modul.
    /// </summary>
    public static IServiceCollection AddIdentityAndSessions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Redis saknas i konfiguration.");

        services.AddDbContext<AppIdentityDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                })
                .UseSnakeCaseNamingConvention());

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(opts =>
            {
                // NIST SP 800-63B: längd är primärt skydd, komplexitet sekundärt.
                // PwnedPasswords-integration planeras för Fas 1 (MAJOR-1, security-audit 2026-04-20).
                opts.Password.RequiredLength = 12;
                opts.Password.RequireNonAlphanumeric = false;
                opts.Password.RequireDigit = false;
                opts.Password.RequireUppercase = false;
                opts.Password.RequireLowercase = false;
                opts.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = redisConnectionString;
            opts.InstanceName = "jobbpilot:";
        });

        // IConnectionMultiplexer registreras separat så RedisSessionStore kan
        // använda Redis SET-kommandon (SADD/SREM/SMEMBERS) för secondary user-
        // sessions-index — krävs för InvalidateAllForUserAsync vid kontoradering
        // (ADR 0024 D4 + ADR 0017 deferred-not stängd här). IDistributedCache
        // stödjer bara key-value, inte SET. Singleton — lazy connect, fungerar
        // även om Redis är ner vid app-start.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));

#pragma warning disable JOBBPILOT0001 // JwtSettings och RsaSecurityKey bevaras för RefreshCommandHandler tills Fas 1, ADR 0017
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        // Singleton RSA-nyckel — läses en gång, återanvänds per token-generering.
        // Förhindrar CNG-handle-läcka vid RSA.Create() per anrop.
        services.AddSingleton<RsaSecurityKey>(sp =>
        {
            var jwt = sp.GetRequiredService<IOptions<JwtSettings>>().Value;
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(jwt.PrivateKeyPath));
            return new RsaSecurityKey(rsa);
        });
#pragma warning restore JOBBPILOT0001

        services.Configure<SessionStoreOptions>(configuration.GetSection(SessionStoreOptions.SectionName));

        // Admin-bootstrap: idempotent seeder kör vid app-startup. Skapar Admin-rollen
        // om saknas och tilldelar till user med email AdminBootstrap__InitialAdminEmail.
        // Senior-cto-advisor-beslut 2026-05-11 (B1 — IaC over manual psql-script).
        services.Configure<AdminBootstrapOptions>(configuration.GetSection(AdminBootstrapOptions.SectionName));
        services.AddHostedService<IdempotentAdminRoleSeeder>();

#pragma warning disable JOBBPILOT0001 // JWT-klasser bevaras för RefreshCommandHandler tills Fas 1, ADR 0017
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAccessTokenRevocationStore, RedisAccessTokenRevocationStore>();
#pragma warning restore JOBBPILOT0001

        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<ISessionStore, RedisSessionStore>();
        services.AddScoped<IUserAccountService, UserAccountService>();

        // H-3 SoC-split (arch-audit 2026-05-11): role-fetch flyttad från
        // SessionAuthenticationHandler till IClaimsTransformation. Körs efter auth,
        // före authorization-policy-utvärdering. Per-request-fetch bibehållen.
        services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation,
            SessionRoleClaimsTransformation>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuthAuditLogger, AuthAuditLogger>();

        return services;
    }

    /// <summary>
    /// HTTP-only audit-portar: <see cref="ICorrelationIdProvider"/> +
    /// <see cref="IRequestContextProvider"/>. Implementationerna beror på
    /// <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/> och får aldrig
    /// laddas i Worker — Worker registrerar egna stubs (per ADR 0022 + ADR 0023 / STEG 9).
    /// </summary>
    public static IServiceCollection AddHttpAuditing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationIdProvider, CorrelationIdProvider>();
        services.AddScoped<IRequestContextProvider, RequestContextProvider>();
        return services;
    }

    /// <summary>
    /// HTTP-fri Identity-modul för Worker. Registrerar
    /// <see cref="AppIdentityDbContext"/>, AspNet IdentityCore (UserManager +
    /// UserStore — utan cookies/sessions/JWT/SignInManager), och de portar
    /// som <see cref="HardDeleteAccountsJob"/> behöver för att radera
    /// Identity-rader vid GDPR Art. 17-cascade (ADR 0024 D6).
    ///
    /// Skiljer sig från <see cref="AddIdentityAndSessions"/> genom att INTE
    /// dra in HTTP-bagage (cookies, AuthenticationScheme, JWT, IHttpContextAccessor).
    /// Får anropas EXKLUSIVT av Worker-composition-roten — Api laddar
    /// AddIdentityAndSessions istället, som täcker fullt Identity-stack
    /// inklusive HTTP. Att anropa båda i samma DI-container ger duplicerade
    /// registreringar.
    /// </summary>
    public static IServiceCollection AddCoreIdentityForWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        services.AddDbContext<AppIdentityDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                })
                .UseSnakeCaseNamingConvention());

        // AddIdentityCore<TUser>() registrerar UserManager + UserStore utan
        // AuthenticationScheme/Cookies/SignInManager — HTTP-fritt.
        // AddDefaultTokenProviders() utelämnas medvetet — token-providers
        // (password-reset, email-confirm) kräver IDataProtectionProvider
        // som är HTTP-bagage. Worker behöver bara CreateAsync/FindByIdAsync/
        // DeleteAsync vilka inte använder token-providers.
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppIdentityDbContext>();

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IAccountHardDeleter, AccountHardDeleter>();

        return services;
    }
}
