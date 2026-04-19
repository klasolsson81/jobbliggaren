using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace JobbPilot.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .Build();

    // Serialize host creation so parallel test classes don't overwrite each
    // other's environment variable before base.CreateHost reads it.
    private static readonly Lock _createHostLock = new();

    // Called by WebApplicationFactory before Program.Main runs — container is
    // already started at this point (InitializeAsync starts it before Services
    // is first accessed, which triggers this method).
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (_createHostLock)
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__Postgres",
                _postgres.GetConnectionString());
            return base.CreateHost(builder);
        }
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        await _postgres.StopAsync();
        await base.DisposeAsync();
    }
}
