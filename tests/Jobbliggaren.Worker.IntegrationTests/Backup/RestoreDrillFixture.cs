using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Identity;
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

namespace Jobbliggaren.Worker.IntegrationTests.Backup;

/// <summary>
/// #197 gate M-4, the drill's CI half — TWO dedicated <c>postgres:18</c> containers: a SOURCE that
/// is dumped and a TARGET that is restored into.
///
/// <para>
/// <b>What this fixture is for.</b> <c>docs/runbooks/backup-restore.md</c> §6 splits the drill in
/// two and gives each half a different job. The CI half proves the <i>semantics</i> — seed through
/// production entry points, dump, hard-delete through <c>IAccountHardDeleter</c>, restore, and
/// assert the erased user has no key while the other decrypts. The ops half proves the units, the
/// credential, the recipient, the retention layout and the <c>age</c> envelope, none of which CI
/// can see: the box holds no private key by design (§1), so no CI process can possess that path.
/// So <c>age</c>, <c>rclone</c> and systemd are deliberately absent here, and their absence is a
/// scope decision rather than an omission.
/// </para>
///
/// <para>
/// <b>TWO containers, and the second one is what makes two flags an oracle at all.</b> Both dumps
/// in <c>deploy/systemd/jobbliggaren-backup.sh</c> pass <c>--no-owner --no-privileges</c>, and §5
/// restores as <c>postgres</c> into a cluster an operator just created. Postgres roles are
/// CLUSTER-global, not per-database — so a drill that restored into a second database on the
/// SOURCE container would find <c>jobbliggaren_app</c> already present, and deleting those two
/// flags from the mechanism would stay green in CI while failing on the operator's workstation
/// with <c>role "jobbliggaren_app" does not exist</c>. A green result that means nothing is the
/// exact defect shape this drill exists to end. The target is therefore a SEPARATE cluster which
/// has never heard of production's roles, which is also what an operator's workstation is.
/// </para>
///
/// <para>
/// <b>Second ground, and it is not the same one.</b> With one container the artefact is a path.
/// With two it is a byte sequence that genuinely leaves one machine and lands on another — read
/// out with <c>ReadFileAsync</c> and pushed in with <c>CopyAsync</c>, both binary-safe. A dump
/// that is never transported is never proven self-contained.
/// </para>
///
/// <para>
/// <b>Its own collection, not <c>[Collection("Worker")]</c>.</b> The drill's evidence is COUNTS
/// over <c>job_seekers</c> and <c>user_data_keys</c> — see the (a)/(b2) queries in §5 step 5. The
/// shared Worker container is seeded by dozens of classes and never truncated between them, so
/// those counts would be neither deterministic nor about this drill. Same argument, and the same
/// resolution, as <see cref="CompanyRegister.CompanyRegisterPlanFixture"/> and
/// <c>JobAdBrowsePlanFixture</c> (#1013) carry for planner statistics.
/// </para>
///
/// <para>
/// <b>The target's master key equals the source's, and that is production-faithful rather than a
/// test accommodation.</b> A restore is read with the same master key the box holds — §7 says a
/// master-key rotation is precisely what makes older DEK artefacts unreadable. One constant here
/// is that fact, not a shortcut around it.
/// </para>
/// </summary>
public sealed class RestoreDrillFixture : IAsyncLifetime
{
    /// <summary>
    /// The database §5 step 3 creates on the restore target (<c>createdb -U postgres
    /// jobbliggaren_restore</c>). Named here because the drill types the runbook's command.
    /// </summary>
    public const string RestoreDatabaseName = "jobbliggaren_restore";

    private readonly PostgreSqlContainer _source = new PostgreSqlBuilder("postgres:18").Build();
    private readonly PostgreSqlContainer _target = new PostgreSqlBuilder("postgres:18").Build();

    /// <summary>
    /// ADR 0066 — deterministic 32-byte AES-256 master key, shared by both graphs (see the
    /// docblock). Runtime-generated, no literal.
    /// </summary>
    internal static readonly string TestMasterKeyBase64 =
        Convert.ToBase64String([.. Enumerable.Range(0, 32).Select(i => (byte)i)]);

    // #842/#544/#692 — deterministic peppers, each distinct from the master key and from each
    // other so nothing can pass by peppering with the wrong secret. AddPersistence registers all
    // three option types with ValidateDataAnnotations; the source graph resolves the audit one
    // through IAuditTrailEraser inside the hard delete, so it must be a real 32-byte value.
    internal static readonly string TestAuditPepperBase64 =
        Convert.ToBase64String([.. Enumerable.Range(100, 32).Select(i => (byte)i)]);

    internal static readonly string TestWatchPepperBase64 =
        Convert.ToBase64String([.. Enumerable.Range(132, 32).Select(i => (byte)i)]);

    internal static readonly string TestFingerprintPepperBase64 =
        Convert.ToBase64String([.. Enumerable.Range(164, 32).Select(i => (byte)i)]);

    /// <summary>The container the drill dumps FROM. Seeded through production entry points.</summary>
    public PostgreSqlContainer Source => _source;

    /// <summary>The container the drill restores INTO. Never migrated — the dump carries the schema.</summary>
    public PostgreSqlContainer Target => _target;

