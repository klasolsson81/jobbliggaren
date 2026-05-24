using Hangfire;
using Hangfire.PostgreSql;
using JobbPilot.Application;
using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Behaviors;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Infrastructure;
using JobbPilot.Worker.Auditing;
using JobbPilot.Worker.Hosting;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Persistence-modul (DbContext, IAppDbContext, IDateTimeProvider, IDbExceptionInspector)
// — utan HTTP-bagage, utan Identity. Worker-kontextens DI-yta är medvetet minimerad
// per ADR 0023 / STEG 9.
builder.Services.AddPersistence(builder.Configuration);

// HTTP-fri Identity-modul för Worker (per ADR 0024 D6 / STEG 10b). UserManager +
// AppIdentityDbContext krävs av AccountHardDeleter (HardDeleteAccountsJob använder
// porten för Identity-DELETE och orphan-cleanup). AddIdentityCore<>() utelämnar
// AuthenticationScheme/Cookies/SignInManager — håller Worker HTTP-fri.
builder.Services.AddCoreIdentityForWorker(builder.Configuration);

// JobTech-integration (F2-P8c). Refit + IJobTechStreamClient + PlatsbankenJobSource
// som IJobSource. Resilience-pipelinen (rate-limiter, retry, CB) registreras via
// Microsoft.Extensions.Http.Resilience. Outgoing HTTP är OK i Worker — ADR 0023
// förbjuder ASP.NET Core HTTP-server-bagage, inte System.Net.Http-utgående trafik.
builder.Services.AddJobSources(builder.Configuration);

// Worker-wrappers för DisableConcurrentExecution-attribut på stream- + snapshot-
// jobben (CTO-rond 2026-05-13 punkt 8 + root-cause-fix 2026-05-16 — snapshot
// tar tiotals min efter streaming-fixen, måste skyddas mot AutomaticRetry-overlap).
builder.Services.AddScoped<JobbPilot.Worker.Hosting.SyncPlatsbankenStreamWorker>();
builder.Services.AddScoped<JobbPilot.Worker.Hosting.SyncPlatsbankenSnapshotWorker>();
// ADR 0032-amendment 2026-05-23 — retention-wrappers (paritet snapshot,
// DisableConcurrentExecution-skydd mot Hangfire-retry-overlap).
builder.Services.AddScoped<JobbPilot.Worker.Hosting.RetainPlatsbankenJobAdsWorker>();
builder.Services.AddScoped<JobbPilot.Worker.Hosting.ExpireJobAdsWorker>();
// TD-13 C5 (ADR 0049 Beslut 4) — DisableConcurrentExecution-wrapper för
// fält-krypterings-backfillen (potentiellt långkörande, paritet snapshot).
builder.Services.AddScoped<JobbPilot.Worker.Hosting.BackfillFieldEncryptionWorker>();

// ADR 0064 — Worker:s landing-stats-refresh-wrapper. Job-klassen registreras
// av AddLandingStats() nedan; wrappern bär bara Hangfire-attributet
// (paritet ExpireJobAdsWorker per ADR 0023 delbeslut 2).
builder.Services.AddScoped<JobbPilot.Worker.Hosting.RefreshLandingStatsWorker>();

// ADR 0064 Variant B — Redis IDistributedCache krävs av RedisLandingStatsCache.
// Worker delar inte AddIdentityAndSessions-stacken med Api (HTTP-fri Worker
// per ADR 0023), så Redis-cache wiras explicit här. Bara IDistributedCache —
// IConnectionMultiplexer (SADD/SREM-API:t för session-store) behövs ej i Worker.
// Fail-loud-paritet med Api Infrastructure/DependencyInjection.cs:438-440 —
// localhost:6379-fallback skulle masquera config-bortfall i Fargate-task som
// faller silent var 5:e min (incident 2026-05-24, dotnet-architect-dom
// agentId a9446dac40e8fef02).
var workerRedisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Redis saknas i Worker-konfiguration. ADR 0064 kräver " +
        "Redis-cache-yta för RefreshLandingStatsJob. Verifiera task-def secrets-block.");
builder.Services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = workerRedisConnectionString;
    opts.InstanceName = "jobbpilot:";
});
builder.Services.AddLandingStats();

builder.Services.AddApplication();

// Worker-stubs av audit-portarna (per ADR 0022 + ADR 0023 / STEG 9).
// HTTP-baserade implementationerna (CorrelationIdProvider, RequestContextProvider,
// CurrentUser) är HTTP-only och får aldrig laddas i Worker.
builder.Services.AddSingleton<ICurrentUser, WorkerSystemUser>();
builder.Services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
builder.Services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();

// Mediator-pipeline — delad konstant per ADR 0008 + ADR 0022 garanterar att Api/Worker
// inte driftar isär. AuditBehavior innerst (atomisk persistens via UoW).
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(JobbPilot.Application.AssemblyMarker)];
});

