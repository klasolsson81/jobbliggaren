using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAdOrganizationNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #311 D1 / ADR 0087 — STORED generated column för arbetsgivarens
            // organisationsnummer. Samma B2-mönster som F6P7 (employment_type +
            // worktime_extent). NESTED path: raw_payload->'employer'->>'organization_number'
            // (till skillnad från top-level-keys som occupation_group / employment_type).
            //
            // LÅSBETEENDE: `ADD COLUMN ... GENERATED ALWAYS AS (...) STORED` är en FULL
            // TABLE REWRITE under ACCESS EXCLUSIVE-lås i PostgreSQL (samma som F6P7/F6P6/
            // F2P9). Lokalt: sekunder mot ~44k rader. Deploy-konsekvens (Hetzner): kort
            // hård paus på job_ads-queries medan tabellen skrivs om.
            //
            // BACKFILL-STATUS (identisk med F6P7): raw_payload saknar
            // employer.organization_number för ALLA befintliga rader — JobTechEmployer-
            // POCO:n deserialiserade fältet aldrig förrän #311 → kolumnen NULL för
            // 100% av raderna direkt efter migrationen. Populering sker FÖRST efter att
            // POCO-tillägget deployats OCH en full re-ingest re-serialiserat raw_payload
            // med det nya fältet. Tills dess är org.nr-filter ett no-op (0 träffar).
            //
            // GDPR / ENSKILD FIRMA-NOT (ADR 0087 D8, Klas Art. 32 risk-accept
            // 2026-06-30): ett org.nr på 10 siffror utan bindestreck kan vara ett
            // personnummer (enskild firma). AT-REST: KLARTEXT, ingen at-rest-
            // kryptering. (Den tidigare "krypterad at-rest"-noten var stale/felaktig
            // — korrigerad per ADR 0087 D8, samma korrigering som raw_payload-noten i
            // JobAdConfiguration; återuppväck den inte.) Kolumnen är MEDVETET klartext:
            // redan publik Platsbanken-data, samma klartext-precedent som raw_payload,
            // queryability-nödvändighet. Skyddet är surfacing-gränsen — org.nr
            // maskeras/flaggas när det är personnummer-format, ALDRIG loggat/surfat
            // oflaggat (CLAUDE.md §5 anti-patterns).
            migrationBuilder.AddColumn<string>(
                name: "organization_number",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'employer'->>'organization_number'",
                stored: true);

            // Partial B-tree-index (NULL exkluderat) — annonser utan
            // employer.organization_number i raw_payload får ingen index-entry.
            // Eftersom kolumnen är NULL för 100% av raderna tills re-ingest är indexet
            // initialt TOMT och växer i takt med re-ingest (korrekt, ingen död
            // index-yta för NULL-rader). PostgreSQL fluent-API saknar partial-index-stöd
            // för shadow properties → raw SQL (samma skäl som F6P7/F6P6/F2P9).
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_organization_number " +
                "ON job_ads (organization_number) " +
                "WHERE organization_number IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_organization_number;");

            migrationBuilder.DropColumn(
                name: "organization_number",
                table: "job_ads");
        }
    }
}