    /// <summary>
    /// The graph that seeds and hard-deletes: <c>AddPersistence</c> + <c>AddCoreIdentityForWorker</c>.
    /// The second is not optional and not decoration — <c>IAccountHardDeleter</c> is registered
    /// there (<c>DependencyInjection.cs</c>, inside <c>AddCoreIdentityForWorker</c>), not in
    /// <c>AddPersistence</c>, and that port is what produces the drill's erased-user state.
    /// </summary>
    public ServiceProvider SourceServices { get; private set; } = null!;

    /// <summary>
    /// The graph that reads the RESTORED database back through production's own decryption path —
    /// the vacuity guard's instrument. Persistence slice only: it resolves <c>AppDbContext</c>,
    /// <c>IUserDataKeyStore</c> and <c>ICurrentDataOwner</c> and needs nothing else.
    ///
    /// <para>
    /// It is built here, before <c>jobbliggaren_restore</c> exists, and that is safe because
    /// building a service provider opens no connection. The first connection happens when a test
    /// resolves <c>AppDbContext</c> — by which time §5 step 3 has run.
    /// </para>
    /// </summary>
    public ServiceProvider RestoredServices { get; private set; } = null!;

    /// <summary>
    /// The superuser connection string for the target's DEFAULT database — where <c>createdb</c>
    /// is issued from. Distinct from <see cref="RestoredServices"/>'s, which points at
    /// <see cref="RestoreDatabaseName"/>.
    /// </summary>
    public string TargetAdminConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_source.StartAsync(), _target.StartAsync());

        // ── SOURCE: production's Phase A posture, then migrate as jobbliggaren_app ──────────────
        //
        // #1232's reasoning, inherited: a fixture that migrates as the Testcontainers superuser
        // cannot see privilege defects by construction. Here it does a second job as well — it is
        // what makes the source's tables owned by a role the TARGET cluster does not have, which
        // is what turns --no-owner/--no-privileges into an oracle (see the docblock).
        //
        // includeIdentitySchema: this graph migrates AppIdentityDbContext as the app role, so the
        // app role needs USAGE + CREATE on `identity`. Production grants exactly that in Phase A.
        var sourceAppConnectionString = await TestDatabaseProvisioner
            .ProvisionAndGetAppConnectionStringAsync(
                _source.GetConnectionString(), includeIdentitySchema: true);

        SourceServices = BuildGraph(sourceAppConnectionString, withIdentity: true,
            applicationName: "Jobbliggaren.Worker.IntegrationTests.RestoreDrill.Source");

        using (var scope = SourceServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // pg_trgm is required by the job-ad trigram-index migration. Production creates it in
            // `ensure-extensions` mode under MASTER credentials — the app role cannot CREATE
            // EXTENSION — so this runs on the superuser connection, exactly as the sibling
            // dedicated fixtures do.
            await using (var superuser = new NpgsqlConnection(_source.GetConnectionString()))
            {
                await superuser.OpenAsync();
                await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
                await cmd.ExecuteNonQueryAsync();
            }

            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>()
                .Database.MigrateAsync();
        }

        // ── TARGET: nothing. Deliberately. ─────────────────────────────────────────────────────
        //
        // No provisioning, no roles, no migration. §5 restores into a database an operator just
        // created on a machine that has never run this application, and the schema arrives inside
        // the dump. Migrating here would hand the restore a schema it is supposed to bring, and
        // provisioning roles here would delete the oracle the second container exists for.
        TargetAdminConnectionString = _target.GetConnectionString();

        var restoredConnectionString = new NpgsqlConnectionStringBuilder(TargetAdminConnectionString)
        {
            Database = RestoreDatabaseName,
        }.ConnectionString;

        RestoredServices = BuildGraph(restoredConnectionString, withIdentity: false,
            applicationName: "Jobbliggaren.Worker.IntegrationTests.RestoreDrill.Restored");
    }

    private static ServiceProvider BuildGraph(
        string connectionString, bool withIdentity, string applicationName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
                ["FieldEncryption:Provider"] = "Local",
                ["FieldEncryption:LocalMasterKeyBase64"] = TestMasterKeyBase64,
                ["AuditPseudonymization:PepperBase64"] = TestAuditPepperBase64,
                ["CompanyWatchPseudonymization:PepperBase64"] = TestWatchPepperBase64,
                ["CvReviewFingerprintPseudonymization:PepperBase64"] = TestFingerprintPepperBase64,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddPersistence(configuration);

        if (withIdentity)
        {
            services.AddCoreIdentityForWorker(configuration);
        }

        // Host parity: a bare ServiceCollection has no generic host, so register a Test
        // environment explicitly — exactly as WorkerTestFixture and the two plan fixtures do.
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = "Test",
            ApplicationName = applicationName,
            ContentRootPath = AppContext.BaseDirectory,
        });

        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        // Null-guarded: InitializeAsync has network- and SQL-bearing failure points BEFORE either
        // provider is assigned, and an unguarded NullReferenceException here would bury the real
        // error. Same guard, same reason, as JobAdBrowsePlanFixture's.
        if (RestoredServices is not null)
        {
            await RestoredServices.DisposeAsync();
        }

        if (SourceServices is not null)
        {
            await SourceServices.DisposeAsync();
        }

        await Task.WhenAll(_source.StopAsync(), _target.StopAsync());
    }
}
