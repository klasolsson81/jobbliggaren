using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — schema + behavioural pins for the BRAND_GROUP extension of
/// <c>company_watches</c> (migration <c>AddCompanyWatchBrandGroupTarget</c>). Testcontainers, not
/// InMemory: the two partial UNIQUEs, the NULLS-DISTINCT coexistence, and the physical column shape are
/// facts only a real Postgres holds — the EF model snapshot is blind to a partial-index predicate and to
/// PG's NULL-uniqueness semantics (#821/#915 lesson).
/// </summary>
[Collection("Api")]
public sealed class CompanyWatchBrandGroupPersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static readonly FixedClock Clock =
        new(new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task OrganizationNumber_IsNullable_AndBrandGroupIdColumn_Exists()
    {
        // A BRAND_GROUP row carries NULL org.nr — the widen must have landed physically (a snapshot-only
        // change would pass EF but leave the DB NOT NULL, rejecting every group insert at runtime).
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await IsNullableAsync(db, "organization_number", ct)).ShouldBeTrue(
            "organization_number måste vara NULLABLE — en BRAND_GROUP-rad bär inget org.nr");
        (await ColumnExistsAsync(db, "brand_group_id", ct)).ShouldBeTrue(
            "brand_group_id-kolumnen måste finnas efter migrationen");
        // Self-proving negative: a column that does NOT exist reports false, so the assertions above
        // cannot pass vacuously against a typo'd table name.
        (await ColumnExistsAsync(db, "definitely_not_a_column", ct)).ShouldBeFalse(
            "kontroll-probe: helpern måste kunna SE frånvaron av en kolumn");
    }

    [Fact]
    public async Task BothPartialUniqueIndexes_Exist_AndOnlyTheBrandGroupOneCarriesTheNotNullPredicate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The original org.nr index is untouched (byte-identical filter) …
        var orgNrDef = await IndexDefAsync(db, "ux_company_watches_user_orgnr_active", ct);
        orgNrDef.ShouldNotBeNull("ux_company_watches_user_orgnr_active måste finnas kvar");
        orgNrDef!.ShouldContain("deleted_at IS NULL");

        // … and the mirror index carries the extra `brand_group_id IS NOT NULL` so EMPLOYER rows (NULL
        // group id) never enter it — the two partial uniques are disjoint by construction.
        var groupDef = await IndexDefAsync(db, "ux_company_watches_user_brand_group_active", ct);
        groupDef.ShouldNotBeNull("ux_company_watches_user_brand_group_active måste finnas");
        groupDef!.ShouldContain("brand_group_id IS NOT NULL");
        groupDef.ShouldContain("deleted_at IS NULL");
    }

    [Fact]
    public async Task EmployerAndTwoGroupWatches_ForOneUser_AllPersist_ViaNullsDistinctCoexistence()
    {
        // One user follows an employer (org.nr, NULL group) AND two distinct brand groups (NULL org.nr,
        // slug set). The EMPLOYER row enters ux_...orgnr_active with a NULL org-key part; the two GROUP
        // rows enter ux_...brand_group_active with distinct slugs — all three coexist (PG NULLS DISTINCT).
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.CompanyWatches.Add(Employer(userId, "5560125790"));
        db.CompanyWatches.Add(Group(userId, "volvo-koncernen"));
        db.CompanyWatches.Add(Group(userId, "h-och-m"));
        await db.SaveChangesAsync(ct);

        var persisted = await db.CompanyWatches.IgnoreQueryFilters()
            .Where(w => w.UserId == userId).ToListAsync(ct);
        persisted.Count.ShouldBe(3);
        persisted.Count(w => w.TargetType == CompanyWatchTargetType.Employer).ShouldBe(1);
        persisted.Count(w => w.TargetType == CompanyWatchTargetType.BrandGroup).ShouldBe(2);
    }

    [Fact]
    public async Task TwoActiveGroupWatches_SameUserSameGroup_AreRejected_ByThePartialUnique()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.CompanyWatches.Add(Group(userId, "volvo-koncernen"));
        db.CompanyWatches.Add(Group(userId, "volvo-koncernen"));

        await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task GroupWatch_AfterSoftDelete_CanBeReinserted_ThePartialUniqueIgnoresDeletedRows()
    {
        // The active-partial predicate is `deleted_at IS NULL`, so a soft-deleted group row does not block
        // a fresh active follow of the same group (parity the org.nr resurrect story). Two PHYSICAL rows is
        // fine at the DB layer here — the resurrect handler keeps it to one at the app layer (Commit 4).
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = Group(userId, "volvo-koncernen");
        first.SoftDelete(Clock);
        db.CompanyWatches.Add(first);
        await db.SaveChangesAsync(ct);

        db.CompanyWatches.Add(Group(userId, "volvo-koncernen"));
        await Should.NotThrowAsync(async () => await db.SaveChangesAsync(ct));
    }

    private static CompanyWatch Employer(Guid userId, string orgNr) =>
        CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, Clock).Value;

    private static CompanyWatch Group(Guid userId, string slug) =>
        CompanyWatch.FollowBrandGroup(userId, BrandGroupId.Create(slug).Value, Clock).Value;

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string column, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'company_watches' AND column_name = {0}
                """,
                column)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem() > 0;
    }

    private static async Task<bool> IsNullableAsync(AppDbContext db, string column, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'company_watches' AND column_name = {0}
                """,
                column)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem() == "YES";
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
