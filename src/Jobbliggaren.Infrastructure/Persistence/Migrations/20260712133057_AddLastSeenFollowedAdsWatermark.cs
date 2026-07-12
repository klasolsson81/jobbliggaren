using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bevakning F2 (#801, RF-6=6B) — additive, nullable. ADD COLUMN
    /// <c>last_seen_followed_ads_at</c> (timestamptz NULL) on <c>job_seekers</c> — the company-follow
    /// USER-read watermark for the in-app follow rail (sibling of <c>last_seen_matches_at</c> /
    /// <c>last_seen_jobs_at</c>). Drives the Översikt "nya annonser från bevakade företag"-count
    /// (NEW = FollowedCompanyAdHit.CreatedAt &gt; this). Null = never seen. DISTINCT from
    /// <c>last_company_watch_scan_at</c> (the system scan mark).
    /// <para>
    /// <b>GDPR:</b> a low-sensitivity per-user behavioural timestamp (when you last acknowledged your
    /// follow rail) — no special-category PII, no DEK column, no encryption surface. Retention parity
    /// with <c>last_seen_matches_at</c>; cleared with the JobSeeker row on Art. 17 hard-delete (no
    /// separate cascade needed — it is a column on the aggregate; DPIA Part E C-E1).
    /// </para>
    /// <para>
    /// <b>Additive + nullable</b> → safe forward migration, no backfill, no data migration. <b>Down:</b>
    /// drops the column (the watermark value is lost — non-destructive to any other data; requires
    /// explicit approval before applying in non-test environments per repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddLastSeenFollowedAdsWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_followed_ads_at",
                table: "job_seekers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_followed_ads_at",
                table: "job_seekers");
        }
    }
}
