using JobbPilot.Application;
using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Infrastructure;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.Auditing;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace JobbPilot.Worker.IntegrationTests.Common;

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
        services.AddSingleton<ICurrentUser, WorkerSystemUser>();
        services.AddScoped<ICorrelationIdProvider, WorkerCorrelationIdProvider>();
        services.AddScoped<IRequestContextProvider, WorkerRequestContextProvider>();
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(JobbPilot.Application.AssemblyMarker)];
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
                ApplicationName = "JobbPilot.Worker.IntegrationTests",
                ContentRootPath = AppContext.BaseDirectory,
            });

        Services = services.BuildServiceProvider();

        // Migration — både AppDbContext och AppIdentityDbContext (krävs för
        // AccountHardDeleter-tester som anropar UserManager).
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<JobbPilot.Infrastructure.Identity.AppIdentityDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.StopAsync();
    }
}
