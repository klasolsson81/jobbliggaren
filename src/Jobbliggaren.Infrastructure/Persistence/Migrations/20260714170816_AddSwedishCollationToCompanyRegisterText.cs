using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #884 (senior-cto-advisor bind 2026-07-14) — declares an ICU <c>sv-SE</c> collation and pins it on
    /// <c>company_register</c>'s two Swedish natural-language text columns (<c>company_name</c>,
    /// <c>sate_kommun_name</c>), because the cluster default <c>en_US.utf8</c> folds Å/Ä/Ö into A and O
    /// and puts "Åkesson AB" between "Ahlberg" and "Bok" in the live browse list (#560 PR-2).
    ///
    /// <para>
    /// <b>The reasoning, the measurements, the scope rule (natural-language text yes, machine
    /// identifiers no) and what this does NOT guard against all live in ADR 0110.</b> They are not
    /// repeated here. An earlier draft of this comment carried them verbatim alongside three other
    /// copies — and a fact kept in four places is a fact that will shortly be true in three of them,
    /// which is the very failure this PR exists to undo. What follows is only what a reader OF THIS
    /// MIGRATION needs and cannot get from the ADR.
    /// </para>
    ///
    /// <para>
    /// <b>Measured at 1,17M rows: it does not rewrite the table, and it rebuilds exactly one index.</b>
    /// <c>company_register</c>'s <c>relfilenode</c> is unchanged — text→text is binary-coercible, so the
    /// stored bytes do not move. Postgres then rebuilds, automatically and atomically, precisely those
    /// indexes that DEPEND on a re-collated column. Today that is one:
    /// <c>ix_company_register_company_name_organization_number</c> (#875), which comes back
    /// <c>indisvalid = t</c> under the new collation. That is stated as a RULE and not as a list of the
    /// indexes left alone, deliberately: whichever indexes this table grows, the ones touching these two
    /// columns are rebuilt and the rest are not, and a list would be one more thing to keep true.
    /// <c>sate_kommun_name</c> is covered by no index at all, so collating it rebuilds nothing. Total:
    /// <b>2,0-8,8 s</b> over four runs — the same work as #875's own 2,4-7,6 s <c>CREATE INDEX</c> here.
    /// </para>
    ///
    /// <para>
    /// <b>The lock class is STRICTER than #875's, and that is the cost being accepted.</b>
    /// <c>ALTER TABLE ... ALTER COLUMN ... TYPE</c> takes <b>ACCESS EXCLUSIVE</b> — it blocks READS as
    /// well as writes. #875's <c>CREATE INDEX</c> took SHARE and blocked only writes, so it could afford
    /// a DISCIPLINARY guard ("the operator avoids the nightly-sync window"); its worst case was a delayed
    /// sync. That premise does not survive here: the worst case is a READ OUTAGE on the browse endpoint.
    /// So the guard is made STRUCTURAL. <c>SET LOCAL lock_timeout = '3s'</c> is the first statement of
    /// both Up and Down. It bounds only the WAIT for the lock, never the rebuild — so a migration that
    /// collides with a long-running transaction fails LOUDLY at deploy instead of silently queueing every
    /// reader behind it. (While an ACCESS EXCLUSIVE request waits, every NEW reader queues behind it: run
    /// this during the nightly sync without the timeout and the browse endpoint goes dark for the SYNC's
    /// duration, not the ALTER's.) <c>statement_timeout</c> is deliberately NOT set — it would kill a
    /// legitimate rebuild.
    /// </para>
    ///
    /// <para>
    /// <b>That guard has a precondition, and without it it fails SILENTLY.</b> <c>SET LOCAL</c> is
    /// transaction-scoped. The repo's deploy path satisfies this — <c>Jobbliggaren.Migrate</c> calls
    /// <c>Database.MigrateAsync()</c>, and EF wraps each migration in a transaction. Applied OUTSIDE a
    /// transaction block (a raw <c>migrations script</c> piped into psql), <c>SET LOCAL</c> emits only a
    /// WARNING and does nothing: the lock wait becomes unbounded again and the structural guard degrades
    /// to zero without erroring. If this is ever applied by hand, wrap it in <c>BEGIN; ... COMMIT;</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Down is HAND-ORDERED, because the scaffolder's order cannot run.</b> <c>dotnet ef migrations
    /// add</c> emitted the <c>DROP COLLATION</c> (from <c>AlterDatabase().OldAnnotation(...)</c>) BEFORE
    /// the <c>AlterColumn</c> calls that stop depending on it. Against a real database that is:
    /// <code>
    /// ERROR:  cannot drop collation swedish because other objects depend on it
    /// DETAIL: column company_name of table company_register depends on collation swedish
    /// </code>
    /// A Down that cannot run is a rollback path the migration CLAIMS but does not have — the same
    /// vacuous shape as #805-3 and #842, and one that would only ever be found mid-rollback, in
    /// production, under pressure. Below: columns first, collation last, verified against Postgres rather
    /// than reasoned about. Down reverts to <c>COLLATE "default"</c> — the cluster's default, whatever it
    /// is — never a hardcoded <c>en_US.utf8</c>, which would make the rollback environment-dependent.
    /// <b>Down is not free:</b> same ACCESS EXCLUSIVE lock, same index rebuild.
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
