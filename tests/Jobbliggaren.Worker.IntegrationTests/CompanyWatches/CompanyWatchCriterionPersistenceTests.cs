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
/// detection, the two indexes, and the physical absence of the demolished <c>deleted_at</c> column.
///
/// <para>
/// <b>The load-bearing test is <see cref="UpdateCriteria_OnAMaterialisedRow_PersistsTheNewCodes"/>.</b>
/// <c>ApplyCriteria</c> mutates the backing lists IN PLACE (Clear + AddRange), so the tracked entity's
/// list instance never changes identity. If EF snapshotted the collection BY REFERENCE, the "original"
/// and the "current" value would be the SAME object, EF would see no change, and <c>SaveChanges</c>
/// would emit no array update: the user edits their criterion, gets a 200, and the old predicate
/// silently stays live. This test is what stands between us and that failure — it is invisible to
/// every unit test and to InMemory, and only a real round-trip in a NEW context catches it.
/// </para>
///
/// <para>
/// <b>What actually prevents it</b> (mutation-verified 2026-07-13, code-reviewer Minor 3): Npgsql's
/// array type mapping supplies its own deep comparer, so the deep snapshot holds even with the
/// configuration's explicit <c>SetValueComparer</c> calls commented out — this suite stays green.
/// The explicit comparer is kept as defense-in-depth and house precedent, but it is NOT the thing
/// doing the work, and no comment here should say otherwise.
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
                "SaveChanges ska rapportera en skriven rad (UpdateCriteria stämplar även UpdatedAt). "
                + "OBS: detta är INTE snapshot-orakeln — en by-reference-snapshot av arrayerna hade "
                + "ändå gett 1 här (UpdatedAt är en skalär ändring), bara utan array-kolumnerna. "
                + "Beviset ligger i scope 3 nedan, som läser tillbaka koderna ur Postgres.");
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
    public async Task DeletedAtColumn_IsPhysicallyGone_FromTheCriteriaTable()
    {
        // This pin guards the SNAPSHOT → PHYSICAL DATABASE link, and it is the only thing that does.
        // Be precise about the division of labour, because overstating a guard is the exact sin this
        // PR exists to correct:
        //
        //   * model ≠ snapshot          → EF's own PendingModelChangesWarning throws at MigrateAsync,
        //                                 and it is LOUD (measured 2026-07-17: dropping DeletedAt from
        //                                 the model with no migration took all 8 tests in this class
        //                                 down at fixture setup, not just this one).
        //   * snapshot ≠ real table     → NOTHING else looks. A hand-written Up() that updates the
        //                                 snapshot but drops the wrong thing, or nothing at all,
        //                                 satisfies EF completely: the model matches the snapshot, so
        //                                 no warning fires, and the column lives on. That gap is what
        //                                 this test closes — and hand-written migrations are exactly
        //                                 where it opens.
        //
        // The column is the one the C-D8/G1 verdict condemned: nothing ever wrote it (delete is
        // HARD), so it holds no data and its drop destroys nothing. That is exactly why it may go
        // with a plain DROP COLUMN rather than the DROP EXPRESSION dance a computed column needs.
        var ct = TestContext.Current.CancellationToken;

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await ColumnExistsAsync(db, "deleted_at", ct);

        exists.ShouldBeFalse(
            "deleted_at ska vara fysiskt borta ur company_watch_criteria — kolumnen var en decoy "
            + "som ingen skrivväg någonsin satte (C-D8/G1: delete är HARD)");

        // Self-proving negative: the same probe finds a column that IS there. Without this, a
        // helper that silently answered "false" for every name — a typo'd table, a changed
        // information_schema shape — would make the assertion above pass vacuously forever.
        (await ColumnExistsAsync(db, "sni_codes", ct)).ShouldBeTrue(
            "kontroll-probe: helpern måste kunna SE en kolumn som finns, annars bevisar raden ovan "
            + "ingenting");
    }

    // No "the filter is gone, so ordinary reads see every row" test lives here, deliberately. One
    // was written for this PR and deleted before merge, because it could not fail:
    //   * its ordinary-read half duplicated RoundTrip_OnAMaterialisedRow (which already reads the
    //     seeded row back through a plain AsNoTracking query);
    //   * its "same row through IgnoreQueryFilters" half was TAUTOLOGICAL — a filter-ignoring read is
    //     strictly wider than a plain one on the same primary key, so if the plain read found the row
    //     the wider read cannot fail to;
    //   * and it claimed to catch "the demolition being half-done", which this PR's own mutation M4
    //     falsified: with the migration's Up() neutered it stayed GREEN (7/8), while only
    //     DeletedAtColumn_IsPhysicallyGone went red.
    // A freshly seeded row passes any filter that does not exclude fresh rows, so no seed-and-read
    // shape can prove "no filter exists". The property is real and IS guarded — by the EF model, at
    // build time: re-adding HasQueryFilter to this aggregate flips it into the filtered set and
    // AccountHardDeleteCascadeFitnessTests immediately demands the matching IgnoreQueryFilters in the
    // Art. 17 cascade. That guard can fail; this test could not. Keeping it would have been the exact
    // thing this PR condemns — a mechanism claiming more than it does (code-reviewer Major 2).

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
        // THIS is what proves the deleted_at drop did not take the index with it. DROP COLUMN
        // silently drops every index that depends on the column, and the EF model snapshot is blind
        // to it — the migration would report success and the erasure sweep would degrade to a seq
        // scan over every criterion in the table, with nothing red anywhere. The index is
        // independent of deleted_at (plain, on user_id alone), so it MUST survive; this pin is the
        // difference between believing that and knowing it.
        //
        // Non-partial is now structural rather than a judgement call: with the lifecycle column gone
        // there is no predicate left to make it partial with. The assertion stays as the tripwire
        // for anyone who reintroduces one.
        var ct = TestContext.Current.CancellationToken;

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexDef = await IndexDefAsync(db, "ix_company_watch_criteria_user_id", ct);

        indexDef.ShouldNotBeNull("ix_company_watch_criteria_user_id måste finnas i schemat");
        var def = indexDef!;
        def.ShouldContain("user_id");
        def.ShouldNotContain("WHERE",
            customMessage: "indexet får INTE vara partiellt — Art. 17-sweepen läser HELA "
            + "user_id-mängden (kriteriet har inget query filter) och ett partiellt index skulle "
            + "tappas i just raderingsvägen");
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

    // Physical column existence, straight from the catalog. The column name is PARAMETERISED (a
    // value in the WHERE clause, not an identifier spliced into the SQL) — same discipline as
    // ColumnUdtNameAsync above.
    private static async Task<bool> ColumnExistsAsync(
        AppDbContext db, string column, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'company_watch_criteria' AND column_name = {0}
                """,
                column)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem() > 0;
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
