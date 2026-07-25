using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// The materialization RULE's truth table — <c>CommandText</c> only, no planner and no database
/// (an unopened <see cref="NpgsqlConnection"/> can still create commands, so this costs
/// microseconds). Infrastructure internals are tested from this project by precedent
/// (<c>ScbPartitionPlannerTests</c>, <c>ScbCompanyRegisterClientTests</c>).
///
/// <para>
/// <b>Why this instrument and not the plan guard.</b> The rule is a function of
/// <c>(criteria, matchCount)</c>, and "did the rule fire for this axis combination" is a
/// COMPOSITION claim: it needs no statistics, no cardinality and no cost model, so pinning it in
/// EXPLAIN would be both slower and weaker (a plan test cannot tell "the CTE is absent because the
/// rule said walk" from "the CTE is absent because someone deleted the rule"). What genuinely
/// needs a planner — does it then pick the selective index INSIDE the CTE, and does it still walk
/// the ORDER BY index when the rule says walk — lives in
/// <c>CompanyRegisterSearchPlanChoiceTests</c> at production cardinality. Two claims, two
/// instruments (ADR 0119).
/// </para>
///
/// <para>
/// <b>The structural tests are the guard against the rejected "bound the CTE with an inner
/// LIMIT" variant</b> (ADR 0119's E1, the #805-3 class): an unordered <c>LIMIT</c> inside the CTE
/// would take an arbitrary subset and the outer <c>ORDER BY</c> would then order the wrong rows —
/// silently wrong rows, page-dependent, with every semantic test green. Latency can be measured;
/// that cannot, so it is pinned structurally here.
/// </para>
/// </summary>
public class CompanyRegisterSearchQueryCompositionTests
{
    /// <summary>Which axes the probed criteria carries. A criteria object cannot be an
    /// <c>InlineData</c> argument, so the theory names the axis and <see cref="Criteria"/>
    /// builds it.</summary>
    public enum SearchAxis
    {
        NameOnly,
        KommunOnly,
        SniOnly,
        OrgNr,
        BrowseAll,
        NameAndKommun,
    }

    private const int PageSize = 20;

    private const string MaterializedPrefix = "WITH m AS MATERIALIZED (";

    private const string Columns =
        "SELECT organization_number, company_name, sate_kommun_code, sate_kommun_name, sni_codes ";

    /// <summary>
    /// The saturation point, taken from the SAME knowledge piece the rule and
    /// <c>BuildCountCommand</c> use — never a re-typed 2 000. A test that hard-codes the cap
    /// passes while production switches branches somewhere else.
    /// </summary>
    private static int Cap => CompanyRegisterSearchCriteria.MaxServableRows(PageSize);

    /// <summary>
    /// ADR 0119's truth table, which is also the rule's documentation. Two cost structures:
    /// a name prefix is <b>klustrad sist</b> (walk depth is set by alphabet position and is
    /// decoupled from the match count, so nothing available at compose time bounds it —
    /// materialize unconditionally), while kommun/SNI/org.nr are <b>gles och jämnt utspridd</b>
    /// (depth ≈ N_active × pageSize / matches, so the count IS the sufficient statistic —
    /// materialize only below saturation).
    /// </summary>
    [Theory]
    // The name axis materializes at ANY count, saturation included: a broad prefix's count
    // saturates (measured `a% s% h% n% t% v% w% y% å%`, 692–2 141 ms unfixed) and it is exactly
    // those the count clause cannot rescue, because the count carries no positional information.
    [InlineData(SearchAxis.NameOnly, 1, true)]
    [InlineData(SearchAxis.NameOnly, 2000, true)]
    [InlineData(SearchAxis.NameAndKommun, 2000, true)]
    // Sparse-and-spread axes: bounded ⇒ materialize; saturated ⇒ keep the walk. The saturated
    // branch is load-bearing, not a default — a big kommun walks in 9,5 ms (Göteborg) and 0,8 ms
    // (Stockholm) and would cost 50 ms / 96 ms materialized.
    [InlineData(SearchAxis.KommunOnly, 1999, true)]
    [InlineData(SearchAxis.KommunOnly, 2000, false)]
    [InlineData(SearchAxis.SniOnly, 1999, true)]
    [InlineData(SearchAxis.SniOnly, 2000, false)]
    [InlineData(SearchAxis.OrgNr, 1, true)]
    [InlineData(SearchAxis.BrowseAll, 2000, false)]
    // Browse-all BELOW the cap materializes — it is not an axis, it is a count. Without this row an
    // "materialize only when some axis is present" mutation (exactly the axis-list design ADR 0119
    // rejected, and which regresses every small kommun) passes the whole table.
    [InlineData(SearchAxis.BrowseAll, 1999, true)]
    public void BuildItemsCommand_MaterializesIffTheMatchSetIsBounded(
        SearchAxis axis, int matchCount, bool expectMaterialized)
    {
        // The InlineData counts are written against the cap this page size derives; if MaxPage or
        // MaxPageSize ever moves, this fails loudly here instead of silently re-labelling rows.
        Cap.ShouldBe(
            2000,
            "This theory's matchCount arguments are written against MaxServableRows(20) = 2 000. A "
            + "paging cap change silently re-labels every row above, so it fails here first.");

        using var connection = new NpgsqlConnection();
        using var cmd = CompanyRegisterSearchQuery.BuildItemsCommand(
            connection, Criteria(axis), matchCount);

        var sql = Normalize(cmd.CommandText);

        sql.StartsWith(MaterializedPrefix, StringComparison.Ordinal).ShouldBe(
            expectMaterialized,
            $"Axis {axis} at matchCount {matchCount} took the wrong branch. The rule is "
            + "`NamePrefix is not null OR matchCount < MaxServableRows(pageSize)` — one clause per "
            + "cost structure, and neither is redundant (the name clause rescues broad prefixes "
            + "whose counts saturate; the count clause rescues sparse axes such as a small "
            + $"kommun).{Environment.NewLine}SQL:{Environment.NewLine}{sql}");
    }

