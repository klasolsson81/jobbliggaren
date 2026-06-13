using System;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogPartitioning : Migration
    {
        // STEG 10a — Audit-log partitioning per ADR 0024 delbeslut 1 + 2.
        //
        // Konverterar audit_log till en native PostgreSQL-partitionerad tabell
        // partitionerad per dag (PARTITION BY RANGE (occurred_at)).
        //
        // Strategi (ADR 0024 D2):
        //   1. Rename audit_log → audit_log_legacy (bevarar data)
        //   2. Skapa ny partitionerad parent-tabell med samma kolumner +
        //      KOMPOSIT-PK (id, occurred_at) — partitions-key måste ingå i PK
        //   3. Skapa default-partition (audit_log_default) som säkerhetsnät
        //   4. Skapa 7 bootstrap-partitions: idag + 6 framåt
        //      (retention-jobbet skapar morgondagens partition + droppar
        //       partitions äldre än 90 dagar dagligen 03:00 UTC)
        //   5. Återflytta rader från legacy via INSERT-SELECT
        //   6. Droppa legacy
        //   7. Återskapa index ix_audit_log_occurred_at (DESC) på parent —
        //      propageras automatiskt till alla partitions
        //
        // PK-ändring: ADR 0022 specade PK = (id) men native partitioning kräver
        // att partition-key (occurred_at) ingår i PK. Komposit-PK (id, occurred_at)
        // är medveten breaking change mot ADR 0022:s schema-spec — ADR 0024 D2
        // dokumenterar undantaget.
        //
        // Audit-data vid migration: dev-DB är tom (0 rader) — verifierat via
        // SELECT COUNT(*) i STEG 10.1. Prod-deploy körs innan Fas 1 går till
        // prod, så även prod-tabellen blir tom vid första körning.

        private const int BootstrapPartitionCount = 7;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Rename befintlig tabell + index + PK-constraint.
            //    Index "ix_audit_log_occurred_at" följer med tabellen vid rename
            //    men constraint-namnet "pk_audit_log" gör det INTE — det måste
            //    rename:as explicit för att inte krocka när nya partitionerade
            //    tabellen skapas med samma constraint-namn.
            //    IF EXISTS-skydd matchar konvention från
            //    20260508093139_AddApplicationStaleDetectionFields.
            migrationBuilder.Sql("ALTER TABLE audit_log RENAME TO audit_log_legacy;");
            migrationBuilder.Sql("ALTER TABLE audit_log_legacy RENAME CONSTRAINT pk_audit_log TO pk_audit_log_legacy;");
            migrationBuilder.Sql("ALTER INDEX IF EXISTS ix_audit_log_occurred_at RENAME TO ix_audit_log_legacy_occurred_at;");

            // 2. Skapa partitionerad parent-tabell. Kolumn-definitioner identiska
            //    med ursprungliga audit_log-tabellen (migration 20260508062501).
            //    PRIMARY KEY (id, occurred_at) — partitions-key ingår per
            //    PG18-krav.
            migrationBuilder.Sql(
                """
                CREATE TABLE audit_log (
                    id uuid NOT NULL,
                    occurred_at timestamp with time zone NOT NULL,
                    correlation_id uuid NOT NULL,
                    user_id uuid NULL,
                    impersonated_by uuid NULL,
                    event_type character varying(100) NOT NULL,
                    aggregate_type character varying(100) NOT NULL,
                    aggregate_id uuid NOT NULL,
                    ip_address character varying(45) NULL,
                    user_agent character varying(256) NULL,
                    CONSTRAINT pk_audit_log PRIMARY KEY (id, occurred_at)
                ) PARTITION BY RANGE (occurred_at);
                """);

            // 3. Bootstrap-partitions: idag + 6 framåt = 7 partitions.
            //    Skapas FÖRE default-partitionen (steg 4) — om default skulle
            //    skapas först och innehålla rader, kan PG behöva re-routa dem
            //    vid range-partition-skapning, vilket failar på överlapp.
            //    Range-first-default-last eliminerar risken permanent eftersom
            //    range-partitions täcker hela bootstrap-fönstret innan default
            //    ens existerar.
            //
            //    AuditLogRetentionJob (10.4) skapar morgondagens partition
            //    dagligen — bootstrap-bufferten täcker första veckan tills
            //    jobbet hunnit etablera sitt rullande fönster.
            //
            //    Datum-bounds är inclusive-from / exclusive-to per
            //    PostgreSQL RANGE-konvention. Alla bounds i UTC.
            //
            //    NOT: ADR 0024 D2 säger "senaste 7 dagar" — koden tolkar det
            //    som "7 dagar framåt från migration-tidpunkt". Tabellen är tom
            //    vid migration (0 rader verifierat 10.1), så inga historiska
            //    rader behöver bakåt-partitions. Alla NYA inserts behöver
            //    framåt-buffer. Tolknings-not separat öppen för Klas att
            //    redirecta till bakåt eller symmetrisk om så önskas.
            var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
            var sb = new StringBuilder();
            for (var offset = 0; offset < BootstrapPartitionCount; offset++)
            {
                var dayStart = today.AddDays(offset);
                var dayEnd = dayStart.AddDays(1);
                var partitionName = $"audit_log_{dayStart:yyyyMMdd}";
                var fromBound = dayStart.ToString("yyyy-MM-dd 00:00:00+00", CultureInfo.InvariantCulture);
                var toBound = dayEnd.ToString("yyyy-MM-dd 00:00:00+00", CultureInfo.InvariantCulture);

                sb.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"CREATE TABLE {partitionName} PARTITION OF audit_log " +
                    $"FOR VALUES FROM ('{fromBound}') TO ('{toBound}');");
            }
            migrationBuilder.Sql(sb.ToString());

            // 4. Default-partition fångar rader vars occurred_at hamnar utanför
            //    explicit partition-range. Säkerhetsnät — i normal drift ska
            //    AuditLogRetentionJob skapa morgondagens partition i tid så att
            //    ingen rad hamnar i default. Om jobbet failer kvarstår
            //    audit-skrivningar i default-partitionen och plockas upp av
            //    nästa lyckade körning (manuell move-procedur i runbooken).
            migrationBuilder.Sql(
                "CREATE TABLE audit_log_default PARTITION OF audit_log DEFAULT;");

            // 5. Återflytta rader från legacy. Explicit kolumnlista istället
            //    för SELECT * — production-DDL får inte bero på implicit
            //    kolumn-ordnings-kontrakt. Om någon i framtiden lägger till
            //    en kolumn på audit_log via separat migration blir denna
            //    INSERT ändå korrekt så länge båda riktningar listar samma
            //    explicit kolumner. INSERT-SELECT routas automatiskt till
            //    rätt partition baserat på occurred_at. Tom tabell i dev
            //    (0 rader verifierat 10.1) — kommandot är safe oavsett radvolym.
            migrationBuilder.Sql(
                """
                INSERT INTO audit_log (
                    id, occurred_at, correlation_id, user_id, impersonated_by,
                    event_type, aggregate_type, aggregate_id, ip_address, user_agent
                )
                SELECT
                    id, occurred_at, correlation_id, user_id, impersonated_by,
                    event_type, aggregate_type, aggregate_id, ip_address, user_agent
                FROM audit_log_legacy;
                """);

            // 6. Droppa legacy + dess index. CASCADE inte nödvändigt — inga
            //    FK refererar audit_log-rader (write-only-design per ADR 0022).
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_audit_log_legacy_occurred_at;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit_log_legacy;");

            // 7. Index på parent — PostgreSQL propagerar automatiskt till alla
            //    befintliga och framtida partitions. BUILD.md §7.2 specar
            //    (occurred_at DESC) för admin-läs-yta och retention.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_audit_log_occurred_at ON audit_log (occurred_at DESC);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversera: konvertera tillbaka till vanlig (icke-partitionerad) tabell
            // med single-column PK (id). Behåll alla rader.

            // 1. Rename partitionerad parent + index + PK-constraint.
            //    Symmetriskt med Up() — constraint-namn måste rename:as för
            //    att inte krocka när vanlig audit_log skapas med pk_audit_log.
            migrationBuilder.Sql("ALTER TABLE audit_log RENAME TO audit_log_partitioned;");
            migrationBuilder.Sql("ALTER TABLE audit_log_partitioned RENAME CONSTRAINT pk_audit_log TO pk_audit_log_partitioned;");
            migrationBuilder.Sql("ALTER INDEX IF EXISTS ix_audit_log_occurred_at RENAME TO ix_audit_log_partitioned_occurred_at;");

            // 2. Skapa vanlig audit_log med original PK = (id).
            migrationBuilder.Sql(
                """
                CREATE TABLE audit_log (
                    id uuid NOT NULL,
                    occurred_at timestamp with time zone NOT NULL,
                    correlation_id uuid NOT NULL,
                    user_id uuid NULL,
                    impersonated_by uuid NULL,
                    event_type character varying(100) NOT NULL,
                    aggregate_type character varying(100) NOT NULL,
                    aggregate_id uuid NOT NULL,
                    ip_address character varying(45) NULL,
                    user_agent character varying(256) NULL,
                    CONSTRAINT pk_audit_log PRIMARY KEY (id)
                );
                """);

            // 3. Återflytta rader från partitionerad version.
            //    Explicit kolumnlista — symmetrisk med Up() (B1 från review).
            migrationBuilder.Sql(
                """
                INSERT INTO audit_log (
                    id, occurred_at, correlation_id, user_id, impersonated_by,
                    event_type, aggregate_type, aggregate_id, ip_address, user_agent
                )
                SELECT
                    id, occurred_at, correlation_id, user_id, impersonated_by,
                    event_type, aggregate_type, aggregate_id, ip_address, user_agent
                FROM audit_log_partitioned;
                """);

            // 4. Droppa partitionerad version (CASCADE tar alla partitions).
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit_log_partitioned CASCADE;");

            // 5. Återskapa index.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_audit_log_occurred_at ON audit_log (occurred_at DESC);");
        }
    }
}
