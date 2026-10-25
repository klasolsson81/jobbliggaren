using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Email.Dmarc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using static Jobbliggaren.Worker.IntegrationTests.Email.Dmarc.DmarcTestReports;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #1981 PR 4a (ADR 0184) — <c>AddDmarcAggregateReports</c> is additive: four new tables, four indexes, no seed, and
/// every other table's columns exactly as before, across Up, Down and a second Up. Run as <c>jobbliggaren_app</c>, the
/// role production migrates with, so the role that owns the tables is the one the ingestion's ANALYZE runs as.
/// </summary>
public sealed class AddDmarcAggregateReportsMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261024143837_AddDmarcAggregateReports";
    private const string PreviousMigration = "20261023051327_AddEmailSendDailyCounts";
    private const string NewTablesList =
        "'dmarc_reports', 'dmarc_report_rows', 'dmarc_mailbox_messages', 'dmarc_mailbox_state'";
    private const string Digest32 = "decode(repeat('ab', 32), 'hex')";
    private const string ReportId = "'00000000-0000-0000-0000-0000000000a1'";
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
    public async Task MigrateAsync_ShouldCreateTheFourTablesEmptyLeavingEveryOtherTableAsItWas_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ColumnsAsync()).ShouldBeEmpty();
        var previousSchema = await ReadSchemaWithoutTheNewTablesAsync();

        await migrator.MigrateAsync(ThisMigration, Ct);
        await AssertUpAsync();
        (await ReadSchemaWithoutTheNewTablesAsync()).ShouldBe(previousSchema);

        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ColumnsAsync()).ShouldBeEmpty("Down drops all four tables and their contents");
        (await Strings($"SELECT tablename FROM pg_tables WHERE tablename IN ({NewTablesList})")).ShouldBeEmpty();
        (await ReadSchemaWithoutTheNewTablesAsync()).ShouldBe(previousSchema);

        await migrator.MigrateAsync(ThisMigration, Ct);
        await AssertUpAsync();
        (await ReadSchemaWithoutTheNewTablesAsync()).ShouldBe(previousSchema);
    }

    [Fact]
    public async Task TheConstraints_ShouldRefuseEveryValueTheDdlBounds_AsTheAppRole()
    {
        await MigrateUpAsync();
        await ExecuteAsync(ReportInsert(ReportId, "'r.example'", "'id-1'", Digest32));
        await ExecuteAsync(
            "INSERT INTO dmarc_mailbox_state (id, last_attempt_at, last_outcome, uid_validity, last_uid) "
            + "VALUES (1, now(), 1, 7, 0)");
        await ExecuteAsync(
            "INSERT INTO dmarc_mailbox_messages (uid_validity, uid, received_at, outcome, attempts, retries) "
            + "VALUES (7, 1, now(), 1, 1, 0)");

        // dmarc_reports
        await ShouldViolateAsync(
            "INSERT INTO dmarc_reports (id, reporter, reporter_report_id, content_sha256, begins_at, ends_at, received_at) "
            + $"VALUES (gen_random_uuid(), 'r.example', 'id-2', {Digest32}, '2026-10-02', '2026-10-02', '2026-10-03')",
            "23514");
        await ShouldViolateAsync(ReportInsert("gen_random_uuid()", "'r.example'", "'id-3'", "decode(repeat('ab', 31), 'hex')"), "23514");
        await ShouldViolateAsync(ReportInsert("gen_random_uuid()", "'r.example'", "'id-4'", "decode(repeat('ab', 33), 'hex')"), "23514");
        await ShouldViolateAsync(ReportInsert("gen_random_uuid()", "'r.example'", "'id-1'", Digest32), "23505");
        await ShouldViolateAsync(ReportInsert(ReportId, "'r.example'", "'id-5'", Digest32), "23505");
        await ShouldViolateAsync(ReportInsert("gen_random_uuid()", "repeat('r', 256)", "'id-6'", Digest32), "22001");

        // dmarc_report_rows
        await ShouldViolateAsync(RowInsert("-1", "'192.0.2.1'", "1", "'jobbliggaren.se'", "1", "1"), "23514");
        await ShouldViolateAsync(RowInsert("0", "'192.0.2.1'", "0", "'jobbliggaren.se'", "1", "1"), "23514");
        await ShouldViolateAsync(RowInsert("0", "'192.0.2.1'", "1", "'jobbliggaren.se'", "0", "1"), "23514");
        await ShouldViolateAsync(RowInsert("0", "'192.0.2.1'", "1", "'jobbliggaren.se'", "1", "0"), "23514");
        await ShouldViolateAsync(RowInsert("0", "'192.0.2.1'", "1", "repeat('a', 254)", "1", "1"), "22001");
        await ShouldViolateAsync(RowInsert("0", "'not-an-address'", "1", "'jobbliggaren.se'", "1", "1"), "22P02");
        await ExecuteAsync(RowInsert("0", "'192.0.2.1'", "1", "'jobbliggaren.se'", "1", "1"));
        await ShouldViolateAsync(RowInsert("0", "'192.0.2.2'", "1", "'jobbliggaren.se'", "1", "1"), "23505");
        await ShouldViolateAsync(
            RowInsert("0", "'192.0.2.1'", "1", "'jobbliggaren.se'", "1", "1")
                .Replace(ReportId, "gen_random_uuid()", StringComparison.Ordinal),
            "23503");

        // dmarc_mailbox_messages
        await ShouldViolateAsync(LedgerInsert("0", "2", "1", "1", "0"), "23514");
        await ShouldViolateAsync(LedgerInsert("7", "0", "1", "1", "0"), "23514");
        await ShouldViolateAsync(LedgerInsert("7", "2", "0", "1", "0"), "23514");
        await ShouldViolateAsync(LedgerInsert("7", "2", "1", "0", "0"), "23514");
        await ShouldViolateAsync(LedgerInsert("7", "2", "1", "3", "0"), "23514");
        await ShouldViolateAsync(LedgerInsert("7", "2", "1", "1", "-1"), "23514");
        await ShouldViolateAsync(LedgerInsert("7", "2", "1", "1", "NULL"), "23502");
        await ShouldViolateAsync(LedgerInsert("7", "1", "1", "1", "0"), "23505");

        // dmarc_mailbox_state
        await ShouldViolateAsync(StateInsert("2", "1", "NULL", "NULL"), "23514");
        await ShouldViolateAsync(StateInsert("1", "0", "NULL", "NULL"), "23514");
        await ShouldViolateAsync("UPDATE dmarc_mailbox_state SET last_uid = -1", "23514");
        await ShouldViolateAsync("UPDATE dmarc_mailbox_state SET uid_validity = 0", "23514");
        await ShouldViolateAsync("UPDATE dmarc_mailbox_state SET last_uid = NULL", "23514");
        await ShouldViolateAsync("UPDATE dmarc_mailbox_state SET uid_validity = NULL", "23514");
        await ShouldViolateAsync(StateInsert("1", "1", "NULL", "NULL"), "23505");
    }

    [Fact]
    public async Task TheSchema_ShouldKeepAnotherDigestOfTheSameReportAndCascadeARemovedReportsRecords()
    {
        await MigrateUpAsync();
        await ExecuteAsync(ReportInsert(ReportId, "'r.example'", "'id-1'", Digest32));
        await ExecuteAsync(ReportInsert("gen_random_uuid()", "'r.example'", "'id-1'", "decode(repeat('cd', 32), 'hex')"));
        await ExecuteAsync(RowInsert("0", "'192.0.2.1'", "3", "'jobbliggaren.se'", "1", "2"));
        await ExecuteAsync(RowInsert("1", "'2001:db8::1'", "4", "'jobbliggaren.se'", "2", "1"));
        await ExecuteAsync(RowInsert("2", "'::ffff:198.51.100.7'", "5", "'jobbliggaren.se'", "1", "1"));

        (await Strings(
            "SELECT host(source_ip) || '|' || family(source_ip) FROM dmarc_report_rows ORDER BY ordinal"))
            .ShouldBe(
                ["192.0.2.1|4", "2001:db8::1|6", "::ffff:198.51.100.7|6"],
                "inet keeps a mapped address as IPv6, which is why the store writes only canonical addresses");
        (await Strings("SELECT count(*)::text FROM dmarc_reports WHERE reporter_report_id = 'id-1'")).ShouldBe(["2"]);

        await ExecuteAsync($"DELETE FROM dmarc_reports WHERE id = {ReportId}");

        (await Strings("SELECT count(*)::text FROM dmarc_report_rows")).ShouldBe(["0"]);
    }

    /// <summary>
    /// The production store's ANALYZE, run as the role that migrated the tables. A role that does not own a table only
    /// gets a warning and the table is skipped, so the proof is the planner's row estimate moving to what was stored.
    /// </summary>
    [Fact]
    public async Task TheStoresAnalyze_ShouldRefreshAllThreeTablesStatistics_AsTheAppRole()
    {
        await MigrateUpAsync();
        var receivedAt = new DateTimeOffset(2031, 3, 4, 10, 0, 0, TimeSpan.Zero);
        await using (var db = NewAppContext())
        {
            var driver = new DmarcIngestionDriver(Store(db));
            await driver.StartAsync(receivedAt);
            (await driver.StoreAsync(Accepted(
                "analyze-1", receivedAt, [Record("192.0.2.10", 1), Record("192.0.2.11", 2), Record("2001:db8::2", 3)])))
                .ShouldBe(Jobbliggaren.Application.Admin.Email.Dmarc.DmarcMessageOutcome.Stored);
        }

        var before = await ReltuplesAsync();
        before.ShouldNotBe("dmarc_mailbox_messages=1|dmarc_report_rows=3|dmarc_reports=1");

        await using (var db = NewAppContext())
            await Store(db).AnalyzeAsync(Ct);

        (await ReltuplesAsync()).ShouldBe("dmarc_mailbox_messages=1|dmarc_report_rows=3|dmarc_reports=1");
        (await Strings(
            $"SELECT DISTINCT tableowner FROM pg_tables WHERE tablename IN ({NewTablesList})"))
            .ShouldBe(["jobbliggaren_app"]);
    }

    private async Task AssertUpAsync()
    {
        (await ColumnsAsync()).ShouldBe([
            "dmarc_mailbox_messages.attempts|smallint||NO|NO|",
            "dmarc_mailbox_messages.outcome|smallint||NO|NO|",
            "dmarc_mailbox_messages.received_at|timestamp with time zone||NO|NO|",
            "dmarc_mailbox_messages.retries|smallint||NO|NO|",
            "dmarc_mailbox_messages.uid|bigint||NO|NO|",
            "dmarc_mailbox_messages.uid_validity|bigint||NO|NO|",
            "dmarc_mailbox_state.id|integer||NO|NO|",
            "dmarc_mailbox_state.last_attempt_at|timestamp with time zone||NO|NO|",
            "dmarc_mailbox_state.last_outcome|smallint||NO|NO|",
            "dmarc_mailbox_state.last_success_at|timestamp with time zone||YES|NO|",
            "dmarc_mailbox_state.last_uid|bigint||YES|NO|",
            "dmarc_mailbox_state.uid_validity|bigint||YES|NO|",
            "dmarc_report_rows.dkim_aligned|smallint||NO|NO|",
            $"dmarc_report_rows.header_from|character varying|{DmarcIngestionLimits.MaxHeaderFromLength}|NO|NO|",
            "dmarc_report_rows.message_count|integer||NO|NO|",
            "dmarc_report_rows.ordinal|integer||NO|NO|",
            "dmarc_report_rows.report_id|uuid||NO|NO|",
            "dmarc_report_rows.source_ip|inet||NO|NO|",
            "dmarc_report_rows.spf_aligned|smallint||NO|NO|",
            "dmarc_reports.begins_at|timestamp with time zone||NO|NO|",
            "dmarc_reports.content_sha256|bytea||NO|NO|",
            "dmarc_reports.ends_at|timestamp with time zone||NO|NO|",
            "dmarc_reports.id|uuid||NO|NO|",
            "dmarc_reports.received_at|timestamp with time zone||NO|NO|",
            $"dmarc_reports.reporter|character varying|{DmarcIngestionLimits.MaxFieldLength}|NO|NO|",
            $"dmarc_reports.reporter_report_id|character varying|{DmarcIngestionLimits.MaxFieldLength}|NO|NO|",
        ]);
        (await ConstraintsAsync()).ShouldBe([
            $"ck_dmarc_mailbox_messages_positive|CHECK (((uid_validity > 0) AND (uid > 0) AND (outcome > 0) AND ((attempts >= 1) AND (attempts <= {DmarcIngestionLimits.MaxCrashAttempts})) AND (retries >= 0)))",
            "ck_dmarc_mailbox_state_positive|CHECK (((last_outcome > 0) AND ((uid_validity IS NULL) OR (uid_validity > 0)) AND ((last_uid IS NULL) OR (last_uid >= 0)) AND ((uid_validity IS NULL) = (last_uid IS NULL))))",
            "ck_dmarc_mailbox_state_singleton|CHECK ((id = 1))",
            "ck_dmarc_report_rows_positive|CHECK (((ordinal >= 0) AND (message_count > 0) AND (dkim_aligned > 0) AND (spf_aligned > 0)))",
            "ck_dmarc_reports_content_sha256|CHECK ((octet_length(content_sha256) = 32))",
            "ck_dmarc_reports_period|CHECK ((ends_at > begins_at))",
            "fk_dmarc_report_rows_dmarc_reports_report_id|FOREIGN KEY (report_id) REFERENCES dmarc_reports(id) ON DELETE CASCADE",
            "pk_dmarc_mailbox_messages|PRIMARY KEY (uid_validity, uid)",
            "pk_dmarc_mailbox_state|PRIMARY KEY (id)",
            "pk_dmarc_report_rows|PRIMARY KEY (report_id, ordinal)",
            "pk_dmarc_reports|PRIMARY KEY (id)",
        ]);
        (await IndexesAsync()).ShouldBe([
            "ix_dmarc_mailbox_messages_received_at|CREATE INDEX ix_dmarc_mailbox_messages_received_at ON public.dmarc_mailbox_messages USING btree (received_at)",
            "ix_dmarc_reports_ends_at|CREATE INDEX ix_dmarc_reports_ends_at ON public.dmarc_reports USING btree (ends_at)",
            "ix_dmarc_reports_received_at|CREATE INDEX ix_dmarc_reports_received_at ON public.dmarc_reports USING btree (received_at)",
            "pk_dmarc_mailbox_messages|CREATE UNIQUE INDEX pk_dmarc_mailbox_messages ON public.dmarc_mailbox_messages USING btree (uid_validity, uid)",
            "pk_dmarc_mailbox_state|CREATE UNIQUE INDEX pk_dmarc_mailbox_state ON public.dmarc_mailbox_state USING btree (id)",
            "pk_dmarc_report_rows|CREATE UNIQUE INDEX pk_dmarc_report_rows ON public.dmarc_report_rows USING btree (report_id, ordinal)",
            "pk_dmarc_reports|CREATE UNIQUE INDEX pk_dmarc_reports ON public.dmarc_reports USING btree (id)",
            "ux_dmarc_reports_reporter_report_id_content_sha256|CREATE UNIQUE INDEX ux_dmarc_reports_reporter_report_id_content_sha256 ON public.dmarc_reports USING btree (reporter, reporter_report_id, content_sha256)",
        ]);

        (await Strings(
            "SELECT (SELECT count(*) FROM dmarc_reports) + (SELECT count(*) FROM dmarc_report_rows) "
            + "+ (SELECT count(*) FROM dmarc_mailbox_messages) + (SELECT count(*) FROM dmarc_mailbox_state) || ''"))
            .ShouldBe(["0"], "Up writes no row: a missing state row is the true state 'never attempted'");
    }

    private static string ReportInsert(string id, string reporter, string reportId, string digest) =>
        $"INSERT INTO dmarc_reports (id, reporter, reporter_report_id, content_sha256, begins_at, ends_at, received_at) "
        + $"VALUES ({id}, {reporter}, {reportId}, {digest}, '2026-10-01', '2026-10-02', '2026-10-02')";

    private static string RowInsert(
        string ordinal, string source, string count, string headerFrom, string dkim, string spf) =>
        "INSERT INTO dmarc_report_rows (report_id, ordinal, source_ip, message_count, header_from, dkim_aligned, spf_aligned) "
        + $"VALUES ({ReportId}, {ordinal}, {source}, {count}, {headerFrom}, {dkim}, {spf})";

    private static string LedgerInsert(string epoch, string uid, string outcome, string attempts, string retries) =>
        "INSERT INTO dmarc_mailbox_messages (uid_validity, uid, received_at, outcome, attempts, retries) "
        + $"VALUES ({epoch}, {uid}, now(), {outcome}, {attempts}, {retries})";

    private static string StateInsert(string id, string outcome, string epoch, string lastUid) =>
        "INSERT INTO dmarc_mailbox_state (id, last_attempt_at, last_outcome, uid_validity, last_uid) "
        + $"VALUES ({id}, now(), {outcome}, {epoch}, {lastUid})";

    private async Task MigrateUpAsync()
    {
        await using var db = NewAppContext();
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, Ct);
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task<string> ReltuplesAsync() => string.Join('|', await Strings(
        $"SELECT relname || '=' || reltuples::bigint FROM pg_class "
        + $"WHERE relname IN ('dmarc_reports', 'dmarc_report_rows', 'dmarc_mailbox_messages') ORDER BY relname COLLATE \"C\""));

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task ShouldViolateAsync(string sql, string sqlState)
    {
        var ex = await Should.ThrowAsync<PostgresException>(async () => await ExecuteAsync(sql));
        ex.SqlState.ShouldBe(sqlState, sql);
    }

    private Task<List<string>> ColumnsAsync() => Strings(
        $"""
        SELECT table_name || '.' || column_name || '|' || data_type || '|'
            || coalesce(character_maximum_length::text, '') || '|' || is_nullable || '|' || is_identity || '|'
            || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name IN ({NewTablesList})
        ORDER BY table_name COLLATE "C", column_name COLLATE "C"
        """);

    private Task<List<string>> ConstraintsAsync() => Strings(
        $"""
        SELECT c.conname || '|' || pg_get_constraintdef(c.oid)
        FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE n.nspname = 'public' AND t.relname IN ({NewTablesList}) AND c.contype <> 'n'
        ORDER BY c.conname COLLATE "C"
        """);

    private Task<List<string>> IndexesAsync() => Strings(
        $"""
        SELECT indexname || '|' || indexdef FROM pg_indexes
        WHERE schemaname = 'public' AND tablename IN ({NewTablesList})
        ORDER BY indexname COLLATE "C"
        """);

    private Task<List<string>> ReadSchemaWithoutTheNewTablesAsync() => Strings(
        $"""
        SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name NOT IN ({NewTablesList})
        ORDER BY table_name COLLATE "C", column_name COLLATE "C"
        """);

    private Task<List<string>> Strings(string sql) => ReadStringsAsync(_appConnectionString, sql);
}
