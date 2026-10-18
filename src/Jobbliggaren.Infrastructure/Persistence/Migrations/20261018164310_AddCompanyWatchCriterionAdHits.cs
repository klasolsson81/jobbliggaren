using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1663 PR 2 (ADR 0175 D5, D6) — the Branschbevakning scan's storage: one new table,
    /// <c>company_watch_criterion_ad_hits</c> (one row per criterion and ad; a foreign key to
    /// <c>company_watch_criteria</c> ON DELETE CASCADE; the bell's two flags NOT NULL DEFAULT false and held by a
    /// CHECK from the start; three indexes), and four columns on <c>company_watch_criteria</c>:
    /// <c>scanned_through</c>, <c>last_scan_outcome</c> and <c>new_ads_seen_through</c> (nullable, nothing to
    /// fill) and <c>criteria_changed_at</c> (NOT NULL). Klas's migration GO of 2026-10-18 names exactly this.
    ///
    /// <para>
    /// <b>The data step.</b> <c>criteria_changed_at</c> is added nullable, filled from <c>created_at</c>, then made
    /// NOT NULL with no default: the domain sets it on every insert, so a physical default would be a value the
    /// model does not know. Filling it from <c>created_at</c> is what bounds an existing criterion's first scan to
    /// the cold-start floor (ADR 0175 D6) instead of to the beginning of time.
    /// </para>
    ///
    /// <para>
    /// <b>Locks.</b> The four <c>ADD COLUMN</c>s and the <c>ALTER</c> take ACCESS EXCLUSIVE on
    /// <c>company_watch_criteria</c> until commit, and the new foreign key takes SHARE ROW EXCLUSIVE on it; the
    /// table holds at most 20 rows per user, so the UPDATE is short. <c>SET LOCAL lock_timeout</c> turns a queue
    /// behind a long transaction into a loud deploy failure that the next reconcile retries. Between this
    /// migration and the api's restart the old code's criterion INSERT names no <c>criteria_changed_at</c> and
    /// fails on the NOT NULL; nothing is stored and the user can retry (accepted in Klas's GO).
    /// </para>
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops the hit table and the four columns,
    /// and every hit and watermark with them. It drops the table first, so the foreign key is gone before the
    /// principal's columns change.
    /// </para>
    /// </summary>
    public partial class AddCompanyWatchCriterionAdHits : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "criteria_changed_at",
                table: "company_watch_criteria",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE company_watch_criteria SET criteria_changed_at = created_at WHERE criteria_changed_at IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "criteria_changed_at",
                table: "company_watch_criteria",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_scan_outcome",
                table: "company_watch_criteria",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "new_ads_seen_through",
                table: "company_watch_criteria",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scanned_through",
                table: "company_watch_criteria",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_watch_criterion_ad_hits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_ad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_watch_criterion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_in_notification_feed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dismissed_from_notification_feed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_watch_criterion_ad_hits", x => x.id);
                    table.CheckConstraint("ck_company_watch_criterion_ad_hits_dismissed_implies_read", "NOT dismissed_from_notification_feed OR read_in_notification_feed");
                    table.ForeignKey(
                        name: "fk_company_watch_criterion_ad_hits_company_watch_criterion_id",
                        column: x => x.company_watch_criterion_id,
                        principalTable: "company_watch_criteria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_watch_criterion_ad_hits_user_created_at",
                table: "company_watch_criterion_ad_hits",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_company_watch_criterion_ad_hits_user_jobad",
                table: "company_watch_criterion_ad_hits",
                columns: new[] { "user_id", "job_ad_id" });

            migrationBuilder.CreateIndex(
                name: "ux_company_watch_criterion_ad_hits_criterion_jobad",
                table: "company_watch_criterion_ad_hits",
                columns: new[] { "company_watch_criterion_id", "job_ad_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.DropTable(
                name: "company_watch_criterion_ad_hits");

            migrationBuilder.DropColumn(
                name: "criteria_changed_at",
                table: "company_watch_criteria");

            migrationBuilder.DropColumn(
                name: "last_scan_outcome",
                table: "company_watch_criteria");

            migrationBuilder.DropColumn(
                name: "new_ads_seen_through",
                table: "company_watch_criteria");

            migrationBuilder.DropColumn(
                name: "scanned_through",
                table: "company_watch_criteria");
        }
    }
}
