using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #293 (ADR 0042 Beslut E amendment) — additive, nullable. ADD COLUMN
    /// <c>last_seen_jobs_at</c> (timestamptz NULL) on <c>job_seekers</c> — the user-read
    /// watermark for the /jobb surface (sibling of <c>last_seen_matches_at</c>, ADR 0080
    /// Beslut 6). Drives the per-user "Ny = ingested since your last visit" tag
    /// (NY = JobAd.CreatedAt &gt; this). Null = never visited.
    /// <para>
    /// <b>GDPR:</b> a low-sensitivity per-user behavioural timestamp (when you last loaded
    /// the job list) — no special-category PII, no DEK column, no encryption surface. Retention
    /// parity with <c>last_seen_matches_at</c>; cleared with the JobSeeker row on Art.17
    /// hard-delete (no separate cascade needed — it is a column on the aggregate).
    /// </para>
    /// <para>
    /// <b>Additive + nullable</b> → safe forward migration, no backfill, no data migration.
    /// <b>Down:</b> drops the column (the watermark value is lost — non-destructive to any
    /// other data; requires explicit approval before applying in non-test environments per
    /// repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddLastSeenJobsWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_jobs_at",
                table: "job_seekers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_jobs_at",
                table: "job_seekers");
        }
    }
}
