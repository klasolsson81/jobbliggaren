using Hangfire;
using Hangfire.PostgreSql;
using Jobbliggaren.Application;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Infrastructure.Logging;
using Jobbliggaren.Worker.Auditing;
using Jobbliggaren.Worker.Hosting;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args is ["--readiness-probe"])
{
    Environment.ExitCode = await WorkerReadinessSocketService.ProbeAsync();
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Secrets arrive as files on a RAM-backed mount, never as container environment values,
// which Docker persists to disk (ADR 0050 gate B-1). This is deliberately the last source,
// so on the server the file wins. Development also reads role-specific Redis files; other
// local options stay in appsettings.Local.json.
builder.Configuration.AddEnvFileSecrets();

// Structured logging to the console and, when Seq:ServerUrl is set, to Seq. Shared with
// the API so the two hosts' sinks cannot drift apart.
builder.Logging.AddJobbliggarenLogging(builder.Configuration);

// DI validation (ADR 0023 amendment 2026-06-06). AddMediator registers every handler in the
// Application assembly, since the source generator scans per assembly, but the Worker
// deliberately loads only its minimal, HTTP-free services. ValidateOnBuild would therefore try
// to construct API-only handlers whose dependencies (such as ISessionStore) the Worker never
// registers and never runs, so it is off. ValidateScopes stays on: captive dependencies matter
// in a host where every job runs in its own scope.
//
// Consequence: a missing dependency of a Worker job surfaces only when Hangfire invokes the job.
// That is why each job and its wrapper below are registered explicitly and together, and why
// WorkerLayerTests and the integration tests resolve them.
builder.ConfigureContainer(new DefaultServiceProviderFactory(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));

// Persistence (DbContext, IAppDbContext, IDateTimeProvider, IDbExceptionInspector) without
// any HTTP services or Identity (ADR 0023).
builder.Services.AddPersistence(builder.Configuration);

// An HTTP-free Identity core (ADR 0024 D6): AccountHardDeleter needs UserManager and
// AppIdentityDbContext for the hard-delete job. AddIdentityCore leaves out authentication
// schemes, cookies and SignInManager.
builder.Services.AddCoreIdentityForWorker(builder.Configuration);

// The JobTech integration: Refit clients and PlatsbankenJobSource as IJobSource, with rate
// limiting, retries and a circuit breaker from Microsoft.Extensions.Http.Resilience. ADR 0023
// bans ASP.NET Core server components from the Worker, not outgoing HTTP.
builder.Services.AddJobSources(builder.Configuration);

// Job wrappers carry the Hangfire attributes, such as DisableConcurrentExecution, so a long
// run is not overlapped by a retry. The Platsbanken snapshot can take tens of minutes.
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.SyncPlatsbankenStreamWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.SyncPlatsbankenSnapshotWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.RetainPlatsbankenJobAdsWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.ExpireJobAdsWorker>();
// The daily matching scan (ADR 0080). The Worker does not call AddInfrastructure, so the
// matching engine's ports are added here; the job resolves them in a child scope per user.
builder.Services.AddMatchingEngine();
// The email sender for match notices and digests, through the same provider switch as the
// API: console in development, a null sender elsewhere unless Scaleway is configured.
builder.Services.AddEmailSender(builder.Configuration, builder.Environment);
builder.Services.AddScoped<Jobbliggaren.Application.Matching.Jobs.BackgroundMatching.BackgroundMatchingJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackgroundMatchingWorker>();
// The nightly followed-company scan (ADR 0087 D5): its own watermark, matched on organisation
// number, with no scorer.
builder.Services.AddScoped<Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan.CompanyWatchScanJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.CompanyWatchScanWorker>();
// The SCB company-register refresh (ADR 0091): orchestrator, bulk store and partition planner,
// plus the certificate-based client with its process-wide rate limiter only when
// ScbRegister:Enabled is true; otherwise a null source, so development and CI without a
// certificate stay offline. Only the Worker populates the register.
builder.Services.AddScbCompanyRegister(builder.Configuration);
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.ScbCompanyRegisterSyncWorker>();
// Registered explicitly like every wrapper here. Without it the job would still run, but only
// because Hangfire.AspNetCore falls back on ActivatorUtilities, a package detail rather than a
// contract of this host.
builder.Services.AddScoped<CompanyWatchCriterionMaterialisationWorker>();
builder.Services.AddScoped<OccupationDivisionProfileWorker>();
// The match digest (ADR 0080): daily and weekly cron entries; each run sends to the users who
// chose that cadence. Its cap comes from the validated Digest options.
builder.Services.AddOptions<Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchOptions>()
    .Bind(builder.Configuration.GetSection(
        Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.DigestDispatchWorker>();
// Marks a match notice left Queued past the threshold as Failed, without re-sending it.
builder.Services.AddScoped<Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper.StrandedMatchReaperJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.StrandedMatchReaperWorker>();
// Deletes parsed CVs left in staging past their retention (GDPR Art. 5(1)(e)) with one
// set-based delete that needs no encryption key.
builder.Services.AddScoped<Jobbliggaren.Application.Resumes.Jobs.ParsedResumeRetention.ParsedResumeRetentionJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.ParsedResumeRetentionWorker>();
// Feedback notices and feedback retention.
builder.Services.AddScoped<Jobbliggaren.Application.Feedback.Jobs.DispatchFeedbackNotifications.FeedbackNotificationDispatchJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.FeedbackNotificationDispatchWorker>();
builder.Services.AddScoped<Jobbliggaren.Application.Feedback.Jobs.FeedbackRetention.FeedbackRetentionJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.FeedbackRetentionWorker>();
// Long-running, one-off backfills (field encryption, occupation codes, employment terms,
// extracted terms, requirements). The API enqueues the Application jobs directly, since it
// cannot reference the Worker; these wrappers carry DisableConcurrentExecution for runs
// scheduled from the Worker.
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillFieldEncryptionWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdSsykWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdKlass2Worker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdExtractedTermsWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdRequirementsWorker>();

// The landing-page statistics refresh (ADR 0064). AddLandingStats() below registers the job;
// the wrapper only carries the Hangfire attribute.
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.RefreshLandingStatsWorker>();

// The publishing cache and process readiness share the Worker identity and connection.
builder.Services.AddWorkerRedisConnection(builder.Configuration);
builder.Services.AddLandingStats();

// Local Swedish text analysis (stemming, analysis, spell-checking) for the CV and matching
// engines. It brings no HTTP dependencies, and its dictionary files are checked at startup.
builder.Services.AddTextAnalysis();

builder.Services.AddApplication();

// Worker implementations of the audit ports (ADR 0022, ADR 0023). The API's versions read the
// HTTP context and must never be loaded here.
builder.Services.AddSingleton<ICurrentUser, WorkerSystemUser>();
builder.Services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
builder.Services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();

// The same Mediator pipeline as the API (ADR 0008, ADR 0022), with AuditBehavior innermost so
// audit rows persist in the same unit of work.
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(Jobbliggaren.Application.AssemblyMarker)];
});

