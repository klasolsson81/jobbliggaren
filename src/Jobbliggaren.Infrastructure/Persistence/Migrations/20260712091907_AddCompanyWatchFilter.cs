using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bevaknings-reconcile PR-F1 (issue #799, RF-2, 2026-07-12) — adds the nullable
    /// <c>filter</c> jsonb column to <c>company_watches</c>. Stores the per-watch
    /// notification narrowing (<see cref="Jobbliggaren.Domain.CompanyWatches.WatchFilterSpec"/>):
    /// an optional municipality allow-list and/or an "only matched ads" flag.
    ///
    /// <para>
    /// <b>Mapping rationale:</b> a property-level <c>ValueConverter</c>
    /// (<c>WatchFilterSpecConversion</c>), mirroring <c>MatchPreferencesConverters</c> —
    /// NOT <c>OwnsOne(...).ToJson()</c>, which does not map <c>IReadOnlyList&lt;string&gt;</c>
    /// stably in Npgsql (issue #3129).
    /// </para>
    ///
    /// <para>
    /// <b>Nullable, NO default (unlike <c>match_preferences</c>' non-null <c>'{}'</c>
    /// default):</b> <c>NULL</c> is the canonical "no filter / show all" representation —
    /// EF never invokes the converter for a null CLR value, so SQL <c>NULL</c> round-trips
    /// as CLR <c>null</c> for free. Every pre-existing <c>company_watches</c> row becomes
    /// <c>NULL</c> on this <c>ADD COLUMN</c> — back-compatible with zero backfill step.
    /// </para>
    ///
    /// <para>
    /// <b>No index (architect design 2026-07-12, question 5):</b> the filter is read
    /// exclusively via its owning <c>company_watches</c> row (the PR-F1 reconcile scan
    /// loads the watch, then evaluates the filter in memory) — it is never queried by
    /// content, so a jsonb index would carry write cost with no read benefit.
    /// </para>
    ///
    /// <para>
    /// <b>RF-11=11B (senior-cto-advisor 2026-07-12, docs/reviews/2026-07-12-bevakning-
    /// reconciling-cto-bind.md):</b> one change-reason per migration — this migration
    /// carries ONLY the <c>filter</c> column. The job/scan-side reconcile changes that
    /// consume it ship as separate, non-schema PRs in the same epic.
    /// </para>
    ///
    /// <para>
    /// <b>GDPR:</b> <c>filter</c> holds JobTech municipality concept-ids (opaque strings,
    /// not free-text PII) and a boolean — not a new sensitive-data surface. Existing
    /// soft-delete (<c>deleted_at</c>) and query filter on <c>company_watches</c> already
    /// cover this column; <c>CompanyWatch.SoftDelete</c> additionally clears the filter
    /// in-app on unfollow (Art. 5(1)(c)/(e) — no latent profiling-adjacent data on a
    /// soft-deleted row). No new encryption surface.
    /// </para>
    ///
    /// <para>
    /// <b>Down:</b> destructive — <c>DROP COLUMN filter</c>. Requires explicit user
    /// approval if ever applied in a non-test environment (repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddCompanyWatchFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filter",
                table: "company_watches",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "filter",
                table: "company_watches");
        }
    }
}
