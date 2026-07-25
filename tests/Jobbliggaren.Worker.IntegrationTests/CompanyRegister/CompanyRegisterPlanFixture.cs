using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// ADR 0119 — a DEDICATED, single-owner <c>postgres:18</c> that owns ONE corpus for
/// <see cref="CompanyRegisterSearchPlanChoiceTests"/>, the register search's plan-CHOICE guard.
/// Mirrors <c>JobAdBrowsePlanFixture</c> (#1013), which carries the same argument for <c>/jobb</c>.
///
/// <para>
/// <b>Why its own container and not the serial <c>[Collection("Worker")]</c> one.</b> A plan-CHOICE
/// assertion runs with NO GUC: it hands the cost-based planner its whole search space and then
/// asserts which plan comes back. That is deterministic only while the test owns the table's
/// STATISTICS at EXPLAIN time. In the shared Worker container <c>company_register</c> is seeded and
/// TRUNCATEd by at least three other classes (the eligibility sibling, the criterion browse pins and
/// its 200 000-row generic-plan characterisation), so determinism there would rest on the collection
/// staying serial, on class ordering, and on nobody adding a fourth seeder — none of which is
/// enforced by anything. ADR 0045 Beslut 5 is explicit that a flaky perf-gate is worse than no
/// perf-gate, so the ~10 s container converts three assumptions into a structural property.
/// </para>
///
/// <para>
/// <b>Deliberately lighter than <c>WorkerTestFixture</c>:</b> the persistence slice only —
/// one Postgres, <c>pg_trgm</c> (required by the job-ad trigram migration), and
/// <c>AppDbContext.MigrateAsync</c>. No Mediator, no identity DB, no email, no matching engine, no
/// CV pipeline. The guard needs a migrated schema and a raw connection to EXPLAIN, nothing else.
/// The migrated schema matters twice over: <c>ix_company_register_company_name_lower</c> is a
/// FUNCTIONAL index invisible to the EF model, created only by
/// <c>20260718191128_AddCompanyRegisterNameSearchIndex</c>, and claim (a) is a statement about it.
/// </para>
///
/// <para>
/// <b>The corpus lives HERE, not in the test class</b> — three claims share ONE seed (two probes
/// plus a no-probe regime), so the corpus is the fixture's responsibility and seeding it per test
/// would triple the class's wall clock for nothing.
/// </para>
/// </summary>
public sealed class CompanyRegisterPlanFixture : IAsyncLifetime
{
    /// <summary>
    /// MEASURED FLOOR, not a guess (ADR 0119; dev DB, 2026-07-25). The sparse-and-spread claim (b)
    /// stops reproducing BELOW this: the planner has no per-value statistic for a non-MCV kommun, so
    /// it prices the probe at the non-MCV AVERAGE — 341 rows here against a true 20 — and it takes
    /// the ordered walk only while that estimate stays above the walk↔bitmap crossover (≈ √(0,68·N)).
    /// At N = 50 000 the average falls to 171, the planner picks Bitmap + Sort UNFIXED, and the guard
    /// would be green on both sides of the fix — vacuous, the #805-3/#842 class this fixture exists
    /// to avoid. At N = 100 000 the unfixed plan is the ordered walk and costs 790,9 ms against
    /// 15,4 ms materialized. Raising N is safe (the margin grows: the estimate scales with N while
    /// the crossover scales with √N); LOWERING it silently returns the guard to decoration.
    /// </summary>
    public const int TotalRows = 100_000;

    /// <summary>
    /// Claim (b)'s probe — <b>gles och jämnt utspridd</b>: a kommun holding 20 of 100 000 rows
    /// (0,02 %), i.e. matches scattered evenly through the sort order so the walk must traverse
    /// essentially the whole index to collect 20 of them. Production shape: kommun 2403 Bjurholm,
    /// 153 Active of 743 654, estimated at 785 and measured at 3 966 ms.
    /// </summary>
    public const string ProbeKommun = "9999";

    /// <summary>
    /// Claim (a)'s probe — <b>klustrad sist</b>: a prefix that is BROAD (3 448 rows, 3,45 %) and
    /// sorts AFTER every other row, because <c>company_name</c> carries the <c>swedish</c> ICU
    /// collation where Ö follows Z (ADR 0110). Both conditions are required together: the planner
    /// walks only when it believes the match set is big enough for <c>LIMIT 20</c> to stop early, and
    /// that belief is only WRONG when the matches sit at the end of the order. A SELECTIVE prefix
    /// reproduces nothing once statistics exist — the planner prices a 2-row match set correctly at
    /// any N, which is why four earlier reproduction attempts at 50 k/200 k/500 k/1,07 M rows all
    /// failed while holding the probe selective.
    /// </summary>
    public const string ProbeNamePrefix = "Ö";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    public ServiceProvider Services { get; private set; } = null!;