// Registered explicitly; Api/Program.cs explains why.
builder.Services.AddMediatorPipelineBehaviors();

// Hangfire storage in its own "hangfire" schema. Creating the schema is allowed only in
// Development and Test; elsewhere the DDL is applied beforehand
// (docs/runbooks/hangfire-schema.md), so the Worker's database user needs no CREATE grant.
//
// SECURITY: the Worker hosts no Hangfire dashboard. If one is ever exposed, it MUST be
// protected with an IDashboardAuthorizationFilter, the admin policy and an IP restriction:
// Hangfire's default is public, and the dashboard shows job arguments (user and aggregate
// ids) and stack traces that may contain personal data.
//
// The connection string prefers HangfireStorage (the Worker's own database role) and falls
// back to Postgres in development.
var hangfireConnectionString = HangfireConnectionStringResolver.Resolve(builder.Configuration);

var hangfireOpts = builder.Configuration.GetSection(HangfireWorkerOptions.SectionName)
    .Get<HangfireWorkerOptions>() ?? new HangfireWorkerOptions();

// An allow-list: only Development and Test may create the schema.
var safeForAutoSchema =
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
if (!safeForAutoSchema && hangfireOpts.PrepareSchemaIfNecessary)
{
    throw new InvalidOperationException(
        $"Hangfire:PrepareSchemaIfNecessary måste vara false utanför Development/Test " +
        $"(aktuell miljö: {builder.Environment.EnvironmentName}). Kör schema-DDL via " +
        "docs/runbooks/hangfire-schema.md innan deploy. (TD-17)");
}

