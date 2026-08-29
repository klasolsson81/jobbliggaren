using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1558 follow-up — the PARTIAL btree that stops the enskild-firma company-watch token
    /// resolution (<c>ListCompanyWatchesQueryHandler</c>) from scanning <c>job_ads</c>. The token
    /// is an HMAC (<c>HmacProtectedIdentityTokenizer</c>) — one-way, no inverse to look up — so
    /// resolving it against a watch means tokenizing every pnr-shaped org.nr in the table and
    /// comparing; the only lever is shrinking what that sweep has to touch.
    ///
    /// <para>
    /// <b>Why PARTIAL and not FUNCTIONAL.</b> A functional index over e.g.
    /// <c>(length(organization_number), substring(organization_number, 3, 1))</c> would make the
    /// predicate sargable, but it still carries an index entry for every row in <c>job_ads</c> —
    /// legal-entity org.nrs included, which is the overwhelming majority (the seeded pin below
    /// keeps the pnr-shaped share at 20 rows in 50,020, and dev measured zero in 106,071 — sole
    /// proprietorships are rare by construction). A PARTIAL index over the same predicate holds
    /// entries for ONLY the pnr-shaped sliver, and — the load-bearing part — lets PostgreSQL drop
    /// the <c>Filter:</c> node entirely when it can PROVE the query predicate implies the index
    /// predicate, rather than reapplying it per row. That proof is what turns the scan's cost from
    /// growing with <c>job_ads</c> into growing with the pnr-shaped sliver only.
    /// </para>
    ///
    /// <para>
    /// <b>Why raw SQL and not <c>CreateIndex</c>:</b> EF Core cannot model an expression
    /// predicate (<c>length(...)</c>, <c>substring(...)</c>), so the model snapshot has no
    /// knowledge of this index — same blind spot as
    /// <c>ix_company_register_company_name_lower</c> (<c>20260718191128_AddCompanyRegisterNameSearchIndex</c>,
    /// the worked case this migration follows). Any future rebuild of <c>job_ads</c> must recreate
    /// this index BY HAND; no scaffolded migration will ever restore it.
    /// </para>
    ///
    /// <para>
    /// <b>The predicate's FORM is bound to <c>ListCompanyWatchesQueryHandler</c>'s LINQ, not just
    /// its rows.</b> A partial index is only usable when PostgreSQL can prove the query predicate
    /// implies the index predicate, and that proof is structural — it survives reordering the
    /// conjuncts, not a change of shape. Rewriting the handler's <c>Substring(2, 1)</c> as a
    /// <c>LIKE</c>, folding the boundary-digit check into a <c>Contains</c> over an array, or
    /// dropping the <c>Length == 10</c> guard as "redundant" all produce the identical row set
    /// while silently breaking the implication proof — same answers, every semantic test green,
    /// and the scan cost reverts to growing with the table. <c>PnrShapedPrefilterQueryPlanTests</c>
    /// is the only thing that sees that regression; it EXPLAIN-pins the plan by index name with an
    /// absent <c>Filter:</c> node, not merely "no Seq Scan" (the #805-3/#842 vacuous-guarantee
    /// discipline). The handler carries a <c>SHAPE-BOUND</c> comment pointing back here.
    /// </para>
    ///
    /// <para>
    /// <b>STATUS-AGNOSTIC on purpose.</b> The predicate does NOT include
    /// <c>status = 'Active'</c>. A followed company keeps its name and its watch regardless of
    /// whether its ads are currently active, so the token must still resolve for an enskild firma
    /// whose ads are all archived — and adding a status conjunct here would both explain a query
    /// the handler does not run and quietly narrow the index out from under the (status-free)
    /// query it does run.
    /// </para>
    ///
    /// <para>
    /// <b>Not shared with <c>CompanyWatchScanJob</c>.</b> That job writes the same three conjuncts
    /// as one arm of an <c>OR</c> nested under a <c>Status == Active &amp;&amp; CreatedAt &gt;
    /// since</c> guard (<c>CompanyWatchScanJob.cs</c>). Its predicate does not imply this index's
    /// predicate, so the job gets no plan change from this migration — it is scoped to the list
    /// handler's query only, and stays a full-table concern for the job.
    /// </para>
    ///
    /// <para>
    /// <b>Measured, not assumed — and the number decays.</b> On the dev database
    /// (106,071 job ads, zero pnr-shaped org.nr — the realistic ratio) a hand-applied copy of this
    /// index took the query from 9,216 buffers / 9,000 heap fetches / 107.3 ms to 1 buffer / 0
    /// heap fetches / 0.012 ms, because the <c>Filter:</c> node disappeared rather than shrank. On
    /// the box (58,823 ads) the unindexed query ran a Seq Scan at 65.5 ms — the plan differs by
    /// environment, the per-row filter cost does not. Regenerate rather than trust this:
    /// <c>EXPLAIN (ANALYZE, BUFFERS) SELECT DISTINCT organization_number FROM job_ads WHERE
    /// organization_number IS NOT NULL AND length(organization_number) = 10 AND
    /// substring(organization_number, 3, 1) IN ('0', '1');</c> before and after the index exists.
    /// </para>
    ///
    /// <para>
    /// <b>Why CONCURRENTLY (+ <c>suppressTransaction</c>, which it requires):</b> a plain
    /// <c>CREATE INDEX</c> takes SHARE lock and blocks writes to <c>job_ads</c> for the whole
    /// build — a table under continuous ingestion from the job-ad sync. Failure semantics: an
    /// aborted CONCURRENTLY build leaves an INVALID index behind, and <c>IF NOT EXISTS</c> treats
    /// an INVALID index as EXISTING — a bare re-run would silently skip the build and stamp the
    /// migration applied. Mechanical recovery, in this order:
    /// <c>DROP INDEX IF EXISTS ix_job_ads_org_nr_pnr_shaped;</c> (plain, not CONCURRENTLY — there
    /// is no completed index for readers to be using, so the stronger lock protects nothing) then
    /// re-run the migration.
    /// </para>
    /// </summary>
    public partial class AddJobAdOrgNrPnrShapedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_job_ads_org_nr_pnr_shaped
                ON job_ads (organization_number)
                WHERE organization_number IS NOT NULL
                  AND length(organization_number) = 10
                  AND substring(organization_number, 3, 1) IN ('0', '1');
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS ix_job_ads_org_nr_pnr_shaped;
                """,
                suppressTransaction: true);
        }
    }
}
