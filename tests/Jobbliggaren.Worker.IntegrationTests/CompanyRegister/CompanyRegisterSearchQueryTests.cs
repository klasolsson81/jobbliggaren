using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — SEMANTIC Testcontainers tests for
/// <see cref="CompanyRegisterSearchQuery"/> against REAL Postgres (the sibling discipline of
/// <c>CompanyWatchBrowseQueryTests</c>: <c>text[]</c> overlap, <c>= ANY</c>, LIKE-prefix under
/// lower(), by-name status and OFFSET/LIMIT ordering are all invisible to EF-InMemory).
///
/// <para>
/// <b>The load-bearing inversion this suite pins:</b> in the CRITERION browse an empty axis is
/// corruption and throws; HERE an absent axis means the clause is OMITTED — browse-all is legal
/// and returns rows. If the implementation ever regressed into binding an empty
/// <c>text[]</c> (<c>sni_codes &amp;&amp; '{}'</c> is FALSE), every all/one-axis test below
/// would return zero rows and go RED — the anti-silent-zero oracle (#805-3 shape).
/// </para>
///
/// <para>
/// The PLAN is pinned separately (<c>CompanyRegisterSearchQueryPlanTests</c>): these tests prove
/// the right ROWS, that one proves the indexes actually serve the shapes.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyRegisterSearchQueryTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

    private const string SniIt = "62010";
    private const string SniConsulting = "70220";
    private const string SniBakery = "10710";

    // 0180 = Stockholm, 1480 = Göteborg. The LEADING ZERO is load-bearing.
    private const string KommunStockholm = "0180";
    private const string KommunGoteborg = "1480";

    [Fact]
    public async Task Search_WithNoAxes_IsBrowseAll_ActiveOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // Asymmetric seed (2 in + 1 out): a count that ignored the status clause would say 3; an
        // implementation that bound empty arrays would say 0. Both lies are RED here.
        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Alpha AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Beta AB", KommunGoteborg, [SniBakery]),
            Entry("5560000038", "Dead AB", KommunStockholm, [SniIt],
                status: CompanyRegisterStatus.Deregistered));

        var page = await SearchAsync(ctx.Db, Criteria(), ct);

        page.TotalCount.ShouldBe(2);
        page.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012", "5560000020"]);
    }

    [Fact]
    public async Task Search_KommunAxisAlone_Filters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Sthlm AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Sthlm 2 AB", KommunStockholm, [SniBakery]),
            Entry("5560000038", "Gbg AB", KommunGoteborg, [SniIt]));

        var page = await SearchAsync(ctx.Db, Criteria(kommun: [KommunStockholm]), ct);

        page.TotalCount.ShouldBe(2);
        page.Items.ShouldAllBe(i => i.SeatMunicipalityCode == KommunStockholm);
    }

    [Fact]
    public async Task Search_SniAxisAlone_Filters_WithOverlapSemantics()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "It AB", KommunStockholm, [SniIt]),
            // Overlap on a NON-primary code is a match (`&&`, never containment).
            Entry("5560000020", "Mixed AB", KommunGoteborg, [SniBakery, SniIt]),
            Entry("5560000038", "Bageri AB", KommunStockholm, [SniBakery]));

        var page = await SearchAsync(ctx.Db, Criteria(sni: [SniIt]), ct);

        page.TotalCount.ShouldBe(2);
        page.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012", "5560000020"]);
    }

    [Fact]
    public async Task Search_CombinedAxes_AreAnd_NotOr()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Match AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Wrong Kommun AB", KommunGoteborg, [SniIt]),
            Entry("5560000038", "Wrong Sni AB", KommunStockholm, [SniBakery]));

        var page = await SearchAsync(
            ctx.Db, Criteria(sni: [SniIt], kommun: [KommunStockholm]), ct);

        page.TotalCount.ShouldBe(1);
        page.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012"]);
    }

    [Fact]
    public async Task Search_OrgNr_IsExactEquality()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Target AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Other AB", KommunStockholm, [SniIt]));

        var page = await SearchAsync(ctx.Db, Criteria(orgnr: "5560000012"), ct);

        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");
    }

    [Fact]
    public async Task Search_NamePrefix_IsCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Volvo Cars AB", KommunGoteborg, [SniIt]),
            Entry("5560000020", "volvofinans bank ab", KommunGoteborg, [SniIt]),
            Entry("5560000038", "Scania AB", KommunStockholm, [SniIt]));

        var page = await SearchAsync(ctx.Db, Criteria(name: "VOLVO"), ct);

        page.TotalCount.ShouldBe(2);
        page.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012", "5560000020"]);
    }

    [Fact]
    public async Task Search_NamePrefix_IsAPrefix_NeverASubstring()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Cars Of Sweden AB", KommunGoteborg, [SniIt]),
            Entry("5560000020", "Volvo Cars AB", KommunGoteborg, [SniIt]));

        var page = await SearchAsync(ctx.Db, Criteria(name: "Cars"), ct);

        // "Volvo Cars AB" contains the term but does not START with it — v1 is honest
        // prefix-only (CTO F2; substring is a later, measured decision).
        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");
    }

    [Fact]
    public async Task Search_NamePrefix_TreatsLikeMetacharactersAsLiterals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "100% Bygg AB", KommunStockholm, [SniIt]),
            // If '%' passed through un-escaped, the pattern "100%%" would match this row too.
            Entry("5560000020", "100 Procent Bygg AB", KommunStockholm, [SniIt]),
            // If '_' passed through un-escaped, "A_B%" would match "AxB ...".
            Entry("5560000038", "A_B Konsult AB", KommunStockholm, [SniIt]),
            Entry("5560000046", "AxB Konsult AB", KommunStockholm, [SniIt]));

        var percent = await SearchAsync(ctx.Db, Criteria(name: "100%"), ct);
        percent.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012"]);

        var underscore = await SearchAsync(ctx.Db, Criteria(name: "A_B"), ct);
        underscore.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000038"]);
    }

    [Fact]
    public async Task Search_NamePrefix_HandlesSwedishCharacters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Öhmans Bygg AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Ohlssons Tak AB", KommunStockholm, [SniIt]));

        // lower('Ö') = 'ö' on BOTH sides (one case-folding authority: Postgres/ICU) — and the
        // prefix must not fold Ö into O.
        var page = await SearchAsync(ctx.Db, Criteria(name: "öhman"), ct);

        page.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012"]);
    }

    [Fact]
    public async Task Search_AllFourAxesTogether_AreAnd()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Volvo IT AB", KommunGoteborg, [SniIt]),
            Entry("5560000020", "Volvo IT Sthlm AB", KommunStockholm, [SniIt]),
            Entry("5560000038", "Volvo Bageri AB", KommunGoteborg, [SniBakery]));

        var page = await SearchAsync(
            ctx.Db,
            Criteria(
                sni: [SniIt], kommun: [KommunGoteborg], name: "volvo", orgnr: "5560000012"),
            ct);

        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");
    }

    [Fact]
    public async Task Search_SortsSwedish_AoAumlOuml_AfterZ()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Zebra AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Åkeriet AB", KommunStockholm, [SniIt]),
            Entry("5560000038", "Alfa AB", KommunStockholm, [SniIt]),
            Entry("5560000046", "Örnen AB", KommunStockholm, [SniIt]));

        var page = await SearchAsync(ctx.Db, Criteria(), ct);

        // The column's `swedish` ICU collation sorts Å/Ö AFTER Z — the alphabetical listing Klas
        // ratified (F2: A→Ö only). NOTE: at this corpus size the count does not saturate, so this
        // exercises the MATERIALIZED branch (ADR 0119) — the collation is inherited through the
        // CTE's output column. The walk branch's ordering is covered by
        // BothPlanBranches_ReturnIdenticalRowsInIdenticalOrder below.
        page.Items.Select(i => i.Name).ShouldBe(
            ["Alfa AB", "Zebra AB", "Åkeriet AB", "Örnen AB"]);
    }

    /// <summary>
    /// ADR 0119's load-bearing property, as an ORACLE rather than an argument: the materialization
    /// rule may only ever be a PERFORMANCE decision, so both branches must return identical rows in
    /// identical order. The composition tests pin that structurally (no ORDER BY or LIMIT inside the
    /// CTE, one shared ordering tail); this compares the two against a real database and the real
    /// <c>swedish</c> collation.
    ///
    /// <para>
    /// It also restores coverage the rule silently took away. Every other <c>SearchAsync</c> test in
    /// this class seeds a small corpus, so its count does not saturate and it now runs the
    /// MATERIALIZED branch — meaning the walk branch, which is what production runs for browse-all
    /// and for every large kommun, lost its ordering and pagination coverage entirely. The failure
    /// mode there is this repo's documented silent one: a sort requested under a collation the index
    /// was not built under is not an error, Postgres just sorts (#884 / ADR 0110).
    /// </para>
    ///
    /// <para>
    /// Both branches are forced through production's factory by feeding it a bounded and a saturated
    /// count for the SAME criteria — the one axis the rule sends both ways.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BothPlanBranches_ReturnIdenticalRowsInIdenticalOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // Å/Ö plus duplicate names: the collation decides the order and only the org.nr tiebreak
        // makes it total. Both are properties a branch change could silently break.
        //
        // INSERTION ORDER DELIBERATELY DIFFERS FROM COLLATION ORDER — do not "tidy" this seed into
        // alphabetical order. The difference is what makes the E1 (inner LIMIT) mutation detectable:
        // an inner limit takes the first rows in physical order (Zebra, Åkeriet) where the correct
        // answer starts Alfa, Alfa. Sorted seed data would disarm the test without changing a line
        // of the assertions.
        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Zebra AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Åkeriet AB", KommunStockholm, [SniIt]),
            Entry("5560000038", "Alfa AB", KommunStockholm, [SniIt]),
            Entry("5560000046", "Örnen AB", KommunStockholm, [SniIt]),
            Entry("5560000053", "Alfa AB", KommunStockholm, [SniIt]),
            Entry("5560000061", "Örnen AB", KommunStockholm, [SniIt]));

        var criteria = Criteria(kommun: [KommunStockholm]);
        var cap = CompanyRegisterSearchCriteria.MaxServableRows(criteria.PageSize);

        var materialized = await ReadItemsAsync(ctx.Db, criteria, matchCount: cap - 1, ct);
        var walked = await ReadItemsAsync(ctx.Db, criteria, matchCount: cap, ct);

        // Guard the guard: if both calls took the SAME branch, the comparison below compares a
        // result set with itself and proves nothing at all.
        materialized.Sql.ShouldContain(
            "MATERIALIZED",
            customMessage: "The bounded-count call did not take the materialized branch, so the "
                + "comparison below is between two identical branches and proves nothing.");
        walked.Sql.ShouldNotContain(
            "MATERIALIZED",
            customMessage: "The saturated-count call did not take the walk branch, so the "
                + "comparison below is between two identical branches and proves nothing.");

        // Compared as projected VALUES, not as records: CompanyBrowseResult carries a string[], and
        // record equality compares arrays by REFERENCE — two independently read result sets would
        // never be equal, and the redacted ToString() makes the diff unreadable on top of that.
        Project(materialized.Rows).ShouldBe(Project(walked.Rows));

        materialized.Rows.Select(r => r.Name).ShouldBe(
            ["Alfa AB", "Alfa AB", "Zebra AB", "Åkeriet AB", "Örnen AB", "Örnen AB"]);
        materialized.Rows.Select(r => r.OrganizationNumber).ShouldBe(
            ["5560000038", "5560000053", "5560000012", "5560000020", "5560000046", "5560000061"],
            "The org.nr tiebreak is what makes the order TOTAL — duplicate names are normal in a "
            + "real register, and without it a page boundary can drop or duplicate a row.");

        // ...and ACROSS A PAGE BOUNDARY, which is where the rejected "bound the CTE with an inner
        // LIMIT" variant (ADR 0119's E1) actually bites: an inner limit takes an arbitrary subset
        // and the outer ORDER BY orders THAT, so page 1 can look perfect while page 2 silently
        // repeats or drops rows. Three pages of two, both branches, compared as one sequence.
        var pagedMaterialized = new List<string>();
        var pagedWalked = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var paged = Criteria(kommun: [KommunStockholm], page: page, pageSize: 2);
            var capForPage = CompanyRegisterSearchCriteria.MaxServableRows(paged.PageSize);
            pagedMaterialized.AddRange(
                Project((await ReadItemsAsync(ctx.Db, paged, capForPage - 1, ct)).Rows));
            pagedWalked.AddRange(Project((await ReadItemsAsync(ctx.Db, paged, capForPage, ct)).Rows));
        }

        pagedMaterialized.ShouldBe(pagedWalked);
        pagedMaterialized.ShouldBe(Project(materialized.Rows));
    }

    /// <summary>
    /// Projects to comparable values: <see cref="CompanyBrowseResult"/> carries a <c>string[]</c>, so
    /// record equality compares arrays by REFERENCE and two independently read result sets are never
    /// equal — and its <c>ToString()</c> is org.nr-redacted (#883), which would make any diff
    /// unreadable. Enumerating the members by hand is FAIL-OPEN against a sixth one being added
    /// later (the comparison would silently stop covering it), so the member count is pinned.
    /// </summary>
    private static IReadOnlyList<string> Project(IReadOnlyList<CompanyBrowseResult> rows)
    {
        typeof(CompanyBrowseResult).GetProperties().Length.ShouldBe(
            5,
            "CompanyBrowseResult gained or lost a member. This projection enumerates its members by "
            + "hand, so the branch-equality comparison has silently stopped covering the whole row — "
            + "add the new member below before updating this count.");

        return [.. rows.Select(r =>
            $"{r.OrganizationNumber}|{r.Name}|{r.SeatMunicipalityCode}|{r.SeatMunicipalityName}|"
            + string.Join(",", r.SniCodes))];
    }

    /// <summary>
    /// Runs production's items command with a CHOSEN <c>matchCount</c> so a single test can exercise
    /// both branches, and returns the emitted SQL alongside the rows so the caller can prove the
    /// branches actually differed.
    /// </summary>
    private static async Task<(string Sql, IReadOnlyList<CompanyBrowseResult> Rows)> ReadItemsAsync(
        AppDbContext db, CompanyRegisterSearchCriteria criteria, int matchCount, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd =
            CompanyRegisterSearchQuery.BuildItemsCommand(connection, criteria, matchCount);

        var rows = new List<CompanyBrowseResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new CompanyBrowseResult(
                OrganizationNumber: reader.GetString(0),
                Name: reader.GetString(1),
                SeatMunicipalityCode: reader.GetString(2),
                SeatMunicipalityName: await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3),
                SniCodes: reader.GetFieldValue<string[]>(4)));
        }

        return (cmd.CommandText, rows);
    }

    [Fact]
    public async Task Search_PagesAreTotallyOrdered_NoRowLostOrDuplicated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // Duplicate NAMES on purpose: only the org.nr tiebreak makes the OFFSET walk total.
        var entries = Enumerable.Range(0, 25)
            .Select(i => Entry(OrgNr(i), $"Företag {i % 5} AB", KommunStockholm, [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await SearchAsync(
                ctx.Db, Criteria(page: page, pageSize: 10), ct);
            seen.AddRange(result.Items.Select(i => i.OrganizationNumber));
        }

        seen.Count.ShouldBe(25);
        seen.ShouldBeUnique();
    }

    [Fact]
    public async Task Search_TotalCount_SaturatesAtTheServableCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        const int PageSize = 2;
        var ceiling = CompanyRegisterSearchCriteria.MaxServableRows(PageSize);
        var entries = Enumerable.Range(0, ceiling + 37)
            .Select(i => Entry(OrgNr(i), $"Företag {i}", KommunStockholm, [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        var page = await SearchAsync(ctx.Db, Criteria(pageSize: PageSize), ct);

        // Browse-all over the register is the WORST case for the lying-pager shape (1,07M rows
        // in production) — the cap makes TotalPages ≤ MaxPage true by construction.
        page.TotalCount.ShouldBe(ceiling);
        page.TotalPages.ShouldBeLessThanOrEqualTo(CompanyRegisterSearchCriteria.MaxPage);
    }

    [Fact]
    public async Task Magnitude_CountsExactly_BelowTheCeiling_AndSharesThePredicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry(OrgNr(1), "Match 1 AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(2), "Match 2 AB", KommunStockholm, [SniIt, SniConsulting]),
            Entry(OrgNr(3), "Wrong Kommun AB", KommunGoteborg, [SniIt]),
            Entry(OrgNr(4), "Dead AB", KommunStockholm, [SniIt],
                status: CompanyRegisterStatus.Deregistered));

        var magnitude = await new CompanyRegisterSearchQuery(ctx.Db).CountMatchingAsync(
            Criteria(sni: [SniIt], kommun: [KommunStockholm]), ceiling: 10_000, ct);

        magnitude.ShouldBe(2);
    }

    [Fact]
    public async Task Magnitude_SaturatesAtTheCallersCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        var entries = Enumerable.Range(0, 7)
            .Select(i => Entry(OrgNr(i), $"Företag {i}", KommunStockholm, [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        // An axis, because since #1149 an axis-free criterion is the ONE shape production never
        // sends here: the handler returns null for a browse-all before reaching the port. The
        // seeded rows all carry SniIt, so the saturation at ceiling 5 is unchanged.
        var magnitude = await new CompanyRegisterSearchQuery(ctx.Db).CountMatchingAsync(
            Criteria(sni: [SniIt]), ceiling: 5, ct);

        magnitude.ShouldBe(5);
    }

    [Fact]
    public async Task Magnitude_RejectsANonPositiveCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await new CompanyRegisterSearchQuery(ctx.Db)
                .CountMatchingAsync(Criteria(sni: [SniIt]), ceiling: 0, ct));
    }

    [Fact]
    public async Task Search_WithNoMatches_ReturnsAnEmptyPage_NotAnError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Bageriet AB", KommunGoteborg, [SniBakery]));

        var page = await SearchAsync(ctx.Db, Criteria(name: "volvo"), ct);

        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    private static string OrgNr(int i) => $"55600{i:D5}";

    private static CompanyRegisterSearchCriteria Criteria(
        string[]? sni = null,
        string[]? kommun = null,
        string? name = null,
        string? orgnr = null,
        int page = 1,
        int pageSize = 20) =>
        CompanyRegisterSearchCriteria.FromTrusted(
            sni ?? [], kommun ?? [], name, orgnr, page, pageSize);

    private static ValueTask<Application.Common.PagedResult<CompanyBrowseResult>> SearchAsync(
        AppDbContext db, CompanyRegisterSearchCriteria criteria, CancellationToken ct) =>
        new CompanyRegisterSearchQuery(db).SearchAsync(criteria, ct);

    private static ScbCompanyRegisterEntry Entry(
        string orgNr,
        string name,
        string municipality,
        string[] sni,
        CompanyRegisterStatus status = CompanyRegisterStatus.Active) =>
        new()
        {
            OrganizationNumber = orgNr,
            Name = name,
            SeatMunicipalityCode = municipality,
            SeatMunicipalityName = municipality == KommunStockholm ? "Stockholm" : "Annan kommun",
            SniCodes = [.. sni],
            HasAdvertisingBlock = false,
            ScbStatusRaw = status == CompanyRegisterStatus.Active ? "1" : "9",
            Status = status,
        };

    // Seed through the PRODUCTION write path (the same bulk upsert the nightly SCB sync uses).
    private static async Task SeedAsync(
        AppDbContext db, CancellationToken ct, params ScbCompanyRegisterEntry[] entries) =>
        _ = await new ScbCompanyRegisterStore(db).UpsertBatchAsync(entries, T0, ct);

    private async Task<ScopedContext> FreshContextAsync(CancellationToken ct)
    {
        var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The "Worker" collection runs serially over ONE Postgres, so a test can own the table.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);
        return new ScopedContext(scope, db);
    }

    private sealed class ScopedContext(AsyncServiceScope scope, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AsyncServiceScope Scope { get; } = scope;
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
}
