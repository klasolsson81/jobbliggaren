using Hangfire;
using Hangfire.PostgreSql;
using Jobbliggaren.Application;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Logging;
using Jobbliggaren.Worker.Auditing;
using Jobbliggaren.Worker.Hosting;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// TD-104 / STEG 6 — persistent strukturerad logg-sink (MEL → Seq, config-gated på
// Seq:ServerUrl). Delad extension med Api så sink-konfig inte driftar mellan hosts.
builder.Logging.AddJobbliggarenLogging(builder.Configuration);

// DI-validerings-policy (ADR 0023 amendment 2026-06-06, senior-cto-advisor).
// Host.CreateApplicationBuilder sätter ValidateOnBuild=true i Development.
// Worker registrerar via AddMediator HELA Application-assemblyns handler-set
// (Mediator.SourceGenerator scannar per assembly — kan inte subset:as), men
// laddar MEDVETET bara sin minimala DI-yta per ADR 0023 (HTTP-fri) — INTE
// AddIdentityAndSessions. Eager ValidateOnBuild
// försöker därför konstruera Api-only-handlers (Auth) vars
// deps (t.ex. ISessionStore) Worker
// aldrig registrerar och aldrig kör → falsk positiv. (IEmailSender registreras
// via den extraherade AddEmailSender för Vag 4-notisjobben, ADR 0080
// PR-4b.) På Fargate
// (Production) var ValidateOnBuild=false så detta dök upp först vid lokal
// Development-boot efter AWS-avveckling (ADR 0066). ValidateScopes BEHÅLLS
// (captive-dependency-skydd är hög-värde i Hangfire-host:en där varje job kör i
// eget scope). Worker:s egna job-handler-deps valideras lazily vid Hangfire-
// invocation + av WorkerLayerTests + integ-tester. Variant C (split av
// Application-assemblyn för isolerad Worker-scan) är framtida TD/Trigger.
builder.ConfigureContainer(new DefaultServiceProviderFactory(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));

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
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.SyncPlatsbankenStreamWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.SyncPlatsbankenSnapshotWorker>();
// ADR 0032-amendment 2026-05-23 — retention-wrappers (paritet snapshot,
// DisableConcurrentExecution-skydd mot Hangfire-retry-overlap).
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.RetainPlatsbankenJobAdsWorker>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.ExpireJobAdsWorker>();
// ADR 0080 Vag 4 PR-3 — den dagliga per-user matchnings-scannen. Wrappern DI-resolverar
// inner-jobbet (BackgroundMatchingJob), så jobbet registreras explicit (paritet ExpireJobAds).
// AddMatchingEngine ger IMatchScorer + IMatchProfileBuilder i Worker-SP — Worker anropar INTE
// AddInfrastructure (HTTP-fri, ADR 0023), så dessa portar (registrerade där) saknas annars och
// ValidateOnBuild=false (TD-103) skulle dölja gapet till Hangfire-invocation 03:20 UTC.
builder.Services.AddMatchingEngine();
// ADR 0080 Vag 4 PR-4b — IEmailSender för bakgrundsmatchnings-notiserna (Top-direkt-hook i
// scannen + DigestDispatchJob). Worker drar INTE in AddInfrastructure (HTTP-fri, ADR 0023)
// utan den extraherade provider-switchen → samma dev=Console/Resend, non-dev=Null-grindning som
// Api, utan drift. Non-dev defaultar till NullEmailSender (vilande) tills Resend explicit
// konfigureras. DI i samma commit som jobben (feedback_di_with_handlers_same_commit).
builder.Services.AddEmailSender(builder.Configuration, builder.Environment);
builder.Services.AddScoped<Jobbliggaren.Application.Matching.Jobs.BackgroundMatching.BackgroundMatchingJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackgroundMatchingWorker>();
// ADR 0087 D5 (#311 PR-4) — den nattliga företagsföljnings-scannen. Egen watermark, org.nr
// IN-membership, INGEN scorer (drar inte in AddMatchingEngine-portar). Behöver bara IAppDbContext +
// IDateTimeProvider. Wrapper + jobb i samma commit (TD-103: Worker ValidateOnBuild=false → en saknad
// dep failar först vid Hangfire-invocation 03:25 UTC).
builder.Services.AddScoped<Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan.CompanyWatchScanJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.CompanyWatchScanWorker>();
// #560 (ADR 0091) — SCB company-register population module + Worker wrapper. AddScbCompanyRegister
// registers the refresh orchestrator + bulk store + partition planner, and (only when
// ScbRegister:Enabled=true) the real cert-based client with the process-wide 10/10s limiter; else a
// Null source so cert-less dev/CI stay dark. Worker-only (the Api never populates — parity the
// registry-free company-watch scan). Wrapper + module in the same commit (TD-103: Worker
// ValidateOnBuild=false → a missing dep fails first at Hangfire-invocation, verified manually in dev).
builder.Services.AddScbCompanyRegister(builder.Configuration);
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.ScbCompanyRegisterSyncWorker>();
// ADR 0080 Vag 4 PR-4b — Strong-digest-dispatch (kadens-cap:ad sammanfattning). Två cron-ingångar
// (Daglig/Veckovis) via DigestDispatchWorker; jobbet filtrerar konsenterade användare på den kadens
// det anropas för (cron = fönstret). Cap via IOptions (Digest-sektionen, ValidateDataAnnotations +
// ValidateOnStart — paritet backfill-options). Wrapper + jobb + options i samma commit (TD-103:
// Worker kör ValidateOnBuild=false, så en saknad dep failar först vid Hangfire-invocation).
builder.Services.AddOptions<Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchOptions>()
    .Bind(builder.Configuration.GetSection(
        Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.DigestDispatchWorker>();
// TD-114 (ADR 0080 Vag 4) — stranded-Queued match reaper. Marks a UserJobAdMatch left
// Queued past the threshold as terminal Failed (no re-send). Needs only IAppDbContext +
// IDateTimeProvider (no IEmailSender / matching engine — it never sends). Wrapper + job in
// the same commit (TD-103: Worker ValidateOnBuild=false → a missing dep fails first at
// Hangfire-invocation; verified manually in dev).
builder.Services.AddScoped<Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper.StrandedMatchReaperJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.StrandedMatchReaperWorker>();
// TD-111 (ADR 0074 F4-8) — ParsedResume staging-retention sweep (GDPR Art. 5(1)(e)).
// Set-based ExecuteDelete, DEK-free (no IRequiresFieldEncryptionKey — see the job doc).
// Wrapper + job in the same commit (TD-103: Worker ValidateOnBuild=false).
builder.Services.AddScoped<Jobbliggaren.Application.Resumes.Jobs.ParsedResumeRetention.ParsedResumeRetentionJob>();
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.ParsedResumeRetentionWorker>();
// TD-13 C5 (ADR 0049 Beslut 4) — DisableConcurrentExecution-wrapper för
// fält-krypterings-backfillen (potentiellt långkörande, paritet snapshot).
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillFieldEncryptionWorker>();
// STEG 6 (2026-05-24) — DisableConcurrentExecution-wrapper för ssyk-backfill
// (~2h körnings-tid vid default-throttle, paritet snapshot/field-encryption).
// Wrappern registreras för framtida cron-användning; Api enqueue:ar för MVP-
// triggern Application-jobbet direkt (Clean Arch — Api refererar inte Worker).
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdSsykWorker>();
// Fas B2 (2026-06-08, ADR 0067) — DisableConcurrentExecution-wrapper för Klass 2-
// backfill (~2,5h körnings-tid vid default-throttle mot ~44k rader, paritet
// ssyk-backfill). Wrappern registreras för framtida cron-användning; Api
// enqueue:ar för triggern Application-jobbet direkt (Clean Arch — Api refererar
// inte Worker).
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdKlass2Worker>();
// Fas 4 STEG 4 (F4-4, ADR 0071/0074) — DisableConcurrentExecution-wrapper för
// extraction-backfill (lokal re-projektion). Wrappern registreras för framtida
// cron-användning; Api enqueue:ar för triggern Application-jobbet direkt.
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdExtractedTermsWorker>();
// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — DisableConcurrentExecution-wrapper för
// requirements re-ingest-backfill (must_have/nice_to_have → Requirement-termer; per-ID-
// refetch, paritet Klass2). Engångs-op; Api enqueue:ar Application-jobbet direkt.
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.BackfillJobAdRequirementsWorker>();

// ADR 0064 — Worker:s landing-stats-refresh-wrapper. Job-klassen registreras
// av AddLandingStats() nedan; wrappern bär bara Hangfire-attributet
// (paritet ExpireJobAdsWorker per ADR 0023 delbeslut 2).
builder.Services.AddScoped<Jobbliggaren.Worker.Hosting.RefreshLandingStatsWorker>();

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
    opts.InstanceName = "jobbliggaren:";
});
builder.Services.AddLandingStats();

// Fas 4 STEG 2 (F4-2) — delad lokal svensk NLP-tier (stemmer/analyzer/spell-check).
// Standalone-modul (speglar AddLandingStats); BCL-only paket → bryter ej Worker:s
// HTTP-fria invariant (ADR 0023). Worker konsumerar den i CV-/matchnings-motorerna
// (F4-4 och framåt). Startup-existens-check på DSSO-filerna körs här.
builder.Services.AddTextAnalysis();

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
    options.Assemblies = [typeof(Jobbliggaren.Application.AssemblyMarker)];
});

// Pipeline-behaviors registreras explicit (se Api/Program.cs för rationale).
builder.Services.AddMediatorPipelineBehaviors();

// Hangfire-storage. Egen schema "hangfire" undviker konflikt med Jobbliggaren-tabeller.
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
// TD-17 punkt 4 — split jobbliggaren_app (Postgres) / jobbliggaren_worker (HangfireStorage)
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