    /// <summary>
    /// The cap SCALES with the caller's page size — <c>pageSize</c> is client-supplied (validated
    /// ≤ <see cref="CompanyRegisterSearchCriteria.MaxPageSize"/>), so the rule reads
    /// <c>MaxServableRows(criteria.PageSize)</c> and NOT a fixed 2 000. Without these rows,
    /// replacing that call with the literal 2 000 is a green mutation: at <c>pageSize = 100</c> the
    /// real cap is 10 000, so a 2 000-match search would take the WALK branch when the rule says
    /// materialize, and at <c>pageSize = 1</c> the cap is 100, so a 100-match search would
    /// materialize when the rule says walk. Both directions are pinned.
    /// </summary>
    [Theory]
    [InlineData(100, 2000, true)]
    [InlineData(100, 10_000, false)]
    [InlineData(1, 99, true)]
    [InlineData(1, 100, false)]
    public void BuildItemsCommand_ReadsTheCapFromTheCallersPageSize_NotAFixedTwoThousand(
        int pageSize, int matchCount, bool expectMaterialized)
    {
        var criteria = CompanyRegisterSearchCriteria.FromTrusted(
            [], ["2403"], null, null, page: 1, pageSize: pageSize);

        using var connection = new NpgsqlConnection();
        using var cmd = CompanyRegisterSearchQuery.BuildItemsCommand(connection, criteria, matchCount);

        Normalize(cmd.CommandText).StartsWith(MaterializedPrefix, StringComparison.Ordinal).ShouldBe(
            expectMaterialized,
            $"At pageSize {pageSize} the servable cap is "
            + $"{CompanyRegisterSearchCriteria.MaxServableRows(pageSize)}, so matchCount "
            + $"{matchCount} must {(expectMaterialized ? "" : "not ")}materialize. The rule must "
            + "read the cap from the CALLER's page size, never a fixed 2 000.");
    }

    [Theory]
    [InlineData(SearchAxis.NameOnly)]
    [InlineData(SearchAxis.KommunOnly)]
    [InlineData(SearchAxis.OrgNr)]
    public void BuildItemsCommand_MaterializedBranch_KeepsOrderingAndPaginationOutsideTheCte(
        SearchAxis axis)
    {
        using var connection = new NpgsqlConnection();
        using var cmd = CompanyRegisterSearchQuery.BuildItemsCommand(connection, Criteria(axis), 1);

        var sql = Normalize(cmd.CommandText);
        sql.ShouldStartWith(MaterializedPrefix);

        // Exactly one of each, and the single ORDER BY/LIMIT/OFFSET sits at the very END of the
        // statement — together that proves none of them is inside the CTE. This is E1's refusal
        // made mechanical: an inner LIMIT selects an arbitrary subset which the outer ORDER BY
        // then orders, i.e. WRONG ROWS per page rather than slow ones.
        Occurrences(sql, "ORDER BY").ShouldBe(1, WrongShapeMessage(sql));
        Occurrences(sql, "LIMIT").ShouldBe(1, WrongShapeMessage(sql));
        Occurrences(sql, "OFFSET").ShouldBe(1, WrongShapeMessage(sql));
        sql.TrimEnd().ShouldEndWith(OrderAndPaginateTail, customMessage: WrongShapeMessage(sql));

        // The reader reads BY ORDINAL, so the outer projection must repeat the CTE's columns in
        // the same order — never `SELECT *`.
        sql.ShouldContain(Columns + "FROM m");
    }

