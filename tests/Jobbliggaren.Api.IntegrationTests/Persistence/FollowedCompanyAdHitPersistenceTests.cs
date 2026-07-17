using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Persistence;

/// <summary>
/// #868 — schema pins for <c>followed_company_ad_hits</c> after the writerless soft-delete axis was
/// retired (migration <c>RetireMatchAndHitDeletedAtAxis</c>, parity the <c>user_job_ad_matches</c>
/// pins in <see cref="UserJobAdMatchPersistenceTests"/>). Testcontainers, not InMemory: the physical
/// column drop and the index survival are facts only a real Postgres can hold — the EF model snapshot
/// is blind to a DROP COLUMN that takes an index with it (#821's lesson).
///
/// <para>
/// No "the filter is gone, so ordinary reads see every row" test lives here, deliberately (the #915
/// lesson): a freshly seeded row passes any filter that does not exclude fresh rows, so no seed-and-read
/// shape can prove "no filter exists". That property is guarded at build time by the EF model — re-adding
/// <c>HasQueryFilter</c> flips this aggregate into <c>AccountHardDeleteCascadeFitnessTests</c>' filtered
/// set, which then demands the matching <c>IgnoreQueryFilters</c> in the Art. 17 cascade. That guard can
/// fail; a seed-and-read test could not.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class FollowedCompanyAdHitPersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task DeletedAtColumn_IsPhysicallyGone_FromTheHitsTable()
    {
        // Guards the SNAPSHOT → PHYSICAL DATABASE link, the one thing model == snapshot cannot: EF's
        // PendingModelChangesWarning fires on model ≠ snapshot, but a hand-written Up() that updates the
        // snapshot while dropping the wrong thing (or nothing) satisfies EF completely. Only a read of
        // information_schema after the migration ran closes that gap.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await ColumnExistsAsync(db, "deleted_at", ct)).ShouldBeFalse(
            "deleted_at ska vara fysiskt borta ur followed_company_ad_hits — en writerless decoy (#868)");

        // Self-proving positive: the same probe finds a column that IS there, so the assertion above
        // cannot pass vacuously (a typo'd table / changed information_schema shape).
        (await ColumnExistsAsync(db, "notification_status", ct)).ShouldBeTrue(
            "kontroll-probe: helpern måste kunna SE en kolumn som finns, annars bevisar raden inget");
    }

    [Theory]
    [InlineData("ux_followed_company_ad_hits_user_jobad_watch")]
    [InlineData("ix_followed_company_ad_hits_user_status")]
    [InlineData("ix_followed_company_ad_hits_user_id")]
    public async Task Index_SurvivesTheDeletedAtDrop_AndIsNotPartial(string indexName)
    {
        // DROP COLUMN silently drops every index whose predicate names the column, and the EF model
        // snapshot is blind to it (#821). None of these three names deleted_at — all are plain B-trees
        // (FollowedCompanyAdHitConfiguration documents the dispatch index as a FULL B-tree explicitly) —
        // so the drop must take none of them; ix_..._user_id in particular serves the Art. 17 sweep.
        // Non-partial is structural now: with the column gone there is no predicate left to make one
        // partial. The assertion stays as the tripwire against re-adding one.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var indexDef = await IndexDefAsync(db, indexName, ct);
        indexDef.ShouldNotBeNull($"{indexName} måste finnas i schemat efter deleted_at-droppen");
        indexDef!.ShouldNotContain("WHERE",
            customMessage: $"{indexName} får INTE vara partiellt — det finns inget deleted_at-predikat kvar");
    }

    // Physical column existence, straight from the catalog. Column name PARAMETERISED (a value in the
    // WHERE clause, never an identifier spliced into SQL). Precedent: CompanyWatchCriterionPersistenceTests.
    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string column, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'followed_company_ad_hits' AND column_name = {0}
                """,
                column)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem() > 0;
    }

    private static async Task<string?> IndexDefAsync(AppDbContext db, string indexName, CancellationToken ct)
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
}
