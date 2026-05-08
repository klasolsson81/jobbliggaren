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

// Persistence-modul (DbContext, IAppDbContext, IDateTimeProvider) — utan HTTP-bagage,
// utan Identity. Worker-kontextens DI-yta är medvetet minimerad per ADR 0023 / STEG 9.
builder.Services.AddPersistence(builder.Configuration);
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
// PrepareSchemaIfNecessary=true för dev/test — i prod sätts false och schema migreras
// via runbook (TD spårat i ADR 0023).
var hangfireConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres saknas i konfiguration.");

builder.Services.AddHangfire(cfg => cfg
    .UseRecommendedSerializerSettings()
    .UseSimpleAssemblyNameTypeSerializer()
    .UsePostgreSqlStorage(
        opts => opts.UseNpgsqlConnection(hangfireConnectionString),
        new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire",
            PrepareSchemaIfNecessary = true,
        }));

// Worker-count explicit satt — default Environment.ProcessorCount blir 1 i Fargate-container
// med 1 vCPU. 4 är lämpligt för IO-bundna Mediator-jobb.
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = 4;
});

// Recurring-jobs registreras vid host-start.
builder.Services.AddHostedService<RecurringJobRegistrar>();

var host = builder.Build();
host.Run();
