using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6P6JobAdKlass1SearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // B1 / Klass 1 (ADR 0067 Beslut 2 + ADR 0043-amendment 2026-06-08).
            // STORED generated columns för Platsbanken sök-paritet — yrkesgrupp
            // (occupation_group, ssyk-level-4) + kommun (municipality_concept_id).
            // Speglar saniterad raw_payload (ADR 0032 §8); STORED → B-tree-
            // indexerbar utan runtime-overhead, drift omöjlig (read-only).
            // OBS: occupation_group är TOP-LEVEL i payloaden (EJ nested under
            // occupation som ssyk_concept_id i F2P9).
            //
            // KRITISKT — låsbeteende vid apply: `ADD COLUMN ... GENERATED ALWAYS
            // AS (...) STORED` är i PostgreSQL en FULL TABLE REWRITE under
            // ACCESS EXCLUSIVE-lås. Det är INTE en metadata-billig operation
            // (discovery-briefen hade fel på den punkten). Lokalt: sekunder mot
            // ~44k rader; F2P9-precedensen körde redan exakt detta mönster utan
            // problem. Deploy-konsekvens (Hetzner): kort hård paus på
            // job_ads-queries medan tabellen skrivs om. Backfill sker
            // automatiskt — PostgreSQL beräknar kolumnvärdena från befintlig
            // raw_payload vid rewrite, ingen re-ingest krävs (dev-DB verifierat:
            // 34843 rader har occupation_group, 33935 har municipality).
            migrationBuilder.AddColumn<string>(
                name: "municipality_concept_id",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'workplace_address'->>'municipality_concept_id'",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "occupation_group_concept_id",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'occupation_group'->>'concept_id'",
                stored: true);

            // Partial B-tree-index (NULL exkluderat) — annonser utan
            // occupation_group / municipality i raw_payload får ingen index-entry,
            // reducerar index-storlek. PostgreSQL fluent-API saknar partial-index-
            // stöd för shadow properties → raw SQL (samma skäl som F2P9).
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_occupation_group_concept_id " +
                "ON job_ads (occupation_group_concept_id) " +
                "WHERE occupation_group_concept_id IS NOT NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_municipality_concept_id " +
                "ON job_ads (municipality_concept_id) " +
                "WHERE municipality_concept_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_municipality_concept_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_occupation_group_concept_id;");

            migrationBuilder.DropColumn(
                name: "municipality_concept_id",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "occupation_group_concept_id",
                table: "job_ads");
        }
    }
}