// The option is bound directly, without IOptions validation, so its range is checked here.
if (hangfireOpts.ShutdownTimeoutSeconds is < 1 or > 300)
{
    throw new InvalidOperationException(
        $"Hangfire:ShutdownTimeoutSeconds måste vara 1-300, fick " +
        $"{hangfireOpts.ShutdownTimeoutSeconds}. Default 25s; host disposal följer på " +
        $"+3s, och orkestratorns grace-period måste ligga över den summan.");
}

builder.Services.AddHangfire(cfg => cfg
    .UseRecommendedSerializerSettings()
    .UseSimpleAssemblyNameTypeSerializer()
    .UsePostgreSqlStorage(
        opts => opts.UseNpgsqlConnection(hangfireConnectionString),
        // The factory sets UseSlidingInvisibilityTimeout, so a long job keeps its lease through
        // a heartbeat instead of being fetched again after 30 minutes.
        HangfireStorageOptionsFactory.Create(hangfireOpts.PrepareSchemaIfNecessary)));

// Four workers, set explicitly so the count does not follow the host's core count; the jobs
// are I/O-bound.
//
// The shutdown timeout is 3 s below host disposal, which must stay below the container's
// stop grace period (`stop_grace_period` for the worker in deploy/docker-compose.yml), so
// Hangfire can commit job state before SIGKILL. All jobs are idempotent: an aborted run is
// picked up by the next one.
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = 4;
    opts.ShutdownTimeout = TimeSpan.FromSeconds(hangfireOpts.ShutdownTimeoutSeconds);
});

// The host's shutdown timeout, set explicitly so the chain (Hangfire, then host disposal 3 s
// later, then the container's grace period, then SIGKILL) is visible in one place. Three
// seconds is enough for EF Core disposal and log flushing.
builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromSeconds(hangfireOpts.ShutdownTimeoutSeconds + 3));

// The Worker memory-trend sampler (ADR 0045 Beslut 3) exists only in the Worker, so its
// options are bound here rather than in a shared module. It is a singleton because it keeps
// state across ticks; its whole dependency chain is singleton-safe.
builder.Services.AddOptions<Jobbliggaren.Application.Common.Telemetry.WorkerMemoryTrendOptions>()
    .Bind(builder.Configuration.GetSection(
        Jobbliggaren.Application.Common.Telemetry.WorkerMemoryTrendOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<
    Jobbliggaren.Application.Common.Telemetry.IProcessMemoryProbe,
    Jobbliggaren.Infrastructure.Diagnostics.ProcessMemoryProbe>();
builder.Services.AddSingleton<Jobbliggaren.Application.Common.Telemetry.WorkerMemoryTrendSampler>();
builder.Services.AddHostedService<Jobbliggaren.Worker.Hosting.WorkerMemoryTrendService>();

// Registers the recurring jobs at host start.
builder.Services.AddHostedService<RecurringJobRegistrar>();

builder.Services.AddHostedService<WorkerReadinessSocketService>();

using var host = builder.Build();
await host.Services.RequireWorkerRedisReadyAsync();
await host.RunAsync();
