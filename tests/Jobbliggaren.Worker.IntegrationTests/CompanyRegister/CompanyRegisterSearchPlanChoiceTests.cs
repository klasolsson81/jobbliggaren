using System.Text.RegularExpressions;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// ADR 0119 — the production-cardinality plan-CHOICE guard for the register search's page query:
/// does the planner, given its FULL search space, actually take the plan the materialization rule
/// intends, in BOTH regimes?
///
/// <para>
/// <b>What it adds over the eligibility sibling.</b>
/// <see cref="CompanyRegisterSearchQueryPlanTests"/> proves each axis is EMITTED in the one shape its
/// index can serve, at 2 000 rows under <c>enable_seqscan = off</c> — a PG 17+ prohibition, which
/// makes those pins statistics-independent and is exactly why they were green throughout the period
/// production spent 1 681 ms on the same query. Eligibility ("can the index serve this") and choice
/// ("does the planner pick it at corpus scale") are different guarantees; this file carries the
/// second, at 100 000 rows with NO GUC. That relationship is the #743 ↔ #1013 pattern reproduced,
/// not collapsed: neither pin replaces the other.
/// </para>
///
/// <para>
/// <b>Three claims, one corpus, two probes</b> (the corpus and the reproduction argument live in
/// <see cref="CompanyRegisterPlanFixture"/>):
/// </para>
/// <list type="bullet">
/// <item><b>(a) klustrad sist</b> — a broad, late-sorting name prefix must be served from inside the
/// materialized CTE by the functional prefix index. RED before the fix.</item>
/// <item><b>(b) gles och jämnt utspridd</b> — a kommun holding 20 rows must be served from inside the
/// CTE by the kommun index. RED before the fix.</item>
/// <item><b>(c) the counterweight</b> — browse-all, whose count saturates, must keep the ordered walk
/// with no CTE and no Sort. GREEN on both sides of the fix: it is the pin that goes red the day
/// someone makes the CTE unconditional, which is the one change that would reintroduce the 7 066 ms
/// browse-all shape #875 removed.</item>
/// </list>
///
/// <para>
/// <b>The load-bearing assertion for (a) and (b) is the ORDER BY index's ABSENCE, not the CTE's
/// presence.</b> This is the distinction that makes the file a guard rather than a snapshot. Before
/// the fix there is no CTE in the SQL at all, so "CTE Scan is missing" would be a claim about the
/// query TEXT and would be red even against a corpus that never reproduced the defect. "The ORDER BY
/// index does not drive the scan" is a claim about the PLANNER: it is false before the fix precisely
/// because this corpus reproduces the ordered walk, and true after it because the barrier forces the
/// match set to be collected before it is ordered.
/// </para>
///
/// <para>
/// <b>Two assertion traps, both measured on the dev register before this file was written</b>
/// (2026-07-25) — each would have produced a broken guard:
/// </para>
/// <list type="number">
/// <item><c>ShouldContain("using " + index)</c>, the #1013 form, is WRONG for (a) and (b). Inside the
/// CTE the planner collects the whole match set, which it does with a Bitmap Index Scan — rendered
/// <c>Bitmap Index Scan on ix_…</c>, never <c>using ix_…</c>. The "using" form is correct only for an
/// ordered walk, i.e. claim (c). <see cref="ShouldBeDrivenBy"/> therefore accepts either rendering:
/// the claim is "this index drives the scan", not "the planner chose one particular node type".</item>
/// <item><c>ShouldNotContain("Sort Key:")</c> must NOT be asserted for (a) or (b). The materialized
/// branch DOES sort — sorting a bounded match set is the entire point of the barrier, and the outer
/// <c>ORDER BY</c> is what makes both branches return identical rows in identical order. That
/// negative belongs to (c) alone, where the ordered walk must serve the order.</item>
/// </list>
/// </summary>
[Collection("CompanyRegisterPlan")]
[Trait("Category", "SmokeTest")]
public class CompanyRegisterSearchPlanChoiceTests(CompanyRegisterPlanFixture fixture)
{
    private readonly CompanyRegisterPlanFixture _fixture = fixture;

    private const string NameLowerIndexName = "ix_company_register_company_name_lower";
    private const string KommunIndexName = "ix_company_register_sate_kommun_code";
    private const string OrderByIndexName = "ix_company_register_company_name_organization_number";

