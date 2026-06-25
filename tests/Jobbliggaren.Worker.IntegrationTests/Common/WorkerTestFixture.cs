using Jobbliggaren.Application;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.Auditing;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Common;

/// <summary>
/// Fixture för Worker-integration-test. Speglar Worker/Program.cs DI-konfig
/// (per ADR 0023 / STEG 9) men UTAN Hangfire-server — testen anropar
/// orchestrator-jobben direkt. Hangfire själv testas av Hangfire-projektets
/// egna tester; vår yta är orchestrator + Mediator-pipeline + audit-paritet.
/// </summary>
public sealed class WorkerTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// TD-13 C2 Seam 1 — den delade deterministiska fake-KMS som hela
    /// <see cref="Services"/>-grafen kör. Scenario 7 mäter cache-memoisering
    /// mot <see cref="DeterministicFakeKms.DecryptCallCount"/> (Worker-collection
    /// är seriell ⇒ deterministisk). Scenario 9 (fail-closed) använder INTE
    /// denna — den direkt-konstruerar store+cache+failing-KMS.
    /// </summary>
    public DeterministicFakeKms FakeKms { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                // TD-13 C2 Seam 1: FieldEncryptionOptions.CmkKeyId har
                // .ValidateOnStart() (fail-closed) — KMS-klienten fakas ändå
                // (sista-vinner-singleton nedan), men options-validering kräver
                // ett icke-tomt CMK-id i testkonfigen.
                ["FieldEncryption:CmkKeyId"] =
                    "arn:aws:kms:eu-north-1:000000000000:key/td13-test-cmk",
                ["FieldEncryption:AwsRegion"] = "eu-north-1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Speglar Worker/Program.cs DI-yta (utan Hangfire — vi anropar jobb direkt)
        services.AddPersistence(configuration);
        services.AddCoreIdentityForWorker(configuration);
        services.AddApplication();
        // F4-9: the deterministic CV-review engine + its NLP tier (parity Program.cs), so the
        // CvReviewEncryptionTests can resolve ICvReviewEngine against the real DEK pipeline.
        services.AddTextAnalysis();
        services.AddCvReview();
        // F4-10: the deterministic CV-build/improve engine (Phase A), so the
        // CvImprovementEncryptionTests can resolve ICvImprovementEngine against the real DEK
        // pipeline (parity AddCvReview). RED until AddCvImprovement() ships in Infrastructure.
        services.AddCvImprovement();
        // F4-10 Phase B: the deterministic QuestPDF CV renderer, so CvRenderEncryptionTests can
        // resolve ICvRenderer against the real DEK pipeline.
        services.AddCvRendering();
        // ADR 0080 Vag 4 PR-3: the matching engine (IMatchScorer + IMatchProfileBuilder), so
        // BackgroundMatchingJobIntegrationTests can resolve them (parity Worker/Program.cs, which
        // calls AddMatchingEngine() — the Worker does not call AddInfrastructure).
        services.AddMatchingEngine();

        // ADR 0080 Vag 4 PR-4b: mirror Worker/Program.cs's email + digest surface so the
        // DigestDispatchJob/Worker resolve from this SP (the digest integration test runs them
        // end-to-end). The Worker is HTTP-free (ADR 0023) so it registers the extracted
        // AddEmailSender directly, NOT AddInvitationsAndEmail. The test env below is "Test" → the
        // Console branch registers ConsoleEmailSender (the default unset Provider → "Console";
        // dev/test-only — a real send is never attempted in-process). The digest options carry the
        // anti-spam cap; bound + validated exactly as Program.cs does.
        var emailEnv = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
        {
            EnvironmentName = "Test",
            ApplicationName = "Jobbliggaren.Worker.IntegrationTests",
            ContentRootPath = AppContext.BaseDirectory,
        };
        services.AddEmailSender(configuration, emailEnv);
        services.AddOptions<Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchOptions>()
            .Bind(configuration.GetSection(
                Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchJob>();
        services.AddScoped<Jobbliggaren.Worker.Hosting.DigestDispatchWorker>();

        // TD-114: mirror Worker/Program.cs's stranded-match reaper registration so its
        // integration test can resolve the worker (WorkerTestFixture does NOT replicate
        // Program.cs DI — it registers explicitly).
        services.AddScoped<Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper.StrandedMatchReaperJob>();
        services.AddScoped<Jobbliggaren.Worker.Hosting.StrandedMatchReaperWorker>();

        services.AddSingleton<ICurrentUser, WorkerSystemUser>();
        services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
        services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(Jobbliggaren.Application.AssemblyMarker)];
        });
        services.AddMediatorPipelineBehaviors();

        // TD-13 C2 Seam 1 (architect-domen 2026-05-18, Variant A): sista-vinner-
        // registrering ⇒ hela grafen (KmsDataKeyProvider → UserDataKeyStore →
        // ScopedUserDataKeyCache) kör den delade deterministiska fake-KMS:en.
        // Produktkod orörd, ingen prod-override-yta. AddPersistence registrerar
        // riktig AmazonKeyManagementServiceClient (DI rad 316) — denna
        // singleton-registrering läggs EFTER och vinner i DI-upplösning.
        services.AddSingleton<Amazon.KeyManagementService.IAmazonKeyManagementService>(
            _ => FakeKms.Substitute);

        // TD-13 hotfix Approach D: FieldEncryptionOptionsValidator tar
        // IHostEnvironment (riktig Worker/Api kör generic host som ger den).
        // Denna fixture bygger en bar ServiceCollection utan host → registrera
        // en Test-env explicit (IsProduction/IsStaging = false → validator
        // loggar warning + Success; CmkKeyId-dummyn ovan gör den Success ändå).
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                EnvironmentName = "Test",
                ApplicationName = "Jobbliggaren.Worker.IntegrationTests",
                ContentRootPath = AppContext.BaseDirectory,
            });

        Services = services.BuildServiceProvider();

        // Migration — både AppDbContext och AppIdentityDbContext (krävs för
        // AccountHardDeleter-tester som anropar UserManager).
        using var scope = Services.CreateScope();
        // F6 P4 — pg_trgm krävs av F6P4aJobAdTrigramIndexes-migrationen. I prod
        // skapas extensionen av Jobbliggaren.Migrate `ensure-extensions`-mode
        // (master-creds, Phase A); test-harnessen replikerar det (Testcontainers
        // postgres-superuser kan CREATE EXTENSION). Idempotent.
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<Jobbliggaren.Infrastructure.Identity.AppIdentityDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.StopAsync();
    }
}
