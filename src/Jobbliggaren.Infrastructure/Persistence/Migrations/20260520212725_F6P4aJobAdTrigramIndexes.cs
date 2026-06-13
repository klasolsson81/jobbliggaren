using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6P4aJobAdTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F6-P4a (senior-cto-advisor 2026-05-20, Approach A). GIN-trigram-index
            // för q-search-LIKE-acceleration på job_ads. Befintlig query använder
            // EF.Functions.Like(j.Title.ToLower(), "%q%") + samma på description
            // (JobAdSearch.cs rad 62-63 + 119-121) → 40s full-table-scan på ~52k
            // rader. Trigram-GIN på lower()-functional matchar EXAKT samma
            // expression → index-acceleration utan handler-ändring (Clean Arch
            // bevarad — INGEN Application-ändring, ren Infrastructure-fix).
            //
            //   - pg_trgm: trusted extension på AWS RDS PostgreSQL 16+ (web-
            //     verifierat 2026-05-20). CREATE EXTENSION IF NOT EXISTS är
            //     idempotent → ofarligt vid re-run.
            //   - USING gin (lower(title) gin_trgm_ops): GIN-trigram-opclass
            //     accelererar ILIKE/LIKE '%substring%'-mönster. lower()-functional
            //     speglar exakt Title.ToLower() i query — utan lower()-match
            //     skulle index inte användas.
            //   - WHERE status = 'Active' AND deleted_at IS NULL: speglar EXAKT
            //     query-predikatet (.Where(j => j.Status == JobAdStatus.Active)
            //     + global query filter j.DeletedAt == null, ADR 0042-Active-
            //     filter). status-kolumnen lagrar strängvärdet (JobAdConfiguration
            //     HasConversion s => s.Value; JobAdStatus.Active.Value == "Active").
            //     Partial → mindre index, GDPR soft-delete-filter respekterat.
            //
            // Npgsql fluent-API saknar functional/partial-/GIN-index-stöd
            // (npgsql/efcore.pg #293, #119) → raw SQL (speglar F2P9- och
            // F2SuggestTitlePrefixIndex-mönstret). CREATE INDEX (ej CONCURRENTLY):
            // EF Core wrappar migrations i transaktion via Migrate schema-task;
            // CONCURRENTLY kan ej köras i transaktion. Dev-volym ~51k rader →
            // ACCESS EXCLUSIVE-låset under GIN-bygget är acceptabelt sekund-
            // intervall — backward-compatible med körande Api.
            //
            // pg_trgm-extension SKAPAS SEPARAT av Jobbliggaren.Migrate
            // `ensure-extensions`-mode (master-creds) i deploy-pipeline FÖRE
            // schema-mode. Grund: jobbliggaren_app-rollen saknar CREATE-privilege
            // på databasen (TD-71 — REVOKE efter A5-deploy). ADR 0033-mönster:
            // extensions tillhör Phase A-domänen (master-privileged DDL), inte
            // Phase E (jobbliggaren_app DDL). Re-deploy är säker: ensure-extensions
            // är idempotent (IF NOT EXISTS).

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_trgm " +
                "ON job_ads USING gin (lower(title) gin_trgm_ops) " +
                "WHERE status = 'Active' AND deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_description_lower_trgm " +
                "ON job_ads USING gin (lower(description) gin_trgm_ops) " +
                "WHERE status = 'Active' AND deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EJ DROP EXTENSION pg_trgm — idempotent additive, kan användas av
            // andra tabeller framöver (speglar F2-mönstret för extensions).
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_description_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_trgm;");
        }
    }
}
