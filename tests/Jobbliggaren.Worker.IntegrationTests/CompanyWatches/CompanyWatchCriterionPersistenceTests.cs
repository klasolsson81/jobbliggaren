using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #560 kriterie-vågen PR-1 — Testcontainers persistence tests for
/// <see cref="CompanyWatchCriterion"/> against REAL Postgres. Deliberately NOT EF-InMemory: every
/// property under test here is one InMemory cannot see — the two <c>text[]</c> columns, the shadow
/// backing-field mapping, the <c>ValueComparer</c> that makes in-place mutation visible to change
/// detection, the soft-delete query filter, and the two indexes.
///
/// <para>
/// <b>The load-bearing test is <see cref="UpdateCriteria_OnAMaterialisedRow_PersistsTheNewCodes"/>.</b>
/// <c>ApplyCriteria</c> mutates the backing lists IN PLACE (Clear + AddRange), so the tracked entity's
/// list instance never changes identity. Without a deep <c>ValueComparer</c>, EF snapshots the
/// collection BY REFERENCE — the "original" and the "current" value are then the SAME object, EF sees
/// no change, and <c>SaveChanges</c> emits NO UPDATE. The write returns success and persists nothing:
/// the user edits their criterion, gets a 200, and the old predicate silently stays live. That failure
/// is invisible to every unit test and to InMemory; only a real round-trip in a NEW context catches it.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchCriterionPersistenceTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    // 0180 = Stockholm, 1480 = Göteborg — the leading zero is the whole point (see below).
    private const string SniIt = "62010";
    private const string SniItConsulting = "62020";
    private const string KommunStockholm = "0180";
    private const string KommunGoteborg = "1480";

    [Fact]
    public async Task Save_RoundTripsBothAxes_InANewScope_LeadingZeroesIntact_OrderNormalized()
    {
        // The plain round-trip, and the one that proves the readonly List<string> backing fields are
        // actually MATERIALISED by EF (field access mode) rather than left at their field initialiser.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        // Deliberately unsorted input — normalization must decide the stored order, not the caller.
        var criterionId = await SeedAsync(
            userId, [SniItConsulting, SniIt], [KommunGoteborg, KommunStockholm], "IT i Stockholm", ct);

        // NEW scope ⇒ a NEW DbContext ⇒ nothing served from the identity map; this reads Postgres.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reloaded = await db.CompanyWatchCriteria
            .AsNoTracking()
            .SingleAsync(c => c.Id == criterionId, ct);

        reloaded.UserId.ShouldBe(userId);
        reloaded.Label.ShouldBe("IT i Stockholm");
        reloaded.CreatedAt.ShouldBe(T0);
        reloaded.DeletedAt.ShouldBeNull();
        reloaded.Criteria.SniCodes.ShouldBe([SniIt, SniItConsulting]);
        reloaded.Criteria.MunicipalityCodes.ShouldBe([KommunStockholm, KommunGoteborg]);

        // …and at the STORAGE layer, byte for byte. A kommun code is a STRING: the moment any layer
        // round-trips "0180" through an integer it becomes "180" — a code belonging to no kommun —
        // and the criterion matches nothing while succeeding loudly. This is the pin that would catch
        // that, because it reads the raw text[] literal Postgres actually holds.
        var storedKommun = await ReadArrayLiteralAsync(db, ArrayColumn.KommunCodes, criterionId, ct);
        storedKommun.ShouldBe("{0180,1480}");

        var storedSni = await ReadArrayLiteralAsync(db, ArrayColumn.SniCodes, criterionId, ct);
        storedSni.ShouldBe("{62010,62020}");
    }

    [Fact]
    public async Task Columns_AreRealTextArrays_NotJson()
    {
        // Fork A1: text[], NOT a jsonb VO. This is not cosmetic — the future notification scan must
        // invert the predicate (`@company_sni && sni_codes`), which is GIN-indexable on text[] and
        // impossible on jsonb. If someone "simplifies" these columns to jsonb, the whole scan design
        // silently loses its index and this test is the tripwire.
        var ct = TestContext.Current.CancellationToken;

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await ColumnUdtNameAsync(db, "sni_codes", ct)).ShouldBe("_text");
        (await ColumnUdtNameAsync(db, "kommun_codes", ct)).ShouldBe("_text");
    }

    [Fact]
    public async Task UpdateCriteria_OnAMaterialisedRow_PersistsTheNewCodes()
    {
        // THE ValueComparer pin (see the class summary). The row must be MATERIALISED from Postgres
        // first — an entity that is merely Added has no snapshot to compare against, so seeding and
        // mutating in one scope would pass even with a broken comparer and prove nothing.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var criterionId = await SeedAsync(userId, [SniIt], [KommunStockholm], "Innan", ct);

        // Scope 2 — load (TRACKED, so change detection runs), mutate in place, save.
        using (var editScope = _fixture.Services.CreateScope())
        {
            var db = editScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterionId, ct);

            var result = criterion.UpdateCriteria(
                CompanyWatchCriteriaSpec.Create([SniItConsulting], [KommunGoteborg]).Value,
                new FixedClock(T0.AddDays(1)));
            result.IsSuccess.ShouldBeTrue();

            var written = await db.SaveChangesAsync(ct);
            written.ShouldBe(1,
                "SaveChanges måste faktiskt skriva en rad — 0 här betyder att EF inte SÅG ändringen "
                + "(ValueComparer:n saknas/är fel ⇒ snapshot by reference ⇒ tyst utebliven UPDATE)");
        }

        // Scope 3 — read back from Postgres. This is where a by-reference snapshot shows up: the row
        // would still carry the OLD codes even though the write reported success.
        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reloaded = await verifyDb.CompanyWatchCriteria
            .AsNoTracking()
            .SingleAsync(c => c.Id == criterionId, ct);

        reloaded.Criteria.SniCodes.ShouldBe([SniItConsulting],
            "uppdaterade SNI-koder måste ha PERSISTERATS, inte bara ändrats i minnet");
        reloaded.Criteria.MunicipalityCodes.ShouldBe([KommunGoteborg],
            "uppdaterade kommunkoder måste ha PERSISTERATS, inte bara ändrats i minnet");
        reloaded.Criteria.SniCodes.ShouldNotContain(SniIt, "den gamla koden får inte ligga kvar");
        reloaded.UpdatedAt.ShouldBe(T0.AddDays(1));
        reloaded.CreatedAt.ShouldBe(T0, "CreatedAt är oföränderlig");
    }

    [Fact]
    public async Task Rename_OnAMaterialisedRow_PersistsTheNewLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var criterionId = await SeedAsync(userId, [SniIt], [KommunStockholm], "Gammalt namn", ct);

        using (var editScope = _fixture.Services.CreateScope())
        {
            var db = editScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterionId, ct);

            criterion.Rename("Nytt namn", new FixedClock(T0.AddDays(2))).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reloaded = await verifyDb.CompanyWatchCriteria
            .AsNoTracking()
            .SingleAsync(c => c.Id == criterionId, ct);
        reloaded.Label.ShouldBe("Nytt namn");
    }

    [Fact]
    public async Task SoftDelete_HidesTheRowFromDefaultQueries_ButKeepsItRetrievableAndIntact()
    {
        // Three things at once, all storage-level:
        //   1. the global query filter (deleted_at IS NULL) hides the row from every ordinary read;
        //   2. IgnoreQueryFilters still retrieves it — which is exactly how the Art. 17 cascade
        //      finds it, and why the user_id index must NOT be partial;
        //   3. the criteria payload is RETAINED on the deleted row (the deliberate contrast with
        //      CompanyWatch, which clears its filter). A gutted row would break the VO's own
        //      invariant, so the domain keeps it and the account cascade is what erases it.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var criterionId = await SeedAsync(userId, [SniIt], [KommunStockholm], "Tas bort", ct);

        using (var deleteScope = _fixture.Services.CreateScope())
        {
            var db = deleteScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterionId, ct);

            criterion.SoftDelete(new FixedClock(T0.AddDays(3)));
            await db.SaveChangesAsync(ct);
        }

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var visible = await verifyDb.CompanyWatchCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == criterionId, ct);
        visible.ShouldBeNull("global query filter ska dölja soft-deletade kriterier");

        var hidden = await verifyDb.CompanyWatchCriteria
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(c => c.Id == criterionId, ct);
        hidden.DeletedAt.ShouldBe(T0.AddDays(3));
        hidden.Criteria.SniCodes.ShouldBe([SniIt],
            "SoftDelete BEHÅLLER kriterierna — en rensad rad hade brutit VO:ts egen invariant");
        hidden.Criteria.MunicipalityCodes.ShouldBe([KommunStockholm]);
    }

    // ---------------------------------------------------------------
    // Index pins — a guarantee nobody has checked is not a guarantee
    // ---------------------------------------------------------------

    [Fact]
    public async Task GinIndex_OnCompanyRegisterSniCodes_Exists_AndUsesGin()
    {
        // ADR 0091 deferred this index for want of a consumer; the criteria wave IS that consumer.
        // The whole "never expand a criterion into per-company rows, invert it into an array-overlap
        // query instead" design rests on it: without a GIN index, `sni_codes && @codes` over ~1.17M
        // register rows is a seq scan on every browse. EF models it via HasMethod("gin") — an
        // annotation that is easy to lose in a model rebuild and impossible to notice, because the
        // query still WORKS, just slowly. This is the tripwire.
        var ct = TestContext.Current.CancellationToken;

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexDef = await IndexDefAsync(db, "ix_company_register_sni_codes_gin", ct);

        indexDef.ShouldNotBeNull("ix_company_register_sni_codes_gin måste finnas i schemat");
        var def = indexDef!;
        def.ShouldContain("USING gin",
            customMessage: "indexet måste vara ett GIN-index — ett btree över text[] stödjer inte &&-overlap");
        def.ShouldContain("sni_codes");
    }

    [Fact]
    public async Task UserIdIndex_OnCompanyWatchCriteria_Exists_AndIsNotPartial()
    {
        // The index is deliberately NOT partial. A "WHERE deleted_at IS NULL" filter would exclude
        // the Art. 17 cascade sweep — which runs IgnoreQueryFilters(), i.e. WITHOUT that predicate —
        // from using it, turning the ERASURE path into a seq scan. A partial index here would look
        // like a harmless optimisation and quietly de-index the one query that must never be slow.
        var ct = TestContext.Current.CancellationToken;

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexDef = await IndexDefAsync(db, "ix_company_watch_criteria_user_id", ct);

        indexDef.ShouldNotBeNull("ix_company_watch_criteria_user_id måste finnas i schemat");
        var def = indexDef!;
        def.ShouldContain("user_id");
        def.ShouldNotContain("WHERE",
            customMessage: "indexet får INTE vara partiellt — Art. 17-sweepen kör IgnoreQueryFilters "
            + "och skulle då tappa indexet i just raderingsvägen");
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<CompanyWatchCriterionId> SeedAsync(
        Guid userId,
        IEnumerable<string> sniCodes,
        IEnumerable<string> municipalityCodes,
        string? label,
        CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var spec = CompanyWatchCriteriaSpec.Create(sniCodes, municipalityCodes);
        spec.IsSuccess.ShouldBeTrue("seed: specen måste vara giltig");

        var criterion = CompanyWatchCriterion.Create(userId, spec.Value, label, new FixedClock(T0));
        criterion.IsSuccess.ShouldBeTrue("seed: kriteriet måste kunna skapas");

        db.CompanyWatchCriteria.Add(criterion.Value);
        await db.SaveChangesAsync(ct);

        return criterion.Value.Id;
    }

    // Reads the raw Postgres array literal (e.g. "{0180,1480}") — the storage-layer truth, unmediated
    // by EF's materialisation. The column name selects a CONSTANT SQL string rather than being
    // interpolated into one: an identifier cannot be parameterised, so the only injection-free way to
    // vary it is to not vary it at all (and it keeps the EF1002 analyzer honest rather than suppressed).
    private static async Task<string> ReadArrayLiteralAsync(
        AppDbContext db, ArrayColumn column, CompanyWatchCriterionId id, CancellationToken ct)
    {
        var sql = column switch
        {
            ArrayColumn.SniCodes =>
                "SELECT sni_codes::text AS \"Value\" FROM company_watch_criteria WHERE id = {0}",
            ArrayColumn.KommunCodes =>
                "SELECT kommun_codes::text AS \"Value\" FROM company_watch_criteria WHERE id = {0}",
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

        var rows = await db.Database.SqlQueryRaw<string>(sql, id.Value).ToListAsync(ct);
        return rows.ShouldHaveSingleItem();
    }

    private enum ArrayColumn
    {
        SniCodes,
        KommunCodes,
    }

    private static async Task<string> ColumnUdtNameAsync(
        AppDbContext db, string column, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT udt_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'company_watch_criteria' AND column_name = {0}
                """,
                column)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem();
    }

    private static async Task<string?> IndexDefAsync(
        AppDbContext db, string indexName, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public' AND indexname = {0}
                """,
                indexName)
            .ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
