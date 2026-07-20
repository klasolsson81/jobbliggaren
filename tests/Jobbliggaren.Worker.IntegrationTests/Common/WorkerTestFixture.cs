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
    /// ADR 0066 — deterministisk 32-byte AES-256 test-master-nyckel för den
    /// lokala envelope-krypteringen (round-trip kräver bara SAMMA nyckel inom
    /// host-livstiden, inte en specifik). Ersätter den tidigare fake-KMS:en.
    /// </summary>
    internal static readonly string TestMasterKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    // #842 — deterministic 32-byte test pepper, distinct from the master key so a test can
    // never pass by accidentally peppering with the encryption key. Runtime-generated, no literal.
    internal static readonly string TestAuditPepperBase64 =
        Convert.ToBase64String([.. Enumerable.Range(100, 32).Select(i => (byte)i)]);

    // #544 — deterministic 32-byte company-watch pepper, distinct from the master key AND the audit
    // pepper so the scan's HMAC token can never collide with either.
    internal static readonly string TestWatchPepperBase64 =
        Convert.ToBase64String([.. Enumerable.Range(132, 32).Select(i => (byte)i)]);

    // #692 — deterministic 32-byte CV-review finding-fingerprint pepper, distinct from every other key.
    // This fixture builds the graph via AddCvReview() below, which resolves HmacFindingFingerprinter;
    // it reads this pepper at construction. Without it, HMACSHA256 silently accepts an empty key (it
    // does NOT throw) and, because the fixture uses BuildServiceProvider() (no host start),
    // ValidateOnStart never fires to catch it — so a Worker test resolving the real reconciler would
    // fingerprint under an unkeyed HMAC. Set it, same reasoning as the audit + watch peppers above.
    internal static readonly string TestFingerprintPepperBase64 =
        Convert.ToBase64String([.. Enumerable.Range(164, 32).Select(i => (byte)i)]);

    /// <summary>
    /// ADR 0066 — räknande <see cref="Application.Common.Security.IDataKeyProvider"/>-
    /// dekoratör runt den riktiga <c>LocalDataKeyProvider</c> som hela
    /// <see cref="Services"/>-grafen kör. Scenario 7 mäter cache-memoisering mot
    /// <see cref="CountingDataKeyProvider.UnwrapCount"/> (Worker-collection är
    /// seriell ⇒ deterministisk). Scenario 9 (fail-closed) använder INTE denna —
    /// den direkt-konstruerar store+cache+failing-provider.
    /// </summary>
    public CountingDataKeyProvider Deks => Services.GetRequiredService<CountingDataKeyProvider>();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                // ADR 0066 — lokal envelope. FieldEncryptionOptionsValidator har
                // .ValidateOnStart() (fail-closed) och kräver en giltig 32-byte
                // master-nyckel i ALLA miljöer; grafen kör den räknande
                // dekoratören runt den riktiga LocalDataKeyProvider (sista-vinner
                // nedan). Provider="Local" är default men sätts explicit här.
                ["FieldEncryption:Provider"] = "Local",
                ["FieldEncryption:LocalMasterKeyBase64"] = TestMasterKeyBase64,

                // #842 — AddPersistence now also registers AuditPseudonymizationOptions with
                // .ValidateOnStart(), fail-closed in ALL environments. No Worker test sends a
                // Mediator message today, so AuditBehavior (which injects the pseudonymiser) is
                // never constructed and the gap is invisible — until the first Worker test that
                // does, which would explode with an OptionsValidationException pointing nowhere
                // near this fixture. Set it now, not after the confusing failure.
                ["AuditPseudonymization:PepperBase64"] = TestAuditPepperBase64,

                // #544 — separate company-watch pepper, fail-closed at startup (all environments), so
                // the Worker host that runs CompanyWatchScanJob + resolves IProtectedIdentityTokenizer
                // boots. Same reasoning as the audit pepper directly above.
                ["CompanyWatchPseudonymization:PepperBase64"] = TestWatchPepperBase64,

                // #692 — separate CV-review finding-fingerprint pepper. The real Worker DOES boot this
                // section: Program.cs calls AddJobSources, which calls AddCvReview, which registers the
                // options + ValidateOnStart. So the prod Worker needs this pepper provisioned too (like
                // the watch pepper), and this fixture — which builds the same graph — supplies it.
                ["CvReviewFingerprintPseudonymization:PepperBase64"] = TestFingerprintPepperBase64,
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
        // #982 — the JobTech ingest keyword extractor + its shared skill index. Registered in
        // AddJobSources in production (not called by this fixture), but the real
        // UpsertExternalJobAdCommandHandler depends on IJobAdKeywordExtractor, so the
        // SyncPlatsbankenStreamJobAuditIsolationTests drives the real upsert path end-to-end. Deps
        // (ITextAnalyzer/IStemmer) come from AddTextAnalysis above; parity DependencyInjection.cs.
        services.AddSingleton<Jobbliggaren.Infrastructure.Taxonomy.SkillTaxonomyIndex>();
        services.AddSingleton<
            Jobbliggaren.Application.JobAds.Abstractions.IJobAdKeywordExtractor,
            Jobbliggaren.Infrastructure.Taxonomy.JobAdKeywordExtractor>();
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
        // AddEmailSender directly (the Api uses the same method). The test env below is "Test" → the
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

        // TD-111: same — register the ParsedResume retention sweep so its integration test resolves it.
        services.AddScoped<Jobbliggaren.Application.Resumes.Jobs.ParsedResumeRetention.ParsedResumeRetentionJob>();
        services.AddScoped<Jobbliggaren.Worker.Hosting.ParsedResumeRetentionWorker>();

        // #664: same — the one-off source_file_name mask backfill (admin-enqueued, no recurring worker)
        // is registered in AddJobSources (which this fixture does not call), so mirror it here for its
        // integration test to resolve it (parity #544's job, minus a worker wrapper).
        services.AddScoped<Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask.BackfillParsedResumeSourceFileNameMaskJob>();

        // #560 (ADR 0091) — the SCB register bulk store, resolved per child scope by
        // ScbCompanyRegisterRefresher. Registered directly (concrete, no port — Fork 2) so the
        // orchestrator Testcontainers test can drive the real filter -> upsert -> sweep -> audit path.
        services.AddScoped<Jobbliggaren.Infrastructure.CompanyRegister.ScbCompanyRegisterStore>();

        services.AddSingleton<ICurrentUser, WorkerSystemUser>();
        services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
        services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(Jobbliggaren.Application.AssemblyMarker)];
        });
        services.AddMediatorPipelineBehaviors();

        // ADR 0066 (#802) — sista-vinner-registrering ⇒ hela grafen
        // (LocalDataKeyProvider → UserDataKeyStore → ScopedUserDataKeyCache) kör
        // den räknande dekoratören runt den RIKTIGA LocalDataKeyProvider.
        // Produktkod orörd, ingen prod-override-yta. AddPersistence registrerar
        // LocalDataKeyProvider som IDataKeyProvider — denna registrering läggs
        // EFTER och vinner. CountingDataKeyProvider.UnwrapCount ger scenario 7:s
        // memoiserings-mätpunkt (ersätter DeterministicFakeKms.DecryptCallCount).
        services.AddSingleton<CountingDataKeyProvider>(sp =>
            new CountingDataKeyProvider(
                ActivatorUtilities.CreateInstance<
                    Jobbliggaren.Infrastructure.Security.LocalDataKeyProvider>(sp)));
        services.AddSingleton<Jobbliggaren.Application.Common.Security.IDataKeyProvider>(
            sp => sp.GetRequiredService<CountingDataKeyProvider>());

        // Riktig Worker/Api kör generic host som ger IHostEnvironment; vissa
        // seedrar (IdempotentAdminRoleSeeder / TaxonomySnapshotSeeder) tar den via
        // ctor. Denna fixture bygger en bar ServiceCollection utan host →
        // registrera en Test-env explicit. (FieldEncryptionOptionsValidator tar
        // den INTE längre efter KMS-borttaget #802 — registreringen är kvar för
        // host-paritet.)
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
