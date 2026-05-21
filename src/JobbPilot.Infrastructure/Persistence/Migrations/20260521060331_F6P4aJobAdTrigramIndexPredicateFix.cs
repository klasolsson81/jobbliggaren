using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6P4aJobAdTrigramIndexPredicateFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F6 P4 (2026-05-21) — KORRIGERING av F6P4aJobAdTrigramIndexes.
            // Ursprungligt partial-predikat `WHERE status = 'Active' AND
            // deleted_at IS NULL` kopierade F2SuggestTitlePrefixIndex-mönstret,
            // MEN det indexet betjänar SuggestJobAdTermsQueryHandler som HAR
            // ett explicit `.Where(j => j.Status == JobAdStatus.Active)`-filter.
            // ListJobAdsQueryHandler / JobAdSearch.ApplyCriteria (q-search-vägen
            // som GIN-trigram-indexet ska accelerera) har INGET status-filter —
            // bara global query filter `deleted_at IS NULL`. PostgreSQL kan
            // bara använda ett partiellt index om query-WHERE implicerar index-
            // WHERE; eftersom q-search saknar `status='Active'` valdes seq scan
            // → q-search förblev ~35-50s efter v0.2.53-dev (index byggt men oanvänt).
            //
            // Fix: partial-predikat = `WHERE deleted_at IS NULL` (matchar EXAKT
            // ListJobAds global query filter — alltid applicerat). DROP + CREATE
            // eftersom partial-predikatet inte kan ALTER:as.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_description_lower_trgm;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_trgm " +
                "ON job_ads USING gin (lower(title) gin_trgm_ops) " +
                "WHERE deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_description_lower_trgm " +
                "ON job_ads USING gin (lower(description) gin_trgm_ops) " +
                "WHERE deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Återställ ursprungligt (felaktiga) partial-predikat.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_description_lower_trgm;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_trgm " +
                "ON job_ads USING gin (lower(title) gin_trgm_ops) " +
                "WHERE status = 'Active' AND deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_description_lower_trgm " +
                "ON job_ads USING gin (lower(description) gin_trgm_ops) " +
                "WHERE status = 'Active' AND deleted_at IS NULL;");
        }
    }
}
