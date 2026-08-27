using System.Net.Http;
using System.Threading.RateLimiting;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Auditing;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Auditing;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.JobSources;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Security.BreachCheck;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.RateLimiting;
using Refit;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The Identity token-provider name for password-reset tokens (#1171). Named rather than
    /// literal per CLAUDE.md §5 (no magic strings): the value has to be byte-identical in the
    /// <c>opts.Tokens.PasswordResetTokenProvider</c> assignment and in the matching
    /// <c>AddTokenProvider</c> call, and a typo in either would silently fall back to a provider
    /// with the shared 24h lifespan rather than fail. It is an Identity registry key, distinct from
    /// <see cref="PasswordResetTokenProviderOptions.Name"/>, which is the DataProtector purpose.
    /// </summary>
    private const string PasswordResetTokenProviderName = "PasswordReset";

    /// <summary>
    /// Composition-root entry för Api. Registrerar alla Infrastructure-moduler.
    /// Worker använder INTE denna metod — Worker anropar bara <see cref="AddPersistence"/>
    /// + egna stub-implementationer av audit-portarna (per ADR 0022 + ADR 0023 / STEG 9).
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddPersistence(configuration);
        services.AddIdentityAndSessions(configuration);
        services.AddHttpAuditing();
        services.AddEmailSender(configuration, environment);
        services.AddJobSources(configuration);
        services.AddCompanyRegistry(configuration, environment);
        services.AddLandingStats();
        services.AddTextAnalysis();
        services.AddCvParsing();
        services.AddDevOnlyTestingSupport(environment);
        return services;
    }

    /// <summary>
    /// DEV-ONLY testing support (#796) — REMOVE BEFORE LAUNCH (Klas). Registers the
    /// token-free confirmed-login seam (<see cref="Jobbliggaren.Application.Dev.Abstractions.IDevEmailConfirmer"/>,
    /// implemented by <see cref="Auth.DevEmailConfirmer"/> over <c>UserManager</c>) used
    /// by the Playwright E2E suite to obtain a confirmed, login-capable user against a
    /// flag-ON backend without a real email round-trip.
    ///
    /// <para>
    /// Registered ONLY in Development — the FIRST of two independent structural gates
    /// (the SECOND is the <c>Program.cs</c> <c>IsDevelopment()</c> gate on the
    /// <c>/api/v1/dev/*</c> endpoint map). The predicate is <c>IsDevelopment()</c>
    /// exactly (not <c>|| IsEnvironment("Test")</c>) so it mirrors the endpoint map-gate
    /// one-for-one: in any deployed environment the port is absent from the container
    /// and <c>ConfirmEmailDevCommandHandler</c> cannot resolve (fail-closed).
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevOnlyTestingSupport(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        // NOTE on fail-closed: Mediator registers ConfirmEmailDevCommandHandler
        // unconditionally, but its IDevEmailConfirmer dependency is registered ONLY here
        // (Development). Outside Development the handler is dead — it can only throw at
        // Send-time (an unreachable path, since the endpoint is also unmapped), NOT at
        // container-build time, because the Api host leaves ValidateOnBuild off outside
        // Development. If the Api is ever hardened to force ValidateOnBuild=true in all
        // environments, this dead handler would turn a deployed boot into a startup crash
        // — remove the whole dev-seam before then (REMOVE BEFORE LAUNCH).
        if (environment.IsDevelopment())
            services.AddScoped<
                Jobbliggaren.Application.Dev.Abstractions.IDevEmailConfirmer,
                Auth.DevEmailConfirmer>();

        return services;
    }

    /// <summary>
    /// #454 (ADR 0088 D3/D6) — company-registry module: binds
    /// <see cref="CompanyRegistry.CompanyRegistryOptions"/> and registers
    /// <c>ICompanyRegistry</c> as a read-through cache decorator
    /// (<see cref="CompanyRegistry.CachedCompanyRegistry"/>, Redis via <c>IDistributedCache</c>)
    /// over the provider selected by <c>CompanyRegistry:Provider</c>: <c>Fake</c> (dev/test
    /// allow-list, mirror <see cref="AddEmailSender"/>'s Console gating — falls back to Null
    /// elsewhere) or <c>Off</c>/missing → <see cref="CompanyRegistry.NullCompanyRegistry"/> (always
    /// Unavailable — the prod-dark backstop until the real SCB adapter lands; fail-CIVIC: the
    /// lookup endpoint degrades, never crashes). Unknown values fail-stop. NO HttpClient in v1 —
    /// the SCB adapter (Sept-2026 API-key API, DPIA-#456-gated) arrives as a follow-up provider
    /// value with its own resilience pipeline + PROCESS-WIDE upstream limiter (10 calls/10 s per
    /// API-Id — a per-user endpoint policy cannot protect a per-API-Id budget).
    /// <para>
    /// <c>IDistributedCache</c> förutsätts registrerad av anroparen (Api via
    /// <see cref="AddIdentityAndSessions"/> — parity <see cref="AddLandingStats"/>-noten). Worker
    /// anropar INTE denna modul (company-watch-scannen är registry-fri, ADR 0088).
    /// </para>
    /// </summary>
    public static IServiceCollection AddCompanyRegistry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<CompanyRegistry.CompanyRegistryOptions>()
            .Bind(configuration.GetSection(CompanyRegistry.CompanyRegistryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var provider = configuration[
            $"{CompanyRegistry.CompanyRegistryOptions.SectionName}:Provider"]
            ?? CompanyRegistry.CompanyRegistryOptions.ProviderOff;

        if (string.Equals(provider, CompanyRegistry.CompanyRegistryOptions.ProviderFake,
                StringComparison.OrdinalIgnoreCase))
        {
            // Dev/Test allow-list (mirror ConsoleEmailSender): fixture-tabellen får aldrig
            // maskera sig som register-sanning utanför dev/test — annars Null.
            if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
                services.AddSingleton<CompanyRegistry.FakeCompanyRegistry>();
            else
                services.AddSingleton<CompanyRegistry.NullCompanyRegistry>();
        }
        else if (string.Equals(provider, CompanyRegistry.CompanyRegistryOptions.ProviderOff,
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<CompanyRegistry.NullCompanyRegistry>();
        }
        else
        {
            throw new InvalidOperationException(
                $"CompanyRegistry:Provider='{provider}' stöds inte i v1. Använd 'Fake' eller 'Off'.");
        }

        // Decorator-wiring: porten resolvar till cache-dekoratorn över den inre providern som
        // switchen registrerade (Fake om registrerad, annars Null). Scoped — port-konsumenten
        // (handlern) är scoped; dekoratorn själv är stateless.
        services.AddScoped<Jobbliggaren.Application.Companies.Abstractions.ICompanyRegistry>(sp =>
        {
            Jobbliggaren.Application.Companies.Abstractions.ICompanyRegistry innerProvider =
                (Jobbliggaren.Application.Companies.Abstractions.ICompanyRegistry?)
                    sp.GetService<CompanyRegistry.FakeCompanyRegistry>()
                ?? sp.GetRequiredService<CompanyRegistry.NullCompanyRegistry>();
            return new CompanyRegistry.CachedCompanyRegistry(
                innerProvider,
                sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
                sp.GetRequiredService<IOptions<CompanyRegistry.CompanyRegistryOptions>>());
        });

        return services;
    }

    /// <summary>
    /// #560 (ADR 0091) — SCB company-register POPULATION module (Worker-only; deliberately NOT part of
    /// <see cref="AddInfrastructure"/> — the Api never populates, only the Worker's recurring job does).
    /// Registers the refresh orchestrator (<see cref="IScbCompanyRegisterRefresher"/>), the bulk store,
    /// and the partition planner unconditionally, and wires the REAL cert-based client ONLY when
    /// <c>ScbRegister:Enabled=true</c> (otherwise <see cref="NullScbCompanyRegisterSource"/> — the
    /// certificate is never touched in CI / cert-less dev). The typed HttpClient gets the client
    /// certificate (loaded from the Windows cert-store by thumbprint — no password in config) plus a
    /// PROCESS-WIDE 10-calls/10-s rate limiter FIRST in the resilience pipeline: a per-endpoint policy
    /// cannot protect SCB's per-API-Id budget, and a breach risks a ban (§12 STOPP condition).
    /// </summary>
    public static IServiceCollection AddScbCompanyRegister(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ScbRegisterOptions>()
            .Bind(configuration.GetSection(ScbRegisterOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ScbCompanyRegisterStore>();
        services.AddScoped<IScbCompanyRegisterRefresher, ScbCompanyRegisterRefresher>();

        var enabled = configuration.GetValue<bool>($"{ScbRegisterOptions.SectionName}:Enabled");
        if (!enabled)
        {
            // Prod-dark / CI: no SCB source, no certificate loaded. The refresh job no-ops.
            services.AddSingleton<IScbCompanyRegisterSource, NullScbCompanyRegisterSource>();
            return services;
        }

        var thumbprint = configuration[$"{ScbRegisterOptions.SectionName}:CertThumbprint"];
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException(
                "ScbRegister:Enabled=true kräver ScbRegister:CertThumbprint (gitignored appsettings.Local.json " +
                "eller env-override ScbRegister__CertThumbprint). Certet får aldrig committas (ADR 0091).");
        }

        services.AddSingleton<ScbClientCertificateProvider>();
        services.AddHttpClient<IScbCompanyRegisterSource, ScbCompanyRegisterClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<ScbRegisterOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromMinutes(opts.HttpTimeoutMinutes);
            })
            // Load the client cert once and keep the handler for the app lifetime — the ~1–3 h run must
            // not rotate the handler mid-flight (which would reload the cert repeatedly).
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var cert = sp.GetRequiredService<ScbClientCertificateProvider>().Load();
                var handler = new HttpClientHandler { ClientCertificateOptions = ClientCertificateOption.Manual };
                handler.ClientCertificates.Add(cert);
                return handler;
            })
            .AddResilienceHandler("scb-register", builder =>
            {
                // Rate-limiter registered FIRST = Polly-outermost (the framework's default order): it
                // paces NEW pipeline executions to <=6/10 s. Retries run INSIDE a single acquired permit
                // and do not re-acquire, so the <=6-calls/10-s ceiling to SCB is upheld by the SEQUENTIAL
                // single-in-flight client (exec N+1 awaits exec N's retries) + exponential backoff +
                // 429-fail-fast (ScbRetryPolicy), not by per-attempt throttling (parity jobstream order).
                builder.AddRateLimiter(_scbRegisterRateLimiter);
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    // Fail fast on HTTP 429 (ScbRetryPolicy): SCB has explicitly signalled overload, so
                    // the extra attempts would only add rejected calls to the API-Id ban counter and mask
                    // the signal. Everything else keeps the framework's default transient handling. A
                    // propagated 429 still trips the circuit breaker below — persistent 429 opens it for
                    // 5 min, the intended backpressure (senior-cto-advisor 2026-07-05).
                    ShouldHandle = static args =>
                        ValueTask.FromResult(ScbRetryPolicy.ShouldRetry(args.Outcome)),
                });
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromMinutes(5),
                });
            });

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

        // Ingestion master switch, same section and same aliasing rationale as the retention
        // contract above. Separate type because the name has to say what it gates: the retention
        // knobs stay live while ingestion is dark.
        services.AddOptions<JobSourceIngestOptions>()
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
            // #483 Low — NO MaxResponseContentBufferSize here (deliberately). It only bounds a
            // BUFFERED content read; both wire paths use HttpCompletionOption.ResponseHeadersRead
            // + ReadAsStreamAsync + per-element DeserializeAsyncEnumerable<JsonElement>
            // (JobTechStreamClient), so a cap would be a NO-OP — it enforces nothing on a streaming
            // read. Protection against a maliciously/accidentally huge response comes from the
            // streaming itself (memory bounded by the largest single element, never the whole
            // response) and from the snapshot floor-guards (absolute 30k / relative 0.80×max7d,
            // SyncPlatsbankenSnapshotJob) that fail-safe a corrupt corpus. It does NOT come from
            // Timeout above: under ResponseHeadersRead, HttpClient.Timeout covers only the
            // header-read phase, so the body-stream read is bounded by the job's CancellationToken
            // (Hangfire abort / shutdown), not by Timeout. Never put a MaxResponseContentBufferSize
            // "cap" back here thinking it bounds something — it does not.
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

        // ADR 0043 — Taxonomi-ACL (Variant A). Singleton: lat in-memory-cache
        // av den bounded, oföränderliga snapshot-tabellen (invalideras vid
        // app-restart efter deploy, samma livscykel som seedern). Seedern är
        // IHostedService som idempotent + version-medvetet populerar
        // taxonomy_concepts från embedded taxonomy-snapshot.json vid startup
        // (speglar IdempotentAdminRoleSeeder). DI i samma commit som port-impl.
        services.AddSingleton<ITaxonomyReadModel,
            Jobbliggaren.Infrastructure.Taxonomy.TaxonomyReadModel>();
        services.AddHostedService<
            Jobbliggaren.Infrastructure.Taxonomy.TaxonomySnapshotSeeder>();

        // Fas 4 STEG 3 (F4-3, ADR 0040 amendment + ADR 0074) — deterministic SSYK
        // level-4 derivation (yrkestitel → ssyk-4 yrkesgrupp; engine proposes, user
        // confirms — ADR 0040 Beslut 4). Singleton with a lazy derivation cache
        // (occupation-name index + label lexemes + the committed frozen
        // occupation-name→ssyk-4 map), mirroring ITaxonomyReadModel; consumes
        // ITaxonomyReadModel (GetTreeAsync) + ITextAnalyzer (AddTextAnalysis). DI in
        // the same commit as the port-impl (feedback_di_with_handlers_same_commit).
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.IOccupationCodeDeriver,
            Jobbliggaren.Infrastructure.Taxonomy.OccupationCodeDeriver>();

        // ADR 0079-amendment (exp-per-occ PR-2) — the import-time per-occupation experience
        // attribution pass. Stateless; reuses the singleton IOccupationCodeDeriver (its union
        // DeriveManyAsync untouched — OCP) + IDateTimeProvider + the promoted PeriodParser. NO
        // AI/LLM. DI in the same commit as the port-impl (feedback_di_with_handlers_same_commit).
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.IOccupationExperienceDeriver,
            Jobbliggaren.Infrastructure.Resumes.Parsing.OccupationExperienceDeriver>();

        // Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the shared inverted skill-taxonomy
        // index (embedded jobad-skill-taxonomy.v30.json), extracted from the extractor so
        // BOTH the ad-side extractor AND the CV-side resolver reuse ONE index (no parallel
        // resolver). Singleton (holds the Lazy index); consumes ITextAnalyzer.
        services.AddSingleton<Jobbliggaren.Infrastructure.Taxonomy.SkillTaxonomyIndex>();

        // Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — deterministic per-job-ad
        // keyword/skill extractor. Singleton; consumes ITextAnalyzer + IStemmer
        // (AddTextAnalysis) + the shared SkillTaxonomyIndex (F4-15). NO AI/LLM.
        // DI in the same commit as the port-impl (feedback_di_with_handlers_same_commit).
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdKeywordExtractor,
            Jobbliggaren.Infrastructure.Taxonomy.JobAdKeywordExtractor>();

        // Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the CV-side skill resolver
        // (free-text CV skill names → JobTech concept-ids), reusing the SAME
        // SkillTaxonomyIndex as the extractor (Decision 6: no parallel resolver).
        // Singleton (depends only on the singleton index). NO AI/LLM.
        services.AddSingleton<
            Jobbliggaren.Application.Matching.Abstractions.ISkillResolver,
            Jobbliggaren.Infrastructure.Taxonomy.SkillResolver>();

        // The deterministic matching engine (scorer + profile builder). Own module
        // (parity AddCvReview) so the HTTP-free Worker AND the Worker test fixture can
        // register the matching ports WITHOUT pulling in the full AddInfrastructure /
        // job-source HTTP wiring (ADR 0023) — the BackgroundMatchingJob (ADR 0080 Vag 4)
        // needs IMatchScorer + IMatchProfileBuilder in the Worker SP.
        services.AddMatchingEngine();

        // Fas 4 STEG 7/9 — the CV knowledge bank + the deterministic review engine that
        // consumes it (own module so both hosts AND the Worker test fixture register them
        // independently of the job-source HTTP wiring). See AddCvReview.
        services.AddCvReview();

        // The improve module (åtgärda-lager) is DEFERRED, not removed (CV-pivot 2026-07-16,
        // ADR 0112, CTO-bind D8 Opt C + mechanism rebind PR-4). Its three endpoints are gone,
        // so nothing can SEND SuggestCvImprovements/Preview/Apply — no endpoint, no Hangfire
        // job, no other in-tree sender. But it stays registered here ON PURPOSE:
        // Mediator.SourceGenerator's AddMediator scans the whole Application assembly and
        // registers the three mothballed handlers regardless; their ctor graph needs
        // IFrameProvider + ICvImprovementEngine, and this Api host runs Development
        // ValidateOnBuild=true, which resolves that graph at host build. Drop this call and
        // host boot throws (measured: 4/4 GetParsedResumeEndpointTests fail on ApiFactory
        // boot). The registration is INERT — lazy singletons the container never constructs,
        // because no command path reaches them. Do NOT copy the Worker's ValidateOnBuild=false
        // (TD-103, a known gap, not a pattern). The module/engine/frames stay revert-ready;
        // #650 pnr-guard + Worker-encryption tests keep guarding the mothballed motor.
        services.AddCvImprovement();

        // Fas 4 STEG 10 — the deterministic CV renderer (QuestPDF ATS-plain + visual from the
        // same JSON source). Own module (sets the QuestPDF Community licence once). See
        // AddCvRendering. NO AI/LLM.
        services.AddCvRendering();

        // #842 (2026-07-13): IRecruiterPiiPurger/RecruiterPiiPurger removed. It was the
        // only Art. 17 erasure path for recruiter PII and it was structurally incapable of
        // erasing anything — it probed raw_payload for a jsonb key the ingest sanitizer
        // guarantees is absent (0 of 93 469 ingested ads carry it), then reported success.
        // The replacement contract is ADR 0106: minimise at ingest (Tier A) + remove the
        // whole ad record on request (Tier B). Nothing is registered here in the meantime;
        // the admin route fails loud with 501 (AdminJobAdsEndpoints).

        // #754 (ADR 0045 Beslut 1 klass (d)) — options + delad reporter för
        // ingestion-throughput-fitness-functionen. Bunden HÄR (inte i
        // Worker/Program.cs) eftersom AddJobSources är den ENDA modulen båda
        // hosts passerar (Api via AddInfrastructure, Worker direkt) — en
        // registrering här kan strukturellt inte drifta mellan Api och Worker
        // (CTO bind #754 Q4; precedent JobSourceRetentionOptions ovan).
        // Same-commit DI (feedback_di_with_handlers_same_commit): options +
        // reporter + de två jobbens ctor-ändring hör ihop — Worker kör
        // ValidateOnBuild=false, så en saknad registrering hade annars
        // synts först vid 02:00 UTC-invocationen.
        services.AddOptions<IngestionThroughputOptions>()
            .Bind(configuration.GetSection(IngestionThroughputOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // Singleton, inte Scoped: reportern är stateless (IOptions + ILogger). Samplern, som
        // FAKTISKT bär state, är också singleton — lifetime ska spegla state, annars signalerar
        // den "per-request-state" till nästa läsare (dotnet-architect, #754). Singleton→Scoped-
        // injektion är alltid laglig, så båda sync-jobben (Scoped) kan konsumera den.
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Jobs.Common.IngestionThroughputReporter>();

        // F2-P8c: Application-orchestrator-jobb. Konsumeras av Hangfire via
        // Worker-wrappers (SyncPlatsbankenStream/SnapshotWorker —
        // DisableConcurrentExecution) som löser jobbet ur DI-scope. Snapshot
        // konsumerades tidigare även av admin-trigger via Mediator, men den
        // endpointen är avvecklad (ADR 0032 §9-amendment 2026-05-16, X4) →
        // jobben är nu Hangfire-only. Registreras scoped för wrapper-resolution
        // + test-discoverability via IServiceProvider.GetService.
        services.AddScoped<Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken.SyncPlatsbankenStreamJob>();
        services.AddScoped<Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken.SyncPlatsbankenSnapshotJob>();
        services.AddScoped<Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads.PurgeStaleRawPayloadsJob>();

        // ADR 0032-amendment 2026-05-23 — snapshot-retention. Port + jobb i
        // samma DI-batch som handler-impl (feedback_di_with_handlers_same_commit).
        // Tracker är scoped: delar AppDbContext med snapshot/retention-jobben.
        services.AddScoped<IJobAdSnapshotMissTracker,
            Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker>();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.RetainPlatsbankenJobAds.RetainPlatsbankenJobAdsJob>();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds.ExpireJobAdsJob>();

        // TD-13 C5 (ADR 0049 Beslut 4). Backfill-orchestrator scoped (paritet
        // PurgeStaleRawPayloadsJob) — DI i samma commit som job/port-impl
        // (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption.BackfillFieldEncryptionJob>();

        // Delad re-ingest-kärna för backfill-jobben (senior-cto-advisor Variant H
        // 2026-06-08). Konsumeras av både ssyk- och Klass2-backfillen — registreras
        // en gång, scoped (paritet jobben).
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.Common.JobAdRefetchBackfillRunner>();

        // STEG 6 (2026-05-24) — ssyk_concept_id-backfill för pre-2026-05-20-
        // fix-rader. IOptions-binding för delay/cap-tunables; jobbet self
        // scoped (paritet BackfillFieldEncryptionJob).
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk.BackfillJobAdSsykOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk.BackfillJobAdSsykOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk.BackfillJobAdSsykJob>();

        // Fas B2 (2026-06-08, ADR 0067 Beslut 2) — Klass 2-backfill (employment_type
        // + worktime_extent) för rader importerade före POCO-tillägget. Tunn wrapper
        // kring JobAdRefetchBackfillRunner med eget NULL-predikat + tunables (paritet
        // ssyk-backfillen). DI i samma commit som jobb/endpoint
        // (feedback_di_with_handlers_same_commit).
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2.BackfillJobAdKlass2Options>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2.BackfillJobAdKlass2Options.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2.BackfillJobAdKlass2Job>();

        // Fas 4 STEG 4 (F4-4) — extraction-backfill (lokal re-projektion av
        // extracted_terms; INGEN JobTech-refetch, till skillnad mot ssyk/Klass2
        // som går via JobAdRefetchBackfillRunner). Self-scoped (paritet
        // BackfillFieldEncryptionJob); tunables via IOptions. DI i samma commit som
        // jobb/port (feedback_di_with_handlers_same_commit).
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsJob>();

        // #842 Tier A — the one-off contact-scrub backfill (local re-projection, parity the
        // extraction backfill above). Execution is Klas-gated (STOPP-5); the admin endpoint
        // defaults to dryRun. DI in the same commit as the job (feedback_di_with_handlers).
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubJob>();

        // #544 (ADR 0090 D5) — one-off backfill that tokenises existing plaintext personnummer-shaped
        // company_watches.organization_number rows. Execution is Klas-gated (STOPP-5, security-auditor
        // B5); the admin endpoint defaults to dryRun. DI in the same commit as the job.
        services.AddOptions<Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken.BackfillCompanyWatchOrgNrTokenOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken.BackfillCompanyWatchOrgNrTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken.BackfillCompanyWatchOrgNrTokenJob>();

        // #664 (#479 Low, GDPR Art. 5(1)(c)/25) — one-off backfill that re-masks pre-#465 personnummer
        // left plaintext in parsed_resumes.source_file_name. DEK-free set-based (ExecuteUpdate over a
        // plaintext projection — NEVER materialise the DEK-bearing ParsedResume; senior-cto-advisor
        // 2026-06-25 ParsedResumeRetentionJob rule). Execution is Klas-gated (STOPP-5); the admin
        // endpoint defaults to dryRun. DI in the same commit as the job.
        services.AddOptions<Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskJob>();

        // Fas 4 STEG 4b (F4-4b) — requirements re-ingest backfill (must_have/
        // nice_to_have-skills → Requirement-termer). Tunn wrapper kring
        // JobAdRefetchBackfillRunner (paritet Klass2). Predikatet behöver Npgsql
        // jsonb ?-operatorn → kapslas i Infrastructure bakom
        // IJobAdRequirementBackfillFilter så Application förblir Npgsql-fritt (CLAUDE.md
        // §2.1). Filtret är stateless → Singleton; jobb +
        // options paritet Klass2. DI i samma commit som jobb/endpoint
        // (feedback_di_with_handlers_same_commit).
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdRequirementBackfillFilter,
            JobAds.JobAdRequirementBackfillFilter>();
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements.BackfillJobAdRequirementsOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements.BackfillJobAdRequirementsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements.BackfillJobAdRequirementsJob>();

        return services;
    }

    /// <summary>
    /// ADR 0064 — publik anonym landing-stats med pre-computed Redis-cache.
    /// Registrerar både Application-jobbet <c>RefreshLandingStatsJob</c> (Worker
    /// orkestrerar via Hangfire) och port-impl <c>RedisLandingStatsCache</c>
    /// (skriv/läs av cache-nyckel <c>landing:stats:v1</c>). Anropas av både
    /// Api (handler-read) och Worker (Worker-job-write).
    /// <para>
    /// IDistributedCache förutsätts registrerad av anroparen (Api via
    /// <see cref="AddIdentityAndSessions"/>; Worker via direkt
    /// <c>AddStackExchangeRedisCache</c> i <c>Program.cs</c>).
    /// </para>
    /// </summary>
    public static IServiceCollection AddLandingStats(this IServiceCollection services)
    {
        services.AddScoped<Jobbliggaren.Application.Landing.Common.ILandingStatsCache,
            Jobbliggaren.Infrastructure.Landing.RedisLandingStatsCache>();
        services.AddScoped<
            Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats.RefreshLandingStatsJob>();
        return services;
    }

    /// <summary>
    /// Fas 4 STEG 2 (F4-2, Swedish) + STEG 9 (F4-9, English) — registers the shared
    /// local NLP tier: <see cref="TextAnalysis.SnowballStemmer"/> (Snowball stemmer,
    /// to_tsvector('swedish')/('english') parity), <see cref="TextAnalysis.LocalTextAnalyzer"/>
    /// (lowercase → tokenise → stopword-filter → stem), and
    /// <see cref="TextAnalysis.HunspellSpellChecker"/> (sv_SE DSSO + en_US).
    /// Standalone module called by BOTH hosts (Api via <see cref="AddInfrastructure"/>,
    /// Worker via <c>Program.cs</c>), mirroring <see cref="AddLandingStats"/> — NLP
    /// has no persistence coupling, so it does not belong in <see cref="AddPersistence"/>.
    /// All three impls are thread-safe singletons; the Hunspell WordList loads lazily
    /// on first use. The packages are plain BCL (no ASP.NET) so the Worker's
    /// HTTP-free invariant (ADR 0023) is preserved.
    ///
    /// <para>
    /// A startup existence-check fails fast at composition if the DSSO Content files
    /// did not reach the output directory — preventing a fail-late on the first
    /// spell-check in production (CTO binding condition, ADR 0074 review).
    /// </para>
    /// </summary>
    public static IServiceCollection AddTextAnalysis(this IServiceCollection services)
    {
        EnsureDssoDictionaryPresent();

        services.AddSingleton<
            Jobbliggaren.Application.Common.Abstractions.TextAnalysis.IStemmer,
            TextAnalysis.SnowballStemmer>();
        services.AddSingleton<
            Jobbliggaren.Application.Common.Abstractions.TextAnalysis.ITextAnalyzer,
            TextAnalysis.LocalTextAnalyzer>();
        services.AddSingleton<
            Jobbliggaren.Application.Common.Abstractions.TextAnalysis.ISpellChecker,
            TextAnalysis.HunspellSpellChecker>();
        return services;
    }

    /// <summary>
    /// Fas 4 STEG 8 (F4-8, ADR 0071/0074) — deterministic CV import/parse tier.
    /// Registers <see cref="Resumes.Parsing.PdfPigOpenXmlCvTextExtractor"/>
    /// (<c>ICvTextExtractor</c> — PdfPig/OpenXml confined here) and
    /// <see cref="Resumes.Parsing.HeadingDrivenResumeSegmenter"/>
    /// (<c>IResumeSegmenter</c> — pure string algorithm over the embedded lexicon).
    /// Both are stateless singletons (only immutable reference data, parity
    /// <see cref="AddTextAnalysis"/>). The lexicon ships as an <c>EmbeddedResource</c>,
    /// so the manifest-resource lookup fails loudly at first load — no separate
    /// file-existence check is needed (unlike the DSSO Content files). NO AI/LLM.
    /// </summary>
    public static IServiceCollection AddCvParsing(this IServiceCollection services)
    {
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.ICvTextExtractor,
            Resumes.Parsing.PdfPigOpenXmlCvTextExtractor>();
        // Fas 4b PR-6b — PDF page-geometry analyzer (ICvLayoutAnalyzer), PdfPig confined here,
        // stateless singleton (parity the extractor). Read at import; feeds B2/D9/E2.
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.ICvLayoutAnalyzer,
            Resumes.Parsing.PdfPigCvLayoutAnalyzer>();
        services.AddCvLexicon();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.IResumeSegmenter,
            Resumes.Parsing.HeadingDrivenResumeSegmenter>();
        return services;
    }

    /// <summary>
    /// Fas 4b 8b.4a/8b.4b — the CV-parsing lexicon and the two knowledge-bank assets that are
    /// cross-validated against it (branschgrupp, cv-conventions). Its own module, and
    /// <b>IDEMPOTENT</b>, for one reason: <see cref="AddCvParsing"/> needs the lexicon for the
    /// segmenter and <see cref="AddCvImprovement"/> needs it for the D6 + B1 transforms — and the
    /// Worker registers the improvement engine <i>without</i> the parsing module
    /// (<c>WorkerTestFixture</c>). Registering the lexicon in only one of them would leave an
    /// unresolvable singleton in the other, and the Worker runs <c>ValidateOnBuild=false</c>
    /// (TD-103), so that gap would surface first at Hangfire-invocation, not at boot. <b>A module
    /// that owns its own dependency cannot rot that way.</b>
    ///
    /// <para>The idempotence guard is not a micro-optimisation: loading the asset twice would
    /// produce TWO <c>CvParsingLexiconData</c> instances, and 8b.4a's guarantee is that the
    /// segmenter and every asset provider hold the SAME one, so that RECOGNITION ("is this a
    /// heading?") and RESOLUTION ("WHICH canonical section is it?") provably cannot disagree.</para>
    ///
    /// <para><b>INSTANCE registrations, not type registrations, and the difference is the whole
    /// point.</b> A type registration (<c>AddSingleton&lt;IPort, Impl&gt;()</c>) constructs Impl at
    /// the FIRST RESOLVE — i.e. inside the first HTTP request that needs it — and
    /// <c>ValidateOnBuild</c> does not instantiate singletons, so it would not catch a broken asset
    /// either. All three of these types validate in their constructors (both providers run a full
    /// cross-asset pin against the lexicon), so registering them by TYPE would mean a malformed
    /// asset surfaces as a 500 inside a user's CV import, cached for the life of the process. That
    /// is not hypothetical: it is exactly the defect 8b.4a PR-1 fixed, where the lexicon's static
    /// ctor loaded on first parse and threw a <c>TypeInitializationException</c> mid-request.
    /// Constructing here makes "fail loud at startup, never mid-request" TRUE rather than merely
    /// claimed — the host refuses to build.</para>
    /// </summary>
    public static IServiceCollection AddCvLexicon(this IServiceCollection services)
    {
        // The sentinel is the LAST thing this method registers, not the first, and the difference is
        // the guard's whole scope. Keyed on CvParsingLexiconData (the first), a caller who registered
        // that type on its own — a future test host injecting a synthetic lexicon — would switch this
        // module OFF and leave ICvConventionsProvider UNREGISTERED. The engines would then fail at
        // first resolve, and in the Worker (ValidateOnBuild=false, TD-103) not until a Hangfire
        // invocation. A guard whose sentinel is narrower than the set it guards is not a guard; it is
        // a claim. (Both review gates flagged it — latent today, since nothing else registers the
        // lexicon, and a composition test now pins that it stays that way.)
        if (services.Any(d =>
                d.ServiceType == typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.ICvConventionsProvider)))
        {
            return services;
        }

        var lexiconData = Resumes.Parsing.CvParsingLexiconLoader.Load();
        services.AddSingleton(lexiconData);

        var lexicon = new Resumes.Parsing.CvParsingLexiconProvider(lexiconData);
        services.AddSingleton<Jobbliggaren.Application.Resumes.Abstractions.ICvParsingLexicon>(lexicon);

        // Asset A (8b.4a) — consumed by the GetCvSectionSuggestions read-slice (ADR 0107), never by
        // the engine. Asset B (8b.4b) — consumed by SectionReorderTransform IN the engine (ADR 0108).
        // Both refuse to construct if they name a section the lexicon does not own.
        services.AddSingleton<Jobbliggaren.Application.KnowledgeBank.Abstractions.IBranschgruppProvider>(
            new KnowledgeBank.BranschgruppProvider(lexicon));
        services.AddSingleton<Jobbliggaren.Application.KnowledgeBank.Abstractions.ICvConventionsProvider>(
            new KnowledgeBank.CvConventionsProvider(lexicon));
        return services;
    }

    /// <summary>
    /// Fas 4 STEG 7/9 (F4-7/F4-9, ADR 0071/0074) — the versioned CV knowledge bank (rubric +
    /// cliché lexicon + weak→strong verb mapping, three ISP ports over embedded VERSIONED
    /// DATA, §5) and the deterministic CV-review engine that scores a ParsedResume against
    /// them. All stateless singletons (bounded immutable data, parity ITaxonomyReadModel).
    /// The engine consumes the NLP-tier <c>ITextAnalyzer</c> + <c>ISpellChecker</c> (Fas 4b
    /// PR-6, C7 spelling), so the caller must also call <see cref="AddTextAnalysis"/>.
    /// Standalone module (parity AddTextAnalysis) so every host AND the Worker test fixture
    /// register it without the job-source HTTP wiring. NO AI/LLM.
    /// </summary>
    public static IServiceCollection AddCvReview(this IServiceCollection services)
    {
        // Fas 4b 8b.4b (ADR 0108) — B1 now assesses the section ORDER, so the review engine reads
        // the parsing lexicon (heading recognition) and cv-conventions (the recommended order),
        // exactly as the improvement engine does. AddCvLexicon() is idempotent, so a host that also
        // calls AddCvParsing()/AddCvImprovement() still gets exactly ONE lexicon instance — and the
        // Worker, which registers this module without AddCvParsing(), is no longer left with an
        // unresolvable singleton. A module that owns its own dependency cannot rot that way.
        services.AddCvLexicon();
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IRubricProvider,
            Jobbliggaren.Infrastructure.KnowledgeBank.RubricProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IClicheLexicon,
            Jobbliggaren.Infrastructure.KnowledgeBank.ClicheLexicon>();
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IVerbMapper,
            Jobbliggaren.Infrastructure.KnowledgeBank.VerbMapper>();
        // IFrameProvider moved to AddCvImprovement (CV-pivot 2026-07-16, ADR 0112): the review
        // engine never consumed it — its only consumers are the improve layer's Preview/Apply
        // handlers, so the registration follows its consumers into the mothballed module (SRP).
        // Fas 4b PR-6 (ADR 0093 §D4): the C7 spelling criterion's proper-noun/tech-term
        // allowlist — versioned KB DATA (§5), loaded + validated once at construction.
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.ISpellingAllowlist,
            Jobbliggaren.Infrastructure.KnowledgeBank.SpellingAllowlistProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.ICvReviewEngine,
            Jobbliggaren.Infrastructure.Resumes.Review.CvReviewEngine>();
        // Fas 4b PR-8 (ADR 0093 §D5(b), CTO-bind PR-8 Q1): the one engine-driven ledger
        // write path — an Application-layer composition over the engine (MatchProfileBuilder
        // registration precedent). Stateless → singleton, parity with the engine it wraps.
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.IResumeReviewReconciler,
            Jobbliggaren.Application.Resumes.Review.ResumeReviewReconciler>();

        // #692 (ADR 0093 §D2(e), security-auditor Fas 4b PR-4 Q4) — the keyed HMAC that fingerprints
        // a finding at rest. DUAL-HOST, like every AddJobSources registration: this module
        // (AddCvReview) is reached by AddJobSources (line ~400), which BOTH hosts pass — Api via
        // AddInfrastructure, Worker directly (Worker/Program.cs) — so BOTH boot this pepper section and
        // BOTH must provision the pepper in prod (parity the watch pepper #544). Only the Api actually
        // COMPUTES a fingerprint, but AddCvReview also registers the dual-host IResumeReviewReconciler,
        // which now depends on IFindingFingerprinter, so the hasher must live wherever the reconciler
        // does (an Api-only seam would leave the Worker's reconciler with an unresolvable dep). This is
        // exactly the host-drift the AddJobSources placement is designed to prevent (see the #754
        // options comment above). BindConfiguration (not .Bind(config.GetSection())) because AddCvReview
        // takes no IConfiguration. ValidateOnStart hard-fails a missing/weak pepper in every environment
        // and BOTH hosts, parity the audit (#842) and watch (#544) peppers.
        services.AddOptions<Security.CvReviewFingerprintPseudonymizationOptions>()
            .BindConfiguration(Security.CvReviewFingerprintPseudonymizationOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.CvReviewFingerprintPseudonymizationOptions>,
            Security.CvReviewFingerprintPseudonymizationOptionsValidator>();
        // Stateless after reading the pepper once → singleton, parity HmacProtectedIdentityTokenizer.
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.IFindingFingerprinter,
            Security.HmacFindingFingerprinter>();
        return services;
    }

    /// <summary>
    /// The deterministic matching engine: the Fast/Full match scorer (F4-5/F4-6, ADR 0076 —
    /// <c>internal</c> in Infrastructure, so it can only be registered from this assembly) and
    /// the SSOT preference→profile mapper (ADR 0076; ADR 0079 STEG 3 PR-D — DEK-free). Own module
    /// (parity <see cref="AddCvReview"/>) so every host AND the Worker (HTTP-free, ADR 0023) +
    /// its test fixture register the matching ports independently of the job-source HTTP wiring.
    /// The <c>BackgroundMatchingJob</c> (ADR 0080 Vag 4 PR-3) consumes both ports in the Worker.
    /// Scoped (both touch <c>AppDbContext</c>). NO AI/LLM.
    /// </summary>
    public static IServiceCollection AddMatchingEngine(this IServiceCollection services)
    {
        services.AddScoped<
            Jobbliggaren.Application.Matching.Abstractions.IMatchScorer,
            Jobbliggaren.Infrastructure.Matching.MatchScorer>();
        services.AddScoped<
            Jobbliggaren.Application.Matching.Abstractions.IMatchProfileBuilder,
            Jobbliggaren.Application.Matching.Profiles.MatchProfileBuilder>();
        // #300 PR-3: MatchProfileBuilder now depends on ITaxonomyReadModel (the related-occupation
        // ACL). The API/Worker register it via AddJobSources, but the HTTP-free Worker test fixture
        // calls only AddMatchingEngine() — so make the matching engine self-contained re: its own
        // dependency closure. TryAdd is idempotent: a no-op where AddJobSources already registered
        // it (AddJobSources runs first in every dual-caller), and the sole registrar elsewhere.
        // TaxonomyReadModel needs only IServiceScopeFactory (always present) → resolves in any SP.
        services.TryAddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.ITaxonomyReadModel,
            Jobbliggaren.Infrastructure.Taxonomy.TaxonomyReadModel>();
        return services;
    }

    /// <summary>
    /// Fas 4 STEG 10 (F4-10, ADR 0071/0074) — the deterministic CV-build/improve engine that
    /// proposes propose-and-approve diffs over a ParsedResume against the knowledge bank
    /// (cliché/verb/date/heading/strip transforms, never synthesised — CTO V-B compute-on-demand,
    /// no persistence). Stateless singleton (parity AddCvReview). Consumes the knowledge-bank
    /// ports (<see cref="AddCvReview"/>) + the NLP-tier <c>ITextAnalyzer</c>
    /// (<see cref="AddTextAnalysis"/>), so the caller must also register those. Standalone module
    /// so every host AND the Worker test fixture register it without the job-source HTTP wiring.
    /// NO AI/LLM. (The QuestPDF renderer is a separate Phase B module, AddCvRendering.)
    /// </summary>
    public static IServiceCollection AddCvImprovement(this IServiceCollection services)
    {
        // Fas 4b 8b.4b — the engine's D6 transform recognises headings through the parsing lexicon
        // and its B1 transform orders them against cv-conventions, so this module now OWNS that
        // dependency instead of assuming a sibling registered it. AddCvLexicon() is idempotent, so
        // a host that also calls AddCvParsing() still gets exactly ONE lexicon instance.
        services.AddCvLexicon();
        // IFrameProvider lives HERE, not in AddCvReview (CV-pivot 2026-07-16, ADR 0112, SRP):
        // its only consumers are the improve layer's Preview/Apply handlers
        // (frameProvider.GetFrameCatalog()) — the review engine never takes it.
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IFrameProvider,
            Jobbliggaren.Infrastructure.KnowledgeBank.FrameProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Improvement.Abstractions.ICvImprovementEngine,
            Jobbliggaren.Infrastructure.Resumes.Improvement.CvImprovementEngine>();
        return services;
    }

    /// <summary>
    /// Fas 4 STEG 10 (F4-10, ADR 0071/0074, BUILD §3.1) — the deterministic CV renderer
    /// (QuestPDF: ATS-plain + visual PDF from the same JSON source). Sets the QuestPDF Community
    /// licence ONCE (fail-fast at registration, parity <see cref="EnsureDssoDictionaryPresent"/>)
    /// before any render. Stateless singleton (parity AddCvReview). Standalone module so every
    /// host AND the Worker test fixture register it. The QuestPDF SDK stays confined to
    /// Infrastructure (the port <c>ICvRenderer</c> is BCL-only). NO AI/LLM.
    /// </summary>
    public static IServiceCollection AddCvRendering(this IServiceCollection services)
    {
        EnsureQuestPdfLicense();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Rendering.Abstractions.ICvRenderer,
            Jobbliggaren.Infrastructure.Resumes.Rendering.CvRenderer>();
        // ICvAccentSwatchProvider + CvAccentSwatchProvider (the template-catalog's hex egress,
        // 8b.3) were removed WITH their single consumer, the catalog handler (CV-pivot
        // 2026-07-16, ADR 0112) — a one-consumer port dies with its consumer in the same
        // commit. CvPalette itself stays: the composer renders every persisted CV through it.
        return services;
    }

    // QuestPDF requires the licence type to be declared once before any document is generated.
    // Community (source-available, free under USD 1M revenue, non-copyleft vs ADR 0050).
    private static void EnsureQuestPdfLicense() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static void EnsureDssoDictionaryPresent()
    {
        foreach (var path in new[]
        {
            TextAnalysis.HunspellSpellChecker.DictionaryPath,
            TextAnalysis.HunspellSpellChecker.AffixPath,
            TextAnalysis.HunspellSpellChecker.EnglishDictionaryPath,
            TextAnalysis.HunspellSpellChecker.EnglishAffixPath,
        })
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Hunspell dictionary file missing: {path}. It ships as a Content " +
                    "file (CopyToOutputDirectory) from Jobbliggaren.Infrastructure (BUILD " +
                    "§3.1: sv_SE = LGPL-3.0 separate unmodified file; en_US = permissive " +
                    "SCOWL/Ispell BSD). Verify the <Content> items in " +
                    "Jobbliggaren.Infrastructure.csproj reached the output directory.");
            }
        }
    }

    // Process-wide rate-limiter för JobStream (1 req/min). FixedWindow är rätt val
    // per dotnet-architect 2026-05-12. QueueLimit=2 (motiverat vid fältet nedan)
    // serialiserar stream/snapshot-krock mot 1/min istället för hård rejection.
    //
    // TESTBARHETSNOT (code-reviewer 2026-05-12 Min-3): static-livscykel betyder att
    // alla tester som använder hela DI-stacken delar samma limiter över hela test-
    // körningen. Resilience-tester (JobTechStreamResilienceTests) bygger därför
    // egen DI-container UTAN denna limiter — de testar bara retry/CB-pipelinen.
    // P8c-Hangfire-jobben kommer dela samma limiter i prod, vilket är den
    // önskade semantiken. IDisposable-warning vid host-shutdown är accepterad
    // bagatell — limitern lever app-lifetime.
    // QueueLimit=2 (var 0): stream(*/10) + snapshot(0 2) krockar på JobTechs
    // 1-req/min-gräns kl 02:00. Med QueueLimit=0 fick förloraren hård
    // RateLimiterRejected → 3 retries inom samma fönster → jobb-fail. Nu
    // serialiseras de mot 1/min istället (root-cause-fix 2026-05-16 del (b),
    // senior-cto-advisor + dotnet-architect). Worst-case väntan QueueLimit×Window
    // = 2 min; CancellationToken bryter väntan. OldestFirst = FIFO-rättvisa.
    private static readonly FixedWindowRateLimiter _streamRateLimiter = new(
        new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

    // #560 (ADR 0091, senior-cto-advisor Fork 7) — process-wide SCB upstream limiter. SCB caps each
    // API-Id at 10 calls / 10 s; a per-endpoint policy cannot protect a per-credential budget, and a
    // breach risks an API-Id ban (a §12 STOPP condition). A SLIDING window (10 × 1 s segments)
    // guarantees the rolling-10 s permit sum never exceeds PermitLimit — unlike a FIXED window, which
    // can emit up to 2×PermitLimit across a boundary (code-reviewer 2026-07-04 Major: 2×8 > 10). The
    // planner issues many small kodtabell/raknaforetag calls, so that burst is not hypothetical.
    // PermitLimit=6 (60% of SCB's 10) keeps a deliberate 4-call safety margin — far beyond any clock
    // skew / SCB-side window edge (≤1-2 calls) — because exceeding the cap risks an API-Id BAN
    // (catastrophic, §12) whereas running slower costs only ~10-30 min extra on a night run Klas
    // explicitly accepted (senior-cto-advisor 2026-07-05: ban-risk-minimization > tempo; supersedes the
    // 1-call margin at 9, honouring Fork 7's "rate budget is code, not config" ruling). The refresh
    // streams sequentially (at most one waiter), but QueueLimit is generous so a throttled call ALWAYS
    // waits rather than being rejected+retried. App-lifetime static (parity _streamRateLimiter); the
    // IDisposable-at-shutdown warning is an accepted bagatelle.
    private static readonly SlidingWindowRateLimiter _scbRegisterRateLimiter = new(
        new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 6,
            Window = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 10,
            QueueLimit = 256,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

    private static void ApplyApiKey(HttpClient client, JobTechOptions options)
    {
        // SECURITY-NOTE (security-auditor 2026-05-12 Min-2): api-key skickas via
        // DefaultRequestHeaders.TryAddWithoutValidation. Microsoft.Extensions.Http
        // EventSource-tracing kan teoretiskt logga request-headers vid aktiverad
        // diagnostik — vi aktiverar den inte i prod (Microsoft.Extensions.Http
        // EventSource är default av). JobTech-api-key ger högre rate-limit på publikt
        // data — låg blast-radius om läckt.
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("api-key", options.ApiKey);

        client.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
    }

    /// <summary>
    /// Email provider-switch (ADR 0080 Vag 4 PR-4b; provider bytt i ADR 0124). Called by BOTH the
    /// Api (<see cref="AddInfrastructure"/>) AND the HTTP-free Worker (ADR 0023) so both register
    /// the SAME dev=Console, non-dev=Null gating without drift. The Worker needs
    /// <see cref="IEmailSender"/> for the Vag 4 match-notification jobs (Top-direct scan hook +
    /// <c>DigestDispatchJob</c>). Binds <see cref="EmailOptions"/> and selects the sender per
    /// <c>Email:Provider</c>.
    /// <para>
    /// Transaktionell mejlväg via Scaleway Transactional Email i fr-par (#183) — HTTPS-API, aldrig
    /// SMTP. <see cref="ConsoleEmailSender"/> skriver mottagar-email + plaintext-token till ILogger
    /// för en RFC 2606/6761-reserverad mottagare (#1208) — registreras BARA i Development/Test
    /// (TD-104/STEG 6 security-auditor Major #1: en PERSISTENT logg-sink gör den raden durabel
    /// PII-lagring). I andra miljöer
    /// faller "Console" tillbaka på <see cref="NullEmailSender"/> (no-op) tills en riktig provider
    /// wiras. Okänt provider-värde fail-stoppas.
    /// </para>
    /// <para>
    /// <b>Defaulten är oförändrad och det är avsiktligt.</b> <c>Email:Provider</c> är osatt i varje
    /// committad <c>appsettings*.json</c>, så <c>?? "Console"</c> gäller: Console i Dev/Test, Null
    /// överallt annars. Att Scaleway-armen finns ändrar ingenting förrän någon sätter nyckeln.
    /// </para>
    /// </summary>
    public static IServiceCollection AddEmailSender(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));

        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Console";
        if (string.Equals(emailProvider, "Console", StringComparison.OrdinalIgnoreCase))
        {
            // Dev/Test allow-list speglar Hangfire-schema-grindens mönster (Worker/Program.cs).
            if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
            {
                services.AddSingleton<IEmailSender, ConsoleEmailSender>();
            }
            else
            {
                services.AddSingleton<IEmailSender, NullEmailSender>();
            }
        }
        else if (string.Equals(emailProvider, "Scaleway", StringComparison.OrdinalIgnoreCase))
        {
            // Läses RÅTT ur IConfiguration, inte via IOptions, så att en felkonfiguration fäller
            // REGISTRERINGEN och inte första utskicket. Det är vad AddEmailSenderGateTests kan
            // asserta mot en naken ServiceCollection utan att boota en host — och det är därför
            // kontrollen inte kan bo i ScalewayEmailSenders konstruktor: AddSingleton<T,TImpl> är
            // LAT, så prod hade bootat rent och fallit först på första mejlet.
            var region = configuration[$"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.Region)}"];
            var secretKey = configuration[$"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.SecretKey)}"];
            var projectId = configuration[$"{ScalewayEmailOptions.SectionName}:{nameof(ScalewayEmailOptions.ProjectId)}"];

            if (string.IsNullOrWhiteSpace(region))
            {
                throw new InvalidOperationException(
                    "Email:Provider='Scaleway' kräver Email:Scaleway:Region (fr-par). Regionen sätts "
                    + "ALLTID explicit — den interpoleras rakt in i endpoint-URL:en och avgör "
                    + "dessutom vilken jurisdiktion e-post lämnar ifrån (#1169).");
            }

            // TVÅ hemligheter, inte två halvor av samma: nyckeln autentiserar anroparen, project-id
            // väljer projektet utskicket debiteras och attribueras till. Var och en krävs för sig,
            // och felet namnger vilken som saknas — annars kostar en tom rad i secrets-filen en
            // felsökningsrunda på fel värde.
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException(
                    "Email:Provider='Scaleway' kräver Email:Scaleway:SecretKey (gitignored "
                    + "appsettings.Local.json / managed secret). Utan nyckel svarar API:t 401 och "
                    + "ingenting levereras — det får aldrig upptäckas som tystnad i drift.");
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new InvalidOperationException(
                    "Email:Provider='Scaleway' kräver Email:Scaleway:ProjectId (gitignored "
                    + "appsettings.Local.json / managed secret). Project-id är en egen hemlighet med "
                    + "egen livscykel, inte en del av SecretKey.");
            }

            // FromAddress grindas här och inte bara av EmailOptions default (security-auditor Minor 3).
            // Konsekvensen är inte kosmetisk: _dmarc.jobbliggaren.se publicerar redan p=reject UTAN
            // rua= (mätt 2026-08-08, ADR 0124), så en avsändaradress utanför den DKIM-verifierade
            // identiteten ger totalt leveransbortfall — tyst, och utan en enda rapport som avslöjar det.
            var fromAddress = configuration[$"{EmailOptions.SectionName}:{nameof(EmailOptions.FromAddress)}"]
                ?? new EmailOptions().FromAddress;
            if (string.IsNullOrWhiteSpace(fromAddress) || !fromAddress.Contains('@', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Email:Provider='Scaleway' kräver en avsändaradress; Email:FromAddress='{fromAddress}' "
                    + "är inte en adress. Den måste dessutom ligga under den hos Scaleway verifierade "
                    + "domän-identiteten — domänens DMARC står på p=reject utan rua=, så ett fel här "
                    + "syns inte som ett fel utan som tystnad.");
            }

            // Backstop för de SEMANTISKA kontroller den råa läsningen inte uttrycker. Registreras
            // ENBART i den här armen. EmailOptions självt får medvetet INTE ValidateOnStart: det
            // bär noll data-annotations (mätt), så det hade asserterat ingenting — och ett senare
            // [Required] hade gjort hela Email-sektionen till ett boot-villkor på DEFAULT-vägen,
            // som appsettings.Local.json.example och local-dev-setup.md §7 lovar mot.
            services.AddOptions<ScalewayEmailOptions>()
                .Bind(configuration.GetSection(ScalewayEmailOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Klient-registreringen + regionvakten bor i Email/ScalewayClientRegistration så att den
            // här filen — en §6.5-hotspot många sessioner redigerar — bara bär switchen.
            services.AddScalewayEmailClient(region);

            // Singleton, och registrerad som AddSingleton<TService, TImplementation> — INTE via en
            // factory-lambda och INTE som typad HttpClient (som är transient bakom en lambda).
            // SKÄLET bor i ScalewayClientRegistration.HttpClientName och står medvetet inte här:
            // det är captive dependency + livstids-konsistens, inte att gate-testerna assertar på
            // ImplementationType. En test-assertion får inte forma en composition root, och att
            // upprepa den som skäl PÅ composition root:en var precis den inversionen (#1339).
            services.AddSingleton<IEmailSender, ScalewayEmailSender>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Email:Provider='{emailProvider}' stöds inte. Använd 'Console' eller 'Scaleway'.");
        }

        return services;
    }

    /// <summary>
    /// Persistence-modul: <see cref="AppDbContext"/>, <see cref="IAppDbContext"/>,
    /// <see cref="IDateTimeProvider"/>, <see cref="ISwedishCalendar"/>. Ingen HTTP-bagage, ingen Identity, ingen Redis.
    /// Worker registrerar denna modul + egna audit-port-stubs.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        // TD-13 C3 (ADR 0049 Mekanik-not 5c, architect+Microsoft Learn
        // 2026-05-18): EF Core auto-discoverar INTE app-DI-interceptorer.
        // Kanonisk mekanik = SINGLETON-interceptorer (ISingletonInterceptor) +
        // (sp,options).AddInterceptors(sp.GetRequiredService<...>()). Singleton
        // → samma instans varje resolution → identisk options-cache-nyckel →
        // EN intern EF-provider (ingen ManyServiceProvidersCreatedWarning,
        // prod-reell läcka annars). Scoped state (cache/owner/encryptor) nås
        // via eventData.Context.GetService<T>() vid invocation, ej ctor.
        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    sp.GetRequiredService<Security.FieldEncryptionSaveChangesInterceptor>(),
                    sp.GetRequiredService<Security.FieldDecryptionMaterializationInterceptor>()));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // Klas-direktiv 2026-07-28: a day boundary a user reads is the SWEDISH
        // one, not UTC.
        //
        // It lives HERE because this module already owns the clock, and the
        // calendar is the clock's sibling — NOT because both hosts need it.
        // That criterion is one this very file rejects: AddTextAnalysis is a
        // standalone module precisely because "called by BOTH hosts" does not
        // imply persistence coupling. Splitting the two time adapters across two
        // modules would cost more cohesion than it buys.
        //
        // Stateless singleton; the zone id resolves once in a static field, and
        // the gate against a runtime that cannot resolve it is
        // SwedishCalendarTests, not this line — type registration is lazy, so a
        // throw here would surface on first use rather than at boot.
        //
        // Three consumers, across both hosts: RefreshLandingStatsJob (Worker) for
        // the "nya idag" day boundary, and — since the ADR 0064 amendment's
        // follow-up landed — GetActivityReportQueryHandler and
        // GetApplicationStatsQueryHandler (Api) for the month windows. The Api
        // side resolves it through AddInfrastructure → AddPersistence.
        services.AddSingleton<ISwedishCalendar, Time.SwedishCalendar>();

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

        // ISystemEventAuditor (ADR 0035) — bypass-port för audit-rader från
        // system-jobben (SyncPlatsbankenStreamJob/SnapshotJob/PurgeStaleRawPayloadsJob).
        // Scoped följer IAppDbContext-livscykeln; per Hangfire-scope ger varje
        // job-execution fresh DbContext + auditor-instans.
        services.AddScoped<ISystemEventAuditor, SystemEventAuditor>();

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

        // ADR 0060 — RecentJobSearches auto-capture-port. Scoped (delar
        // IAppDbContext-livstid; egen SaveChangesAsync per capture per CTO-dom).
        // Konsumeras av RecentJobSearchCaptureBehavior i pipeline.
        services.AddScoped<
            Jobbliggaren.Application.RecentJobSearches.Abstractions.IRecentJobSearchCapturer,
            RecentJobSearches.RecentJobSearchCapturer>();

        // ADR 0062 — IJobAdSearchQuery: hela sök-kompositionen (FTS-hybrid +
        // ts_rank-relevans) flyttad Application→Infrastructure eftersom
        // PostgreSQL FTS-LINQ ligger i Npgsql-assemblyn (arch-test-förbjuden i
        // Application). Scoped — delar request-scopets AppDbContext, paritet med
        // hur handlers konsumerar IAppDbContext (till skillnad från
        // ITaxonomyReadModel som är singleton pga snapshot-cache). DI i samma
        // commit som port-impl (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdSearchQuery,
            JobAds.JobAdSearchQuery>();

        // #842 / ADR 0106 Tier B — the matching behind the Art. 17 erasure command (the channels are
        // documented on the port; do not restate them here). Infrastructure for the same reason as
        // IJobAdSearchQuery above: FTS, jsonb_path_query and the ARE regex are Npgsql concerns,
        // arch-test-forbidden in Application.
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IRecruiterErasureMatchQuery,
            JobAds.RecruiterErasureMatchQuery>();

        // #842 — HMAC-SHA256(server pepper) for the Art. 17 audit payload (ADR 0090 D5).
        // Singleton: the pepper is read once and the instance is stateless.
        //
        // Fail-closed startup: a missing or short pepper aborts boot in EVERY environment (mirrors
        // FieldEncryptionOptions). An HMAC under a weak or absent key looks protected while being
        // reversible, so a silently-tolerated default would be worse than no pseudonymisation at all.
        services.AddOptions<Security.AuditPseudonymizationOptions>()
            .Bind(configuration.GetSection(Security.AuditPseudonymizationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.AuditPseudonymizationOptions>,
            Security.AuditPseudonymizationOptionsValidator>();
        services.AddSingleton<
            Jobbliggaren.Application.Common.Security.IIdentifierPseudonymizer,
            Security.HmacIdentifierPseudonymizer>();

        // #544 (ADR 0090 D5) — SEPARATE watch pepper for the enskild-firma org.nr at-rest token
        // (security-auditor B1: one key = one purpose; R1 — unlike the rotation-tolerant audit
        // pepper). Same fail-closed ValidateOnStart posture.
        //
        // "Non-rotatable" is precise only with its condition attached (#198): NON-ROTATABLE ONCE
        // ANY ROW EXISTS. BackfillCompanyWatchOrgNrTokenJob destroyed the plaintext organisation
        // number in place, so an existing token cannot be recomputed under a new pepper — not
        // expensively, but mathematically. While company_watches is empty the pepper is simply a
        // value, and replacing it costs nothing. The window closes at the FIRST row, which is why
        // #198's cutover replaces it rather than carrying the exposed value forward.
        services.AddOptions<Security.CompanyWatchPseudonymizationOptions>()
            .Bind(configuration.GetSection(Security.CompanyWatchPseudonymizationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.CompanyWatchPseudonymizationOptions>,
            Security.CompanyWatchPseudonymizationOptionsValidator>();
        services.AddSingleton<
            Jobbliggaren.Application.Common.Security.IProtectedIdentityTokenizer,
            Security.HmacProtectedIdentityTokenizer>();

        // F4-14 (ADR 0076 Decision 4/5) — IPerUserJobAdSearchQuery: den
        // per-användar-match-sorten ("Sortera efter matchning"). SEPARAT port från
        // IJobAdSearchQuery (som förblir match-ren/cachebar) men delar filter-SPOT:en
        // (JobAdSearchComposition) + den rena port-counten. Scoped paritet
        // IJobAdSearchQuery; DI i samma commit som port-impl
        // (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IPerUserJobAdSearchQuery,
            JobAds.PerUserJobAdSearchQuery>();

        // ADR 0087 D6/D7 (#311 PR-2b C2) — IEmployerDisambiguationQuery: the org.nr disambiguation
        // projection (DISTINCT org.nr + company_name + COUNT via ILIKE + GROUP BY). A SEPARATE read
        // concern from IJobAdSearchQuery (D6 — never folded into the filter/facet port); lives in
        // Infrastructure because ILIKE/GROUP BY are Npgsql-assembly LINQ (arch-test-forbidden in
        // Application, parity IJobAdSearchQuery). Scoped (shares the request AppDbContext). DI in the
        // same commit as the port-impl (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IEmployerDisambiguationQuery,
            JobAds.EmployerDisambiguationQuery>();

        // #311 #455 (ADR 0087 D2/D8(c)) — IJobAdEmployerReader: resolves the STORED organization_number
        // shadow column for a set of ads (id = ANY raw SQL + EF.Property, Npgsql-assembly concerns
        // arch-forbidden in Application, parity IJobAdSearchQuery). Server-side org.nr resolution for the
        // #455 follow-from-card command + follow-state batch (raw org.nr never surfaced, D8(c)). Scoped
        // (shares the request AppDbContext). DI in the same commit as the port-impl
        // (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdEmployerReader,
            JobAds.JobAdEmployerReader>();

        // #560 kriterie-vågen PR-2 (CTO Fork A1/B1) — ICompanyWatchBrowseQuery: the criteria browse
        // over the local SCB company_register. Registered HERE and not in AddScbCompanyRegister: that
        // module is the POPULATION channel, is Worker-only, and is gated on ScbRegister:Enabled — but
        // browsing rows that are already in the table is an Api read concern and must not depend on
        // whether the nightly SCB sync is switched on. Lives in Infrastructure because the predicate is
        // raw Npgsql (`sni_codes && @sni` — the ONLY shape the GIN index can serve; LINQ compiles it to
        // an unnest subquery that silently cannot use the index) and because the register replica is
        // deliberately NOT a DbSet on IAppDbContext (DPIA C-D4 / M-C5 firewall). Scoped — shares the
        // request AppDbContext, parity IJobAdSearchQuery. DI in the same commit as the port-impl
        // (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.CompanyWatches.Abstractions.ICompanyWatchBrowseQuery,
            CompanyRegister.CompanyWatchBrowseQuery>();

        // #560 company-search wave (CTO F1) — ICompanyRegisterSearchQuery: the GENERAL register
        // search (/foretag/sok; every axis optional, browse-all legal). A SEPARATE port from the
        // criterion browse above — opposite absent-axis semantics (omitted clause vs fail-loud),
        // bound as two ports by senior-cto-advisor 2026-07-18. Same placement rationale as the
        // sibling: Api read concern (never gated on ScbRegister:Enabled), raw Npgsql (GIN `&&` +
        // functional lower()-prefix are the only index-servable shapes), register off
        // IAppDbContext (DPIA C-D4/M-C5). Scoped — shares the request AppDbContext. DI in the
        // same commit as the port-impl (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.ICompanyRegisterSearchQuery,
            CompanyRegister.CompanyRegisterSearchQuery>();

        // #994 — ICompanyRegisterNameReader: resolves company_name by org.nr from the local SCB
        // register replica, the SECOND read-model the company-watch list falls back to when the
        // job_ads name projection is empty (a followed 0-ad company; ADR 0087 D3 keeps it a READ
        // projection — no snapshot). Plain EF LINQ over the concrete AppDbContext.Set<>() (PK
        // `= ANY`, no index-shape need for raw SQL, unlike the search sibling above); the register
        // stays off IAppDbContext (DPIA C-D4/M-C5). An Api read concern, never gated on
        // ScbRegister:Enabled (parity the browse ports). Scoped — shares the request AppDbContext.
        // DI in the same commit as the port-impl (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.ICompanyRegisterNameReader,
            CompanyRegister.CompanyRegisterNameReader>();

        // #560 PR-3 (CTO Fork G2) — the SCB reference data (SNI 2025 + län/kommun) behind
        // ICriterionReferenceProvider: ONE authority for the Application existence-validator and the
        // FE picker tree. INSTANCE registration, deliberately: the loaders run HERE, at host build,
        // so a malformed embedded asset fails the host loudly instead of 500-ing the first create
        // (AddSingleton<IPort, Impl>() is lazy — the BranschgruppProvider precedent). Immutable +
        // thread-safe, so a singleton instance is correct.
        services.AddSingleton<Jobbliggaren.Application.CompanyWatches.Abstractions.ICriterionReferenceProvider>(
            new CompanyRegister.Reference.CriterionReferenceProvider(
                CompanyRegister.Reference.CriterionReferenceLoader.LoadSni(),
                CompanyRegister.Reference.CriterionReferenceLoader.LoadKommuner()));

        // #311 PR-5 (ADR 0087 D4) — the curated brand-group catalogue behind IBrandGroupProvider. Same
        // eager-INSTANCE fail-loud posture as the reference provider above: BrandGroupLoader runs HERE,
        // at host build, so a malformed (or personnummer-shaped-member) catalogue kills the host loudly
        // instead of surfacing on the first group follow. Immutable + thread-safe → singleton instance.
        services.AddSingleton<Jobbliggaren.Application.CompanyWatches.Abstractions.IBrandGroupProvider>(
            new CompanyWatches.BrandGroupProvider(CompanyWatches.BrandGroupLoader.Load()));

        // STEG 6 Approach B (2026-05-24) — fritext→SSYK-expansion för
        // recall-lift på terms som "systemutvecklare". IOptions-binding från
        // appsettings.json SearchSynonyms-sektion. DI i samma commit som
        // port-impl (feedback_di_with_handlers_same_commit). Scoped paritet
        // IJobAdSearchQuery (samma livscykel).
        services.AddOptions<Jobbliggaren.Application.JobAds.Abstractions.SearchSynonymsOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Abstractions.SearchSynonymsOptions.SectionName));
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IOccupationSynonymExpander,
            JobAds.OccupationSynonymExpander>();

        // #630 PR 4 (design §11, superseding ADR 0085 §3) — /ansokningar
        // attention-prioritisation thresholds. Application owns the contract; bound
        // here (ApplicationAttention section) with data-annotation + start-time
        // validation (parity with the digest/backfill options). The per-aggregate
        // Application.GhostedThresholdDays is intentionally NOT bound and no longer
        // feeds any signal — ghost-suggest keys on the GhostSuggestDays option.
        services.AddOptions<Jobbliggaren.Application.Applications.Attention.ApplicationAttentionOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.Applications.Attention.ApplicationAttentionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TD-13 (ADR 0049) / ADR 0066 — lokal envelope-fält-kryptering.
        // Registrerad i AddPersistence: per-användare-DEK + interceptor-paret
        // (C3) lever på AppDbContext-livscykeln; måste vara tillgänglig i både
        // Api och Worker (HardDeleteAccountsJob crypto-erasure, C6).
        // AesGcmFieldEncryptor + LocalDataKeyProvider är stateless/trådsäkra →
        // singleton. Fail-closed startup via IValidateOptions
        // (.ValidateOnStart()): en tom/ogiltig lokal master-nyckel hård-failar
        // i ALLA miljöer (FieldEncryptionOptionsValidator) — provider-agnostiskt
        // sedan KMS-grenen togs bort (#802).
        services.AddOptions<Security.FieldEncryptionOptions>()
            .Bind(configuration.GetSection(Security.FieldEncryptionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.FieldEncryptionOptions>,
            Security.FieldEncryptionOptionsValidator>();

        // IFieldEncryptor (AES-256-GCM-primitiv) är AWS-fri och oberoende av
        // DEK-wrap-mekanismen — registreras ovillkorligt.
        services.AddSingleton<Jobbliggaren.Application.Common.Security.IFieldEncryptor,
            Security.AesGcmFieldEncryptor>();

        // ADR 0066 (AWS-exit klar, #802) — Local är enda DEK-providern. En
        // utelämnad Provider defaultar Local; ett explicit icke-Local-värde
        // (t.ex. en kvarlämnad "Kms" i stale config) MÅSTE dö loud vid boot —
        // aldrig tyst falla till Local (det skulle maskera en felkonfiguration;
        // #802-footgunklassen). Den AWS-KMS-baserade providern + klienten är
        // borttagna; ingen Amazon-SDK-instans registreras PÅ KRYPTERINGSVÄGEN. (Sedan #183 finns
        // INGEN Amazon-klient alls i lösningen — e-postarmen flyttade till Scaleway och tog
        // AWSSDK-paketet med sig, så parentesens tidigare undantag för SES-avsändaren har inget
        // kvar att undanta. NoAmazonReferenceTests pinnar det.)
        var fieldEncryptionProvider = configuration[
            $"{Security.FieldEncryptionOptions.SectionName}:Provider"];
        if (!string.IsNullOrWhiteSpace(fieldEncryptionProvider)
            && !string.Equals(fieldEncryptionProvider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"FieldEncryption:Provider='{fieldEncryptionProvider}' stöds inte. " +
                "AWS KMS-providern är borttagen (ADR 0066/#802) — enda giltiga " +
                "värdet är 'Local' (eller utelämna nyckeln för default).");
        }

        services.AddSingleton<Jobbliggaren.Application.Common.Security.IDataKeyProvider,
            Security.LocalDataKeyProvider>();

        // TD-13 C2 (ADR 0049 Beslut 1, CTO FRÅGA 2). Scoped: delar scopets
        // AppDbContext (DeleteDataKeysAsync deltar i hard-delete-transaktionen
        // C6) + cachen nollar nyckelmaterial vid scope-dispose. UserDataKey
        // exponeras aldrig via IAppDbContext (arch-test-spärr).
        // C3-justering: registrera konkreta ScopedUserDataKeyCache + låt
        // IUserDataKeyCache forwarda till SAMMA scoped-instans, så
        // FieldDecryptionMaterializationInterceptor (injicerar konkreta typen
        // för synkron internal TryPeekCachedDek, Seam 3) och store delar
        // cache-instans per scope.
        services.AddScoped<Security.ScopedUserDataKeyCache>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.IUserDataKeyCache>(
            sp => sp.GetRequiredService<Security.ScopedUserDataKeyCache>());
        services.AddScoped<Jobbliggaren.Application.Common.Security.IUserDataKeyStore,
            Security.UserDataKeyStore>();

        // TD-13 C5 (ADR 0049 Beslut 4, architect-låst 2026-05-19). Backfill-
        // porten äger per-owner fresh DI-scope via IServiceScopeFactory
        // (cross-user-DEK-isolering, §5.1) → Scoped. DI i samma commit som
        // port/job-impl (feedback_di_with_handlers_same_commit).
        services.AddScoped<
            Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption.IFieldEncryptionBackfiller,
            Security.FieldEncryptionBackfiller>();

        // TD-13 C3 (Mekanik-not 5c). Interceptor-paret SINGLETON (stateless,
        // ISingletonInterceptor; scoped state via Context.GetService vid
        // invocation). ICurrentDataOwner förblir Scoped (request/job-bunden).
        services.AddSingleton<Security.FieldEncryptionSaveChangesInterceptor>();
        services.AddSingleton<Security.FieldDecryptionMaterializationInterceptor>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.ICurrentDataOwner,
            Security.CurrentDataOwner>();

        // Fas 4b PR-9a (ADR 0093 §D5 / ADR 0100) — Form C: binärcipher stateless →
        // singleton (paritet IFieldEncryptor); write-path-sealern Scoped (peekar
        // scopets ScopedUserDataKeyCache via ICurrentDataOwner, CTO Q2 explicit seal).
        services.AddSingleton<Jobbliggaren.Application.Common.Security.IBinaryFieldEncryptor,
            Security.BinaryFieldEncryptor>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.IBinaryFieldSealer,
            Security.BinaryFieldSealer>();
        // Fas 4b PR-9b (ADR 0100 §D3 read-path) — the read-side opener, Scoped for the same reason
        // as the sealer (peeks the scope's ScopedUserDataKeyCache via ICurrentDataOwner).
        services.AddScoped<Jobbliggaren.Application.Common.Security.IBinaryFieldOpener,
            Security.BinaryFieldOpener>();

        return services;
    }

    /// <summary>
    /// #1350 — the Data-Protection key discriminator. Api-scoped on purpose: the Worker mints and
    /// validates no DataProtector tokens, so it must not share this keyring.
    ///
    /// <para>
    /// The value is deliberately NOT the assembly name. NetArchTest's <c>HaveDependencyOnAny</c>
    /// searches <b>const string fields</b> as well as IL references —
    /// <c>DependencySearch.FindTypes(..., serachForDependencyInFieldConstant: true)</c> reaches
    /// <c>TypeDefinitionCheckingContext.CheckFields</c>, which feeds every constant string field's
    /// VALUE to the dependency check. A <c>const string</c> holding <c>Jobbliggaren.Api</c> therefore
    /// counts as a dependency on the Api assembly and fails
    /// <c>DomainLayerTests.Infrastructure_should_not_depend_on_Api_or_Worker</c> — measured
    /// 2026-08-19, and the only difference between a red and a green run.
    ///
    /// The rule is therefore "not in a const field", NOT "not in a string": an inline literal in a
    /// method body passes, because the IL scan only looks at type, method and field REFERENCES.
    /// Inlining it would be worse anyway — a magic string per CLAUDE.md §5, whose silence would then
    /// be an accident of the tool rather than a decision.
    /// </para>
    ///
    /// <para>
    /// Whatever it is, it must never change again: it is the key discriminator, and a new value
    /// stops the persisted keyring resolving every token minted under the old one.
    /// </para>
    /// </summary>
    public const string DataProtectionApplicationName = "jobbliggaren-api";

    /// <summary>
    /// #1350 — configuration key for the directory the keyring is persisted to. Unset means the
    /// framework default (dev); the deployed host sets it and mounts a writable volume there.
    /// <c>DeployComposeDataProtectionTests</c> reads this constant, so a rename here fails that test
    /// rather than silently un-persisting the keys.
    /// </summary>
    public const string DataProtectionKeyPathConfigKey = "DataProtection:KeyPath";

    /// <summary>
    /// #1350 — the Api's Data-Protection keyring. Its own method rather than four lines inside
    /// <see cref="AddIdentityAndSessions"/>, because that one needs Postgres and Redis to register
    /// at all and this must be testable without either (CLAUDE.md §2.4).
    ///
    /// <para>
    /// <b>Api only.</b> <c>AddCoreIdentityForWorker</c> deliberately registers no
    /// <c>IDataProtectionProvider</c>, and the only consumer is <c>PasswordResetTokenProvider</c>'s
    /// constructor. Sharing a keyring with the Worker would hand it cryptographic reach over tokens
    /// it never mints or validates, and re-open the cross-process coupling the 2026-07-10 ruling
    /// rejected. This codebase has no antiforgery, so the keyring's blast radius is the three
    /// token KINDS those providers mint - activation, password reset, change email - and
    /// nothing else. (Two <c>DataProtectorTokenProvider</c>s, not three: of the four
    /// <c>AddDefaultTokenProviders</c> registers only Default is DataProtector-based, the other
    /// three being TOTP, plus the named password-reset provider.) Regenerate with
    /// <c>git grep -in antiforgery -- src/</c> and read the result as a property, not a count — a
    /// comment naming it will match.
    /// </para>
    /// </summary>
    public static IServiceCollection AddApiDataProtection(
        this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection()
            // Pinned rather than derived. Without this the key discriminator comes from
            // IHostEnvironment.ContentRootPath, so an image-layout change would silently stop the
            // PERSISTED keyring from resolving older tokens — the same defect, reintroduced after
            // the fix and harder to see. Setting it now invalidates nothing that was not already
            // dying on the next recreate.
            .SetApplicationName(DataProtectionApplicationName);

        // Optional by design, in the ratified Seq:ServerUrl shape: set → persist, unset → framework
        // default, so a dev boot is unchanged and CLAUDE.md §11's dev-boot contract is not engaged.
        //
        // A Production-gated fail-loud was available (ForwardedHeadersConfig does exactly that on an
        // empty KnownNetworks) and was declined for a reason worth keeping: a boot refusal only
        // covers "the key is missing" and says nothing about "the directory is unwritable", which is
        // the more expensive failure and the one that shipped past five green mutation axes.
        // DeployComposeDataProtectionTests covers both, in CI.
        var keyPath = configuration[DataProtectionKeyPathConfigKey];
        if (!string.IsNullOrWhiteSpace(keyPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        return services;
    }

    /// <summary>
    /// Identity, sessions, Redis, HTTP-baserad <see cref="ICurrentUser"/>,
    /// auth audit logger. HTTP-only. Worker laddar inte denna modul.
    /// (#827: "JWT-rester" stod här tills de resterna faktiskt raderades.)
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

        services.AddApiDataProtection(configuration);

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
                // NIST SP 800-63B: length is the primary defense, complexity secondary.
                // The §5.1.1.2 blocklist requirement (breach-corpus check on registration/
                // change) is enforced by PwnedPasswordValidator (#616), chained below —
                // HIBP k-anonymity, fail-open per CTO-bind (see AddBreachedPasswordCheck).
                opts.Password.RequiredLength = 12;
                opts.Password.RequireNonAlphanumeric = false;
                opts.Password.RequireDigit = false;
                opts.Password.RequireUppercase = false;
                opts.Password.RequireLowercase = false;
                opts.User.RequireUniqueEmail = true;

                // #679 (CTO-bind #1): route the change-email confirmation token through the
                // opaque DataProtector provider that .AddDefaultTokenProviders() below registers.
                // Identity's default ChangeEmailTokenProvider is the "Email" provider — a 6-digit
                // TOTP that is short-lived (~9 min, breaks a normal email round-trip) and
                // brute-forceable (10^6, stateless), which on the PUBLIC confirm endpoint would be
                // an account-takeover path. The DataProtector token is HMAC'd + encrypted, bound to
                // (SecurityStamp, new email), single-use (SecurityStamp rotates on ChangeEmailAsync),
                // and honours the 24h TokenLifespan. Email-confirm already defaults here; password-reset
                // did too until #1171 moved it to its own named provider (see below) for a shorter life.
                opts.Tokens.ChangeEmailTokenProvider = TokenOptions.DefaultProvider;

                // #714 (defense-in-depth): pin the email-confirmation token to the same opaque
                // DataProtector provider. Unlike ChangeEmail above, EmailConfirmationTokenProvider
                // ALREADY defaults to TokenOptions.DefaultProvider (it is PasswordReset/email-confirm
                // that default here, not the "Email" TOTP provider), so this is not a fix but an
                // explicit, self-documenting guard against a future Identity default drift — the
                // registration confirm endpoint is PUBLIC, so a short brute-forceable TOTP would be an
                // account-activation-takeover path (parity with the #679 rationale).
                opts.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultProvider;

                // #1171: password-reset gets its OWN provider, registered by name below, so its
                // lifespan can be shorter than the shared 24h without touching the two kinds above.
                // The shared DataProtectionTokenProviderOptions is one type read by one provider, so
                // configuring it would shorten all three — and the change-email and email-confirm
                // bodies promise 24h in published copy. PasswordResetTokenProviderOptions carries the
                // number and EmailTemplates.PasswordReset reads the same constant.
                opts.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName;

                // #503 (OWASP A07 / NIST SP 800-63B §5.2.2): per-account anti-automation on
                // login. ValidateCredentialsAsync (UserAccountService) counts failed attempts
                // via AccessFailedAsync and short-circuits locked accounts via IsLockedOutAsync.
                // Temporary, auto-expiring lockout (avoid self-DoS): 5 attempts -> 15 min, on
                // top of the per-IP AuthWrite throttle (20/min).
                opts.Lockout.MaxFailedAccessAttempts = 5;
                opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                opts.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders()
            // #1171: its own ProviderMap name, distinct from the four AddDefaultTokenProviders registers
            // (Default/Email/Phone/Authenticator), so it adds a provider rather than replacing one.
            .AddTokenProvider<PasswordResetTokenProvider<ApplicationUser>>(PasswordResetTokenProviderName)
            // #616 (CTO-bind Variant B): breached-password rejection at the UserManager
            // chokepoint — CreateAsync + ChangePasswordAsync (and any future reset flow)
            // are covered by this ONE registration. Api-EXCLUSIVE: AddCoreIdentityForWorker
            // never chains this, so the Worker stays HTTP-free (ADR 0023).
            .AddPasswordValidator<PwnedPasswordValidator>();

        services.AddBreachedPasswordCheck(configuration);

        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = redisConnectionString;
            opts.InstanceName = "jobbliggaren:";
        });

        // IConnectionMultiplexer registreras separat så RedisSessionStore kan
        // använda Redis SET-kommandon (SADD/SREM/SMEMBERS) för secondary user-
        // sessions-index — krävs för InvalidateAllForUserAsync vid kontoradering
        // (ADR 0024 D4 + ADR 0017 deferred-not stängd här). IDistributedCache
        // stödjer bara key-value, inte SET. Singleton — lazy connect, fungerar
        // även om Redis är ner vid app-start.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));

        // #746 — bind + validate at startup: SessionStoreOptionsValidator caps SlideThreshold to
        // [0.0, 0.25] (a bad throttle value must fail the boot, not silently widen the Art.17
        // orphan self-heal window). ValidateOnStart() forces the check eagerly.
        services.AddOptions<SessionStoreOptions>()
            .Bind(configuration.GetSection(SessionStoreOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SessionStoreOptions>, SessionStoreOptionsValidator>();

        // #714 — email-confirmation-first registration toggle (Application-owned contract, bound
        // here). Read by RegisterCommandHandler + UserAccountService.ValidateCredentialsAsync.
        //
        // ADR 0083 Amendment 2026-08-03: bound via AddOptions/ValidateOnStart so AuthOptionsValidator
        // can refuse the one unsafe combination (RegistrationsOpen without RequireEmailConfirmation,
        // outside Development/Test). Registered HERE only — AddCoreIdentityForWorker binds the same
        // section with a plain Configure, deliberately: the Worker owns no registration surface, so a
        // shared env file must not take it down for a condition it cannot exercise.
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();

        // DEV-ONLY throwaway tooling toggle — REMOVE BEFORE LAUNCH with everything it gates
        // (docs/runbooks/release-checklist.md). Plain Bind, deliberately NOT ValidateOnStart:
        // there is no unsafe COMBINATION to refuse here, and a validator that rejected the flag
        // outside Development would forbid its only purpose. Fail-closed lives in the default
        // (false) and in the two independent gates that read it — the map gate in Program.cs and
        // the handler's own refusal.
        services.Configure<DevToolsOptions>(configuration.GetSection(DevToolsOptions.SectionName));

        // #733/#703 — Redis-backed anti-email-bomb cooldown gate (ICooldownGate) + its window options. The
        // gate is the #733 primitive generalised (a policy-free check-and-set on a (scope, subject) pair)
        // and now throttles four requester-chosen-address outbound surfaces: confirmation-link resend
        // (#733, window from ResendCooldownOptions), the register account-exists notice, the
        // change-email request, and the forgot-password request (#703/#1171, windows from
        // AuthEmailCooldownOptions). Api-only — the cooldown runs
        // in the request path. ValidateOnStart + [Range] so a misconfigured window fails the host loud (a
        // security invariant), parity DigestDispatchOptions. The two option sections stay independent so the
        // already-shipped Auth:ResendCooldown key is not broken.
        services.AddOptions<ResendCooldownOptions>()
            .Bind(configuration.GetSection(ResendCooldownOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AuthEmailCooldownOptions>()
            .Bind(configuration.GetSection(AuthEmailCooldownOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<ICooldownGate, RedisCooldownGate>();

        // #1171 — the out-of-band forgot-password dispatch. Api-EXCLUSIVE for the same reason the
        // cooldown is (it runs in the request path) and for one more that is structural: the consumer
        // MINTS a reset token, which needs the token providers only this composition registers. The
        // Worker cannot host it. Singleton because the channel is the shared state; the hosted service
        // is its only reader.
        services.AddOptions<PasswordResetDispatchOptions>()
            .Bind(configuration.GetSection(PasswordResetDispatchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<PasswordResetDispatchChannel>();
        services.AddSingleton<IPasswordResetDispatcher>(
            sp => sp.GetRequiredService<PasswordResetDispatchChannel>());
        services.AddHostedService<PasswordResetDispatchService>();

        // Admin-bootstrap: idempotent seeder kör vid app-startup. Skapar Admin-rollen
        // om saknas och tilldelar till user med email AdminBootstrap__InitialAdminEmail.
        // Senior-cto-advisor-beslut 2026-05-11 (B1 — IaC over manual psql-script).
        services.Configure<AdminBootstrapOptions>(configuration.GetSection(AdminBootstrapOptions.SectionName));
        services.AddHostedService<IdempotentAdminRoleSeeder>();

        // #511 (senior-cto-advisor Variant C, 2026-07-10): the concrete store is wrapped in a
        // resilience decorator that translates the degraded-Redis exceptions RedisSessionStore
        // does not itself wrap (RedisTimeoutException/RedisServerException) into
        // SessionStoreUnavailableException, so the Api middleware renders a 503 rather than an
        // unhandled 500. RedisSessionStore.cs (auth-lane hotspot, §6.5) stays untouched, and the
        // Redis exception knowledge stays inside Infrastructure (§2.1 — the Api pipeline never
        // sees a StackExchange.Redis type).
        services.AddScoped<RedisSessionStore>();
        services.AddScoped<ISessionStore>(sp =>
            new SessionStoreResilienceDecorator(sp.GetRequiredService<RedisSessionStore>()));

        // #481 Low — login-timing equalizer (singleton: owns one PasswordHasher + a memoized dummy
        // hash). Injected into UserAccountService to pay a constant PBKDF2 cost on the unknown-email
        // login branch so response timing does not enumerate registered accounts.
        services.AddSingleton<ILoginTimingEqualizer, LoginTimingEqualizer>();
        services.AddScoped<IUserAccountService, UserAccountService>();

        // #746 PR-B: role resolution moved OUT of an IClaimsTransformation (which ran on every
        // authenticated request) and INTO the Api-layer Admin authorization handler
        // (AdminRoleAuthorizationHandler), which resolves roles on demand only when the Admin policy
        // is evaluated — so non-admin requests and 429'd floods resolve zero roles (epic #737 d2/d4).
        // Per-request fetch (immediate-revoke, A1) is preserved there; no cache is introduced.

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuthAuditLogger, AuthAuditLogger>();

        // PR2c (C5, epik #481) — the single re-auth check (consumed by ReauthenticationBehavior +
        // the /auth/verify handler). Registered ONLY in the Api composition: it depends on
        // ISessionStore/ICurrentUser (above), which the HTTP-free Worker (ADR 0023) does not have.
        // ReauthenticationBehavior injects IEnumerable<IReauthenticationService> so it still
        // constructs in the Worker (empty sequence → the re-auth guard never fires there).
        services.AddScoped<IReauthenticationService, Jobbliggaren.Application.Auth.ReauthenticationService>();

        return services;
    }

    /// <summary>
    /// #616 (ADR: HIBP breach check) — typed HttpClient for the Pwned Passwords k-anonymity
    /// range API behind <see cref="IBreachedPasswordChecker"/>. Api-ONLY: called from
    /// <see cref="AddIdentityAndSessions"/>; the Worker composition (<see cref="AddCoreIdentityForWorker"/>)
    /// stays HTTP-free (ADR 0023) and gets no extra password validators.
    ///
    /// <para>
    /// Resilience is CTO-bound for an INTERACTIVE hot path, deliberately NOT the batch-ingest
    /// profile of AddStandardResilienceHandler (ADR 0032 / BUILD.md §9.1): total attempt budget
    /// ~2 s, ZERO retries (fail-open makes a retry pure added latency for a waiting user), and a
    /// circuit breaker so a sustained HIBP outage fails open INSTANTLY instead of costing every
    /// registration the full timeout.
    /// </para>
    /// </summary>
    public static IServiceCollection AddBreachedPasswordCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BreachCheckOptions>()
            .Bind(configuration.GetSection(BreachCheckOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Opt-OUT kill switch: a missing section/key means ENABLED (defaultValue: true).
        // Do not copy the ScbRegister opt-in idiom here — a silently-disabled breach check
        // would be an invisible security regression.
        var enabled = configuration.GetValue(
            $"{BreachCheckOptions.SectionName}:{nameof(BreachCheckOptions.Enabled)}", defaultValue: true);
        if (!enabled)
        {
            services.AddSingleton<IBreachedPasswordChecker, DisabledBreachedPasswordChecker>();
            return services;
        }

        services.AddHttpClient<IBreachedPasswordChecker, HibpPasswordBreachClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<BreachCheckOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                // Response-size side-channel defense: pads every range response to 800–1000
                // lines (padding lines carry count 0 — the client discards them).
                client.DefaultRequestHeaders.Add("Add-Padding", "true");
                // HIBP rejects UA-less requests. First outgoing client in the codebase to need
                // a User-Agent — deliberate new pattern, set here in parity with ApplyApiKey.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Jobbliggaren/1.0 (+https://jobbliggaren.se)");
                // Backstop only — the Polly attempt timeout below is the real ~2 s budget.
                client.Timeout = TimeSpan.FromSeconds(10);
                // Padded responses are ~40 kB; cap the buffer as DoS hygiene.
                client.MaxResponseContentBufferSize = 1_000_000;
            })
            // CRITICAL (CTO-bind fail-open observability condition): the default HttpClientFactory
            // LogicalHandler/ClientHandler loggers write the full request URI — which contains the
            // 5-char SHA-1 prefix — at Information. Nothing credential-derived may reach the logs,
            // so ALL default client logging is removed; EventId 5001 in HibpPasswordBreachClient is
            // the only telemetry this client emits.
            .RemoveAllLoggers()
            .AddResilienceHandler("hibp-breach-check", (builder, context) =>
            {
                var opts = context.ServiceProvider
                    .GetRequiredService<IOptions<BreachCheckOptions>>().Value;

                // Circuit breaker FIRST (= outermost) so the inner attempt-timeout's
                // TimeoutRejectedException is counted by the breaker's default ShouldHandle —
                // reversed order would mean timeouts never open the circuit (CTO-bind FORK 3).
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    MinimumThroughput = opts.CircuitBreakerMinimumThroughput,
                    FailureRatio = opts.CircuitBreakerFailureRatio,
                    SamplingDuration = TimeSpan.FromSeconds(opts.CircuitBreakerSamplingSeconds),
                    BreakDuration = TimeSpan.FromSeconds(opts.CircuitBreakerBreakSeconds),
                });

                // Total budget for the single attempt. NO AddRetry — retry 0 is the CTO-bound
                // interactive profile (a failed attempt goes straight to fail-open).
                builder.AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSeconds));
            });

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

        // #481 Low — required by UserAccountService's constructor (see AddIdentityAndSessions). The
        // Worker never logs in, but the dependency must resolve wherever UserAccountService is built.
        services.AddSingleton<ILoginTimingEqualizer, LoginTimingEqualizer>();

        // #714 — DI-parity: UserAccountService (built here too) now injects IOptions<AuthOptions> for
        // the EmailConfirmed login gate. Bind it in the Worker composition as well or the container
        // cannot construct UserAccountService (same discipline as ILoginTimingEqualizer above). The
        // Worker never logs in, so the gate is inert here, but the option must resolve.
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IAccountHardDeleter, AccountHardDeleter>();

        return services;
    }
}
