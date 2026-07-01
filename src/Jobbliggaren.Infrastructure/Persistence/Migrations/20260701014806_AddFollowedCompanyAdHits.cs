using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    // ADR 0087 D5 (#311 PR-4) — the company-follow notification rail.
    //
    // followed_company_ad_hits: the notification-delivery record for a new ad from a followed
    // employer (FollowedCompanyAdHit aggregate). job_ad_id + company_watch_id are by-identity
    // references with NO FK (ADR 0058/0059 soft-delete isolation — a retracted ad or an
    // unfollowed/erased watch must not delete a delivery row). notification_status is the
    // Pending → Queued → Sent dedup state machine (stored by NAME). UNIQUE(user_id, job_ad_id,
    // company_watch_id) is the dedup spine (idempotent scan). No grade / no score column (a
    // company-follow hit is not scored; ADR 0071/0076). Art. 17: erased by-UserId in
    // AccountHardDeleter (FK-less, so an explicit RemoveRange — the cascade fitness gate enforces it).
    //
    // last_company_watch_scan_at (job_seekers): the per-user company-follow scan high-water-mark
    // (ADR 0087 D4; sibling of last_match_scan_at). Advanced atomically with the hit inserts by
    // CompanyWatchScanJob (idempotency). Nullable (null = never scanned); no backfill.

    /// <inheritdoc />
    public partial class AddFollowedCompanyAdHits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_company_watch_scan_at",
                table: "job_seekers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "followed_company_ad_hits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_ad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_watch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_followed_company_ad_hits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_followed_company_ad_hits_user_id",
                table: "followed_company_ad_hits",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_followed_company_ad_hits_user_status",
                table: "followed_company_ad_hits",
                columns: new[] { "user_id", "notification_status" });

            migrationBuilder.CreateIndex(
                name: "ux_followed_company_ad_hits_user_jobad_watch",
                table: "followed_company_ad_hits",
                columns: new[] { "user_id", "job_ad_id", "company_watch_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "followed_company_ad_hits");

            migrationBuilder.DropColumn(
                name: "last_company_watch_scan_at",
                table: "job_seekers");
        }
    }
}
