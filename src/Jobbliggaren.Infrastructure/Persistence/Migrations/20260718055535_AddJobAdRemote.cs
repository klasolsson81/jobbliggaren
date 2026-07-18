using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #551 — AF's remote/distans classification as an ORDINARY column on <c>job_ads</c>, written in C#
    /// through <c>JobAd.SetSourcePayload</c> exactly like the six #841 source facets. This is NOT a STORED
    /// generated column: the JobSearch response schema carries no per-ad remote field to derive from (ADR
    /// 0067 Beslut 3, amended 2026-07-18), and deriving durable state from <c>raw_payload</c> is the #841
    /// data-loss trap regardless (see <c>MaterialiseJobAdSourceFacets</c>'s class doc — DO NOT regenerate
    /// that migration, and do not let this one drift toward <c>HasComputedColumnSql</c> either).
    ///
    /// <c>remote boolean NOT NULL DEFAULT false</c> — not nullable, deliberately: the fail-safe direction
    /// is structural. An ad the harvest never spoke about reads <c>false</c>, so it stays subject to the
    /// #552 ort gate until a successful harvest lifts it. Existing rows backfill to <c>false</c> via the
    /// column DEFAULT — a plain <c>ADD COLUMN … DEFAULT false</c> is a metadata-only change on PG 11+ (no
    /// table rewrite), so this is safe at any table size.
    ///
    /// The partial index below is raw SQL because the fluent API cannot express a partial index (same
    /// pattern as the six #841 facet indexes and <c>ix_job_ads_ssyk_concept_id</c> et al.). Remote ads are
    /// sparse (~1.4%) and both readers query <c>remote = true</c> — the grade override in
    /// <c>MatchScorer</c>/<c>PerUserJobAdSearchQuery</c> today, and the future Distans facet filter — so a
    /// non-partial index would waste space indexing the ~98.6% of rows nobody ever looks up by this column.
    /// </summary>
    public partial class AddJobAdRemote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "remote",
                table: "job_ads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_remote ON job_ads (id) WHERE remote;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Index before column: DROP COLUMN would take ix_job_ads_remote with it implicitly, but
            // dropping it explicitly first keeps the two operations legible and matches the #841 sibling
            // migrations' Down shape (index lifecycle stated, not left to an implicit side effect).
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_remote;");

            migrationBuilder.DropColumn(
                name: "remote",
                table: "job_ads");
        }
    }
}
