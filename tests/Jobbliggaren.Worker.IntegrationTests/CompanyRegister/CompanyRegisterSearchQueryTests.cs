using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        // The column's `swedish` ICU collation sorts Å/Ö AFTER Z — the browse-all default order
        // is the alphabetical listing Klas ratified (F2: A→Ö only).
        page.Items.Select(i => i.Name).ShouldBe(
            ["Alfa AB", "Zebra AB", "Åkeriet AB", "Örnen AB"]);
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

        var magnitude = await new CompanyRegisterSearchQuery(ctx.Db).CountMatchingAsync(
            Criteria(), ceiling: 5, ct);

        magnitude.ShouldBe(5);
    }

    [Fact]
    public async Task Magnitude_RejectsANonPositiveCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await new CompanyRegisterSearchQuery(ctx.Db)
                .CountMatchingAsync(Criteria(), ceiling: 0, ct));
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
