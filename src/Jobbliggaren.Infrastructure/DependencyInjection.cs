using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.RateLimiting;
using Refit;
using Resend;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure;

public static class DependencyInjection
{
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

        // Fas 4 STEG 10 — the deterministic CV-build/improve engine (propose-and-approve diffs;
        // consumes the knowledge bank + ITextAnalyzer, both already registered above). See
        // AddCvImprovement. NO AI/LLM.
        services.AddCvImprovement();

        // Fas 4 STEG 10 — the deterministic CV renderer (QuestPDF ATS-plain + visual from the
        // same JSON source). Own module (sets the QuestPDF Community licence once). See
        // AddCvRendering. NO AI/LLM.
        services.AddCvRendering();

        // TD-73 prod-gating: Right-to-erasure-impl för rekryterar-PII (ADR 0032
        // §8 amendment 2026-05-13). Postgres-specifik JsonContains-LINQ kapslas
        // in i Infrastructure för att hålla Application Npgsql-fri (Clean Arch).
        services.AddScoped<IRecruiterPiiPurger, RecruiterPiiPurger>();

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

        // Fas 4 STEG 4b (F4-4b) — requirements re-ingest backfill (must_have/
        // nice_to_have-skills → Requirement-termer). Tunn wrapper kring
        // JobAdRefetchBackfillRunner (paritet Klass2). Predikatet behöver Npgsql
        // jsonb ?-operatorn → kapslas i Infrastructure bakom
        // IJobAdRequirementBackfillFilter så Application förblir Npgsql-fritt (CLAUDE.md
        // §2.1, paritet IRecruiterPiiPurger). Filtret är stateless → Singleton; jobb +
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
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.IResumeSegmenter,
            Resumes.Parsing.HeadingDrivenResumeSegmenter>();
        return services;
    }

    /// <summary>
    /// Fas 4 STEG 7/9 (F4-7/F4-9, ADR 0071/0074) — the versioned CV knowledge bank (rubric +
    /// cliché lexicon + weak→strong verb mapping, three ISP ports over embedded VERSIONED
    /// DATA, §5) and the deterministic CV-review engine that scores a ParsedResume against
    /// them. All stateless singletons (bounded immutable data, parity ITaxonomyReadModel).
    /// The engine consumes the NLP-tier <c>ITextAnalyzer</c>, so the caller must also call
    /// <see cref="AddTextAnalysis"/>. Standalone module (parity AddTextAnalysis) so every host
    /// AND the Worker test fixture register it without the job-source HTTP wiring.
    /// NO ISpellChecker on the engine (C1 is NotAssessedV1, V-F). NO AI/LLM.
    /// </summary>
    public static IServiceCollection AddCvReview(this IServiceCollection services)
    {
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IRubricProvider,
            Jobbliggaren.Infrastructure.KnowledgeBank.RubricProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IClicheLexicon,
            Jobbliggaren.Infrastructure.KnowledgeBank.ClicheLexicon>();
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IVerbMapper,
            Jobbliggaren.Infrastructure.KnowledgeBank.VerbMapper>();
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IFrameProvider,
            Jobbliggaren.Infrastructure.KnowledgeBank.FrameProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.ICvReviewEngine,
            Jobbliggaren.Infrastructure.Resumes.Review.CvReviewEngine>();
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
    /// Email provider-switch (ADR 0080 Vag 4 PR-4b). Called by BOTH the Api
    /// (<see cref="AddInfrastructure"/>) AND the HTTP-free Worker (ADR 0023) so both register the
    /// SAME dev=Console/Resend, non-dev=Null gating without drift. The Worker needs
    /// <see cref="IEmailSender"/> for the Vag 4 match-notification jobs (Top-direct scan hook +
    /// <c>DigestDispatchJob</c>). Binds <see cref="EmailOptions"/> and selects the sender per
    /// <c>Email:Provider</c>.
    /// <para>
    /// ADR 0066 — AWS SES borttaget; transaktionell mejlväg via Resend (TD-101).
    /// <see cref="ConsoleEmailSender"/> skriver mottagar-email + plaintext-token till ILogger
    /// (dev-providern) — registreras BARA i Development/Test (TD-104/STEG 6 security-auditor
    /// Major #1: en PERSISTENT logg-sink gör den raden durabel PII-lagring). I andra miljöer
    /// faller "Console" tillbaka på <see cref="NullEmailSender"/> (no-op) tills en riktig provider
    /// wiras. Resend = US-processor: prod-utskick kräver DPA/SCC + security-auditor-sign-off
    /// (CTO 2026-06-24); dev = test-mode. Okänt provider-värde fail-stoppas.
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
        else if (string.Equals(emailProvider, "Resend", StringComparison.OrdinalIgnoreCase))
        {
            // Nyckel ENDAST via gitignored appsettings.Local.json (Email:ApiKey) → fail-LOUD om
            // den saknas (ingen tyst no-op som ser ut att skicka).
            var apiKey = configuration[$"{EmailOptions.SectionName}:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "Email:Provider='Resend' kräver Email:ApiKey (gitignored appsettings.Local.json).");
            }

            // AddResend registrerar IResend som en IHttpClientFactory-typed client (transient).
            services.AddResend(o => o.ApiToken = apiKey);
            // Transient (EJ Singleton): en singleton-sender som fångar den transienta IResend
            // vore en captive dependency som fryser HttpMessageHandler-rotationen.
            services.AddTransient<IEmailSender, ResendEmailSender>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Email:Provider='{emailProvider}' stöds inte. Använd 'Console' eller 'Resend'.");
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

        // TD-13 (ADR 0049) — KMS-envelope fält-kryptering. Registrerad i
        // AddPersistence: per-användare-DEK + interceptor-paret (C3) lever på
        // AppDbContext-livscykeln; måste vara tillgänglig i både Api och
        // Worker (HardDeleteAccountsJob crypto-erasure, C6). KMS-klient +
        // KmsEnvelopeEncryptor är stateless/trådsäkra → singleton (samma
        // mönster som SES-klienten). Fail-closed startup: ADR 0049 Beslut 4
        // mekanik-not (CTO-triage 2026-05-18 Approach D) — miljö-villkorad
        // validering via IValidateOptions (.ValidateOnStart() triggar den vid
        // boot). Hård fail i Production/Staging; warning i Development/Test
        // (runtime-guard i KmsDataKeyProvider är det faktiska fail-closed-
        // skyddet i alla miljöer). Löser C1 J3-regression: global .Validate()
        // bröt ~6 KMS-fakande integ-test-hostar.
        services.AddOptions<Security.FieldEncryptionOptions>()
            .Bind(configuration.GetSection(Security.FieldEncryptionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.FieldEncryptionOptions>,
            Security.FieldEncryptionOptionsValidator>();

        // IFieldEncryptor (AES-256-GCM-primitiv) är AWS-fri och delas av BÅDA
        // DEK-providers — registreras ovillkorligt. Bara DEK-wrap/unwrap
        // (IDataKeyProvider) skiljer Kms- från Local-grenen.
        services.AddSingleton<Jobbliggaren.Application.Common.Security.IFieldEncryptor,
            Security.KmsEnvelopeEncryptor>();

        // ADR 0066 — provider-switch (paritet EmailOptions.Provider). Default
        // "Kms" bevarar befintligt beteende i alla miljöer som inte explicit
        // väljer Local (integ-test-fixturer override:ar KMS-klienten last-wins;
        // prod glömmer-Provider → KMS-försök → loud runtime-fail, ingen tyst
        // lokal krypto). Dev sätter "Local" i appsettings.Development.json.
        var fieldEncryptionProvider = configuration[
            $"{Security.FieldEncryptionOptions.SectionName}:Provider"] ?? "Kms";
        if (string.Equals(fieldEncryptionProvider, "Kms", StringComparison.OrdinalIgnoreCase))
        {
            var kmsRegion = configuration[
                $"{Security.FieldEncryptionOptions.SectionName}:AwsRegion"] ?? "eu-north-1";
            services.AddSingleton<Amazon.KeyManagementService.IAmazonKeyManagementService>(
                _ => new Amazon.KeyManagementService.AmazonKeyManagementServiceClient(
                    Amazon.RegionEndpoint.GetBySystemName(kmsRegion)));
            services.AddSingleton<Jobbliggaren.Application.Common.Security.IDataKeyProvider,
                Security.KmsDataKeyProvider>();
        }
        else if (string.Equals(fieldEncryptionProvider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            // Local-grenen registrerar INTE IAmazonKeyManagementService — ingen
            // onödig AWS-SDK-instans. Master-nyckeln binds via IOptions
            // (appsettings.Local.json, gitignored).
            services.AddSingleton<Jobbliggaren.Application.Common.Security.IDataKeyProvider,
                Security.LocalDataKeyProvider>();
        }
        else
        {
            throw new InvalidOperationException(
                $"FieldEncryption:Provider='{fieldEncryptionProvider}' stöds inte. " +
                "Använd 'Kms' eller 'Local'.");
        }

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
                // NIST SP 800-63B: length is the primary defense, complexity secondary.
                // PwnedPasswords integration (breach-corpus check on registration/change)
                // deferred to #616 (sibling to epic #481) — an external k-anonymity
                // integration with its own GDPR-egress + security gate (senior-cto-advisor #503 G4/SoC).
                opts.Password.RequiredLength = 12;
                opts.Password.RequireNonAlphanumeric = false;
                opts.Password.RequireDigit = false;
                opts.Password.RequireUppercase = false;
                opts.Password.RequireLowercase = false;
                opts.User.RequireUniqueEmail = true;

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
            .AddDefaultTokenProviders();

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

#pragma warning disable JOBBLIGGAREN0001 // JwtSettings och RsaSecurityKey bevaras för RefreshCommandHandler tills Fas 1, ADR 0017
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
#pragma warning restore JOBBLIGGAREN0001

        services.Configure<SessionStoreOptions>(configuration.GetSection(SessionStoreOptions.SectionName));

        // Admin-bootstrap: idempotent seeder kör vid app-startup. Skapar Admin-rollen
        // om saknas och tilldelar till user med email AdminBootstrap__InitialAdminEmail.
        // Senior-cto-advisor-beslut 2026-05-11 (B1 — IaC over manual psql-script).
        services.Configure<AdminBootstrapOptions>(configuration.GetSection(AdminBootstrapOptions.SectionName));
        services.AddHostedService<IdempotentAdminRoleSeeder>();

#pragma warning disable JOBBLIGGAREN0001 // JWT-klasser bevaras för RefreshCommandHandler tills Fas 1, ADR 0017
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAccessTokenRevocationStore, RedisAccessTokenRevocationStore>();
#pragma warning restore JOBBLIGGAREN0001

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

        // PR2c (C5, epik #481) — the single re-auth check (consumed by ReauthenticationBehavior +
        // the /auth/verify handler). Registered ONLY in the Api composition: it depends on
        // ISessionStore/ICurrentUser (above), which the HTTP-free Worker (ADR 0023) does not have.
        // ReauthenticationBehavior injects IEnumerable<IReauthenticationService> so it still
        // constructs in the Worker (empty sequence → the re-auth guard never fires there).
        services.AddScoped<IReauthenticationService, Jobbliggaren.Application.Auth.ReauthenticationService>();

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
