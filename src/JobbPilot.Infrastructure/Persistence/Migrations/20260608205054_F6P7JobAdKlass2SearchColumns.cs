using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6P7JobAdKlass2SearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // B2 / Klass 2 (ADR 0067 Beslut 2, Platsbanken sök-paritet). STORED
            // generated columns för anställningsform (employment_type) + omfattning
            // (worktime_extent). Båda TOP-LEVEL i payloaden (som occupation_group
            // i F6P6). NAMNGLAPP: kolumnen worktime_extent_concept_id läser payload-
            // pathen working_hours_type (taxonomi-typ worktime-extent ≠ wire-key).
            //
            // LÅSBETEENDE: `ADD COLUMN ... GENERATED ALWAYS AS (...) STORED` är i
            // PostgreSQL en FULL TABLE REWRITE under ACCESS EXCLUSIVE-lås (samma som
            // F6P6/F2P9). Lokalt: sekunder mot ~44k rader. Deploy-konsekvens
            // (Hetzner): kort hård paus på job_ads-queries medan tabellen skrivs om.
            //
            // KRITISK SKILLNAD MOT F6P6: rewrite BACKFILLAR INGENTING. raw_payload
            // saknar employment_type/working_hours_type-keys för ALLA befintliga
            // rader (JobTechHit-POCO deserialiserade dem aldrig förrän B2) →
            // kolumnerna blir NULL för 100% av raderna. Populering sker FÖRST efter
            // att POCO-tillägget deployats OCH en full re-ingest re-serialiserat
            // raw_payload med de nya fälten (backfill-klass2-jobbet, per-ID-refetch
            // — undviker snapshot-trunkering, samma mönster som ssyk-backfillen).
            // Tills dess är employment_type/worktime_extent-filter ett no-op (0
            // träffar) — exakt regressions-mönstret F6 P4 fixade för ssyk/region.
            migrationBuilder.AddColumn<string>(
                name: "employment_type_concept_id",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'employment_type'->>'concept_id'",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "worktime_extent_concept_id",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'working_hours_type'->>'concept_id'",
                stored: true);

            // Partial B-tree-index (NULL exkluderat) — annonser utan employment_type
            // / working_hours_type i raw_payload får ingen index-entry. Eftersom
            // kolumnerna är NULL för 100% av raderna tills re-ingest är indexet
            // initialt TOMT och växer i takt med re-ingest (korrekt, ingen död
            // index-yta för NULL-rader). PostgreSQL fluent-API saknar partial-index-
            // stöd för shadow properties → raw SQL (samma skäl som F6P6/F2P9).
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_employment_type_concept_id " +
                "ON job_ads (employment_type_concept_id) " +
                "WHERE employment_type_concept_id IS NOT NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_worktime_extent_concept_id " +
                "ON job_ads (worktime_extent_concept_id) " +
                "WHERE worktime_extent_concept_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_worktime_extent_concept_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_employment_type_concept_id;");

            migrationBuilder.DropColumn(
                name: "employment_type_concept_id",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "worktime_extent_concept_id",
                table: "job_ads");
        }
    }
}
