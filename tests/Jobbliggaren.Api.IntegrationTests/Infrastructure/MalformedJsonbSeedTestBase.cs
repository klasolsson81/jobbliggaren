using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

// #352 — any [Collection("Api")] test that deliberately persists malformed/legacy jsonb
// into a VO-bearing column MUST derive from this base. The shared Testcontainers Postgres
// means a toxic row that outlives its test poisons any neighbour doing a broad load that
// materializes the EF value-converter per row. Clearing on BOTH entry and exit makes every
// such test order-independent (generalizes #300 PR-4). Raw SQL DELETE bypasses the converter
// — the toxic rows cannot be read back, only deleted.
public abstract class MalformedJsonbSeedTestBase(ApiFactory factory) : IAsyncLifetime
{
    protected ApiFactory Factory { get; } = factory;

    // The toxic tables this fixture seeds. List children before parents (FK direction)
    // if a fixture clears more than one.
    protected abstract IReadOnlyList<string> TablesToClear { get; }

    public async ValueTask InitializeAsync() => await ClearAsync();

    public async ValueTask DisposeAsync()
    {
        await ClearAsync();
        GC.SuppressFinalize(this);
    }

    private async Task ClearAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var table in TablesToClear)
            // `table` is a controlled constant from a hardcoded subclass override (never user
            // input) — interpolating it into the DELETE is safe (no SQL-injection surface). A
            // table identifier cannot be a SQL parameter, so EF1002 is suppressed with that
            // justification rather than worked around.
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection — controlled constant, see above.
            await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {table};", TestContext.Current.CancellationToken);
#pragma warning restore EF1002
    }
}