    // ADR 0066 — deterministic 32-byte AES-256 master key. AddPersistence wires the two
    // field-encryption interceptors and resolves LocalDataKeyProvider, which validates this on first
    // IOptions.Value access. company_register carries NO DEK column (it is a public-register replica)
    // and this guard never SaveChanges — it bulk-INSERTs via raw SQL and EXPLAINs — so the key exists
    // purely so the DbContext can be constructed. Runtime-generated, no literal.
    internal static readonly string TestMasterKeyBase64 =
        Convert.ToBase64String([.. Enumerable.Range(0, 32).Select(i => (byte)i)]);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["FieldEncryption:Provider"] = "Local",
                ["FieldEncryption:LocalMasterKeyBase64"] = TestMasterKeyBase64,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddPersistence(configuration);

        // Host parity: a bare ServiceCollection has no generic host, so register a Test environment
        // explicitly — exactly as WorkerTestFixture and JobAdBrowsePlanFixture do.
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = "Test",
            ApplicationName = "Jobbliggaren.Worker.IntegrationTests",
            ContentRootPath = AppContext.BaseDirectory,
        });

        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Required by the job-ad trigram-index migration (created by Jobbliggaren.Migrate
        // ensure-extensions in production; the Testcontainers superuser may CREATE EXTENSION).
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await db.Database.MigrateAsync();
        await SeedProductionRegimeAsync(db);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.StopAsync();
    }

    /// <summary>
    /// ONE corpus, TWO probes (ADR 0119): a late-sorting broad name cluster for claim (a) and a
    /// sparse kommun for claim (b). They cannot interfere — (a) probes a name prefix, (b) a kommun
    /// code, and (c) probes neither. Bulk INSERT rather than the domain store: this seeds a PLANNER
    /// regime, not a semantic fixture, and <c>company_register</c> has no encrypted column, so a raw
    /// insert is faithful and fast.
    ///
    /// <para>
    /// Three properties of the shape are load-bearing, each measured rather than assumed:
    /// </para>
    /// <list type="number">
    /// <item><b>Near-uniform kommun sizes (292 codes, ~343 rows each) plus ONE tiny probe.</b> The
    /// planner estimates a non-MCV value at the average of the non-MCV values, so a uniform
    /// distribution maximises that average — which is what keeps the estimate (341) above the
    /// crossover while the truth (20) is far below it. A SKEWED corpus fails here for a subtle
    /// reason: the big kommuner would absorb the mass into the MCV list, dragging the non-MCV
    /// average down toward the probe's true size and letting the planner get it right.</item>
    /// <item><b>Names spread over all 29 Swedish first letters.</b> Statistics for the
    /// <c>lower(company_name)</c> expression index are what make the prefix estimate honest. With
    /// only two name shapes the histogram has no letter boundaries and the estimate for the probe
    /// prefix collapses (MEASURED: 10 rows against a true 1 000), so the planner chooses Bitmap +
    /// Sort even UNFIXED and claim (a) reproduces nothing. The letter spread is not decoration; it
    /// is the difference between a red guard and a vacuous one.</item>
    /// <item><b><c>company_name</c> decorrelated from insertion order</b> (bijective
    /// <c>(i·7919) mod N</c>, gcd 1). A correlated column makes the planner price the ORDER BY
    /// index's heap fetches as sequential, i.e. flatters the exact plan under test.</item>
    /// </list>
    ///
    /// <para>
    /// <b><c>ANALYZE</c> is mandatory for two independent reasons</b> and neither is hygiene. First,
    /// TRUNCATE wipes the statistics and a statistics-free planner makes an arbitrary choice — a
    /// flaky pin. Second, and subtler: <c>company_register</c> was measured in production-dev to have
    /// NEVER been analysed, and without ANALYZE this guard would reproduce that MISSING-STATISTICS
    /// defect instead of the CLUSTERING one it names. It would then be red before the fix for the
    /// wrong reason, and would flip meaning the day the ANALYZE-after-sync fix lands. A guard that is
    /// red for the wrong reason is not a stricter guard; it has stopped measuring what it claims.
    /// </para>
    /// </summary>
    private static async Task SeedProductionRegimeAsync(AppDbContext db)
    {
        db.Database.SetCommandTimeout(300);

        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;");

        var seed =
            "INSERT INTO company_register ("
            + "organization_number, company_name, sate_kommun_code, sate_kommun_name, "
            + "sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at) "
            + "SELECT lpad(i::text, 10, '0'), "
            // First letter cycles the 29-letter Swedish alphabet; rows landing on the last letter
            // form the late-sorting probe cluster. The numeric tail is decorrelated from i.
            + "upper(substr('abcdefghijklmnopqrstuvwxyzåäö', 1 + ((i * 7919) % 29), 1)) "
            + "  || 'foretag ' || ((i * 7919) % " + TotalRows + ") || ' AB', "
            + "CASE WHEN i < " + ProbeKommunRows + " THEN '" + ProbeKommun + "' "
            + "     ELSE lpad((1000 + ((i * 7919) % 292))::text, 4, '0') END, "
            + "'Ort', ARRAY['62010'], false, '1', 'Active', now(), now() "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(seed);

        await db.Database.ExecuteSqlRawAsync("ANALYZE company_register;");
    }

    /// <summary>
    /// 20 rows — far below the non-MCV average the planner will price this kommun at, which IS the
    /// defect. Every other row is <c>Active</c> too, so <c>status</c> has selectivity ≈ 1 and is
    /// worthless to the planner, exactly as in the Active-dominated production register.
    /// </summary>
    private const int ProbeKommunRows = 20;
}
