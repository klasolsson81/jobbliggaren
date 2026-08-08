using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1013 — a DEDICATED, single-owner <c>postgres:18</c> for the /jobb browse-sort plan-CHOICE guard
/// (<see cref="JobAdBrowseSortQueryPlanTests"/>), NOT the shared <c>[Collection("Api")]</c> container.
///
/// <para>
/// <b>Why its own container, and not <c>ApiFactory</c>'s.</b> A plan-CHOICE assertion runs with NO GUC —
/// it lets the cost-based planner have the whole search space, then asserts which plan it picks. That is
/// only deterministic if the test owns the table's STATISTICS at EXPLAIN time. The shared Api container
/// has ~62 classes that seed <c>job_ads</c> (a rough count, measured 2026-07-20 — illustrative, not
/// load-bearing) and never truncates between them (ADR 0045 Beslut 5 — "a
/// flaky perf-gate is worse than no perf-gate"), so a no-GUC choice there flakes between Index Scan and
/// Bitmap + Sort at the accumulated, execution-order-dependent row estimate. A TRUNCATE to fix that would
/// wipe the other classes' seed. Exclusive ownership of the container is the only way to get both a
/// production-scale regime AND determinism — the same reason the mature sibling
/// <c>CompanyWatchBrowseQueryPlanTests</c> owns <c>company_register</c> in its serial Worker collection.
/// </para>
///
/// <para>
/// <b>Deliberately lighter than <c>ApiFactory</c>.</b> This is the persistence slice only — one Postgres,
/// <c>pg_trgm</c>, and <c>AppDbContext.MigrateAsync</c>. No <c>WebApplicationFactory</c>, no identity DB,
/// no Redis, no HTTP host. Its weight is ≈ <c>WorkerTestFixture</c>, not a second Api host. The plan guard
/// needs a migrated schema and a raw connection to EXPLAIN — nothing more.
/// </para>
/// </summary>
public sealed class JobAdBrowsePlanFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    public ServiceProvider Services { get; private set; } = null!;

    // ADR 0066 — deterministic 32-byte AES-256 master key for the local envelope encryption. AddPersistence
    // wires the two field-encryption interceptors into AppDbContext and resolves LocalDataKeyProvider, which
    // reads FieldEncryption:LocalMasterKeyBase64 (validated on first IOptions.Value access). job_ads carries
    // NO DEK-encrypted column, and this guard never SaveChanges (it seeds via raw SQL and only EXPLAINs), so
    // the key is provisioned purely so the DbContext can be constructed. Runtime-generated, no literal.
    internal static readonly string TestMasterKeyBase64 =
        Convert.ToBase64String([.. Enumerable.Range(0, 32).Select(i => (byte)i)]);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // #1232 — TWO CONNECTIONS, MIRRORING THE REAL BOOT ORDER, and the split is the point.
        //
        // This fixture used to do everything as the Testcontainers superuser against a database
        // where `REVOKE ALL ON DATABASE … FROM PUBLIC` had never run, which made it blind BY
        // CONSTRUCTION to the two 42501s #1229 repaired — both found by booting the stack on the
        // Netcup box rather than by CI. Provisioning with production's own Phase A statements and
        // then migrating as `jobbliggaren_app` is what turns this into an oracle for that class.
        //
        // `CREATE EXTENSION pg_trgm` (below) still runs as the superuser, because that is what
        // production does: `ensure-extensions` mode connects with MASTER credentials, and only
        // `schema` mode drops to the app role. So the credential switch belongs exactly here —
        // between provisioning and MigrateAsync — rather than at the top.
        var appConnectionString = await TestDatabaseProvisioner
            .ProvisionAndGetAppConnectionStringAsync(_postgres.GetConnectionString());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = appConnectionString,
                ["FieldEncryption:Provider"] = "Local",
                ["FieldEncryption:LocalMasterKeyBase64"] = TestMasterKeyBase64,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // AddPersistence registers AppDbContext (+ its two field-encryption interceptors + IDataKeyProvider)
        // and nothing HTTP-bound — exactly the slice this fixture needs. No AddApplication / identity /
        // matching / email: this guard resolves only AppDbContext and runs raw SQL through it.
        services.AddPersistence(configuration);

        // Host parity: the bare ServiceCollection has no generic host, so register a Test environment
        // explicitly. No AddPersistence registration takes IHostEnvironment after the KMS removal (#802);
        // this is kept purely for host parity, exactly as WorkerTestFixture keeps it (WorkerTestFixture.cs).
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = "Test",
            ApplicationName = "Jobbliggaren.Api.IntegrationTests",
            ContentRootPath = AppContext.BaseDirectory,
        });

        Services = services.BuildServiceProvider();

        // pg_trgm is required by the job_ads trigram-index migration. Production creates it in
        // `ensure-extensions` mode under MASTER credentials — the app role cannot CREATE
        // EXTENSION — so this runs on the superuser connection, not through AppDbContext.
        await using (var superuser = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await superuser.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
            await cmd.ExecuteNonQueryAsync();
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // As `jobbliggaren_app`, against a database Phase A provisioned — i.e. under the same
        // privilege posture `schema` mode runs under in production. A missing grant fails HERE
        // now, instead of on a clean-database boot in Nürnberg.
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.StopAsync();
    }
}