    [Fact]
    public void BuildItemsCommand_BothBranches_CarryTheIdenticalPredicateAndParameters()
    {
        // Kommun-only, because it is the one axis the rule sends BOTH ways: below the cap it
        // materializes, at the cap it walks. Same criteria, same rows, same order — the branch is
        // a performance decision and can never be a correctness one. That property is what makes a
        // planner directive acceptable in this query at all, so it is pinned rather than argued.
        var criteria = Criteria(SearchAxis.KommunOnly);
        using var connection = new NpgsqlConnection();

        using var materialized =
            CompanyRegisterSearchQuery.BuildItemsCommand(connection, criteria, Cap - 1);
        using var walk = CompanyRegisterSearchQuery.BuildItemsCommand(connection, criteria, Cap);

        var walkSql = Normalize(walk.CommandText);
        walkSql.ShouldStartWith(Columns);

        // The predicate is not re-typed here: it is whatever the walk branch emitted between its
        // projection and its ORDER BY tail. A hand-copied WHERE would drift from production and
        // then agree with itself.
        var predicate = walkSql[Columns.Length..].Replace(OrderAndPaginateTail, string.Empty, StringComparison.Ordinal).TrimEnd();
        predicate.ShouldContain("WHERE status = @status");
        predicate.ShouldContain("AND sate_kommun_code = ANY(@kommun)");

        Normalize(materialized.CommandText).ShouldContain(
            predicate,
            customMessage:
                "The materialized branch's predicate has drifted from the walk branch's. Both are "
                + "composed by ComposeFromWhere for exactly this reason — if they can differ, the "
                + "two branches can return different ROWS and the branch choice stops being a "
                + "performance-only decision.");

        ParameterNames(materialized).ShouldBe(ParameterNames(walk));
    }

    private static readonly string OrderAndPaginateTail = Normalize(
        """

        ORDER BY company_name, organization_number
        LIMIT @limit OFFSET @offset;
        """);

    private static CompanyRegisterSearchCriteria Criteria(SearchAxis axis) => axis switch
    {
        SearchAxis.NameOnly => Build(name: "Volvo"),
        SearchAxis.KommunOnly => Build(kommun: ["2403"]),
        SearchAxis.SniOnly => Build(sni: ["62010"]),
        SearchAxis.OrgNr => Build(orgnr: "5560125790"),
        SearchAxis.BrowseAll => Build(),
        SearchAxis.NameAndKommun => Build(name: "Volvo", kommun: ["1480"]),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    private static CompanyRegisterSearchCriteria Build(
        string[]? sni = null, string[]? kommun = null, string? name = null, string? orgnr = null) =>
        CompanyRegisterSearchCriteria.FromTrusted(
            sni ?? [], kommun ?? [], name, orgnr, page: 1, pageSize: PageSize);

    /// <summary>
    /// Newline-normalized: the composition mixes C# raw string literals (which carry the SOURCE
    /// file's line endings — CRLF in this repo on Windows) with explicit <c>"\n"</c> appends, so an
    /// assertion written against either one alone would pass on one checkout and fail on the other.
    /// </summary>
    private static string Normalize(string sql) =>
        sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static IReadOnlyList<string> ParameterNames(NpgsqlCommand cmd) =>
        [.. cmd.Parameters.Select(p => p.ParameterName).Order(StringComparer.Ordinal)];

    private static string WrongShapeMessage(string sql) =>
        "The materialized branch no longer keeps ORDER BY / LIMIT / OFFSET in the OUTER query "
        + "only. An inner LIMIT takes an arbitrary subset and the outer ORDER BY then orders THAT "
        + "— silently wrong rows across pages, with every semantic test still green (the #805-3 "
        + $"class, refused as ADR 0119's alternative E1).{Environment.NewLine}SQL:"
        + $"{Environment.NewLine}{sql}";
}
