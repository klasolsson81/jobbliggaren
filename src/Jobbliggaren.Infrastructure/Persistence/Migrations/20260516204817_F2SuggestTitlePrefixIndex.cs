using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F2SuggestTitlePrefixIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0042 Beslut C / senior-cto-advisor Variant A (2026-05-16):
            // typeahead-prefix-index för SuggestJobAdTermsQueryHandler.
            // Functional partial B-tree-index. INGEN extension (ej pg_trgm) —
            // left-anchored LIKE räcker, lägre op-yta.
            //
            //   - lower(title) text_pattern_ops: query-handlern producerar
            //     lower(title) LIKE 'prefix%' ESCAPE '\'. text_pattern_ops är
            //     rätt operator-class för left-anchored LIKE när DB-collation
            //     ≠ C (default-btree-opclass stödjer ej prefix-LIKE under
            //     icke-C-collation).
            //   - WHERE status='Active' AND deleted_at IS NULL: speglar EXAKT
            //     query-predikatet (.Where(j => j.Status == JobAdStatus.Active)
            //     + global query filter j.DeletedAt == null). status-kolumnen
            //     lagrar strängvärdet (JobAdConfiguration HasConversion
            //     s => s.Value; JobAdStatus.Active.Value == "Active"). Partial
            //     → mindre index, GDPR soft-delete-filter respekterat.
            //
            // Npgsql fluent-API saknar functional/partial-index-stöd
            // (npgsql/efcore.pg #293, #119) → raw SQL (speglar F2P9-mönstret).
            // CREATE INDEX (ej CONCURRENTLY): migration körs i transaktion via
            // Migrate schema-task; CONCURRENTLY kan ej köras i transaktion.
            // F2P9 använde icke-CONCURRENTLY → spegla. På Fas 2-volym
            // (5–15k rader) är ACCESS EXCLUSIVE-låset under index-bygget
            // millisekunder — backward-compatible med körande Api.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_prefix " +
                "ON job_ads (lower(title) text_pattern_ops) " +
                "WHERE status = 'Active' AND deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_prefix;");
        }
    }
}
