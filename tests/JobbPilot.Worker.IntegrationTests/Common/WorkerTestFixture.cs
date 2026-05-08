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

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Warning));

        // Speglar Worker/Program.cs DI-yta (utan Hangfire — vi anropar jobb direkt)
        services.AddPersistence(configuration);
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

        Services = services.BuildServiceProvider();

        // Migration
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.StopAsync();
    }
}
