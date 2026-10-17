using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2161 (ADR 0172) — the account-activity record: one new table, <c>account_activity</c>, one row per profile
    /// (<c>job_seeker_id</c> primary key, foreign key to <c>job_seekers</c> ON DELETE CASCADE) holding the minute the
    /// account last signed in (<c>last_login_at</c>, nullable) and the minute it last made an authenticated request
    /// (<c>last_active_at</c>). No existing column changes and no row is written: recording starts with the release.
    ///
    /// <para>
    /// <b>Locks.</b> Creating the foreign key takes SHARE ROW EXCLUSIVE on <c>job_seekers</c> until commit, so writes
    /// to that table wait for this transaction (reads do not). The new table is empty, so the key has nothing to
    /// validate and the transaction is short; <c>SET LOCAL lock_timeout</c> turns a queue behind a long transaction
    /// into a loud deploy failure that the next reconcile retries.
    /// </para>
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops the table and every recorded login and
    /// activity minute with it. A code rollback makes <c>Migrate</c> refuse (schema ahead, exit 3) until
    /// <c>MIGRATE_ALLOW_SCHEMA_AHEAD</c> names this id; while the table stays, the cascade keeps erasing its rows
    /// with their profiles whatever code runs.
    /// </para>
    /// </summary>
    public partial class AddAccountActivity : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.CreateTable(
                name: "account_activity",
                columns: table => new
                {
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_activity", x => x.job_seeker_id);
                    table.ForeignKey(
                        name: "fk_account_activity_job_seekers_job_seeker_id",
                        column: x => x.job_seeker_id,
                        principalTable: "job_seekers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.DropTable(
                name: "account_activity");
        }
    }
}
