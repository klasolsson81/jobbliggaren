using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #1681 (ADR 0139) — the three seams the first test round measured only in appearance
/// (test-writer scoped re-check, 2026-09-06). Each of them had the same shape: production argues a
/// property in prose, and nothing could fail if the property stopped holding.
///
/// <list type="number">
///   <item><b>The composition root's options binding.</b> The unit-level binding test rebuilt the
///     binding in its own <c>ServiceCollection</c>, so mutating
///     <c>DependencyInjection.cs</c> changed nothing it observed. This suite reads
///     <c>IOptions&lt;CompanyWatchMaterialisationOptions&gt;</c> out of the REAL graph the fixture
///     builds through <c>AddPersistence</c>.</item>
///   <item><b>The paging loop.</b> At the shipped page size of 500 no fixture could produce a second
///     page, leaving termination and totality unmeasured — and two of the surviving mutants are
///     INFINITE LOOPS in operation.</item>
///   <item><b>The ANALYZE.</b> Nothing asserted it happened, that it happened once per RUN rather
///     than once per criterion, or that it covered both tables — while the read path's member
///     lookup depends on the selectivity estimate it maintains.</item>
/// </list>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchMaterialisationSeamTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 5, 30, 0, TimeSpan.Zero);

    private const string SniIt = "62010";


    [Fact]
    public void TheRealCompositionRoot_BindsTheMaterialisationOptions_FromTheirOwnSection()
    {
        // Reads the graph AddPersistence actually built. The fixture supplies deliberately
        // distinguishable values: CompanyWatchMaterialisation:CadenceCron = "11 11 * * *" and
        // ScbRegister:SyncCadenceCron = "0 6 * * 6", with ScbRegister:Enabled = false.
        //
        // Under the mutation this exists to kill — DependencyInjection.cs binding against
        // GetSection(ScbRegisterOptions.SectionName) — BOTH assertions fail independently:
        // CadenceCron finds no "CadenceCron" key under ScbRegister and falls back to the shipped
        // default "30 5 * * *", and Enabled binds to ScbRegister:Enabled = false. Two kills, and
        // neither is available to a test that rebuilds the binding itself.
        var options = _fixture.Services
            .GetRequiredService<IOptions<CompanyWatchMaterialisationOptions>>().Value;

        options.CadenceCron.ShouldBe("11 11 * * *",
            "materialiseringen måste binda ur CompanyWatchMaterialisation-sektionen; en bindning mot "
            + "ScbRegister hittar ingen CadenceCron och faller till defaulten");
        options.Enabled.ShouldBeTrue(
            "en bindning mot ScbRegister skulle plocka upp dess Enabled=false — vilket ÄR Major 3, "
            + "en nivå ner");
    }

    [Theory]
    // The corpus size relative to the page size is the whole point. 4 with page size 2 is an EXACT
    // MULTIPLE, so the last page fetched is EMPTY and termination depends on the loop's single exit
    // treating an empty page as short. 5 is the non-multiple, where the last page is partial. 1 is
    // the single short page, and 0 criteria (via the other suites) is the empty corpus.
    //
    // This theory is also what MEASURED the loop's redundancy: an earlier version had two exits, and
    // deleting either left every case green because each covered for the other. The redundant one is
    // gone, so the mutation of what remains is now genuinely fatal (verified 2026-09-06).
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(1, 2)]
    public async Task Materialise_WalksEveryCriterionExactlyOnce_AcrossPageBoundaries(
        int criterionCount, int pageSize)
    {
        var ct = TestContext.Current.CancellationToken;

        // A non-terminating loop must fail RED rather than hang the suite, so the run gets its own
        // deadline. Without this, the two infinite-loop mutants would time the whole job out in CI
        // instead of naming themselves here.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));

        await ResetAsync(ct);
        await SeedRegisterAsync(criterionCount, ct);

        // Criterion i watches kommun i, and register row i sits in kommun i — so each criterion
        // matches EXACTLY ONE company. That one-to-one shape is what lets a skipped or re-walked
        // page show up as a per-criterion member count rather than only in a total.
        var ids = new List<CompanyWatchCriterionId>();
        for (var i = 1; i <= criterionCount; i++)
            ids.Add(await SeedCriterionAsync(Guid.NewGuid(), Kommun(i), ct));

        var result = await RunWithPageSizeAsync(pageSize, deadline.Token);

        // CriteriaSeen counts what the LOOP visited, so a page that is skipped or re-walked shows up
        // here. Dropping `offset += pageSize` or `Skip(offset)` re-walks page one forever, and
        // dropping the single exit never terminates at all — each hits the deadline above rather
        // than hanging the suite. Both verified by mutation, 2026-09-06.
        result.CriteriaSeen.ShouldBe(criterionCount);
        result.CriteriaMaterialised.ShouldBe(criterionCount);
        result.MembersWritten.ShouldBe(criterionCount);

        // Totality, not just arithmetic: EVERY criterion has its own member row. Dropping the
        // ORDER BY makes OFFSET paging non-total — rows can be skipped or duplicated between pages —
        // which the counters alone would not necessarily reveal but this does.
        foreach (var id in ids)
            (await ReadMemberCountAsync(id, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Materialise_AnalysesBothTables_ExactlyOncePerRun_NeverOncePerCriterion()
    {
        // AGENTS.md §3.6, measured with the instrument ScbCompanyRegisterRefresherIntegrationTests
        // already established: pg_stat_user_tables.analyze_count, which PostgreSQL 15+ writes
        // synchronously so no polling is needed.
        //
        // THREE criteria, and that number is what makes the assertion discriminating: an ANALYZE
        // moved inside the per-criterion loop — the anti-pattern §3.6 names — would land the counter
        // at +3 rather than +1. Deleting the call entirely lands it at +0.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);
        await SeedRegisterAsync(3, ct);
        for (var i = 1; i <= 3; i++)
            await SeedCriterionAsync(Guid.NewGuid(), Kommun(i), ct);

        var before = await ReadAnalyzeCountsAsync(ct);

        var result = await RunWithPageSizeAsync(500, ct);
        result.CriteriaMaterialised.ShouldBe(3);

        var after = await ReadAnalyzeCountsAsync(ct);

        after.Members.ShouldBe(before.Members + 1,
            "exakt EN ANALYZE per körning på medlemstabellen — aldrig en per kriterium (§3.6)");
        after.Materialisations.ShouldBe(before.Materialisations + 1,
            "och den måste täcka BÅDA tabellerna; att analysera bara den ena är en tyst halvmätning");
    }

    // ----- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// The production orchestrator with one option overridden. Same construction as
    /// <c>CompanyWatchCriterionMaterialisationTests.RunDisabledAsync</c>: everything else comes out of
    /// the real graph, so only the knob under test differs.
    /// </summary>
    private async Task<CompanyWatchCriterionMaterialisationResult> RunWithPageSizeAsync(
        int pageSize, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = new CompanyWatchCriterionMaterialiser(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>(),
            scope.ServiceProvider.GetRequiredService<IDateTimeProvider>(),
            Options.Create(new CompanyWatchMaterialisationOptions { CriterionPageSize = pageSize }),
            NullLogger<CompanyWatchCriterionMaterialiser>.Instance);

        return await materialiser.MaterialiseAsync(ct);
    }

    private async Task ResetAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_watch_criteria;", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM company_register;", ct);
    }

    /// <summary>One ACTIVE legal-entity company per criterion, all matching the same predicate, so
    /// every criterion materialises exactly one member and a skipped criterion is visible.</summary>
    private async Task SeedRegisterAsync(int count, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            SELECT '558' || lpad(i::text, 7, '0'), 'Bolag ' || i, lpad(i::text, 4, '0'), NULL,
                   ARRAY[{1}], false, '1', 'Active', now(), now()
            FROM generate_series(1, {0}) i;
            """,
            [count, SniIt], ct);
    }

    /// <summary>The 4-digit kommun code criterion i watches; register row i carries the same one.</summary>
    private static string Kommun(int i) => i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<CompanyWatchCriterionId> SeedCriterionAsync(
        Guid userId, string kommun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var spec = CompanyWatchCriteriaSpec.Create([SniIt], [kommun]);
        spec.IsSuccess.ShouldBeTrue("seed: specen måste vara giltig");

        var criterion = CompanyWatchCriterion.Create(userId, spec.Value, null, new FixedClock(T0));
        criterion.IsSuccess.ShouldBeTrue("seed: kriteriet måste kunna skapas");

        db.CompanyWatchCriteria.Add(criterion.Value);
        await db.SaveChangesAsync(ct);
        return criterion.Value.Id;
    }

    private async Task<int> ReadMemberCountAsync(CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM company_watch_criterion_members "
                + "WHERE criterion_id = {0};",
                id.Value)
            .ToListAsync(ct);
        return rows[0];
    }

    private async Task<(long Members, long Materialisations)> ReadAnalyzeCountsAsync(
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database
            .SqlQueryRaw<AnalyzeRow>(
                """
                SELECT relname, analyze_count
                FROM pg_stat_user_tables
                WHERE relname IN ('company_watch_criterion_members',
                                  'company_watch_criterion_materialisations');
                """)
            .ToListAsync(ct);

        return (
            rows.SingleOrDefault(r => r.Relname == "company_watch_criterion_members")?.AnalyzeCount ?? 0,
            rows.SingleOrDefault(r => r.Relname == "company_watch_criterion_materialisations")?.AnalyzeCount ?? 0);
    }

    private sealed record AnalyzeRow(string Relname, long AnalyzeCount);

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
