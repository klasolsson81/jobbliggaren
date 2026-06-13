using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6P4FtsSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F6 P4 (ADR 0062) — FTS search_vector. STORED tsvector generated
            // column härledd från title + description av PostgreSQL
            // ('swedish'-config för svensk stemming). to_tsvector + GIN på
            // tsvector är core PostgreSQL (PG 16+) — ingen ny extension.
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "job_ads",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('swedish', coalesce(title,'') || ' ' || coalesce(description,''))",
                stored: true);

            // GIN-index via raw SQL — PostgreSQL fluent-API saknar partial-
            // index-stöd för shadow properties (samma skäl som F2P9 / F6P4a
            // partial-index). Partial-predikat = `WHERE deleted_at IS NULL`,
            // EJ `status = 'Active'`: ListJobAds / JobAdSearch.ApplyCriteria
            // (FTS-search-vägen) har INGET status-filter, bara global query
            // filter `deleted_at IS NULL`. PostgreSQL kan bara använda ett
            // partiellt index om query-WHERE implicerar index-WHERE — se
            // F6P4aJobAdTrigramIndexPredicateFix för partial-predikat-buggen
            // v0.2.53→54.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_search_vector " +
                "ON job_ads USING gin (search_vector) " +
                "WHERE deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_search_vector;");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "job_ads");
        }
    }
}