    [Fact]
    public async Task BroadLateSortingNamePrefix_MaterializesAndIsServedByThePrefixIndex()
    {
        var criteria = Criteria(name: CompanyRegisterPlanFixture.ProbeNamePrefix);
        var plan = await ExplainItemsAsync(criteria);

        // The barrier held: the match set is collected, then ordered, then cut to 20.
        plan.ShouldContain("CTE Scan", Case.Insensitive, BarrierGoneMessage("(a) klustrad sist", plan));

        // ...and inside it the FUNCTIONAL prefix index does the work. This is also the first time
        // production uses it for this query: it shipped in 20260718191128 and the items query had
        // never touched it, because the walk always looked cheaper to the planner.
        ShouldBeDrivenBy(plan, NameLowerIndexName, "(a) klustrad sist");

        // THE load-bearing claim. Unfixed, this corpus produces
        //   Index Scan using ix_company_register_company_name_organization_number
        //     Filter: lower(company_name) ~~ 'ö%'
        // priced at 66 for LIMIT 20 while actually costing 387,5 ms, because the prefix constrains
        // company_name which IS the sort key: every match sits in ONE contiguous run at the END of
        // the order, so the walk traverses the whole index to reach it. The planner cannot see that
        // — it prices the correct plan at 1 665, i.e. 25x MORE expensive — which is why no
        // statistics fix and no additional index can move it, and why the barrier is the mechanism.
        plan.ShouldNotContain(
            OrderByIndexName,
            Case.Insensitive,
            WalkSurvivedMessage("(a) klustrad sist", plan));
    }

    [Fact]
    public async Task SparseKommun_UnderTheServableCap_MaterializesAndIsServedByTheKommunIndex()
    {
        var criteria = Criteria(kommun: [CompanyRegisterPlanFixture.ProbeKommun]);

        // The branch is reached through the COUNT clause, not the name clause — so this test also
        // proves the count clause is not redundant. Its marginal coverage is exactly this regime.
        var matchCount = await CountAsync(criteria);
        matchCount.ShouldBeLessThan(
            CompanyRegisterSearchCriteria.MaxServableRows(PageSize),
            "The probe kommun's match count must stay BELOW the servable cap, or this test silently "
            + "starts exercising the walk branch and stops testing what it names.");

        var plan = await ExplainItemsAsync(criteria);

        plan.ShouldContain(
            "CTE Scan", Case.Insensitive, BarrierGoneMessage("(b) gles och jämnt utspridd", plan));
        ShouldBeDrivenBy(plan, KommunIndexName, "(b) gles och jämnt utspridd");

        // Unfixed, this corpus produces the ordered walk with a kommun Filter, priced at 567 for
        // LIMIT 20 and actually costing 790,9 ms. The planner has no per-value statistic for a
        // non-MCV kommun, so it prices the probe at the non-MCV AVERAGE (341) instead of its true
        // size (20), and then believes LIMIT 20 will stop after ~1/17 of the index. Production
        // shape: kommun 2403, 153 Active, estimated 785, measured 3 966 ms.
        plan.ShouldNotContain(
            OrderByIndexName,
            Case.Insensitive,
            WalkSurvivedMessage("(b) gles och jämnt utspridd", plan));
    }