// Pipeline-behaviors registreras explicit (se Api/Program.cs för rationale).
builder.Services.AddMediatorPipelineBehaviors();

// Hangfire-storage. Egen schema "hangfire" undviker konflikt med JobbPilot-tabeller.
// PrepareSchemaIfNecessary styrs per miljö via HangfireWorkerOptions (TD-17 punkt 1):
// dev/test = true (enklare lokal uppstart), prod = false (schema-DDL körs via
// docs/runbooks/hangfire-schema.md innan första prod-deploy så Worker-DB-user kan
// köras med minimal GRANT-set).
//
// SECURITY (TD-17 punkt 3): Worker hostar idag ingen Hangfire-dashboard. Om
// dashboard någonsin exponeras (i Api eller dev-tooling) MÅSTE den skyddas via
// custom IDashboardAuthorizationFilter + admin-policy + IP-restrict — Hangfire-
// default är PUBLIK. Dashboard exponerar job-arguments (user-IDs/aggregat-IDs)
// och stack-traces (potentiellt PII). Se docs/runbooks/hangfire-schema.md.
//
// TD-17 punkt 4 — split jobbpilot_app (Postgres) / jobbpilot_worker (HangfireStorage)
// via fallback-kedja. Prod-overlay sätter HangfireStorage; dev faller tillbaka på
// Postgres. Resolver lyft till testbar statisk metod (STEG 12).
var hangfireConnectionString = HangfireConnectionStringResolver.Resolve(builder.Configuration);

var hangfireOpts = builder.Configuration.GetSection(HangfireWorkerOptions.SectionName)
    .Get<HangfireWorkerOptions>() ?? new HangfireWorkerOptions();

// Production-defense via allow-list: bara Development och Test får auto-skapa schema.
// Staging/Preprod/Demo/Production etc. tvingas till explicit overlay (TD-17 punkt 1,
// security-auditor STEG 11 Sec-Major-1+4). Worker-DB-användarens GRANT-set ska aldrig
// innehålla CREATE i icke-dev-miljöer.
var safeForAutoSchema =
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
if (!safeForAutoSchema && hangfireOpts.PrepareSchemaIfNecessary)
{
    throw new InvalidOperationException(
        $"Hangfire:PrepareSchemaIfNecessary måste vara false utanför Development/Test " +
        $"(aktuell miljö: {builder.Environment.EnvironmentName}). Kör schema-DDL via " +
        "docs/runbooks/hangfire-schema.md innan deploy. (TD-17)");
}

// Range-validering på ShutdownTimeoutSeconds — fail-loud om någon sätter 0/negativt
// eller orealistiskt högt värde via overlay. Direct-bound config (utan IOptions) ger
// ingen DataAnnotations-validering "gratis" — manuell guard räcker för en option.
if (hangfireOpts.ShutdownTimeoutSeconds is < 1 or > 300)
{
    throw new InvalidOperationException(
        $"Hangfire:ShutdownTimeoutSeconds måste vara 1-300, fick " +
        $"{hangfireOpts.ShutdownTimeoutSeconds}. Default 25s (strax under Fargate 30s).");
}

builder.Services.AddHangfire(cfg => cfg
    .UseRecommendedSerializerSettings()
    .UseSimpleAssemblyNameTypeSerializer()
    .UsePostgreSqlStorage(
        opts => opts.UseNpgsqlConnection(hangfireConnectionString),
        new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire",
            PrepareSchemaIfNecessary = hangfireOpts.PrepareSchemaIfNecessary,
        }));

// Worker-count explicit satt — default Environment.ProcessorCount blir 1 i Fargate-container
// med 1 vCPU. 4 är lämpligt för IO-bundna Mediator-jobb.
//
// ShutdownTimeout strax under Fargate default stopTimeout (30 s) så Hangfire hinner
// committa job-state innan SIGKILL (TD-17 punkt 6). Alla jobb är idempotenta — vid
// abort plockar nästa daily run upp igen via orphan/state-check.
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = 4;
    opts.ShutdownTimeout = TimeSpan.FromSeconds(hangfireOpts.ShutdownTimeoutSeconds);
});

// Generic Host shutdown-timeout — explicit satt så hela timeout-kedjan (Hangfire 25s →
// Host disposal 28s → Fargate 30s → SIGKILL) är synlig på ett ställe. 3s marginal mellan
// Hangfire-stop och host-disposal räcker för EF Core dispose + log-flush.
builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromSeconds(hangfireOpts.ShutdownTimeoutSeconds + 3));

// Recurring-jobs registreras vid host-start.
builder.Services.AddHostedService<RecurringJobRegistrar>();

var host = builder.Build();
host.Run();
