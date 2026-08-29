using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1558 follow-up — the PARTIAL btree that stops the enskild-firma company-watch token
    /// resolution (<c>ListCompanyWatchesQueryHandler</c>) from scanning <c>job_ads</c>. The token is
    /// an HMAC (<c>HmacProtectedIdentityTokenizer</c>) — one-way, no inverse to look up — so
    /// resolving it against a watch means tokenizing every pnr-shaped org.nr in the table and
    /// comparing. The only lever is shrinking what that sweep has to touch.
    ///
    /// <para>
    /// <b>Why PARTIAL and not FUNCTIONAL — three grounds, and the third is the decisive one.</b>
    /// (1) Size: a functional index over <c>(length(...), substring(...))</c> is sargable but still
    /// carries an entry for every row in <c>job_ads</c>, legal-entity org.nrs included, which is the
    /// overwhelming majority. The partial form carries entries only for the pnr-shaped sliver.
    /// (2) Absorption: PostgreSQL drops the <c>Filter:</c> node entirely once it can PROVE the query
    /// predicate implies the index predicate, instead of re-applying it per row. (3) <b>Covering by
    /// construction</b>: <c>organization_number</c> is the index key AND the query's only projected
    /// column AND its DISTINCT key, so the scan is Index Only with zero heap fetches. A functional
    /// index is not covering — it would need a heap fetch per matching row just to produce the
    /// column, unless one adds <c>INCLUDE</c> or moves the column into the key. That third ground is
    /// why the measurement below lands on 1 buffer rather than somewhere between.
    /// </para>
    ///
    /// <para>
    /// <b>What the partial form costs, stated honestly.</b> A functional index needs no implication
    /// proof, only expression matching, so it survives a drift the partial form does not: drop the
    /// <c>Length == 10</c> guard from the handler and a functional index stays usable (worse
    /// selectivity, no plan collapse) while this one's proof fails outright and the scan reverts.
    /// The large constant factor was bought with increased form-fragility — which is precisely why
    /// <c>PnrShapedPrefilterQueryPlanTests</c> is load-bearing rather than decorative.
    /// </para>
    ///
    /// <para>
    /// <b>Why raw SQL when the index IS modelled.</b> It is declared fluently in
    /// <c>JobAdConfiguration</c> (<c>HasIndex(...).HasFilter(...)</c>), so the model snapshot knows
    /// it, a future rebuild of <c>job_ads</c> regenerates it, and a later
    /// <c>HasIndex(j =&gt; j.OrganizationNumber)</c> cannot be scaffolded as a silent unfiltered
    /// duplicate. EF models a partial index fine: <c>HasFilter</c> takes raw SQL and never parses
    /// it. Only an expression KEY is beyond it — <c>ix_company_register_company_name_lower</c>'s
    /// <c>lower(company_name)</c> is that case and this is not, which is why that migration's
    /// "the snapshot cannot know" reasoning does NOT transfer here. The scaffolded
    /// <c>CreateIndex</c> was replaced by the raw statement for ONE reason: <c>CONCURRENTLY</c>.
    /// </para>
    ///
    /// <para>
    /// <b>The predicate's FORM is bound to the handler's LINQ, not just its rows.</b> A partial
    /// index is usable only while the implication proof holds, and that proof is structural: it
    /// survives reordering the conjuncts, not a change of shape. Rewriting <c>Substring(2, 1)</c>
    /// as a <c>LIKE</c>, folding the boundary digits into a <c>Contains</c> over a collection, or
    /// dropping the <c>Length == 10</c> guard as "redundant" each produce the identical row set
    /// while silently breaking the proof — same answers, every semantic test green, and the scan
    /// cost back to growing with the table. The <c>Contains</c> case is the subtle one and it is
    /// CONDITIONAL: the prover expands a <c>ScalarArrayOpExpr</c> only when its array is a Const, so
    /// a <c>Contains</c> over a value EF parameterises (<c>= ANY(@p)</c>) breaks the proof while one
    /// EF folds to a literal does not. Measured 2026-08-29: mutating the handler to
    /// <c>Contains</c> over a <c>static readonly string[]</c> kept the plan pin GREEN — that shape
    /// still proved. Dropping the <c>Length == 10</c> guard did NOT: it fell to a Seq Scan. Do not
    /// read "a Contains breaks it" as unconditional; read the pin.
    /// The binding is named at three sites: <c>JobAdConfiguration</c>'s filter text,
    /// <c>ListCompanyWatchesQueryHandler.PnrShapedAdPredicate</c>, and the plan pin — which EXPLAINs
    /// that predicate field itself rather than a copied SQL string.
    /// </para>
    ///
    /// <para>
    /// <b>STATUS-AGNOSTIC on purpose.</b> The predicate does NOT include <c>status = 'Active'</c>.
    /// A followed company keeps its name and its watch regardless of whether its ads are currently
    /// active, so the token must still resolve for an enskild firma whose ads are all archived — and
    /// a status conjunct here would both describe a query the handler does not run and narrow the
    /// index out from under the (status-free) one it does.
    /// </para>
    ///
    /// <para>
    /// <b>Not shared with <c>CompanyWatchScanJob</c>.</b> That job writes the same three conjuncts
    /// as one arm of an <c>OR</c> beside <c>abOrgNrs.Contains(...)</c>, nested under
    /// <c>Status == Active &amp;&amp; CreatedAt &gt; since</c>. Implication of a disjunction requires
    /// BOTH arms to imply, and the org.nr-list arm says nothing about <c>length()</c> or
    /// <c>substring()</c> — so its predicate does not imply this index's and the index cannot absorb
    /// it. Whether PostgreSQL still reaches the index for that one arm through a BitmapOr path is
    /// NOT measured here, so no claim is made about the job's plan either way.
    /// </para>
    ///
    /// <para>
    /// <b>Measured 2026-08-29, and the numbers decay.</b> On the dev database (106 071 job ads,
    /// zero pnr-shaped org.nr — the realistic ratio, sole proprietorships are rare by construction)
    /// a hand-applied copy of this index took the query from 9 216 buffers / 9 000 heap fetches /
    /// 107.3 ms to 1 buffer / 0 heap fetches / 0.012 ms, because the <c>Filter:</c> node disappeared
    /// rather than shrank. On the box (58 823 ads) the unindexed query ran a Seq Scan at 65.5 ms —
    /// the plan differs by environment, the per-row filter cost does not. Regenerate rather than
    /// trust these:
    /// <c>EXPLAIN (ANALYZE, BUFFERS) SELECT DISTINCT organization_number FROM job_ads WHERE
    /// organization_number IS NOT NULL AND length(organization_number) = 10 AND
    /// substring(organization_number, 3, 1) IN ('0', '1');</c>
    /// </para>
    ///
    /// <para>
    /// <b>Why CONCURRENTLY (+ <c>suppressTransaction</c>, which it requires):</b> a plain
    /// <c>CREATE INDEX</c> takes a SHARE lock and blocks writes to <c>job_ads</c> for the whole
    /// build — a table under continuous ingestion. Failure semantics: an aborted CONCURRENTLY build
    /// leaves an INVALID index behind, and <c>IF NOT EXISTS</c> treats an INVALID index as EXISTING,
    /// so a bare re-run would silently skip the build and stamp the migration applied. Mechanical
    /// recovery, in this order: <c>DROP INDEX IF EXISTS ix_job_ads_org_nr_pnr_shaped;</c> (plain,
    /// not CONCURRENTLY — there is no completed index for readers to be using, so the stronger lock
    /// protects nothing) then re-run the migration.
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
