using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #1663 PR 2 (ADR 0175 D5, D6) — <c>AddCompanyWatchCriterionAdHits</c>: the hit table with its foreign key,
/// CHECK and three indexes, and four columns on <c>company_watch_criteria</c>, of which
/// <c>criteria_changed_at</c> is filled from <c>created_at</c> for the criteria that exist. Every other table's
/// columns are exactly as before across Up, Down and a second Up. Run as <c>jobbliggaren_app</c>, the role
/// production migrates with.
///
/// <para>
/// The criteria that exist before Up are inserted with SQL at the previous schema: they stand for the rows the
/// code before this migration wrote (<c>CompanyWatchCriterion.Create</c>), which the current model can no longer
/// write there because it maps the new columns. Every row after Up is written by the production factories.
/// </para>
/// </summary>
public sealed class AddCompanyWatchCriterionAdHitsMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261018164310_AddCompanyWatchCriterionAdHits";
    private const string PreviousMigration = "20261017151355_AddAccountActivity";
    private const string HitTable = "company_watch_criterion_ad_hits";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] NewCriterionColumns =
        ["criteria_changed_at", "last_scan_outcome", "new_ads_seen_through", "scanned_through"];
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(Ct);
        _appConnectionString = await TestDatabaseProvisioner.ProvisionAndGetAppConnectionStringAsync(
            _postgres.GetConnectionString(), ct: Ct);
        await using var superuser = new NpgsqlConnection(_postgres.GetConnectionString());
        await superuser.OpenAsync(Ct);
        await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
        await extension.ExecuteNonQueryAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task MigrateAsync_ShouldAddTheHitTableAndTheScanColumns_FillingCriteriaChangedAtFromCreatedAt_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await HitColumnsAsync()).ShouldBeEmpty();
        var previousSchema = await ReadSchemaOutsideThisMigrationAsync();
        var older = await InsertCriterionAtThePreviousSchemaAsync(Now.AddDays(-40));
        var newer = await InsertCriterionAtThePreviousSchemaAsync(Now.AddDays(-2));

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync();
        (await ReadSchemaOutsideThisMigrationAsync()).ShouldBe(previousSchema);
        (await CriteriaChangedAtAsync(older)).ShouldBe(Now.AddDays(-40));
        (await CriteriaChangedAtAsync(newer)).ShouldBe(Now.AddDays(-2));

        // The production factories write a criterion and its hit; the database holds the key, the CHECK and the
        // cascade.
        var (criterionId, jobAdId) = await WriteCriterionWithHitAsync();
        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => WriteHitAsync(criterionId, jobAdId));
        duplicate.InnerException.ShouldBeOfType<PostgresException>().SqlState
            .ShouldBe(PostgresErrorCodes.UniqueViolation);
        var check = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            $"UPDATE {HitTable} SET dismissed_from_notification_feed = true WHERE company_watch_criterion_id = @id",
            criterionId));
        check.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        await ExecuteAsync("DELETE FROM company_watch_criteria WHERE id = @id", criterionId);
        (await CountHitsAsync(criterionId)).ShouldBe(0, "deleting a criterion takes its hits with it");

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await HitColumnsAsync()).ShouldBeEmpty();
        (await CriterionColumnNamesAsync()).ShouldNotContain(c => NewCriterionColumns.Contains(c));
        (await ReadSchemaOutsideThisMigrationAsync()).ShouldBe(previousSchema);

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync();
        (await ReadSchemaOutsideThisMigrationAsync()).ShouldBe(previousSchema);
        (await CriteriaChangedAtAsync(older)).ShouldBe(Now.AddDays(-40));
    }

    private async Task AssertUpAsync()
    {
        (await HitColumnsAsync()).ShouldBe([
            "company_watch_criterion_id|uuid|NO|",
            "created_at|timestamp with time zone|NO|",
            "dismissed_from_notification_feed|boolean|NO|false",
            "id|uuid|NO|",
            "job_ad_id|uuid|NO|",
            "read_in_notification_feed|boolean|NO|false",
            "user_id|uuid|NO|",
        ]);
        (await ConstraintsAsync()).ShouldBe([
            "ck_company_watch_criterion_ad_hits_dismissed_implies_read|c||",
            "fk_company_watch_criterion_ad_hits_company_watch_criterion_id|f|company_watch_criteria|c",
            "pk_company_watch_criterion_ad_hits|p||",
        ]);
        (await IndexDefinitionsAsync()).ShouldBe([
            "ix_company_watch_criterion_ad_hits_user_created_at|CREATE INDEX ix_company_watch_criterion_ad_hits_user_created_at ON public.company_watch_criterion_ad_hits USING btree (user_id, created_at DESC)",
            "ix_company_watch_criterion_ad_hits_user_jobad|CREATE INDEX ix_company_watch_criterion_ad_hits_user_jobad ON public.company_watch_criterion_ad_hits USING btree (user_id, job_ad_id)",
            "pk_company_watch_criterion_ad_hits|CREATE UNIQUE INDEX pk_company_watch_criterion_ad_hits ON public.company_watch_criterion_ad_hits USING btree (id)",
            "ux_company_watch_criterion_ad_hits_criterion_jobad|CREATE UNIQUE INDEX ux_company_watch_criterion_ad_hits_criterion_jobad ON public.company_watch_criterion_ad_hits USING btree (company_watch_criterion_id, job_ad_id)",
        ], "three indexes beside the key, the follow table's count; the FK leads the UNIQUE, so EF adds none of its own");
        (await NewCriterionColumnsAsync()).ShouldBe([
            "criteria_changed_at|timestamp with time zone|NO|",
            "last_scan_outcome|character varying|YES|",
            "new_ads_seen_through|timestamp with time zone|YES|",
            "scanned_through|timestamp with time zone|YES|",
        ], "criteria_changed_at carries no physical default: the domain sets it on every insert");
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task<Guid> InsertCriterionAtThePreviousSchemaAsync(DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO company_watch_criteria (id, user_id, sni_codes, kommun_codes, label, created_at, updated_at)
            VALUES (@id, @user, ARRAY['62010'], ARRAY['0180'], NULL, @created, @created)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", Guid.NewGuid());
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync(Ct);
        return id;
    }

    private async Task<(CompanyWatchCriterionId, JobAdId)> WriteCriterionWithHitAsync()
    {
        var clock = new FixedClock(Now);
        var criterion = CompanyWatchCriterion.Create(
            Guid.NewGuid(), CompanyWatchCriteriaSpec.Create(["62010"], ["0180"]).Value, null, clock).Value;
        var jobAdId = JobAdId.New();
        await using var writer = NewAppContext();
        writer.CompanyWatchCriteria.Add(criterion);
        writer.CompanyWatchCriterionAdHits.Add(CompanyWatchCriterionAdHit.Create(criterion, jobAdId, clock).Value);
        await writer.SaveChangesAsync(Ct);
        return (criterion.Id, jobAdId);
    }

    private async Task WriteHitAsync(CompanyWatchCriterionId criterionId, JobAdId jobAdId)
    {
        await using var writer = NewAppContext();
        var criterion = await writer.CompanyWatchCriteria.SingleAsync(c => c.Id == criterionId, Ct);
        writer.CompanyWatchCriterionAdHits.Add(
            CompanyWatchCriterionAdHit.Create(criterion, jobAdId, new FixedClock(Now)).Value);
        await writer.SaveChangesAsync(Ct);
    }

    private async Task ExecuteAsync(string sql, CompanyWatchCriterionId criterionId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", criterionId.Value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<int> CountHitsAsync(CompanyWatchCriterionId criterionId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"SELECT count(*)::int FROM {HitTable} WHERE company_watch_criterion_id = @id", connection);
        command.Parameters.AddWithValue("id", criterionId.Value);
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<DateTimeOffset> CriteriaChangedAtAsync(Guid criterionId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT criteria_changed_at FROM company_watch_criteria WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", criterionId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    private Task<List<string>> HitColumnsAsync() => ReadStringsAsync(
        $"""
        SELECT column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = '{HitTable}'
        ORDER BY column_name
        """);

    private Task<List<string>> NewCriterionColumnsAsync() => ReadStringsAsync(
        """
        SELECT column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'company_watch_criteria'
          AND column_name IN ('criteria_changed_at', 'last_scan_outcome', 'new_ads_seen_through', 'scanned_through')
        ORDER BY column_name
        """);

    private Task<List<string>> CriterionColumnNamesAsync() => ReadStringsAsync(
        """
        SELECT column_name FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'company_watch_criteria'
        ORDER BY column_name
        """);

    private Task<List<string>> ConstraintsAsync() => ReadStringsAsync(
        $"""
        SELECT c.conname || '|' || c.contype::text || '|' || coalesce(r.relname, '') || '|'
            || CASE WHEN c.contype = 'f' THEN c.confdeltype::text ELSE '' END
        FROM pg_constraint c
        LEFT JOIN pg_class r ON r.oid = c.confrelid
        WHERE c.conrelid = 'public.{HitTable}'::regclass AND c.contype <> 'n'
        ORDER BY c.conname
        """);

    private Task<List<string>> IndexDefinitionsAsync() => ReadStringsAsync(
        $"SELECT indexname || '|' || indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = '{HitTable}' ORDER BY indexname");

    // Every column outside this migration's own: the hit table and the four new criterion columns are left out.
    private Task<List<string>> ReadSchemaOutsideThisMigrationAsync() => ReadStringsAsync(
        $"""
        SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name <> '{HitTable}'
          AND NOT (table_name = 'company_watch_criteria'
                   AND column_name IN ('criteria_changed_at', 'last_scan_outcome', 'new_ads_seen_through', 'scanned_through'))
        ORDER BY table_name, column_name
        """);

    private async Task<List<string>> ReadStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            values.Add(reader.GetString(0));
        return values;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
