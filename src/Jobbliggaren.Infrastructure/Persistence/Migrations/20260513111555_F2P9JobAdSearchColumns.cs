using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F2P9JobAdSearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F2-P9 (TD-70, CTO-rond 2026-05-13 Q2-C). Postgres generated columns
            // som speglar saniterad raw_payload-data (ADR 0032 §8). STORED →
            // B-tree-indexerbar utan runtime-overhead. Drift omöjlig (read-only).
            migrationBuilder.AddColumn<string>(
                name: "region_concept_id",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'workplace_address'->>'region_concept_id'",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "ssyk_concept_id",
                table: "job_ads",
                type: "text",
                nullable: true,
                computedColumnSql: "raw_payload->'occupation'->>'concept_id'",
                stored: true);

            // Partial B-tree-index (NULL exkluderat) — manuell annons utan
            // raw_payload får ingen index-entry, reducerar index-storlek.
            // PostgreSQL fluent-API saknar partial-index-stöd för shadow
            // properties som inte är top-level Domain → raw SQL.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_ssyk_concept_id " +
                "ON job_ads (ssyk_concept_id) " +
                "WHERE ssyk_concept_id IS NOT NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_region_concept_id " +
                "ON job_ads (region_concept_id) " +
                "WHERE region_concept_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_region_concept_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_ssyk_concept_id;");

            migrationBuilder.DropColumn(
                name: "region_concept_id",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "ssyk_concept_id",
                table: "job_ads");
        }
    }
}
