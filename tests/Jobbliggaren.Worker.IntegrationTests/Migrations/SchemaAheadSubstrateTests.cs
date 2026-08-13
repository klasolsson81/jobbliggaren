using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Migrate;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// Substrate pins for the schema-ahead gate (#1236), against a real
/// <c>__EFMigrationsHistory</c> written by EF itself.
///
/// <para>
/// <b>The premise (CLAUDE.md §5 Tests).</b> "The history holds a migration this assembly does
/// not contain" is a state no path in THIS tree's <c>src/</c> produces — its actor is <b>a newer
/// image's migrate step</b>, which is not callable from here. So the seed goes through
/// production's own writer shape instead: <see cref="IHistoryRepository.GetInsertScript"/> from
/// the same configured context, which is the exact transform any newer assembly's
/// <c>MigrateAsync</c> uses to write its rows. That is also the mechanically necessary choice:
/// <c>BuildAppOptions</c> applies snake_case naming, so the history columns are
/// <c>migration_id</c>/<c>product_version</c> and a hand-written PascalCase INSERT would target
/// columns that do not exist — or worse, a hand-written snake_case copy would be a second truth.
/// </para>
///
/// <para>
/// <b>Own container, one journey method.</b> The class is its own <see cref="IAsyncLifetime"/>
/// (xunit news a class instance per test method, so one method = one container), provisioned
/// with production's Phase A posture and migrated as the app role — the same shape `schema`
/// mode runs, bare <c>BuildAppOptions</c> with no interceptors.
/// </para>
/// </summary>
public sealed class SchemaAheadSubstrateTests : IAsyncLifetime
{
    /// <summary>
    /// Unmistakably synthetic, sorted after every real ID, and named for its actor.
    /// </summary>
    private const string SeededByNewerImage = "20991231235959_SeededByNewerImage";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _appConnectionString = await TestDatabaseProvisioner
            .ProvisionAndGetAppConnectionStringAsync(_postgres.GetConnectionString());

        // pg_trgm is required by the job-ad trigram-index migration. Production creates it in
        // `ensure-extensions` mode under master credentials before `schema` runs; the app role
        // cannot (no CREATE on the database), so the superuser stands in for that mode here.
        await using var superuser = new NpgsqlConnection(_postgres.GetConnectionString());
        await superuser.OpenAsync();
        await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    [Fact]
    public async Task SchemaMode_Substrate_GateMatchesEfAndRefusesTheSeededFuture()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewAppContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.Count.ShouldBeGreaterThan(1); // the journey needs a partial state to exist

        // --- 1. Forward state: migrate to the FIRST migration only, as the app role. ---------
        await db.GetService<IMigrator>().MigrateAsync(assembly[0], ct);

        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var efPending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var forward = SchemaAheadGate.Decide(applied, assembly, overrideValue: null);

        // The equivalence that licenses deriving pending client-side in RunSchemaAsync:
        // same set, same (assembly) order as EF's own answer — in a state where it is non-empty.
        forward.Verdict.ShouldBe(SchemaAheadVerdict.Proceed);
        forward.Pending.ShouldBe(efPending);
        efPending.ShouldNotBeEmpty();

        // --- 2. Seed the future row through production's writer shape. -----------------------
        // Actor: a newer image's migrate step (see the class docblock). The product_version
        // value is data the gate never reads; EF's own assembly version keeps it truthful.
        var historyRepository = db.GetService<IHistoryRepository>();
        var insert = historyRepository.GetInsertScript(new HistoryRow(
            SeededByNewerImage,
            typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0"));
        await db.Database.ExecuteSqlRawAsync(insert, ct);

        // --- 3. Divergence: unknown row AND unapplied assembly migrations at once. -----------
        applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var diverged = SchemaAheadGate.Decide(applied, assembly, overrideValue: null);
        diverged.Verdict.ShouldBe(SchemaAheadVerdict.RefuseDivergence);

        // The override blesses old-code-on-newer-schema only — never an apply into a fork.
        SchemaAheadGate.Decide(applied, assembly, SeededByNewerImage)
            .Verdict.ShouldBe(SchemaAheadVerdict.RefuseDivergence);

        // --- 4. EF applies the rest and tolerates the unknown row — in the APPLY direction. --
        await db.Database.MigrateAsync(ct);

        // --- 5. The control pin: THE vulnerability itself, on our exact EF version. ----------
        // Pending is empty although the history holds a row this assembly cannot name — this
        // silence is what #1236 exists for, and it is the tripwire: if a future EF starts
        // refusing or warning here, the gate can simplify.
        applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        efPending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        efPending.ShouldBeEmpty();
        applied.ShouldContain(SeededByNewerImage);

        // --- 6. The gate turns that silence into the refusal, and the override into consent. -
        var behind = SchemaAheadGate.Decide(applied, assembly, overrideValue: null);
        behind.Verdict.ShouldBe(SchemaAheadVerdict.RefuseSchemaAhead);
        behind.Unknown.ShouldBe([SeededByNewerImage]);
        behind.Pending.ShouldBe(efPending); // equivalence holds in the in-sync-but-ahead state too

        SchemaAheadGate.Decide(applied, assembly, SeededByNewerImage)
            .Verdict.ShouldBe(SchemaAheadVerdict.OverriddenNoOp);
    }
}
