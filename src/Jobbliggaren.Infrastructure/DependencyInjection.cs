using System.Net.Http;
using System.Threading.RateLimiting;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Admin.Accounts;
using Jobbliggaren.Infrastructure.Auditing;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Access;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Jobbliggaren.Infrastructure.Auth.Auditing;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth.Registration;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.JobSources;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.RateLimiting;
using Refit;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The API's entry point. The Worker does not call it; it registers
    /// <see cref="AddPersistence"/> and the modules it needs, with its own implementations of the
    /// audit ports (ADR 0022, ADR 0023).
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
        Admin.HostBridge.HostBridgeServiceCollectionExtensions.AddHostBridge(services, configuration);
        services.AddDevOnlyTestingSupport(environment);
        return services;
    }

    /// <summary>
    /// DEV-ONLY testing support — REMOVE BEFORE LAUNCH (Klas). Registers the ports of the two seams the
    /// Playwright E2E suite logs in through without a real mailbox: the seed seam's address policy
    /// (<see cref="Jobbliggaren.Application.Dev.Abstractions.IDevSeedableAddressPolicy"/>, ADR 0142 part 5a)
    /// and the login-code capture (<see cref="AddDevLoginCodeCapture"/>, #1735).
    ///
    /// <para>
    /// Registered ONLY in Development — one of two independent structural gates
    /// (the other is the <c>Program.cs</c> <c>IsDevelopment()</c> gate on the
    /// <c>/api/v1/dev/*</c> endpoint map). The predicate is <c>IsDevelopment()</c>
    /// exactly (not <c>|| IsEnvironment("Test")</c>) so it mirrors the endpoint map-gate
    /// one-for-one: in any deployed environment the ports are absent from the container
    /// and the seams' command handlers cannot resolve (fail-closed).
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevOnlyTestingSupport(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        // NOTE on fail-closed: Mediator registers the seams' command handlers unconditionally,
        // but their dev-only dependencies are registered ONLY here (Development). Outside
        // Development the handlers are dead — they can only throw at
        // Send-time (an unreachable path, since the endpoint is also unmapped), NOT at
        // container-build time, because the Api host leaves ValidateOnBuild off outside
        // Development. If the Api is ever hardened to force ValidateOnBuild=true in all
        // environments, these dead handlers would turn a deployed boot into a startup crash
        // — remove the whole dev-seam before then (REMOVE BEFORE LAUNCH).
        if (environment.IsDevelopment())
        {
            services.AddSingleton<
                Jobbliggaren.Application.Dev.Abstractions.IDevSeedableAddressPolicy,
                Auth.DevSeedableAddressPolicy>();
            services.AddDevLoginCodeCapture();
        }

        return services;
    }

    /// <summary>
    /// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Wraps the last-registered <see cref="IEmailSender"/> in
    /// <see cref="Auth.DevLoginCodeCapturingEmailSender"/> and registers the capture it feeds as
    /// <see cref="Jobbliggaren.Application.Dev.Abstractions.IDevLoginCodeReader"/> (#1735). Its one production
    /// caller is <see cref="AddDevOnlyTestingSupport"/>, under <c>IsDevelopment()</c> and no flag (security-auditor
    /// Q15 condition 1). Internal so no host can call it; the integration host calls it again after it swaps
    /// the sender, since that swap removes the wrapper.
    /// </summary>
    internal static IServiceCollection AddDevLoginCodeCapture(this IServiceCollection services)
    {
        var sender = services.LastOrDefault(d => d.ServiceType == typeof(IEmailSender))
            ?? throw new InvalidOperationException("The login-code capture wraps an IEmailSender; none is registered.");

        services.TryAddSingleton<Auth.DevLoginCodeCapture>();
        services.TryAddSingleton<Jobbliggaren.Application.Dev.Abstractions.IDevLoginCodeReader>(
            sp => sp.GetRequiredService<Auth.DevLoginCodeCapture>());
        services.Remove(sender);
        services.Add(new ServiceDescriptor(
            typeof(IEmailSender),
            sp => new Auth.DevLoginCodeCapturingEmailSender(
                (IEmailSender)(sender.ImplementationInstance
                    ?? sender.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, sender.ImplementationType!)),
                sp.GetRequiredService<Auth.DevLoginCodeCapture>()),
            sender.Lifetime));
        return services;
    }

    /// <summary>
    /// The company-registry lookup port (ADR 0088): binds
    /// <see cref="CompanyRegistry.CompanyRegistryOptions"/> and registers <c>ICompanyRegistry</c>
    /// as a read-through Redis cache (<see cref="CompanyRegistry.CachedCompanyRegistry"/>) over the
    /// provider named by <c>CompanyRegistry:Provider</c>. <c>Fake</c> is allowed only in
    /// Development and Test, like <see cref="AddEmailSender"/>'s console sender, and falls back to
    /// Null elsewhere; <c>Off</c> or no value registers
    /// <see cref="CompanyRegistry.NullCompanyRegistry"/>, which always answers Unavailable so the
    /// lookup degrades instead of failing. Any other value stops startup.
    /// <para>
    /// The caller registers <c>IDistributedCache</c> first (the API through
    /// <see cref="AddIdentityAndSessions"/>). The Worker does not call this module.
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
            // The fixture table must never pass for register data outside Development and Test.
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

        // The port resolves to the cache decorator over whichever provider was registered above.
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
    /// Populates the local SCB company register (ADR 0091). Worker only, so deliberately not part
    /// of <see cref="AddInfrastructure"/>. The refresh orchestrator
    /// (<see cref="IScbCompanyRegisterRefresher"/>), bulk store and partition planner are always
    /// registered; the certificate-based client only when <c>ScbRegister:Enabled</c> is true,
    /// otherwise <see cref="NullScbCompanyRegisterSource"/>, so CI and development without the
    /// certificate never touch it. The client authenticates with a certificate loaded by
    /// thumbprint and puts a process-wide rate limiter first in its pipeline: SCB's budget is per
    /// API id, which no per-endpoint policy can protect, and exceeding it risks a ban.
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
            // No SCB source and no certificate; the refresh job does nothing.
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
                // The rate limiter is registered first, so it is outermost and paces new executions.
                // Retries run inside one acquired permit, so the ceiling towards SCB is held by the
                // sequential client (one call in flight), exponential backoff and failing fast on
                // 429, not by throttling each attempt.
                builder.AddRateLimiter(_scbRegisterRateLimiter);
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    // A 429 is not retried: SCB has signalled overload, and further attempts would
                    // only add to the ban counter. Other failures keep the default transient handling.
                    // A persistent 429 still opens the circuit breaker below for five minutes.
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
    /// The JobTech integration (ADR 0032): the Refit <c>IJobTechSearchClient</c>, the typed
    /// <c>IJobTechStreamClient</c>, <see cref="JobTechPayloadSanitizer"/> and
    /// <see cref="PlatsbankenJobSource"/> as <see cref="IJobSource"/>, together with the jobs,
    /// taxonomy services and backfills built on them. Both hosts call it. The search client uses
    /// the standard resilience pipeline; the stream client has its own (rate limiter, retry,
    /// circuit breaker) because JobStream allows one request per minute.
    /// </summary>
    public static IServiceCollection AddJobSources(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JobTechOptions>()
            .Bind(configuration.GetSection(JobTechOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The Application-owned retention options bind to the same section as JobTechOptions,
        // so Application jobs such as PurgeStaleRawPayloadsJob need not depend on the
        // Infrastructure type.
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

        // JobSearch has no published rate limit, so the standard pipeline is enough.
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

        // JobStream (NDJSON snapshot and stream).
        services.AddHttpClient<IJobTechStreamClient, JobTechStreamClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<JobTechOptions>>().Value;
            client.BaseAddress = new Uri(options.JobStreamBaseUrl);
            ApplyApiKey(client, options);
            client.Timeout = TimeSpan.FromMinutes(5);
            // Deliberately no MaxResponseContentBufferSize: it bounds only a buffered read, and
            // both paths in JobTechStreamClient stream the body (ResponseHeadersRead and
            // per-element deserialisation), so a cap would enforce nothing. A huge response is
            // contained by the streaming itself (memory is bounded by the largest element) and by
            // SyncPlatsbankenSnapshotJob's floor guards against a corrupt corpus. Timeout covers
            // only reading the headers; the body read is bounded by the job's cancellation token.
        })
        .AddResilienceHandler("jobstream", builder =>
        {
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

        // The taxonomy anti-corruption layer (ADR 0043). The read model is a singleton with a
        // lazy in-memory cache of the immutable snapshot table, refreshed on restart. The seeder
        // fills taxonomy_concepts from the embedded taxonomy-snapshot.json at startup,
        // idempotently and aware of the snapshot version.
        services.AddSingleton<ITaxonomyReadModel,
            Jobbliggaren.Infrastructure.Taxonomy.TaxonomyReadModel>();
        services.AddHostedService<
            Jobbliggaren.Infrastructure.Taxonomy.TaxonomySnapshotSeeder>();

        // Deterministic derivation of an SSYK level-4 occupation group from a job title: the
        // engine proposes, the user confirms (ADR 0040 Beslut 4). A singleton with a lazy cache.
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.IOccupationCodeDeriver,
            Jobbliggaren.Infrastructure.Taxonomy.OccupationCodeDeriver>();

        // Attributes experience to occupations when a CV is imported (ADR 0079). Stateless.
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.IOccupationExperienceDeriver,
            Jobbliggaren.Infrastructure.Resumes.Parsing.OccupationExperienceDeriver>();

        // One shared skill-taxonomy index, used by both the job-ad extractor and the CV skill
        // resolver so the two sides cannot diverge (ADR 0076 Decision 6).
        services.AddSingleton<Jobbliggaren.Infrastructure.Taxonomy.SkillTaxonomyIndex>();

        // Deterministic keyword and skill extraction per job ad.
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdKeywordExtractor,
            Jobbliggaren.Infrastructure.Taxonomy.JobAdKeywordExtractor>();

        // Resolves free-text CV skill names to JobTech concept ids through the same index.
        services.AddSingleton<
            Jobbliggaren.Application.Matching.Abstractions.ISkillResolver,
            Jobbliggaren.Infrastructure.Taxonomy.SkillResolver>();

        // The matching engine is its own module so the Worker test fixture can register it
        // without the rest of AddInfrastructure.
        services.AddMatchingEngine();

        // The CV knowledge bank and review engine, also its own module; see AddCvReview.
        services.AddCvReview();

        // The CV improvement module is deferred, not removed (ADR 0112): its endpoints are gone,
        // so nothing sends its commands. It stays registered on purpose. AddMediator registers
        // the deferred handlers anyway, and in Development the API validates the container on
        // build, so without these registrations host startup fails. The registrations are inert:
        // lazy singletons nothing constructs. Do not answer this by turning ValidateOnBuild off
        // as the Worker does; that is a known gap, not a pattern.
        services.AddCvImprovement();

        // The CV renderer; see AddCvRendering.
        services.AddCvRendering();

        // The ingestion-throughput measurement (ADR 0045). Bound here because both hosts call
        // AddJobSources, so the registration cannot drift between them.
        services.AddOptions<IngestionThroughputOptions>()
            .Bind(configuration.GetSection(IngestionThroughputOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        // A singleton: it is stateless, and scoped jobs may depend on a singleton.
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Jobs.Common.IngestionThroughputReporter>();

        services.AddScoped<Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken.SyncPlatsbankenStreamJob>();
        services.AddScoped<Jobbliggaren.Application.JobAds.Jobs.SyncPlatsbanken.SyncPlatsbankenSnapshotJob>();
        services.AddScoped<Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads.PurgeStaleRawPayloadsJob>();

        // Snapshot retention (ADR 0032). The miss tracker is scoped so it shares the DbContext
        // with the jobs.
        services.AddScoped<IJobAdSnapshotMissTracker,
            Jobbliggaren.Infrastructure.JobAds.SnapshotMisses.JobAdSnapshotMissTracker>();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.RetainPlatsbankenJobAds.RetainPlatsbankenJobAdsJob>();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds.ExpireJobAdsJob>();

        // The field-encryption backfill (ADR 0049 Beslut 4).
        services.AddScoped<
            Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption.BackfillFieldEncryptionJob>();

        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.Common.JobAdRefetchBackfillRunner>();

        // Backfills ssyk_concept_id on older rows. Its delay and cap are options.
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk.BackfillJobAdSsykOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk.BackfillJobAdSsykOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk.BackfillJobAdSsykJob>();

        // Backfills employment_type and worktime_extent on older rows (ADR 0067 Beslut 2).
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2.BackfillJobAdKlass2Options>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2.BackfillJobAdKlass2Options.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2.BackfillJobAdKlass2Job>();

        // Recomputes extracted_terms locally, without fetching from JobTech.
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms.BackfillJobAdExtractedTermsJob>();

        // A one-off scrub of recruiter contact details (ADR 0106 Tier A). It runs only on the
        // owner's decision, and the admin endpoint defaults to a dry run.
        services.AddOptions<Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub.BackfillRecruiterContactScrubJob>();

        // A one-off backfill that tokenises organisation numbers shaped like a personal identity
        // number in company_watches (ADR 0090 D5). Owner-gated; the endpoint defaults to a dry run.
        services.AddOptions<Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken.BackfillCompanyWatchOrgNrTokenOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken.BackfillCompanyWatchOrgNrTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken.BackfillCompanyWatchOrgNrTokenJob>();

        // A one-off backfill that masks personal identity numbers left in
        // parsed_resumes.source_file_name (GDPR Art. 5(1)(c), 25). It runs as one set-based update
        // over the plain column and never loads the encrypted ParsedResume. Owner-gated; the
        // endpoint defaults to a dry run.
        services.AddOptions<Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<
            Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskJob>();

        // Re-ingests must-have and nice-to-have skills as requirement terms. Its predicate needs
        // Npgsql's jsonb `?` operator, so it sits behind IJobAdRequirementBackfillFilter and
        // Application stays free of Npgsql (AGENTS.md §2.1).
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
    /// Public landing-page statistics from a precomputed Redis cache (ADR 0064): the
    /// <c>RefreshLandingStatsJob</c> the Worker runs, and <c>RedisLandingStatsCache</c>, which
    /// writes and reads <c>landing:stats:v1</c>. The API reads, the Worker writes.
    /// <para>
    /// The caller registers <c>IDistributedCache</c> first (the API through
    /// <see cref="AddIdentityAndSessions"/>, the Worker through <c>AddWorkerRedisConnection</c>).
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
    /// The local text-analysis tier for Swedish and English:
    /// <see cref="TextAnalysis.SnowballStemmer"/> (matching PostgreSQL's
    /// to_tsvector('swedish') and ('english')), <see cref="TextAnalysis.LocalTextAnalyzer"/>
    /// (lowercase, tokenise, drop stop words, stem) and
    /// <see cref="TextAnalysis.HunspellSpellChecker"/> (sv_SE DSSO and en_US). Both hosts call
    /// it. All three are thread-safe singletons, and the Hunspell word list loads on first use.
    ///
    /// <para>
    /// Startup fails at once if the dictionary files did not reach the output directory, rather
    /// than at the first spell-check in production.
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
    /// CV import and parsing: <see cref="Resumes.Parsing.PdfPigOpenXmlCvTextExtractor"/>
    /// (<c>ICvTextExtractor</c>; PdfPig and Open XML stay in this assembly), the PDF layout
    /// analyser, and <see cref="Resumes.Parsing.HeadingDrivenResumeSegmenter"/>
    /// (<c>IResumeSegmenter</c>, a string algorithm over the embedded lexicon). All are
    /// stateless singletons over immutable data.
    /// </summary>
    public static IServiceCollection AddCvParsing(this IServiceCollection services)
    {
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Abstractions.ICvTextExtractor,
            Resumes.Parsing.PdfPigOpenXmlCvTextExtractor>();
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
    /// The CV-parsing lexicon and the two knowledge-bank assets validated against it
    /// (branschgrupp, cv-conventions). It is its own idempotent module because
    /// <see cref="AddCvParsing"/>, <see cref="AddCvReview"/> and <see cref="AddCvImprovement"/>
    /// all need it, and the Worker registers some of them without the others; a module that owns
    /// its dependency cannot leave one of them with an unresolvable singleton.
    ///
    /// <para>Idempotence also guarantees a single <c>CvParsingLexiconData</c> instance, so
    /// recognising a heading and resolving which section it is cannot disagree.</para>
    ///
    /// <para>The services are registered as constructed instances, not as types. A type
    /// registration would construct them at the first resolve, inside the first request that
    /// needs them, and <c>ValidateOnBuild</c> does not instantiate singletons. They validate the
    /// assets in their constructors, so constructing them here makes a malformed asset stop the
    /// host at startup instead of failing a user's CV import.</para>
    /// </summary>
    public static IServiceCollection AddCvLexicon(this IServiceCollection services)
    {
        // The guard keys on the LAST service this method registers. Keyed on the first, a caller
        // that registered only that type (a test host with a synthetic lexicon) would switch the
        // module off and leave ICvConventionsProvider unregistered.
        if (services.Any(d =>
                d.ServiceType == typeof(Jobbliggaren.Application.KnowledgeBank.Abstractions.ICvConventionsProvider)))
        {
            return services;
        }

        var lexiconData = Resumes.Parsing.CvParsingLexiconLoader.Load();
        services.AddSingleton(lexiconData);

        var lexicon = new Resumes.Parsing.CvParsingLexiconProvider(lexiconData);
        services.AddSingleton<Jobbliggaren.Application.Resumes.Abstractions.ICvParsingLexicon>(lexicon);

        // Branschgrupp feeds the section suggestions (ADR 0107); cv-conventions feeds the section
        // order (ADR 0108). Both refuse to construct if they name a section the lexicon lacks.
        services.AddSingleton<Jobbliggaren.Application.KnowledgeBank.Abstractions.IBranschgruppProvider>(
            new KnowledgeBank.BranschgruppProvider(lexicon));
        services.AddSingleton<Jobbliggaren.Application.KnowledgeBank.Abstractions.ICvConventionsProvider>(
            new KnowledgeBank.CvConventionsProvider(lexicon));
        return services;
    }

    /// <summary>
    /// The versioned CV knowledge bank (rubric, cliché lexicon, verb mapping, spelling allow-list,
    /// all embedded versioned data) and the deterministic review engine that assesses a CV
    /// against it. The engine uses <c>ITextAnalyzer</c> and <c>ISpellChecker</c>, so the caller
    /// must also call <see cref="AddTextAnalysis"/>.
    /// </summary>
    public static IServiceCollection AddCvReview(this IServiceCollection services)
    {
        // The section-order criterion reads the lexicon and cv-conventions (ADR 0108).
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
        // The spelling criterion's allow-list of proper nouns and technical terms (ADR 0093 §D4).
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.ISpellingAllowlist,
            Jobbliggaren.Infrastructure.KnowledgeBank.SpellingAllowlistProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.ICvReviewEngine,
            Jobbliggaren.Infrastructure.Resumes.Review.CvReviewEngine>();
        // The only path by which the engine writes review findings (ADR 0093 §D5(b)).
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.IResumeReviewReconciler,
            Jobbliggaren.Application.Resumes.Review.ResumeReviewReconciler>();

        // The keyed HMAC that fingerprints a stored finding (ADR 0093 §D2(e)). AddJobSources calls
        // this module, and both hosts call AddJobSources, so both must be given the pepper: only
        // the API computes fingerprints, but the reconciler registered above depends on the
        // fingerprinter in both hosts. BindConfiguration, because this method takes no
        // IConfiguration. ValidateOnStart refuses a missing or weak pepper in every environment.
        services.AddOptions<Security.CvReviewFingerprintPseudonymizationOptions>()
            .BindConfiguration(Security.CvReviewFingerprintPseudonymizationOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.CvReviewFingerprintPseudonymizationOptions>,
            Security.CvReviewFingerprintPseudonymizationOptionsValidator>();
        // Stateless once the pepper is read.
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Review.Abstractions.IFindingFingerprinter,
            Security.HmacFindingFingerprinter>();
        return services;
    }

    /// <summary>
    /// The deterministic matching engine (ADR 0076): the match scorer, internal to this assembly,
    /// and the builder that turns preferences into a match profile. Its own module so the Worker
    /// test fixture can register it without the job-source wiring. Scoped, since both use
    /// <c>AppDbContext</c>.
    /// </summary>
    public static IServiceCollection AddMatchingEngine(this IServiceCollection services)
    {
        services.AddScoped<
            Jobbliggaren.Application.Matching.Abstractions.IMatchScorer,
            Jobbliggaren.Infrastructure.Matching.MatchScorer>();
        services.AddScoped<
            Jobbliggaren.Application.Matching.Abstractions.IMatchProfileBuilder,
            Jobbliggaren.Application.Matching.Profiles.MatchProfileBuilder>();
        // MatchProfileBuilder needs ITaxonomyReadModel. AddJobSources registers it in both hosts,
        // but the Worker test fixture calls only this method, so TryAdd keeps the module
        // self-contained.
        services.TryAddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.ITaxonomyReadModel,
            Jobbliggaren.Infrastructure.Taxonomy.TaxonomyReadModel>();
        return services;
    }

    /// <summary>
    /// The deterministic CV improvement engine, which proposes changes for the user to approve
    /// and never invents content. Deferred: no endpoint sends its commands (ADR 0112), but it
    /// stays registered; see <see cref="AddJobSources"/>. It uses the knowledge-bank ports
    /// (<see cref="AddCvReview"/>) and <c>ITextAnalyzer</c> (<see cref="AddTextAnalysis"/>).
    /// </summary>
    public static IServiceCollection AddCvImprovement(this IServiceCollection services)
    {
        services.AddCvLexicon();
        // Only the improvement handlers use IFrameProvider, so it is registered here.
        services.AddSingleton<
            Jobbliggaren.Application.KnowledgeBank.Abstractions.IFrameProvider,
            Jobbliggaren.Infrastructure.KnowledgeBank.FrameProvider>();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Improvement.Abstractions.ICvImprovementEngine,
            Jobbliggaren.Infrastructure.Resumes.Improvement.CvImprovementEngine>();
        return services;
    }

    /// <summary>
    /// The CV renderer: an ATS-friendly and a visual PDF from the same structured data, with
    /// QuestPDF. The QuestPDF licence is set once here, before any render. QuestPDF stays in this
    /// assembly; the <c>ICvRenderer</c> port depends on nothing outside the base library.
    /// </summary>
    public static IServiceCollection AddCvRendering(this IServiceCollection services)
    {
        Resumes.Rendering.QuestPdfConfiguration.Apply();
        services.AddSingleton<
            Jobbliggaren.Application.Resumes.Rendering.Abstractions.ICvRenderer,
            Jobbliggaren.Infrastructure.Resumes.Rendering.CvRenderer>();
        return services;
    }

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

    // A process-wide limiter for JobStream's one request per minute, shared by the stream and
    // snapshot jobs. The stream (every ten minutes) and the nightly snapshot collide at 02:00;
    // with a queue of two, the later call waits for the next window instead of being rejected
    // and failing its retries inside the same minute. The longest wait is two minutes, and the
    // job's cancellation token ends it. Because the limiter is static, every test that builds
    // the full DI stack shares it; JobTechStreamResilienceTests builds its own container
    // without it.
    private static readonly FixedWindowRateLimiter _streamRateLimiter = new(
        new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

    // A process-wide limiter for SCB (ADR 0091). SCB allows each API id 10 calls per 10 seconds,
    // and exceeding that risks a ban. A sliding window keeps any rolling 10 seconds within the
    // limit, which a fixed window does not across its boundary. Six permits leave a deliberate
    // margin below SCB's ten: running slower only lengthens a night run, while a ban would stop
    // the register. The budget is code, not configuration. The queue is generous, so a throttled
    // call always waits instead of being rejected and retried.
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
        // SECURITY: Microsoft.Extensions.Http's EventSource tracing could log request headers,
        // the key included, if it were enabled; it is off by default and not enabled in
        // production. The key only raises the rate limit on public data.
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation("api-key", options.ApiKey);

        client.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
    }

    /// <summary>
    /// Selects the <see cref="IEmailSender"/> by <c>Email:Provider</c> and binds
    /// <see cref="EmailOptions"/> (ADR 0124). Both hosts call it, so their gating cannot drift.
    /// <para>
    /// <c>Scaleway</c> sends through Scaleway Transactional Email in fr-par, over its HTTPS API,
    /// never SMTP. <c>Console</c>, the default, registers <see cref="ConsoleEmailSender"/> only
    /// in Development and Test: it writes the body, sign-in codes included, to the log for a
    /// recipient at a reserved domain, and a persistent log sink would make that durable personal
    /// data. Elsewhere <c>Console</c> falls back to <see cref="NullEmailSender"/>. Any other value
    /// stops startup.
    /// </para>
    /// <para>
    /// No committed appsettings file sets <c>Email:Provider</c>, so the default applies until a
    /// deployment sets it.
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
            // An allow-list of Development and Test, like the Worker's Hangfire schema gate.
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
            // Read straight from IConfiguration, not through IOptions, so a misconfiguration fails
            // the registration rather than the first email. The check cannot live in the sender's
            // constructor: a type registration is lazy, so the host would start cleanly and fail on
            // the first email.
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

            // Two separate secrets: the key authenticates the caller, the project id selects the
            // project that is billed. Each is checked on its own, so the error names the missing one.
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

            // The sender address is checked here, not left to the EmailOptions default. The
            // domain's DMARC policy rejects mail outside the verified identity without sending
            // reports (ADR 0124), so a wrong address would lose every email silently.
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

            // A backstop for the checks the raw reads above do not express, registered only for
            // this provider. EmailOptions itself deliberately has no ValidateOnStart: a future
            // [Required] on it would make the Email section a startup condition for the default
            // path too, which the local setup does not provide.
            services.AddOptions<ScalewayEmailOptions>()
                .Bind(configuration.GetSection(ScalewayEmailOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // The client registration and the region guard live in Email/ScalewayClientRegistration.
            services.AddScalewayEmailClient(region);

            // A singleton type registration, not a factory or a typed HttpClient; the reason is
            // documented at ScalewayClientRegistration.HttpClientName.
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
    /// Persistence: <see cref="AppDbContext"/>, <see cref="IAppDbContext"/>,
    /// <see cref="IDateTimeProvider"/> and <see cref="ISwedishCalendar"/>, with no HTTP services,
    /// Identity or Redis. Both hosts call it.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        // EF Core does not discover interceptors from the application's container, so they are
        // added from it explicitly (ADR 0049). The field-encryption interceptors are singletons:
        // the same instance on every resolution keeps the options cache key identical, so EF
        // builds one internal service provider instead of one per context. They reach scoped
        // state through eventData.Context.GetService<T>() when they run.
        //
        // EF logs a failed statement and a failed save before any catch runs, but a UNIQUE
        // violation is an expected outcome here: several upserts absorb 23505 by design
        // (ADR 0032 §5). These two events are therefore logged at Information, not Error, and
        // their volume is set by the category rules in both hosts' appsettings.json
        // (EfCoreLoggingConfigurationTests). A genuine failure is still logged at Error by
        // LoggingBehavior on every Mediator path.
        services.TryAddScoped<ProtectedAccountTransaction>();
        services.TryAddScoped<ProtectedAccountTransactionInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Log(
                    (RelationalEventId.CommandError, LogLevel.Information),
                    (CoreEventId.SaveChangesFailed, LogLevel.Information)))
                .AddInterceptors(
                    sp.GetRequiredService<ProtectedAccountTransactionInterceptor>(),
                    sp.GetRequiredService<Security.FieldEncryptionSaveChangesInterceptor>(),
                    sp.GetRequiredService<Security.FieldDecryptionMaterializationInterceptor>()));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // Day and month boundaries a user sees are Swedish ones, not UTC. The calendar sits next
        // to the clock it extends. SwedishCalendarTests guards against a runtime that cannot
        // resolve the time zone, since this lazy registration would only fail on first use.
        services.AddSingleton<ISwedishCalendar, Time.SwedishCalendar>();

        // Recognises provider-specific database errors such as 23505, so Application can react
        // to them without depending on Npgsql (ADR 0032 §5).
        services.AddSingleton<IDbExceptionInspector, DbExceptionInspector>();

        // Audit-log maintenance and erasure (ADR 0024 D1, D3), used by Worker jobs, so registered
        // here rather than with the HTTP-only services.
        services.AddScoped<IAuditPartitionMaintainer, AuditPartitionMaintainer>();
        services.AddScoped<IAuditTrailEraser, AuditTrailEraser>();

        // Audit rows written by system jobs, outside the Mediator pipeline (ADR 0035).
        services.AddScoped<ISystemEventAuditor, SystemEventAuditor>();

        // IP truncation (ADR 0024 D7), shared by the audit pipeline and the auth log so the same
        // /24 and /48 masking applies everywhere.
        services.AddSingleton<IIpAnonymizer, IpAnonymizer>();

        // Structured logging of ownership mismatches (ADR 0031).
        services.AddSingleton<IFailedAccessLogger, FailedAccessLogger>();

        // Saves a deliberate search as a recent search (ADR 0060), from the pipeline behavior.
        services.AddScoped<
            Jobbliggaren.Application.RecentJobSearches.Abstractions.IRecentJobSearchCapturer,
            RecentJobSearches.RecentJobSearchCapturer>();

        // Job-ad search (ADR 0062). Full-text search and ts_rank relevance live in the Npgsql
        // assembly, which Application may not reference, so the query is implemented here.
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdSearchQuery,
            JobAds.JobAdSearchQuery>();

        // The matching behind the Art. 17 recruiter erasure (ADR 0106 Tier B; the channels are
        // documented on the port). Implemented here for the same reason as the search above.
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IRecruiterErasureMatchQuery,
            JobAds.RecruiterErasureMatchQuery>();

        // HMAC-SHA256 under a server pepper, for the Art. 17 audit payload (ADR 0090 D5).
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

        // A separate pepper for the stored token of a sole trader's organisation number (ADR 0090
        // D5): one key, one purpose. Same fail-closed startup check.
        //
        // It cannot be rotated once any row exists: the plaintext number is not kept, so an
        // existing token cannot be recomputed under a new pepper. While company_watches is empty,
        // replacing it costs nothing.
        services.AddOptions<Security.CompanyWatchPseudonymizationOptions>()
            .Bind(configuration.GetSection(Security.CompanyWatchPseudonymizationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.CompanyWatchPseudonymizationOptions>,
            Security.CompanyWatchPseudonymizationOptionsValidator>();
        services.AddSingleton<
            Jobbliggaren.Application.Common.Security.IProtectedIdentityTokenizer,
            Security.HmacProtectedIdentityTokenizer>();

        // Sorting by match for one user (ADR 0076). A separate port, so the general search stays
        // free of per-user data; both share the filters in JobAdSearchComposition.
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IPerUserJobAdSearchQuery,
            JobAds.PerUserJobAdSearchQuery>();

        // Tells employers with similar names apart by organisation number (ADR 0087 D6, D7). ILIKE
        // needs the Npgsql assembly, so it is implemented here.
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IEmployerDisambiguationQuery,
            JobAds.EmployerDisambiguationQuery>();

        // Reads the stored organisation number for a set of ads on the server, so following a
        // company from a job card never sends the raw number to the client (ADR 0087 D8(c)).
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdEmployerReader,
            JobAds.JobAdEmployerReader>();

        // Browsing the local SCB register by watch criteria. Registered here, not in
        // AddScbCompanyRegister: that module populates the register, runs only in the Worker and
        // depends on ScbRegister:Enabled, while reading rows already in the table must not. The
        // predicate is raw SQL (`sni_codes && @sni`), the only shape the GIN index can serve; LINQ
        // would compile it to a subquery that cannot use the index. The register is deliberately
        // not a DbSet on IAppDbContext (DPIA C-D4, M-C5).
        services.AddScoped<
            Jobbliggaren.Application.CompanyWatches.Abstractions.ICompanyWatchBrowseQuery,
            CompanyRegister.CompanyWatchBrowseQuery>();

        // Materialises which companies match each industry watch (ADR 0139). Registered here, not in
        // AddScbCompanyRegister, for the same reason as the browse above and one more: the
        // materialiser is the enforcement point that keeps de-registered companies from being
        // counted (replacing DPIA M-D6), and an enforcement point must not depend on a flag that
        // defaults to false.
        //
        // [Required] on CadenceCron rejects only an empty value; a malformed cron is rejected by
        // Hangfire when RecurringJobRegistrar starts in the Worker, not by the API, which binds the
        // same options.
        services.AddOptions<CompanyRegister.CompanyWatchMaterialisationOptions>()
            .Bind(configuration.GetSection(CompanyRegister.CompanyWatchMaterialisationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CompanyRegister.CompanyWatchCriterionMemberStore>();
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.ICompanyWatchCriterionMaterialiser,
            CompanyRegister.CompanyWatchCriterionMaterialiser>();

        // The occupation-by-industry profile, with a writer and a reader port. Also here rather
        // than in AddScbCompanyRegister, so the profile does not disappear when the SCB sync is
        // off and leave the picker's occupation block unprofiled.
        services.AddOptions<CompanyRegister.OccupationDivisionProfileOptions>()
            .Bind(configuration.GetSection(CompanyRegister.OccupationDivisionProfileOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<CompanyRegister.OccupationDivisionProfileStore>();
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.IOccupationDivisionProfileBuilder,
            CompanyRegister.OccupationDivisionProfileBuilder>();
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.IOccupationDivisionProfileQuery,
            CompanyRegister.OccupationDivisionProfileQuery>();

        // The general company search on /foretag/sok, where every filter is optional. A separate
        // port from the criteria browse, which refuses a missing filter instead. Same placement
        // and raw-SQL reasons as the browse.
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.ICompanyRegisterSearchQuery,
            CompanyRegister.CompanyRegisterSearchQuery>();

        // Company names from the register, for a followed company with no ads to take the name
        // from (ADR 0087 D3).
        services.AddScoped<
            Jobbliggaren.Application.CompanyRegister.Abstractions.ICompanyRegisterNameReader,
            CompanyRegister.CompanyRegisterNameReader>();

        // SCB reference data (SNI 2025, regions and municipalities): one source for validating
        // watch criteria and for the picker tree. Registered as a constructed instance, so a
        // malformed embedded file stops the host at startup rather than failing a request.
        services.AddSingleton<Jobbliggaren.Application.CompanyWatches.Abstractions.ICriterionReferenceProvider>(
            new CompanyRegister.Reference.CriterionReferenceProvider(
                CompanyRegister.Reference.CriterionReferenceLoader.LoadSni(),
                CompanyRegister.Reference.CriterionReferenceLoader.LoadKommuner(),
                CompanyRegister.Reference.CriterionReferenceLoader.LoadAliases()));

        // The curated brand-group catalogue (ADR 0087 D4), loaded at startup in the same way, so a
        // malformed catalogue, or one with a member shaped like a personal identity number, stops
        // the host.
        services.AddSingleton<Jobbliggaren.Application.CompanyWatches.Abstractions.IBrandGroupProvider>(
            new CompanyWatches.BrandGroupProvider(CompanyWatches.BrandGroupLoader.Load()));

        // Expands free-text terms such as "systemutvecklare" to SSYK codes for better recall,
        // from the SearchSynonyms section.
        services.AddOptions<Jobbliggaren.Application.JobAds.Abstractions.SearchSynonymsOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.JobAds.Abstractions.SearchSynonymsOptions.SectionName));
        services.AddScoped<
            Jobbliggaren.Application.JobAds.Abstractions.IOccupationSynonymExpander,
            JobAds.OccupationSynonymExpander>();

        // Thresholds for which applications need attention on /ansokningar. The suggestion to
        // mark an application as ghosted uses GhostSuggestDays; Application.GhostedThresholdDays
        // is deliberately not bound and feeds no signal.
        services.AddOptions<Jobbliggaren.Application.Applications.Attention.ApplicationAttentionOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.Applications.Attention.ApplicationAttentionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Envelope field encryption (ADR 0049, ADR 0066). Registered with persistence because the
        // per-user data keys and the interceptors follow the AppDbContext's lifetime, and both
        // hosts need them (the hard-delete job erases keys). An empty or invalid master key stops
        // startup in every environment.
        services.AddOptions<Security.FieldEncryptionOptions>()
            .Bind(configuration.GetSection(Security.FieldEncryptionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<Security.FieldEncryptionOptions>,
            Security.FieldEncryptionOptionsValidator>();

        // The AES-256-GCM primitive, independent of how data keys are wrapped.
        services.AddSingleton<Jobbliggaren.Application.Common.Security.IFieldEncryptor,
            Security.AesGcmFieldEncryptor>();

        // Local is the only data-key provider (ADR 0066). An omitted Provider means Local; any
        // other value, such as a stale "Kms", stops startup instead of silently falling back,
        // which would hide a misconfiguration.
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

        // Per-user data keys (ADR 0049 Beslut 1). Scoped: the store shares the scope's
        // AppDbContext, so key deletion joins the hard-delete transaction, and the cache wipes key
        // material when the scope ends. UserDataKey is never exposed through IAppDbContext (an
        // architecture test enforces it). The concrete cache and IUserDataKeyCache resolve to the
        // same scoped instance, because the decryption interceptor peeks the concrete type
        // synchronously.
        services.AddScoped<Security.ScopedUserDataKeyCache>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.IUserDataKeyCache>(
            sp => sp.GetRequiredService<Security.ScopedUserDataKeyCache>());
        services.AddScoped<Jobbliggaren.Application.Common.Security.IUserDataKeyStore,
            Security.UserDataKeyStore>();

        // The encryption backfill opens a fresh scope per data owner, so one user's key never
        // shares a scope with another's (ADR 0049 Beslut 4).
        services.AddScoped<
            Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption.IFieldEncryptionBackfiller,
            Security.FieldEncryptionBackfiller>();

        // The interceptors are singletons (see AddDbContext above); the current data owner is
        // scoped to the request or job.
        services.AddSingleton<Security.FieldEncryptionSaveChangesInterceptor>();
        services.AddSingleton<Security.FieldDecryptionMaterializationInterceptor>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.ICurrentDataOwner,
            Security.CurrentDataOwner>();

        // Encryption of uploaded files (ADR 0100): the cipher is stateless; the sealer and the
        // opener are scoped because they read the scope's key cache.
        services.AddSingleton<Jobbliggaren.Application.Common.Security.IBinaryFieldEncryptor,
            Security.BinaryFieldEncryptor>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.IBinaryFieldSealer,
            Security.BinaryFieldSealer>();
        services.AddScoped<Jobbliggaren.Application.Common.Security.IBinaryFieldOpener,
            Security.BinaryFieldOpener>();

        // #1979 — the feedback switch, recipient and statistics read, for both hosts.
        Feedback.FeedbackServiceCollectionExtensions.AddFeedback(services, configuration);

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
    /// #1735 — the connection string NAME of the non-persisted Redis instance both stacks declare
    /// (<c>redis-volatile</c>, ADR 0142 D1). <c>VolatileRedis</c> rather than <c>RedisVolatile</c>:
    /// <c>ConnectionStrings__Redis</c> would otherwise be a strict prefix of this key's environment form, and
    /// the compose pins scan lines. <c>DeployComposeVolatileRedisTests</c> derives the environment variable
    /// from this constant, so a rename here fails that pin rather than silently pointing the deploy stack at
    /// nothing.
    /// </summary>
    public const string VolatileRedisConnectionStringName = "VolatileRedis";

    /// <summary>
    /// #1350 — the Api's Data-Protection keyring. Its own method rather than four lines inside
    /// <see cref="AddIdentityAndSessions"/>, because that one needs Postgres and Redis to register
    /// at all and this must be testable without either (CLAUDE.md §2.4).
    ///
    /// <para>
    /// <b>Api only.</b> <c>AddCoreIdentityForWorker</c> deliberately registers no
    /// <c>IDataProtectionProvider</c>. Its consumers are Identity's token provider (one
    /// <c>DataProtectorTokenProvider</c>: of the four <c>AddDefaultTokenProviders</c> registers only Default
    /// is DataProtector-based, the other three being TOTP), the login challenge store (#1735, purpose
    /// <c>RedisLoginChallengeStore.ProtectorPurpose</c>), the grant store (<c>RedisGrantStore</c>), the OAuth state
    /// store (#1744, <c>RedisOAuthStateStore</c>) and the store of address changes an administrator starts (#1975,
    /// <c>RedisAccountEmailChangeStore</c>). Sharing a
    /// keyring with the Worker would hand it cryptographic reach over credentials it never mints or
    /// validates, and re-open the cross-process coupling the 2026-07-10 ruling rejected. This codebase has
    /// no antiforgery, so the keyring's blast radius is the one token KIND that provider mints - change
    /// email - plus every live login challenge's address and code (ADR 0142 D1), every live grant, every live
    /// OAuth flow's PKCE verifier and every pending address change's new address and code, and
    /// nothing else. The keys are persisted unprotected on the file system (no
    /// <c>ProtectKeysWith*</c>), so whoever reads the keyring volume reads all of it. Regenerate with
    /// <c>git grep -in -e antiforgery -e "CreateProtector(" -- src/</c> and read the result as a property,
    /// not a count — a comment naming it will match.
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
    /// Identity, sessions, Redis, the HTTP-based <see cref="ICurrentUser"/> and the auth audit
    /// logger. API only; the Worker does not load this module.
    /// </summary>
    public static IServiceCollection AddIdentityAndSessions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        // #1735 — no fallback to ConnectionStrings:Redis, in any environment: the refusal is what keeps the
        // login challenge's keys off the persisted instance, not rollout discipline. IsNullOrWhiteSpace
        // rather than `??`: compose renders an unset variable as "", which `??` lets through.
        var volatileRedisConnectionString = configuration.GetConnectionString(VolatileRedisConnectionStringName);
        if (string.IsNullOrWhiteSpace(volatileRedisConnectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{VolatileRedisConnectionStringName} is missing. The login challenge store "
                + "and the rate budgets run on the non-persisted Redis instance and have no fallback "
                + "(docs/runbooks/local-dev-setup.md; deploy/docker-compose.yml `redis-volatile`).");
        }

        services.AddApiDataProtection(configuration);

        services.AddDbContext<AppIdentityDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<ProtectedAccountTransactionInterceptor>()));

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(opts =>
            {
                opts.User.RequireUniqueEmail = true;

                TheUserNameIsTheAddress(opts.User);

                opts.Tokens.ChangeEmailTokenProvider = TokenOptions.DefaultProvider;

            })
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();

        services.AddApiRedisConnections(configuration);

        services.AddScoped<SqlAccountAccess>();
        services.AddScoped<IAccountAccessCoordinator>(sp => sp.GetRequiredService<SqlAccountAccess>());
        services.AddScoped<IAccountAccessReader>(sp => sp.GetRequiredService<SqlAccountAccess>());
        services.AddScoped<IAccountAccessWriter>(sp => sp.GetRequiredService<SqlAccountAccess>());
        services.AddScoped<IAccountEmailChangeRequests, SqlAccountEmailChangeRequests>();
        services.AddScoped<IAccountAccessCleanup, AccountAccessCleanup>();

        // #746 — bind + validate at startup: SessionStoreOptionsValidator caps SlideThreshold to
        // [0.0, 0.25] (a bad throttle value must fail the boot, not silently widen the Art.17
        // orphan self-heal window). ValidateOnStart() forces the check eagerly.
        services.AddOptions<SessionStoreOptions>()
            .Bind(configuration.GetSection(SessionStoreOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SessionStoreOptions>, SessionStoreOptionsValidator>();

        // The registration kill-switch (Application-owned contract, bound here). ADR 0083 Amendment
        // 2026-08-03: bound via AddOptions/ValidateOnStart so AuthOptionsValidator refuses to boot outside
        // Development/Test when the registered sender cannot deliver. Registered HERE only: the Worker
        // owns no login or registration surface, so a shared env file must not take it down for a
        // condition it cannot exercise.
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

        // #703 — the anti-email-bomb windows of the change-email and login-challenge requests. Api-only —
        // they run in the request path. ValidateOnStart + [Range] so a misconfigured window fails the host
        // loud (a security invariant), parity DigestDispatchOptions.
        services.AddOptions<AuthEmailCooldownOptions>()
            .Bind(configuration.GetSection(AuthEmailCooldownOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // #1735 (ADR 0142 D1/D2) — the login challenge's per-address counters (the cooldown, the mail budget
        // and the code budget). Api-only: it runs in the request path and on the volatile connection above,
        // which the Worker composition does not have.
        services.AddSingleton<IRateBudget, RedisRateBudget>();

        // #1735 (ADR 0142 D1) — the login challenge store. Api-only for the same reason, and one more: it
        // protects the address and the code with the Api's Data-Protection keyring (AddApiDataProtection).
        services.AddSingleton<ILoginChallengeStore, RedisLoginChallengeStore>();

        // #1737 (ADR 0142 D1/D3) — the grant a proven new address redeems at `complete`, and the per-address
        // claim taken before an account is created. On the volatile connection, Api-only like the two above;
        // the grant store protects its payload with the Api's keyring too.
        services.AddSingleton<IGrantStore, RedisGrantStore>();
        services.AddSingleton<IRegistrationClaim, RedisRegistrationClaim>();

        // #1975 (ADR 0153) — an address change an administrator starts, pending until the account's owner completes
        // it. On the volatile connection and the Api's keyring, like the stores above.
        services.AddSingleton<IAccountEmailChangeStore, RedisAccountEmailChangeStore>();

        // #1735 (ADR 0142 D2) — the login challenge's own dispatch: its own channel instance, capacity and
        // drop event. Api-EXCLUSIVE: the consumer's store
        // protects with this composition's Data-Protection keyring and runs on its volatile Redis connection.
        // LoginChallengeCompositionTests pins the pair (here yes, AddCoreIdentityForWorker no); a hand-written
        // line in Worker/Program.cs is caught by nothing but a reader.
        services.AddOptions<LoginChallengeDispatchOptions>()
            .Bind(configuration.GetSection(LoginChallengeDispatchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<LoginChallengeDispatchChannel>();
        services.AddSingleton<ILoginChallengeDispatcher>(
            sp => sp.GetRequiredService<LoginChallengeDispatchChannel>());
        services.AddHostedService<LoginChallengeDispatchService>();
        services.AddScoped<ILoginAccountLookup, UserAccountService>();
        services.AddScoped<IPasswordlessAccountCreator, UserAccountService>();
        services.AddScoped<AccountRegistrar>();
        services.AddScoped<LoginSubjectResolver>();
        services.AddScoped<LoginChallengeIssuer>();
        services.AddScoped<LoginChallengeAdmission>();
        services.AddScoped<IInboxProofRecorder, IdentityInboxProofRecorder>();
        services.AddScoped<PasswordlessSessionGrant>();
        services.AddScoped<LoginProofOutcome>();

        // #1744 (ADR 0142 D8) — the external-login spine: the started flows on the volatile connection and the
        // Api's keyring, like the grant store; the links in Identity's AspNetUserLogins. A provider without keys is
        // not registered at all.
        services.AddSingleton<IOAuthStateStore, RedisOAuthStateStore>();
        services.AddSingleton<RegisteredProviders>();
        services.AddScoped<IExternalLoginLookup, IdentityExternalLoginStore>();
        services.AddScoped<IExternalLoginWriter, IdentityExternalLoginStore>();
        // #1746: outside the gate below, since a provider whose keys were removed keeps its rows.
        services.AddScoped<IExternalLoginEraser, IdentityExternalLoginStore>();
        services.AddScoped<ExternalLoginLinker>();
        services.AddExternalIdentityProviders(configuration);

        // At startup, ensures the Admin role exists. Idempotent.
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
            new AccessControlledSessionStore(
                new SessionStoreResilienceDecorator(sp.GetRequiredService<RedisSessionStore>()),
                sp.GetRequiredService<IAccountAccessReader>(), sp.GetRequiredService<IAccountAccessCoordinator>()));

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<ConfirmedAddressSwap>();

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

        // #1974 (ADR 0151) — the admin account directory reads Identity's tables and normalises with Identity's
        // own normaliser, so it is registered with Identity, in the Api composition only.
        services.AddScoped<IAccountDirectory, SqlAccountDirectory>();

        return services;
    }

    /// <summary>
    /// The HTTP-based audit ports, <see cref="ICorrelationIdProvider"/> and
    /// <see cref="IRequestContextProvider"/>. They depend on
    /// <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/> and must never be loaded in the
    /// Worker, which registers its own (ADR 0022, ADR 0023).
    /// </summary>
    public static IServiceCollection AddHttpAuditing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationIdProvider, CorrelationIdProvider>();
        services.AddScoped<IRequestContextProvider, RequestContextProvider>();
        return services;
    }

    // The user name IS the address here, so Identity's default ASCII user-name charset refused addresses both
    // email validators admit (o'brien@, björn@). What may be stored is StorableAddress's question, asked at the
    // writers in UserAccountService. One rule for both compositions: they validate the same rows.
    private static void TheUserNameIsTheAddress(UserOptions user) =>
        user.AllowedUserNameCharacters = string.Empty;

    /// <summary>
    /// An HTTP-free Identity module for the Worker: <see cref="AppIdentityDbContext"/>, the Identity
    /// core (UserManager and UserStore, without cookies, sessions or SignInManager) and the ports
    /// <see cref="HardDeleteAccountsJob"/> needs to delete Identity rows in the Art. 17 cascade
    /// (ADR 0024 D6). Called only by the Worker; the API calls
    /// <see cref="AddIdentityAndSessions"/>, and calling both registers services twice.
    /// </summary>
    public static IServiceCollection AddCoreIdentityForWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres saknas i konfiguration.");

        services.AddDbContext<AppIdentityDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppIdentityDbContext).Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity");
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<ProtectedAccountTransactionInterceptor>()));

        // No token providers: they need the API's Data Protection keyring, and the Worker's use
        // of UserManager (create, find, delete) needs none.
        services.AddIdentityCore<ApplicationUser>(opts => TheUserNameIsTheAddress(opts.User))
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppIdentityDbContext>();

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IAccountHardDeleter, AccountHardDeleter>();
        services.AddScoped<SqlAccountAccess>();
        services.AddScoped<IAccountAccessCoordinator>(sp => sp.GetRequiredService<SqlAccountAccess>());
        services.AddScoped<IAccountAccessReader>(sp => sp.GetRequiredService<SqlAccountAccess>());
        services.AddScoped<IAccountAccessWriter>(sp => sp.GetRequiredService<SqlAccountAccess>());

        return services;
    }
}
