using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The collection every persistence-slice class joins, so the run holds ONE
/// <see cref="SharedPostgresFixture"/> instead of one container per test.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedPostgresFixtureGroup : ICollectionFixture<SharedPostgresFixture>
{
    public const string Name = "SharedPostgres";
}

/// <summary>
/// One <c>postgres:18</c> for the whole run, migrated ONCE into a template database. A test takes a
/// clone of the template (<c>CREATE DATABASE … TEMPLATE</c>) and drops it afterwards, so it still
/// owns a fresh, fully migrated database — the property the per-test containers this replaces were
/// paying for — without a container start and a full migration run per test method.
///
/// <para>
/// Measured 2026-09-24 on the runner (#1785): the classes that implemented <see cref="IAsyncLifetime"/>
/// on the test class with their own container carried 13.9 of the suite's 17.6 minutes for 480 of its
/// 2 282 tests, the Postgres half at 2.0 s per test — nearly all of it container start plus migrations.
/// </para>
///
/// <para>
/// The template is migrated as the container's superuser, exactly as the replaced fixtures did, so this
/// fixture proves nothing about privileges. That oracle is <c>TestDatabaseProvisioner</c> (#1232), and a
/// class that needs it keeps its own container.
/// </para>
/// </summary>
public sealed class SharedPostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "jobbliggaren_template";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private int _clones;

    /// <summary>
    /// The superuser connection string against the container's default database: no application
    /// schema, for read-only oracle queries against the server itself (the stemmer parity gates read
    /// <c>to_tsvector</c> and <c>pg_read_file</c>). Nothing is migrated into it.
    /// </summary>
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        AdminConnectionString = _postgres.GetConnectionString();

        await ExecuteAsync($"CREATE DATABASE \"{TemplateDatabase}\"");
        var template = WithDatabase(TemplateDatabase);
        await using (var provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options
                .UseNpgsql(template, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention())
            .BuildServiceProvider())
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // pg_trgm is required by the trigram-index migration (parity ApiFactory); the superuser can create it.
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            await db.Database.MigrateAsync();
        }

        // A template with an open connection cannot be copied (55006). The migrator's connection went
        // back to the pool rather than away, so the pool is cleared; then the template is closed to
        // connections altogether and whatever still holds one is terminated, so a later leak fails at
        // its own connect instead of failing every clone after it.
        using (var pooled = new NpgsqlConnection(template))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await ExecuteAsync($"ALTER DATABASE \"{TemplateDatabase}\" WITH ALLOW_CONNECTIONS false");
        await ExecuteAsync(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TemplateDatabase}' AND pid <> pg_backend_pid()");
    }

    /// <summary>
    /// A fresh, fully migrated database of the caller's own. Returned through
    /// <see cref="DropDatabaseAsync"/> when the test is done.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken ct = default)
    {
        var name = $"jobbliggaren_t{Interlocked.Increment(ref _clones)}";
        await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"", ct);
        return WithDatabase(name);
    }

    /// <summary>
    /// Drops a clone. Its pool is cleared first so the drop does not race the test's own last
    /// connection, and <c>WITH (FORCE)</c> ends whatever a disposed graph still held.
    /// </summary>
    public async Task DropDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database
            ?? throw new ArgumentException("The connection string names no database.", nameof(connectionString));
        using (var pooled = new NpgsqlConnection(connectionString))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private string WithDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database }.ConnectionString;

    private async Task ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
