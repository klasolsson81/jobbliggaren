using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2130 PR 2 (ADR 0162, amended by ADR 0168) — the notification bell's own per-row read
    /// state, as <c>read_in_notification_feed boolean NOT NULL DEFAULT false</c> on the two source tables
    /// (<c>user_job_ad_matches</c>, <c>followed_company_ad_hits</c>), plus ONE separately named line: the index
    /// swap on <c>followed_company_ad_hits</c> (ADR 0162 s5, Q8).
    ///
    /// <para>
    /// <b>The two <c>Preferences</c> off-stamps move the snapshot only.</b> <c>NotificationFeedMatchesOffAt</c> and
    /// <c>NotificationFeedFollowedAdsOffAt</c> live in the <c>job_seekers.preferences</c> jsonb, null meaning on, so
    /// they carry no DDL (the form of <c>AddNotificationMailPauses</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Locks.</b> <c>ADD COLUMN</c> takes ACCESS EXCLUSIVE, and the two statements run in one transaction, so both
    /// locks are held to commit. With a non-volatile <c>DEFAULT false</c> the value lives in the table's metadata
    /// (PostgreSQL 18, <c>ALTER TABLE</c>: no rewrite), so the hold is milliseconds at any row count. The
    /// only real exposure is queueing behind a long transaction while readers (the bell, Översikt, /matchningar)
    /// and the five-minute Worker run queue behind the lock: <c>SET LOCAL lock_timeout</c> turns that queue into a
    /// loud deploy failure that the next reconcile retries. It binds only inside the migration's own transaction,
    /// which <c>Database.MigrateAsync()</c> provides.
    /// </para>
    ///
    /// <para>
    /// <b>No data step, no backfill.</b> <c>false</c> means "not individually read in the bell", which is today's
    /// meaning for every existing row: a row is unread in the bell iff it is past the watermark AND not flagged. A
    /// <c>now()</c>-style backfill would hide notifications the user has not seen.
    /// </para>
    ///
    /// <para>
    /// <b>The index swap is the strikeable line.</b> <c>ix_followed_company_ad_hits_user_id</c> is replaced by
    /// <c>ix_followed_company_ad_hits_user_created_at (user_id, created_at DESC)</c>. The prefix gives the same equality
    /// on <c>user_id</c> (the Art. 17 sweep and the scan's per-user grouping), so the number of indexes and the write
    /// cost are unchanged. This migration adds access paths keyed <c>(user_id, created_at)</c> on that table — the
    /// token, the newest-first window, "Markera alla" and the backlog mark — which the plain index cannot order. In this
    /// transaction the table already holds the ACCESS EXCLUSIVE of <c>ADD COLUMN</c>, so the DROP and the CREATE INDEX
    /// (alone, SHARE) also block reads, for milliseconds at this table's size; neither is <c>CONCURRENTLY</c>. Striking
    /// the line touches seven artefacts: the four statements marked below (Up and Down), the Designer and the snapshot
    /// (the new name is in both), <c>FollowedCompanyAdHitConfiguration</c>, <c>FollowedCompanyAdHitPersistenceTests</c>,
    /// <c>AddNotificationFeedReadStateMigrationTests</c> and ADR 0168 D8.
    /// </para>
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops both columns, so every bell read mark is
    /// lost (every notification reads as unread again), and it restores the old index. A code rollback to the
    /// previous image makes <c>Migrate</c> refuse (schema ahead, exit 3) until the operator sets
    /// <c>MIGRATE_ALLOW_SCHEMA_AHEAD</c> to this exact migration id.
    /// </para>
    /// </summary>
    public partial class AddNotificationFeedReadState : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<bool>(
                name: "read_in_notification_feed",
                table: "user_job_ad_matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "read_in_notification_feed",
                table: "followed_company_ad_hits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ── STRIKEABLE LINE (Klas can strike this block from the GO): the index swap, ADR 0162 s5 ──
            migrationBuilder.DropIndex(
                name: "ix_followed_company_ad_hits_user_id",
                table: "followed_company_ad_hits");

            migrationBuilder.CreateIndex(
                name: "ix_followed_company_ad_hits_user_created_at",
                table: "followed_company_ad_hits",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
            // ── end of the strikeable line ──
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            // ── STRIKEABLE LINE: the index swap, reversed ──
            migrationBuilder.DropIndex(
                name: "ix_followed_company_ad_hits_user_created_at",
                table: "followed_company_ad_hits");

            migrationBuilder.CreateIndex(
                name: "ix_followed_company_ad_hits_user_id",
                table: "followed_company_ad_hits",
                column: "user_id");
            // ── end of the strikeable line ──

            migrationBuilder.DropColumn(
                name: "read_in_notification_feed",
                table: "user_job_ad_matches");

            migrationBuilder.DropColumn(
                name: "read_in_notification_feed",
                table: "followed_company_ad_hits");
        }
    }
}
