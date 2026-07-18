using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — THE PINS for <see cref="CompanyRegisterSearchQuery"/>: the search
/// predicate's three index-served shapes, each pinned POSITIVELY BY INDEX NAME on the EXACT
/// command production emits (the sibling <c>CompanyWatchBrowseQueryPlanTests</c> discipline —
/// its docblock carries the full argument for every rule this file follows: positive-on-name
/// never no-Seq-Scan, factories-not-retyped-SQL, eligibility-vs-choice instrumentation).
///
/// <para>
/// What each pin claims:
/// <list type="bullet">
/// <item><b>SNI GIN (eligibility, <c>enable_seqscan=off</c>):</b> the axis is emitted as the
/// array-overlap <c>&amp;&amp;</c> — the only GIN-servable shape. The naive LINQ form compiles
/// to an <c>unnest</c> the index cannot answer while every semantic test stays green.</item>
/// <item><b>Name-prefix functional index (eligibility, <c>enable_seqscan=off</c>):</b> the name
/// axis is emitted as <c>lower(company_name) LIKE …</c> — the only shape
/// <c>ix_company_register_company_name_lower</c> (expression + <c>text_pattern_ops</c>) can
/// serve. The naive <c>company_name ILIKE @p</c> returns the SAME rows with no index at all —
/// the exact vacuous-guarantee class (#805-3/#842), mutation-verified.</item>
/// <item><b>Browse-all ordered walk (plan CHOICE, no GUC):</b> the no-axis default must walk
/// <c>ix_company_register_company_name_organization_number</c> in order and stop at LIMIT — a
/// Sort node over the whole 1,07M-row register is the 7 066 ms shape #875 measured.</item>
/// <item><b>org.nr PK (eligibility):</b> equality against the primary key.</item>
/// </list>
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyRegisterSearchQueryPlanTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private const string GinIndexName = "ix_company_register_sni_codes_gin";
    private const string OverlapOperator = "&&";
    private const string NameLowerIndexName = "ix_company_register_company_name_lower";
    private const string OrderByIndexName = "ix_company_register_company_name_organization_number";
    private const string PkIndexName = "pk_company_register";

    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

    // Every seeded row sits in this kommun and is Active → those predicates have selectivity
    // ≈ 1.0 and are worthless to the planner; only the probed axis discriminates (the sibling's
    // seeding rationale, verbatim).
    private const string SeededKommun = "0180";
    private const string ProbeSni = "62010";
    private const string FillerSni = "99999";
    private const string ProbeNamePrefix = "Volvo";
    private const int SeededRows = 2000;
    private const int ProbeMatches = 2;

    [Fact]
    public async Task SniAxis_ItemsQuery_UsesTheSniGinIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        var plan = await ExplainAsync(
            ctx.Db,
            conn => CompanyRegisterSearchQuery.BuildItemsCommand(
                conn, Criteria(sni: [ProbeSni])),
            ct);

        plan.ShouldContain(
            GinIndexName,
            customMessage:
                "The search's SNI axis is no longer served by the GIN index — the predicate has "
                + "stopped being emitted as the array-overlap operator (the naive LINQ form "
                + "compiles to an unnest subquery no GIN can answer) while every semantic test "
                + $"stays green.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
        plan.ShouldContain(
            OverlapOperator,
            customMessage:
                "The SNI plan no longer carries the array-overlap operator — a switch to @> "
                + "containment would also be GIN-servable but is the WRONG semantics "
                + $"(partial overlap must match).{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task NamePrefix_ItemsQuery_UsesTheLowerPatternOpsIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        var plan = await ExplainAsync(
            ctx.Db,
            conn => CompanyRegisterSearchQuery.BuildItemsCommand(
                conn, Criteria(name: ProbeNamePrefix)),
            ct);

        plan.ShouldContain(
            NameLowerIndexName,
            customMessage:
                "The name-prefix search no longer uses " + NameLowerIndexName + " — the "
                + "predicate has drifted from the ONE shape the functional index serves "
                + "(lower(company_name) LIKE, literal-escaped, ESCAPE '\\'). The usual causes: "
                + "ILIKE on the raw column, lower() dropped on one side, or the pattern no "
                + "longer a plan-time-derivable prefix. The search still returns the right rows "
                + "via a sequential scan over 1,07M, so every semantic test stays green while "
                + $"the migration's index is cosmetic.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task BrowseAll_WalksTheOrderByIndexInOrder_AndStopsEarly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        // NO enable_seqscan=off: this pin claims a plan CHOICE (the #875 instrumentation rule —
        // a choice made inside a prohibition is not production's choice). Browse-all matches
        // EVERY row, which is exactly the shape where a Sort over the whole match set is the
        // 7 066 ms regression and the ordered walk + LIMIT stop is the fix.
        var plan = await ExplainAsync(
            ctx.Db,
            conn => CompanyRegisterSearchQuery.BuildItemsCommand(conn, Criteria()),
            ct,
            disableSeqScan: false);

        plan.ShouldContain(
            OrderByIndexName,
            customMessage:
                "The browse-all default no longer walks " + OrderByIndexName + " in order — it "
                + "is sorting the whole register to answer LIMIT 20 (the pre-#875 7 066 ms "
                + $"shape).{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
        plan.ShouldNotContain(
            "Sort Key:",
            customMessage:
                "The browse-all plan still SORTS — reaching the index is not the guarantee, "
                + "walking it in order and stopping at LIMIT is."
                + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task OrgNrAxis_ItemsQuery_UsesThePrimaryKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        var plan = await ExplainAsync(
            ctx.Db,
            conn => CompanyRegisterSearchQuery.BuildItemsCommand(
                conn, Criteria(orgnr: "5500000001")),
            ct);

        plan.ShouldContain(
            PkIndexName,
            customMessage:
                "The org.nr equality search no longer uses the primary key — the predicate has "
                + $"drifted from `organization_number = @orgnr`.{Environment.NewLine}Plan:"
                + $"{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task CountQuery_SniAxis_UsesTheSniGinIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        // The count is a SECOND command text and therefore a SECOND plan — pinning only the
        // items query would leave it free to regress into a full scan on its own.
        var plan = await ExplainAsync(
            ctx.Db,
            conn => CompanyRegisterSearchQuery.BuildCountCommand(
                conn, Criteria(sni: [ProbeSni])),
            ct);

        plan.ShouldContain(
            GinIndexName,
            customMessage:
                "The search COUNT no longer uses the GIN index — it can regress independently "
                + $"of the items query.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task MagnitudeQuery_NamePrefix_UsesTheLowerPatternOpsIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        // Same statement text as the count today — pinned SEPARATELY for the sibling's stated
        // reason: that reuse is one refactor away from being false, and a magnitude regressed
        // to a full scan walks 1,07M rows for every headline.
        var plan = await ExplainAsync(
            ctx.Db,
            conn => CompanyRegisterSearchQuery.BuildMagnitudeCommand(
                conn, Criteria(name: ProbeNamePrefix), ceiling: 10_000),
            ct);

        plan.ShouldContain(
            NameLowerIndexName,
            customMessage:
                "The magnitude count for a name-prefix search no longer uses "
                + NameLowerIndexName
                + $".{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    private static CompanyRegisterSearchCriteria Criteria(
        string[]? sni = null, string? name = null, string? orgnr = null) =>
        CompanyRegisterSearchCriteria.FromTrusted(
            sni ?? [], [], name, orgnr, page: 1, pageSize: 20);

    private static async Task<string> ExplainAsync(
        AppDbContext db,
        Func<NpgsqlConnection, NpgsqlCommand> build,
        CancellationToken ct,
        bool disableSeqScan = true)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // SET LOCAL needs a transaction block, and scoping it there means the GUC cannot leak
        // into a sibling test on this shared connection. On PG 17+ enable_seqscan=off is an
        // effective PROHIBITION (disabled_nodes), which is FINE for the eligibility pins and
        // exactly why the browse-all CHOICE pin must not use it (the sibling's truth-sync).
        await using var tx = await connection.BeginTransactionAsync(ct);

        if (disableSeqScan)
        {
            await using var guc = connection.CreateCommand();
            guc.Transaction = tx;
            guc.CommandText = "SET LOCAL enable_seqscan = off;";
            await guc.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = build(connection);
        cmd.Transaction = tx;
        // EXPLAIN, not EXPLAIN ANALYZE — a PLANNER assertion; row truth is the semantic suite's.
        cmd.CommandText = "EXPLAIN " + cmd.CommandText;

        var lines = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                lines.Add(reader.GetString(0));
        }

        await tx.RollbackAsync(ct);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// One seed serves every pin: only ~0,1 % of rows carry the probe SNI and only 2 rows carry
    /// the probe name prefix (both axes selective → their index paths win unambiguously under
    /// the eligibility GUC), while EVERY row matches status (selectivity 1.0 — worthless to the
    /// planner, exactly like production's Active-dominated register). Names are deterministically
    /// shuffled so <c>company_name</c> is not correlated with insertion order (the sibling's
    /// planner-flattery note). ANALYZE is mandatory — TRUNCATE wipes the statistics.
    /// </summary>
    private async Task<ScopedContext> SeededContextAsync(CancellationToken ct)
    {
        var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);

        var entries = Enumerable.Range(0, SeededRows)
            .Select(i => new ScbCompanyRegisterEntry
            {
                OrganizationNumber = $"55{i:D8}",
                Name = i < ProbeMatches
                    ? $"{ProbeNamePrefix} {i} AB"
                    : $"Företag {(i * 7919) % SeededRows:D4} AB",
                SeatMunicipalityCode = SeededKommun,
                SeatMunicipalityName = "Stockholm",
                SniCodes = [i < ProbeMatches ? ProbeSni : FillerSni],
                HasAdvertisingBlock = false,
                ScbStatusRaw = "1",
                Status = CompanyRegisterStatus.Active,
            })
            .ToList();

        await new ScbCompanyRegisterStore(db).UpsertBatchAsync(entries, T0, ct);
        await db.Database.ExecuteSqlRawAsync("ANALYZE company_register;", ct);

        return new ScopedContext(scope, db);
    }

    private sealed class ScopedContext(AsyncServiceScope scope, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AsyncServiceScope Scope { get; } = scope;
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
}
