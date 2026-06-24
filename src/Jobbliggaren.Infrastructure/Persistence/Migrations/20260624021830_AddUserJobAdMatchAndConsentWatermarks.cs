using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0080 Vag 4 PR-1 — two additive schema changes:
    /// <list type="number">
    /// <item><b>NEW TABLE <c>user_job_ad_matches</c></b> — persisted background-match
    ///   results (Worker scan → dedup spine). Columns: <c>id</c> (uuid PK),
    ///   <c>user_id</c> (uuid, no FK — ADR 0058/0059), <c>job_ad_id</c> (uuid, no FK),
    ///   <c>grade</c> (varchar(20), enum name — Good/Strong/Top),
    ///   <c>notification_status</c> (varchar(20), enum name — Pending/Queued/Sent),
    ///   <c>matched_skill_concept_ids</c> (jsonb, DEK-free non-PII taxonomy ids, default
    ///   <c>'[]'</c>), <c>created_at</c> (timestamptz NOT NULL), <c>sent_at</c> (timestamptz
    ///   NULL), <c>deleted_at</c> (timestamptz NULL, soft-delete sentinel).
    ///   Indexes: UNIQUE <c>ux_user_job_ad_matches_user_jobad</c> (user_id, job_ad_id) —
    ///   the dedup spine (idempotent re-scan); <c>ix_user_job_ad_matches_user_created_at</c>
    ///   (user_id ASC, created_at DESC) — digest pagination;
    ///   <c>ix_user_job_ad_matches_user_status</c> (user_id, notification_status) —
    ///   dispatch query; <c>ix_user_job_ad_matches_user_id</c> (user_id) — Art.17 cascade.
    ///   No FK to <c>job_ads</c> or <c>job_seekers</c> per ADR 0058/0059.</item>
    /// <item><b>job_seekers: ADD COLUMN <c>last_match_scan_at</c></b> (timestamptz NULL)
    ///   — Worker scan high-water-mark (Beslut 2). Null = never scanned.</item>
    /// <item><b>job_seekers: ADD COLUMN <c>last_seen_matches_at</c></b> (timestamptz NULL)
    ///   — user-read watermark (Beslut 6). Null = never seen.</item>
    /// </list>
    /// <para>
    /// <b>GDPR:</b> <c>matched_skill_concept_ids</c> = taxonomy concept-id strings,
    /// non-PII (ADR 0079 Beslut 1). Soft-delete on <c>user_job_ad_matches</c> via
    /// <c>deleted_at</c> + global query filter. No DEK column, no encryption surface.
    /// Consent timestamps remain in <c>job_seekers.preferences</c> jsonb (additive
    /// — no migration needed).
    /// </para>
    /// <para>
    /// <b>Down:</b> drops <c>user_job_ad_matches</c> (all data lost) and the two nullable
    /// columns from <c>job_seekers</c>. Requires explicit approval before applying in
    /// non-test environments (repo convention, destructive classification).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddUserJobAdMatchAndConsentWatermarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_match_scan_at",
                table: "job_seekers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_matches_at",
                table: "job_seekers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_job_ad_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_ad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    matched_skill_concept_ids = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_job_ad_matches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_job_ad_matches_user_created_at",
                table: "user_job_ad_matches",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_user_job_ad_matches_user_id",
                table: "user_job_ad_matches",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_job_ad_matches_user_status",
                table: "user_job_ad_matches",
                columns: new[] { "user_id", "notification_status" });

            migrationBuilder.CreateIndex(
                name: "ux_user_job_ad_matches_user_jobad",
                table: "user_job_ad_matches",
                columns: new[] { "user_id", "job_ad_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_job_ad_matches");

            migrationBuilder.DropColumn(
                name: "last_match_scan_at",
                table: "job_seekers");

            migrationBuilder.DropColumn(
                name: "last_seen_matches_at",
                table: "job_seekers");
        }
    }
}
