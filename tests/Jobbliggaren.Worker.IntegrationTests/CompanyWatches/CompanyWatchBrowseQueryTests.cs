using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #560 kriterie-vågen PR-2 — SEMANTIC Testcontainers tests for <see cref="CompanyWatchBrowseQuery"/>
/// against REAL Postgres. Deliberately not EF-InMemory: every property under test is one InMemory
/// cannot see — the <c>text[]</c> array-overlap operator, <c>= ANY</c>, the by-name status column, and
/// OFFSET/LIMIT ordering.
///
/// <para>
/// The PLAN is pinned separately (<see cref="CompanyWatchBrowseQueryPlanTests"/>): these tests prove
/// the query returns the RIGHT ROWS, that one proves it does so USING THE GIN INDEX. Both are needed —
/// a query that returns the right rows via a sequential scan passes every test here while making PR-1's
/// index cosmetic (dotnet-architect Q5).
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchBrowseQueryTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private const string SniIt = "62010";
    private const string SniConsulting = "70220";
    private const string SniBakery = "10710";

    // 0180 = Stockholm, 1480 = Göteborg, 0114 = Upplands Väsby. The LEADING ZERO is load-bearing.
    private const string KommunStockholm = "0180";
    private const string KommunGoteborg = "1480";
    private const string KommunUpplandsVasby = "0114";

    [Fact]
    public async Task Browse_ReturnsOnlyCompanies_MatchingBothAxes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Match AB", KommunStockholm, [SniIt]),
            // SNI overlaps, kommun does NOT → excluded.
            Entry("5560000020", "Wrong Kommun AB", KommunGoteborg, [SniIt]),
            // Kommun matches, SNI does NOT → excluded.
            Entry("5560000038", "Wrong Sni AB", KommunStockholm, [SniBakery]));

        var page = await BrowseAsync(ctx.Db, Spec([SniIt], [KommunStockholm]), ct);

        // Together these prove the predicate is an AND across the two axes, not an OR.
        page.TotalCount.ShouldBe(1);
        page.Items.Select(i => i.OrganizationNumber).ShouldBe(["5560000012"]);
    }

    [Fact]
    public async Task Browse_NeverReturnsADeregisteredCompany()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Alive AB", KommunStockholm, [SniIt]),
            Entry("5560000020", "Dead AB", KommunStockholm, [SniIt],
                status: CompanyRegisterStatus.Deregistered));

        var page = await BrowseAsync(ctx.Db, Spec([SniIt], [KommunStockholm]), ct);

        // DPIA M-D6 — a de-registered company is never surfaced. The port's `status = 'Active'` is
        // POSITIVE polarity on purpose: `status <> 'Deregistered'` would start silently surfacing any
        // third status the enum ever gains.
        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");
    }

    [Fact]
    public async Task Browse_PartialSniOverlap_IsAMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // The company carries two SNI codes; the criterion asks for one of them plus one it lacks.
        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Overlap AB", KommunStockholm, [SniIt, SniConsulting]));

        var page = await BrowseAsync(ctx.Db, Spec([SniConsulting, SniBakery], [KommunStockholm]), ct);

        // THE test a reviewer misses. This pins `&&` (OVERLAP) against `@>` (CONTAINMENT). A
        // well-meaning switch to @> would silently demand the company carry EVERY code the user picked
        // — near-zero hits, no error, no failing test but this one. Overlap is the bound semantics
        // (Fork B1: "watch these industries", not "companies in all of these industries at once").
        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");
    }

    [Fact]
    public async Task Browse_MatchesExactFiveDigitSniCodes_NeverAPrefix()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Exact AB", KommunStockholm, ["62010"]),
            Entry("5560000020", "Truncated AB", KommunStockholm, ["6201"]),
            Entry("5560000038", "Extended AB", KommunStockholm, ["620100"]));

        var page = await BrowseAsync(ctx.Db, Spec(["62010"], [KommunStockholm]), ct);

        // Fork B1 — exact 5-digit leaf match. An industry-level pick is expanded to leaves by the
        // picker (PR-3), never by a prefix query: array overlap is an equality test per element, and
        // that is exactly what keeps the predicate GIN-indexable.
        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");
    }

    [Fact]
    public async Task Browse_MunicipalityCode_KeepsItsLeadingZero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Väsby AB", KommunUpplandsVasby, [SniIt]));

        var hit = await BrowseAsync(ctx.Db, Spec([SniIt], ["0114"]), ct);
        var miss = await BrowseAsync(ctx.Db, Spec([SniIt], ["114"]), ct);

        // The kommun code is a STRING whose leading zero is significant (0114 = Upplands Väsby). Parse
        // it to an int anywhere in the chain and "114" matches nothing — a silent zero-result that
        // looks exactly like an honest "no companies here".
        hit.TotalCount.ShouldBe(1);
        hit.Items.Single().SeatMunicipalityCode.ShouldBe("0114");
        miss.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Browse_ReturnsACompanyWithAnAdvertisingBlock_AndNeverSurfacesTheFlag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Reklamspärr AB", KommunStockholm, [SniIt], hasAdvertisingBlock: true));

        var page = await BrowseAsync(ctx.Db, Spec([SniIt], [KommunStockholm]), ct);

        // DPIA C-D3 / Fork E1 is "never SURFACED", NOT "never RETURNED" — and the difference matters in
        // both directions. Silently dropping these companies would be an under-surfacing vacuity: a
        // jobseeker's spontaneous application is not direct marketing, so the reklamspärr does not
        // apply to this passive, user-initiated view (security-auditor ratified, scoped to exactly
        // that). The company IS returned...
        page.TotalCount.ShouldBe(1);
        page.Items.Single().OrganizationNumber.ShouldBe("5560000012");

        // ...and the flag is un-surfaceable: CompanyBrowseResult has no advertising-block member at
        // all, so the SELECT never even fetches the column. What is never fetched cannot leak.
        typeof(CompanyBrowseResult).GetProperties()
            .Select(p => p.Name)
            .ShouldNotContain(n => n.Contains("Advertising", StringComparison.OrdinalIgnoreCase)
                                   || n.Contains("Reklam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Browse_SortsSwedish_SoAringAumlOuml_ComeAfterZ_NotAmongAAndO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // #884. Å, Ä and Ö are three DISTINCT LETTERS in Swedish, and they come after Z in that order.
        // Under the cluster's en_US.utf8 default they fold into A and O. Both orders below are MEASURED
        // on postgres:18.3 against this exact seed, not derived:
        //
        //   en_US.utf8:  Ahlberg  Åkesson  Älvsborg  Ärlig  Bok  Cederlund  Ödegaard  Öhman  Svensson  Zebra
        //   sv-SE (ICU): Ahlberg  Bok  Cederlund  Svensson  Zebra  Åkesson  Älvsborg  Ärlig  Ödegaard  Öhman
        //
        // "Åkesson AB" lands between Ahlberg and Bok; "Öhman AB" lands ahead of Svensson. In a Swedish
        // civic service that is simply wrong, and it is the LIVE browse list users page through.
        //
        // The Å/Ä/Ö names are INTERLEAVED with Latin-alphabet ones ON PURPOSE: a seed of only Å/Ä/Ö
        // names sorts identically under both collations, so it would pass while proving nothing. What is
        // asserted is the ORDER of the mixed set — and the two orders diverge at position 2, so this
        // test cannot be green under the wrong collation. It fails today and passes after the migration;
        // that transition is the acceptance criterion of this PR.
        //
        // Å/Ä/Ö only — no V/W pair. Swedish collation has historically treated V and W as one letter and
        // CLDR tailoring there has moved; that is not the invariant #884 pins, and seeding it would make
        // this test fail for a reason it is not about.
        await SeedAsync(ctx.Db, ct,
            Entry(OrgNr(1), "Öhman AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(2), "Ahlberg AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(3), "Ärlig AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(4), "Zebra AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(5), "Åkesson AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(6), "Cederlund AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(7), "Ödegaard AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(8), "Svensson AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(9), "Älvsborg AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(10), "Bok AB", KommunStockholm, [SniIt]));

        var page = await BrowseAsync(ctx.Db, Spec([SniIt], [KommunStockholm]), ct);

        // Asserts the ORDER, not the string "swedish" anywhere: a name-based guard would pass against a
        // collation that is merely CALLED Swedish. Only the ordering can tell the truth about that.
        page.Items.Select(i => i.Name).ShouldBe([
            "Ahlberg AB", "Bok AB", "Cederlund AB", "Svensson AB", "Zebra AB",
            "Åkesson AB", "Älvsborg AB", "Ärlig AB", "Ödegaard AB", "Öhman AB"]);
    }

    [Fact]
    public async Task EverySwedishTextColumn_CarriesTheSameIcuSwedishCollation_NotJustTheSortedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // #884. Browse_SortsSwedish_... guards company_name, because company_name is SORTED and a wrong
        // collation shows up as a wrong order. sate_kommun_name (Åre, Älvdalen, Örnsköldsvik) is NOT
        // sorted by anything today — so nothing above can see it, and deleting its .UseCollation(...)
        // leaves every test in this repo green. An unguarded collation defended by an ADR section and a
        // code comment is precisely the shape this PR exists to delete: a guarantee asserted in prose
        // and enforced nowhere (#805-3, #842). So it is enforced HERE.
        //
        // Shape-based, never name-based: this asserts the collation's PROVIDER and LOCALE, never the
        // string "swedish". A name assertion would pass against a collation that is merely CALLED
        // Swedish while collating as en_US — which is the exact class of lie the guard exists to catch.
        // It also asserts the two columns share ONE collation OID: two collations with the same locale
        // are not interchangeable to the planner, and a divergence between them is invisible everywhere
        // else.
        const string Sql = """
            SELECT a.attname,
                   c.collprovider::text AS provider,
                   c.colllocale        AS locale,
                   c.oid::text         AS collation_oid
            FROM pg_attribute a
            JOIN pg_collation c ON c.oid = a.attcollation
            WHERE a.attrelid = 'company_register'::regclass
              AND a.attname IN ('company_name', 'sate_kommun_name')
            ORDER BY a.attname;
            """;

        var rows = new List<(string Column, string Provider, string? Locale, string Oid)>();
        var conn = ctx.Db.Database.GetDbConnection();
        await ctx.Db.Database.OpenConnectionAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = Sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetString(0), reader.GetString(1),
                          reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3)));
        }

        // A column with no explicit collation does not join pg_collation to a real row at all — it
        // carries the database default. So a missing row IS the failure, and it is the FIRST thing
        // asserted: drop .UseCollation(...) from either column and this line is what goes red.
        rows.Select(r => r.Column).ShouldBe(["company_name", "sate_kommun_name"]);

        foreach (var row in rows)
        {
            row.Provider.ShouldBe("i", $"{row.Column} is not collated by ICU.");
            row.Locale.ShouldBe("sv-SE", $"{row.Column} is not collated as Swedish.");
        }

        rows.Select(r => r.Oid).Distinct().Count().ShouldBe(
            1,
            "company_name and sate_kommun_name carry DIFFERENT collation objects. Two collations with "
            + "the same locale are not interchangeable to the planner, and nothing else in this repo "
            + "can see the difference.");
    }

    [Fact]
    public async Task Browse_PagesAreTotallyOrdered_NoRowLostOrDuplicated_AndTotalCountIsStable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // 25 matching companies. DUPLICATE company_name on purpose (in a real register duplicate legal
        // names are normal): without them this test is VACUOUS — it would pass even with a non-total
        // ORDER BY, and so would prove nothing about the organization_number tiebreak. With them, a
        // missing tiebreak lets Postgres order the ties arbitrarily per page and the union below breaks.
        var entries = Enumerable.Range(0, 25)
            .Select(i => Entry(
                OrgNr(i),
                // Five names, five companies each → 20 ties to get wrong.
                $"Delad Firma {i % 5}",
                KommunStockholm,
                [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        var spec = Spec([SniIt], [KommunStockholm]);
        var p1 = await BrowseAsync(ctx.Db, spec, ct, page: 1, pageSize: 10);
        var p2 = await BrowseAsync(ctx.Db, spec, ct, page: 2, pageSize: 10);
        var p3 = await BrowseAsync(ctx.Db, spec, ct, page: 3, pageSize: 10);

        p1.Items.Count.ShouldBe(10);
        p2.Items.Count.ShouldBe(10);
        p3.Items.Count.ShouldBe(5);

        // The separate count query must agree with the page query on every page — they share one
        // WHERE fragment AND one parameter binding precisely so they cannot drift.
        p1.TotalCount.ShouldBe(25);
        p2.TotalCount.ShouldBe(25);
        p3.TotalCount.ShouldBe(25);

        var seen = p1.Items.Concat(p2.Items).Concat(p3.Items)
            .Select(i => i.OrganizationNumber)
            .ToList();

        // Nothing dropped, nothing served twice — the OFFSET walk is stable because the ORDER BY is
        // total (company_name, organization_number = the PK).
        seen.Distinct().Count().ShouldBe(25);
        seen.Order(StringComparer.Ordinal)
            .ShouldBe(entries.Select(e => e.OrganizationNumber).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Browse_WithNoMatches_ReturnsAnEmptyPage_NotAnError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct, Entry("5560000012", "Bageriet AB", KommunGoteborg, [SniBakery]));

        var page = await BrowseAsync(ctx.Db, Spec([SniIt], [KommunStockholm]), ct);

        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Browse_ProjectsTheCompanyFields_TheBrowseSurfaceRenders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct,
            Entry("5560000012", "Acme AB", KommunStockholm, [SniIt, SniConsulting]));

        var page = await BrowseAsync(ctx.Db, Spec([SniIt], [KommunStockholm]), ct);

        var hit = page.Items.Single();
        hit.OrganizationNumber.ShouldBe("5560000012");
        hit.Name.ShouldBe("Acme AB");
        hit.SeatMunicipalityCode.ShouldBe(KommunStockholm);
        hit.SeatMunicipalityName.ShouldBe("Stockholm");
        // The company's FULL SNI list comes back, not just the codes the criterion asked for — the
        // browse surface shows what the company actually does.
        hit.SniCodes.Order(StringComparer.Ordinal).ShouldBe([SniIt, SniConsulting]);
    }

    [Fact]
    public async Task Browse_WithAnEmptyAxis_ThrowsInsteadOfSilentlyReturningNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct, Entry("5560000012", "Acme AB", KommunStockholm, [SniIt]));

        // A spec rehydrated from a corrupt row can carry an empty axis: Create() forbids it, but
        // FromTrusted() does not re-validate (by design). In SQL an empty axis is not an error —
        // `sni_codes && '{}'` is FALSE — so the browse would return zero rows and look like an honest
        // "no companies match". Fail loud instead: a silent miss is this product's cardinal sin.
        var emptySni = CompanyWatchCriteriaSpec.FromTrusted([], [KommunStockholm]);
        var emptyKommun = CompanyWatchCriteriaSpec.FromTrusted([SniIt], []);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await BrowseAsync(ctx.Db, emptySni, ct));
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await BrowseAsync(ctx.Db, emptyKommun, ct));
    }

    [Fact]
    public async Task Browse_TotalCount_SaturatesAtTheServableCeiling_NeverReportsMore()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // pageSize 2 -> the surface can serve at most MaxPage * 2 = 200 rows. Seed MORE than that.
        const int PageSize = 2;
        var ceiling = CompanyBrowseCriteria.MaxServableRows(PageSize);
        var entries = Enumerable.Range(0, ceiling + 37)
            .Select(i => Entry(OrgNr(i), $"Företag {i}", KommunStockholm, [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        var page = await BrowseAsync(
            ctx.Db, Spec([SniIt], [KommunStockholm]), ct, page: 1, pageSize: PageSize);

        // The count SATURATES — it never reports the 37 extra rows it can never serve. This is a
        // CORRECTNESS property, not a perf one: an uncapped count is what would let the pager below lie.
        page.TotalCount.ShouldBe(ceiling);
    }

    [Fact]
    public async Task Browse_PagerCanNeverAdvertiseAPageTheValidatorWouldReject()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        const int PageSize = 2;
        var entries = Enumerable.Range(0, CompanyBrowseCriteria.MaxServableRows(PageSize) + 37)
            .Select(i => Entry(OrgNr(i), $"Företag {i}", KommunStockholm, [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        var page = await BrowseAsync(
            ctx.Db, Spec([SniIt], [KommunStockholm]), ct, page: 1, pageSize: PageSize);

        // THE regression pin for the lying pager. PagedResult.TotalPages is ceil(TotalCount / PageSize),
        // and BrowseCompaniesQueryValidator rejects Page > MaxPage with a 400. Before the count was
        // capped, a bound-legal broad criterion (1000 SNI x 290 kommuner matches all 1 170 000 register
        // rows) would have made the pager advertise 58 500 pages of which 100 are fetchable — an
        // authoritative number the system that emitted it does not back. Not slow: FALSE, and the same
        // shape as the vacuous JobAd.DeletedAt filter (#805-3). The cap makes this true by construction.
        page.TotalPages.ShouldBeLessThanOrEqualTo(CompanyBrowseCriteria.MaxPage);
    }

    [Fact]
    public async Task Magnitude_CountsExactly_BelowTheCeiling_AndSharesTheBrowsePredicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // Three matching + one wrong-kommun + one wrong-SNI + one deregistered: the magnitude must
        // count through the SAME three-clause predicate as the page query (status AND kommun AND
        // sni) — a drift on any clause changes 3 into something else.
        await SeedAsync(ctx.Db, ct,
            Entry(OrgNr(1), "Match 1 AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(2), "Match 2 AB", KommunStockholm, [SniIt, SniConsulting]),
            Entry(OrgNr(3), "Match 3 AB", KommunStockholm, [SniIt]),
            Entry(OrgNr(4), "Wrong Kommun AB", KommunGoteborg, [SniIt]),
            Entry(OrgNr(5), "Wrong Sni AB", KommunStockholm, [SniBakery]),
            Entry(OrgNr(6), "Dead AB", KommunStockholm, [SniIt],
                status: CompanyRegisterStatus.Deregistered));

        var magnitude = await new CompanyWatchBrowseQuery(ctx.Db).CountMatchingCompaniesAsync(
            Spec([SniIt], [KommunStockholm]), ceiling: 10_000, ct);

        magnitude.ShouldBe(3);
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

        var magnitude = await new CompanyWatchBrowseQuery(ctx.Db).CountMatchingCompaniesAsync(
            Spec([SniIt], [KommunStockholm]), ceiling: 5, ct);

        // min(true count, ceiling): the caller reads "== ceiling" as SATURATED and renders "5+".
        magnitude.ShouldBe(5);
    }

    [Fact]
    public async Task Magnitude_AndPaginationCount_AreTwoDifferentCeilings()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        // THE Fork G3 pin. Seed past the pagination surface's servable ceiling (pageSize 2 →
        // MaxPage × 2 = 200 rows servable; 215 match). The PAGER's TotalCount saturates at 200 —
        // it answers "how many rows can I serve". The MAGNITUDE, asked with a higher product
        // ceiling, answers "how many companies match": 215, exact. Fold the two into one number
        // and one of the two answers becomes a lie — that is why the port has two methods with
        // two caps instead of one count read two ways.
        const int PageSize = 2;
        var servable = CompanyBrowseCriteria.MaxServableRows(PageSize);
        var total = servable + 15;
        var entries = Enumerable.Range(0, total)
            .Select(i => Entry(OrgNr(i), $"Företag {i}", KommunStockholm, [SniIt]))
            .ToArray();
        await SeedAsync(ctx.Db, ct, entries);

        var spec = Spec([SniIt], [KommunStockholm]);
        var query = new CompanyWatchBrowseQuery(ctx.Db);

        var page = await query.BrowseAsync(new CompanyBrowseCriteria(spec, 1, PageSize), ct);
        var magnitude = await query.CountMatchingCompaniesAsync(spec, ceiling: 10_000, ct);

        page.TotalCount.ShouldBe(servable);
        magnitude.ShouldBe(total);
    }

    [Fact]
    public async Task Magnitude_WithAnEmptyAxis_ThrowsInsteadOfSilentlyCountingZero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await SeedAsync(ctx.Db, ct, Entry(OrgNr(1), "Acme AB", KommunStockholm, [SniIt]));

        // The magnitude runs through the same BindPredicate as the page query, so a corrupt spec
        // fails LOUD here too — a silent zero would render "0 företag matchar" over a live watch.
        var corrupt = CompanyWatchCriteriaSpec.FromTrusted([], [KommunStockholm]);
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await new CompanyWatchBrowseQuery(ctx.Db)
                .CountMatchingCompaniesAsync(corrupt, ceiling: 10, ct));
    }

    [Fact]
    public async Task Magnitude_RejectsANonPositiveCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await new CompanyWatchBrowseQuery(ctx.Db)
                .CountMatchingCompaniesAsync(Spec([SniIt], [KommunStockholm]), ceiling: 0, ct));
    }

    private static string OrgNr(int i) => $"55600{i:D5}";

    private static CompanyWatchCriteriaSpec Spec(string[] sni, string[] kommun) =>
        CompanyWatchCriteriaSpec.FromTrusted(sni, kommun);

    private static ValueTask<Application.Common.PagedResult<CompanyBrowseResult>> BrowseAsync(
        AppDbContext db,
        CompanyWatchCriteriaSpec spec,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20) =>
        new CompanyWatchBrowseQuery(db)
            .BrowseAsync(new CompanyBrowseCriteria(spec, page, pageSize), ct);

    private static ScbCompanyRegisterEntry Entry(
        string orgNr,
        string name,
        string municipality,
        string[] sni,
        CompanyRegisterStatus status = CompanyRegisterStatus.Active,
        bool hasAdvertisingBlock = false) =>
        new()
        {
            OrganizationNumber = orgNr,
            Name = name,
            SeatMunicipalityCode = municipality,
            SeatMunicipalityName = municipality == KommunStockholm ? "Stockholm" : "Annan kommun",
            SniCodes = [.. sni],
            HasAdvertisingBlock = hasAdvertisingBlock,
            ScbStatusRaw = status == CompanyRegisterStatus.Active ? "1" : "9",
            Status = status,
        };

    // Seed through the PRODUCTION write path (the same bulk upsert the nightly SCB sync uses) rather
    // than an EF AddRange — the text[] column and the by-name status are written exactly as production
    // writes them.
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
