using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #884 (senior-cto-advisor bind 2026-07-14) — declares an ICU <c>sv-SE</c> collation and pins it on
    /// <c>company_register</c>'s two Swedish natural-language text columns: <c>company_name</c> and
    /// <c>sate_kommun_name</c>.
    ///
    /// <para>
    /// <b>The defect.</b> The cluster collates under <c>en_US.utf8</c>, which folds Å/Ä/Ö into A and O.
    /// Swedish treats them as three distinct letters that sort AFTER Z. Both orders are MEASURED on
    /// postgres:18.3, not derived:
    /// <code>
    /// en_US.utf8:  Ahlberg  Åkesson  Älvsborg  Ärlig  Bok  Cederlund  Ödegaard  Öhman  Svensson  Zebra
    /// sv-SE (ICU): Ahlberg  Bok  Cederlund  Svensson  Zebra  Åkesson  Älvsborg  Ärlig  Ödegaard  Öhman
    /// </code>
    /// "Åkesson AB" lands between Ahlberg and Bok; "Öhman AB" lands ahead of Svensson. This is the LIVE
    /// browse list (#560 PR-2, <c>CompanyWatchBrowseQuery</c>) that users page through — in a Swedish
    /// civic service.
    /// </para>
    ///
    /// <para>
    /// <b>ICU, and that is a fact rather than a preference.</b> The <c>postgres:18.3</c> image ships
    /// exactly five libc collations (C, C.utf8, POSIX, en_US, en_US.utf8) and <c>locale -a</c> confirms
    /// no Swedish locale is generated. A libc route would require <c>locale-gen sv_SE.UTF-8</c> baked
    /// into every image that ever runs this migration — dev compose, Testcontainers, CI, prod — making
    /// the schema's correctness depend on the OS instead of on the database. ICU ships 871 collations
    /// here, including a deterministic <c>sv-SE</c>. Deterministic is required, not incidental: under a
    /// non-deterministic collation equality stops being byte equality, which would silently change
    /// <c>=</c> semantics on these columns.
    /// </para>
    ///
    /// <para>
    /// <b>Measured mechanics on the 1,17M-row register.</b> The table is NOT rewritten: its
    /// <c>relfilenode</c> is unchanged across the ALTER (16385 → 16385) because text→text is binary
    /// coercible and the stored bytes do not move. Exactly ONE index is rebuilt, automatically and
    /// atomically — <c>ix_company_register_company_name_organization_number</c> (#875), whose
    /// <c>relfilenode</c> changes (16407 → 16411) and which comes back <c>indisvalid = t</c> under the
    /// new collation. The GIN, status, kommun-code and PK indexes are untouched. <c>sate_kommun_name</c>
    /// has no index at all, so collating it rebuilds nothing — its marginal cost is one statement.
    /// Total ALTER time measured across four runs at 1,17M rows: <b>2,0-8,8 s</b>, consistent with
    /// #875's own 2,4-7,6 s <c>CREATE INDEX</c> on this table, which is the same work.
    /// </para>
    ///
    /// <para>
    /// <b>Ops note — the lock class is STRICTER than #875's, and that is the cost being accepted.</b>
    /// <c>ALTER TABLE ... ALTER COLUMN ... TYPE</c> takes <b>ACCESS EXCLUSIVE</b>, which blocks READS as
    /// well as writes. #875's <c>CREATE INDEX</c> took SHARE and blocked only writes, so it could lean on
    /// a DISCIPLINARY guard ("the operator avoids the nightly-sync window") — its worst case was a
    /// delayed sync. That premise does not survive here: the worst case is a READ OUTAGE on the browse
    /// endpoint. The guard is therefore made STRUCTURAL. <c>SET LOCAL lock_timeout = '3s'</c> is the
    /// first statement of both Up and Down: it bounds only the WAIT for the lock, never the rebuild
    /// itself, so a migration that collides with a long-running transaction fails LOUDLY at deploy
    /// instead of silently queueing every reader behind it. (While an ACCESS EXCLUSIVE request waits,
    /// every new reader queues behind it — run this during the nightly sync without the timeout and the
    /// browse endpoint goes dark for the SYNC's duration, not the ALTER's.) <c>statement_timeout</c> is
    /// deliberately NOT set — it would kill a legitimate rebuild.
    /// </para>
    ///
    /// <para>
    /// <b>The lock guard has one precondition, and it is worth naming because the guard fails SILENTLY
    /// without it.</b> <c>SET LOCAL</c> is transaction-scoped, and the repo's deploy path satisfies that
    /// — <c>Jobbliggaren.Migrate</c> calls <c>Database.MigrateAsync()</c>, and EF wraps each migration in
    /// a transaction. But applied OUTSIDE a transaction block (e.g. a raw
    /// <c>migrations script --idempotent</c> piped into psql without a wrapping BEGIN), <c>SET LOCAL</c>
    /// emits only a WARNING ("SET LOCAL can only be used in transaction blocks") and does nothing: the
    /// ACCESS EXCLUSIVE wait becomes unbounded again, and the structural guard degrades to zero without
    /// erroring. If this migration is ever applied by hand, wrap it in <c>BEGIN; ... COMMIT;</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Write path: no measurable regression.</b> The nightly SCB sync writes <c>company_name</c> on
    /// every upserted row, so it maintains the collated index. Measured with a counterbalanced design
    /// (each collation run once on a cold table and once on a warm one, so the position effect cancels)
    /// at 1,17M rows against the real <c>ScbCompanyRegisterStore</c> statement: libc 170,0 s / 107,5 s;
    /// ICU 128,6 s / 81,6 s. ICU is nominally faster in both positions — that is NOT claimed as a win:
    /// libc alone measured 102-170 s across five runs on this host, so the spread WITHIN one collation
    /// exceeds the difference BETWEEN them. The honest statement is that no regression is detectable.
    /// It would not change the decision either way: ADR 0045 budgets read latency, not batch writes, and
    /// ADR 0091's sync is dominated by a multi-hour rate-limited SCB fetch (10 calls per 10 s) next to
    /// which any of these numbers is noise.
    /// </para>
    ///
    /// <para>
    /// <b>Down is HAND-ORDERED, because the scaffolder's order cannot run.</b> <c>dotnet ef migrations
    /// add</c> emitted the <c>DROP COLLATION</c> (from <c>AlterDatabase().OldAnnotation(...)</c>) BEFORE
    /// the <c>AlterColumn</c> calls that stop depending on it. Executed against a real database, that is:
    /// <code>
    /// ERROR:  cannot drop collation swedish because other objects depend on it
    /// DETAIL: column company_name of table company_register depends on collation swedish
    /// </code>
    /// A Down that cannot run is a rollback path the migration CLAIMS but does not have — the same
    /// vacuous shape as #805-3 and #842, and one that would only ever be discovered mid-rollback, in
    /// production, under pressure. The operations below are ordered columns-first, collation-last, and
    /// that order is verified against Postgres rather than reasoned about. Down reverts to
    /// <c>COLLATE "default"</c> — the cluster's default, whatever it happens to be — never to a
    /// hardcoded <c>en_US.utf8</c>, which would make the rollback environment-dependent.
    /// <b>Down is not free:</b> it takes the same ACCESS EXCLUSIVE lock and rebuilds the same index.
    /// </para>
    ///
    /// <para>
    /// <b>Portability has a receipt.</b> <c>pg_dump --schema-only</c> emits both
    /// <c>CREATE COLLATION public.swedish (provider = icu, locale = 'sv-SE')</c> and
    /// <c>company_name text NOT NULL COLLATE public.swedish</c> / <c>sate_kommun_name text COLLATE
    /// public.swedish</c>, so a dump/restore carries the sort order with it. That is strictly better than
    /// the status quo, under which the order depended on how the TARGET cluster had been initdb'd.
    /// </para>
    ///
    /// <para>
    /// <b>What this migration does NOT protect against</b> — stated so it is not mistaken for a
    /// guarantee: collation-VERSION drift. An ICU or glibc upgrade can change how an existing collation
    /// sorts, which silently invalidates every btree built under it. Postgres 18 tracks
    /// <c>pg_collation.collversion</c> for BOTH providers and warns, but no code change can prevent it;
    /// the countermeasure is an ops step (REINDEX + <c>ALTER COLLATION ... REFRESH VERSION</c> after any
    /// Postgres image or major-version bump), recorded in the release runbook. This migration does not
    /// increase that exposure — <c>en_US.utf8</c> carries it already (collversion 2.41). It is the first
    /// time the repo names it.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddSwedishCollationToCompanyRegisterText : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            // The collation must exist before the columns that reference it.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:swedish", "sv-SE,sv-SE,icu,True");

            migrationBuilder.AlterColumn<string>(
                name: "sate_kommun_name",
                table: "company_register",
                type: "text",
                nullable: true,
                collation: "swedish",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "company_name",
                table: "company_register",
                type: "text",
                nullable: false,
                collation: "swedish",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            // COLUMNS FIRST. The scaffolder put the AlterDatabase (which emits DROP COLLATION) here, and
            // Postgres rejects that while these two columns still reference the collation. See the
            // class doc-comment: the generated Down could not run at all.
            migrationBuilder.AlterColumn<string>(
                name: "sate_kommun_name",
                table: "company_register",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "swedish");

            migrationBuilder.AlterColumn<string>(
                name: "company_name",
                table: "company_register",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "swedish");

            // ...and only now does nothing depend on it.
            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:CollationDefinition:swedish", "sv-SE,sv-SE,icu,True");
        }
    }
}
