using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2151 (ADR 0169, amends ADR 0168) — the X on a bell notification: <c>dismissed_from_notification_feed boolean
    /// NOT NULL DEFAULT false</c> on the two source tables (<c>user_job_ad_matches</c>, <c>followed_company_ad_hits</c>),
    /// and one CHECK on each holding the invariant that a dismissed row is a read row.
    ///
    /// <para>
    /// <b>Locks.</b> Same as <c>AddNotificationFeedReadState</c>: <c>ADD COLUMN</c> with a constant default is catalog
    /// only (PostgreSQL 18, no rewrite) but takes ACCESS EXCLUSIVE until commit, and <c>SET LOCAL lock_timeout</c>
    /// turns a queue behind a long transaction into a loud deploy failure that the next reconcile retries. The CHECK
    /// validates every existing row under the lock <c>ADD COLUMN</c> already holds; every row passes, because the new
    /// column is false everywhere. No backfill, no index.
    /// </para>
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops both columns, so every dismissed
    /// notification returns to the bell as a read one (the rows were never deleted, and the read flag stays), and it
    /// drops the two CHECKs. A code rollback
    /// makes <c>Migrate</c> refuse (schema ahead, exit 3) until <c>MIGRATE_ALLOW_SCHEMA_AHEAD</c> names this id.
    /// </para>
    /// </summary>
    public partial class AddNotificationFeedDismissal : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<bool>(
                name: "dismissed_from_notification_feed",
                table: "user_job_ad_matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "dismissed_from_notification_feed",
                table: "followed_company_ad_hits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_job_ad_matches_dismissed_implies_read",
                table: "user_job_ad_matches",
                sql: "NOT dismissed_from_notification_feed OR read_in_notification_feed");

            migrationBuilder.AddCheckConstraint(
                name: "ck_followed_company_ad_hits_dismissed_implies_read",
                table: "followed_company_ad_hits",
                sql: "NOT dismissed_from_notification_feed OR read_in_notification_feed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_job_ad_matches_dismissed_implies_read",
                table: "user_job_ad_matches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_followed_company_ad_hits_dismissed_implies_read",
                table: "followed_company_ad_hits");

            migrationBuilder.DropColumn(
                name: "dismissed_from_notification_feed",
                table: "user_job_ad_matches");

            migrationBuilder.DropColumn(
                name: "dismissed_from_notification_feed",
                table: "followed_company_ad_hits");
        }
    }
}