    [Fact]
    public async Task BrowseAll_SaturatesTheCap_AndKeepsTheOrderedWalkWithoutMaterializing()
    {
        var criteria = Criteria();

        var matchCount = await CountAsync(criteria);
        matchCount.ShouldBe(
            CompanyRegisterSearchCriteria.MaxServableRows(PageSize),
            "Browse-all must SATURATE the servable cap at this corpus size — that saturation is the "
            + "signal the rule reads as \"we cannot bound this match set\".");

        var plan = await ExplainItemsAsync(criteria);

        // The counterweight: no barrier when the rule says walk. Unconditional materialization would
        // collect and Sort all 100 000 rows to answer LIMIT 20 — the pre-#875 shape, measured at
        // 7 066 ms p95 against ADR 0045's 300 ms budget.
        plan.ShouldNotContain("CTE Scan", Case.Insensitive, OverFiringMessage(plan));

        // Here the strict "using" form IS correct and deliberate: only an ordered Index Scan can
        // SERVE the ordering, and a bitmap scan never can.
        plan.ShouldContain(
            "using " + OrderByIndexName,
            Case.Insensitive,
            $"Browse-all no longer walks {OrderByIndexName} in order.{Environment.NewLine}"
            + $"Plan:{Environment.NewLine}{plan}");

        // Reaching the index is not the guarantee — WALKING it in order and stopping at LIMIT 20 is.
        plan.ShouldNotContain(
            "Sort Key:",
            Case.Insensitive,
            "Browse-all reaches the index but STILL sorts, so the whole active set is being ordered "
            + $"per page.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    private const int PageSize = 20;

    private static CompanyRegisterSearchCriteria Criteria(
        string? name = null, string[]? kommun = null) =>
        CompanyRegisterSearchCriteria.FromTrusted(
            [], kommun ?? [], name, organizationNumber: null, page: 1, pageSize: PageSize);

    /// <summary>
    /// Accepts either rendering of "this index drives the scan": <c>Index Scan using X</c> /
    /// <c>Index Only Scan using X</c> for an ordered walk, and <c>Bitmap Index Scan on X</c> for the
    /// set-collection the CTE performs. Deliberately NOT a bare <c>ShouldContain(indexName)</c>: the
    /// name can appear in a plan without driving anything, and deliberately not the node-specific
    /// form either, which would go red for a BETTER plan (a plain ordered Index Scan inside the CTE
    /// would satisfy this claim just as well).
    /// </summary>
    private static void ShouldBeDrivenBy(string plan, string indexName, string claim)
    {
        Regex.IsMatch(
                plan,
                @"(Index Scan using|Index Only Scan using|Bitmap Index Scan on)\s+" + Regex.Escape(indexName),
                RegexOptions.IgnoreCase)
            .ShouldBeTrue(
                $"Claim {claim}: {indexName} is no longer the scan DRIVER inside the materialized "
                + "CTE. The rows may still be correct — the CTE has no LIMIT, so both branches always "
                + "return the same rows in the same order — but the match set is now being collected "
                + "some other way, and the whole latency argument for the barrier rests on this index "
                + $"serving it.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    /// <summary>
    /// <c>matchCount</c> comes from EXECUTING production's own
    /// <see cref="CompanyRegisterSearchQuery.BuildCountCommand"/> — never hand-typed. A constant here
    /// would let the guard pass while production took the other branch: a pin that measures itself.
    /// </summary>
    private async Task<int> CountAsync(CompanyRegisterSearchCriteria criteria)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var connection = await OpenAsync(scope);

        await using var cmd = CompanyRegisterSearchQuery.BuildCountCommand(connection, criteria);
        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>EXPLAIN</c> (not <c>EXPLAIN ANALYZE</c> — this is a PLANNER assertion; row-level truth is
    /// the semantic suite's job) of the EXACT command production emits, with <b>NO GUC</b>: a choice
    /// made inside <c>enable_seqscan = off</c> is not production's choice. No transaction either,
    /// because there is no <c>SET LOCAL</c> to scope.
    /// </summary>
    private async Task<string> ExplainItemsAsync(CompanyRegisterSearchCriteria criteria)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var connection = await OpenAsync(scope);

        var matchCount = await CountAsync(criteria);

        await using var cmd =
            CompanyRegisterSearchQuery.BuildItemsCommand(connection, criteria, matchCount);
        cmd.CommandText = "EXPLAIN " + cmd.CommandText;

        var lines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(reader.GetString(0));

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<NpgsqlConnection> OpenAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static string BarrierGoneMessage(string claim, string plan) =>
        $"Claim {claim}: there is no CTE Scan node, so the materialization barrier is GONE — either "
        + "the rule stopped firing for this axis (a composition change: see "
        + "CompanyRegisterSearchQueryCompositionTests) or MATERIALIZED was dropped from the CTE, "
        + "which lets Postgres inline a single-reference CTE and restores the ordered walk verbatim."
        + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}";

    private static string WalkSurvivedMessage(string claim, string plan) =>
        $"Claim {claim}: {OrderByIndexName} is back in the plan, i.e. the planner is once again "
        + "answering LIMIT 20 by walking the sort order and filtering — the defect this rule exists "
        + "to remove (measured on the 743 654-Active dev register: a broad late prefix 2 141 ms, a "
        + "153-row kommun 3 966 ms, both WITH correct statistics). Note that correct statistics are "
        + "not a fix here and never were: ANALYZE fixed a SELECTIVE prefix (2 084 -> 0,153 ms) and "
        + "made the sparse kommun case sixteen times WORSE (244 -> 3 966 ms), because a "
        + "better-informed planner commits harder to a walk whose depth is inversely proportional to "
        + $"a match count it now estimates accurately.{Environment.NewLine}Plan:"
        + $"{Environment.NewLine}{plan}";

    private static string OverFiringMessage(string plan) =>
        "Browse-all is now MATERIALIZING. The rule has become unconditional (or the cap moved), so "
        + "every unfiltered page view collects and sorts the entire register to answer LIMIT 20 — "
        + "the pre-#875 shape, 7 066 ms p95 against a 300 ms budget. The two clauses of the rule are "
        + "narrow on purpose: a big kommun walks in 9,5 ms and would cost 50 ms materialized."
        + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}";
}
