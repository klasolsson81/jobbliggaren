using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #312 (ADR 0115) — additive, nullable. ADD COLUMN <c>results_seen_at</c> (timestamptz NULL) on
    /// <c>saved_searches</c> — the per-search USER-read watermark for the in-app "N nya träffar"-count
    /// (sibling of <c>last_seen_matches_at</c> / <c>last_seen_jobs_at</c> / <c>last_seen_followed_ads_at</c>).
    /// Drives the /sokningar count (NEW = JobAd.CreatedAt &gt; this, via the live CountNewSinceAsync
    /// window). DISTINCT from <c>last_run_at</c> (the deferred email-phase scan mark, ADR 0039 Beslut 2).
    /// <para>
    /// <b>GDPR:</b> a low-sensitivity per-user behavioural timestamp (when you last saw a saved search's
    /// results) — no special-category PII, no personnummer, no DEK column, no encryption surface.
    /// Retention parity with <c>last_seen_matches_at</c>; cleared with the SavedSearch row on Art. 17
    /// hard-delete (no separate cascade needed — it is a column on the aggregate, which is in
    /// AccountHardDeleter's CascadeMap; DPIA #312 C-SS1, machine-guarded by the cascade fitness test).
    /// </para>
    /// <para>
    /// <b>Additive + nullable, WITH backfill</b> → safe forward migration. The one-row backfill sets
    /// existing rows to <c>now()</c> (NOT <c>created_at</c>) so no historical backlog surfaces as "new"
    /// on launch — parity with a fresh search's ctor baseline; the count handler additionally coalesces
    /// a null defensively to the search's CreatedAt. <b>Down:</b> drops the column (the backfilled
    /// baseline + any post-launch MarkResultsSeen advances are lost — non-destructive to any other data;
    /// requires explicit approval before applying in non-test environments per repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddSavedSearchResultsSeenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "results_seen_at",
                table: "saved_searches",
                type: "timestamp with time zone",
                nullable: true);

            // #312 (ADR 0115) — backfill befintliga rader till now() så ingen historisk
            // backlogg tänds som "N nya träffar" vid feature-lansering (en ny sökning
            // init:ar samma watermark i ctor:n; parity). Kolumnen förblir nullable/null-
            // tolerant (räknings-handlern coalescar null → sökningens CreatedAt defensivt),
            // men efter denna backfill är alla existerande rader satta.
            migrationBuilder.Sql(
                "UPDATE saved_searches SET results_seen_at = now() WHERE results_seen_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "results_seen_at",
                table: "saved_searches");
        }
    }
}
