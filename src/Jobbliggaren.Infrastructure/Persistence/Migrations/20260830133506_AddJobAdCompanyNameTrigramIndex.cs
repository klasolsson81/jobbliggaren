using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1546 — the trigram index that lets <c>?q=&lt;company name&gt;</c> reach an employer's ads.
    ///
    /// <para>
    /// The hero field promises "Sök efter yrke, arbetsgivare eller ort" while <c>search_vector</c> spans
    /// <c>title</c> + <c>description</c> only, so an employer name matched nothing at all. This index
    /// serves the <c>lower(company_name) LIKE '%q%'</c> branch added to the q-path in
    /// <c>JobAdSearchComposition</c>.
    /// </para>
    ///
    /// <para>
    /// <b><c>search_vector</c> was deliberately NOT widened instead.</b> It is a STORED generated column
    /// and PostgreSQL cannot ALTER a generated expression, so redefining it means DROP COLUMN + ADD
    /// COLUMN: a full rewrite of <c>job_ads</c> plus a GIN rebuild, under ACCESS EXCLUSIVE, on a table
    /// under continuous ingestion. It would also shift <c>ts_rank</c> for every existing search. One
    /// CONCURRENTLY-built index buys the same recall without touching a byte of the table.
    /// </para>
    ///
    /// <para>
    /// <b>Raw SQL, not fluent.</b> The index KEY is an expression (<c>lower(company_name)</c>), which is
    /// outside EF's reach — unlike a partial index's <c>HasFilter</c>, which takes raw SQL and is never
    /// parsed. So this index is invisible to the model snapshot, exactly like
    /// <c>ix_company_register_company_name_lower</c>. <c>JobAdCompanyNameIndexOracleTests</c> is what
    /// sees it, and reads its definition out of <c>pg_indexes</c> rather than reasoning about it.
    /// </para>
    ///
    /// <para>
    /// <b>CONCURRENTLY, per the <c>20260829091621_AddJobAdOrgNrPnrShapedIndex</c> precedent.</b> A plain
    /// <c>CREATE INDEX</c> takes a SHARE lock and blocks writes to <c>job_ads</c> for the whole build.
    /// Failure semantics are the same and worth repeating: an aborted CONCURRENTLY build leaves an
    /// INVALID index behind, and <c>IF NOT EXISTS</c> treats an INVALID index as EXISTING, so a bare
    /// re-run would silently skip the build and stamp the migration applied. Mechanical recovery, in
    /// this order: <c>DROP INDEX IF EXISTS ix_job_ads_company_name_lower_trgm;</c> (plain, not
    /// CONCURRENTLY — there is no completed index for readers to be using) then re-run.
    /// </para>
    ///
    /// <para>
    /// <c>pg_trgm</c> is NOT created here. The <c>jobbliggaren_app</c> role lacks CREATE on the database;
    /// the extension is installed out of band by <c>Jobbliggaren.Migrate</c>'s <c>ensure-extensions</c>
    /// mode, which is why <c>20260520212725_F6P4aJobAdTrigramIndexes</c> does not create it either.
    /// </para>
    ///
    /// <para>
    /// No predicate, deliberately (#821 Q2): a partial index is usable only while the query's WHERE
    /// provably implies the index's, an uncheckable coupling this repo has already paid for once.
    /// </para>
    /// </summary>
    public partial class AddJobAdCompanyNameTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_job_ads_company_name_lower_trgm
                ON job_ads USING gin (lower(company_name) gin_trgm_ops);
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS ix_job_ads_company_name_lower_trgm;
                """,
                suppressTransaction: true);
        }
    }
}
