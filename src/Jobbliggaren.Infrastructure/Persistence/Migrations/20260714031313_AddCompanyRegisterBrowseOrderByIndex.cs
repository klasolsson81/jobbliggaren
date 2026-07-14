using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #875 (senior-cto-advisor bind 2026-07-14) — ONE plain btree index:
    /// <c>company_register (company_name, organization_number)</c>. Serves the ORDER BY
    /// <c>CompanyWatchBrowseQuery.ItemsSql</c> issues (paginated, LIMIT/OFFSET) over the 1,17M-row
    /// register. EF-modelled in <c>ScbCompanyRegisterEntryConfiguration</c> — parity with that
    /// table's four existing indexes, all of which are EF-modelled, none raw SQL.
    ///
    /// <para>
    /// <b>Measured effect (production's actual post-sync state — GIN's fastupdate pending list
    /// full, p95 over n=20).</b> SELECTIVE (5 SNI x 3 kommun): 32 ms -&gt; 36 ms (unaffected; stays
    /// on BitmapAnd(GIN, kommun) -&gt; Sort over a handful of hits). BROAD (40 x 290): 206 ms
    /// -&gt; 37 ms. WORST (1000 x 290, bound-legal maximum): 7 066 ms -&gt; 26 ms against ADR 0045's
    /// 300 ms budget — a ~23x-over regression closed. The index turns the plan from "materialize
    /// the whole match set, then Sort" into an ordered Index Scan that LIMIT stops after 20 rows.
    /// </para>
    ///
    /// <para>
    /// <b>Ops note — plain, not CONCURRENTLY (a stated choice, updating PR-1's precedent for this
    /// table).</b> A plain (non-CONCURRENTLY) <c>CREATE INDEX</c> takes a SHARE lock on
    /// <c>company_register</c> for the build's duration — it blocks WRITES but NOT reads. Measured
    /// build time on the 1,17M-row register: 2,4-7,6 s. The register's ONLY writer is the nightly
    /// SCB sync; ADR 0091's operational note already instructs operators not to run schema
    /// migrations while that job is running, and the deploy is manually approved, so the operator
    /// controls the timing. PR-1 (<c>AddCompanyWatchCriteriaAndRegisterSniGin</c>) chose plain for
    /// the sibling GIN index on this same table with the reasoning "no live consumer yet" — that
    /// premise is now FALSE (PR-2, #879, shipped <c>CompanyWatchBrowseQuery</c> as a live READ
    /// consumer). It does not change the conclusion: a SHARE lock does not block reads, so the
    /// browse endpoint is unaffected by this migration regardless of when it runs — only the
    /// nightly sync write would wait, for single-digit seconds, during an already-avoided window.
    /// <c>CREATE INDEX CONCURRENTLY</c> (which would need <c>suppressTransaction: true</c>, pulling
    /// this migration out of the standard transactional envelope, and which risks leaving an
    /// INVALID index behind if it races a concurrent write) buys real safety only against a writer
    /// this table does not have outside one controlled, avoidable window — so the added operational
    /// complexity is not spent here.
    /// </para>
    ///
    /// <para>
    /// <b>Collation — inherited from the column, which is now explicitly Swedish (#884).</b> No
    /// <c>UseCollation(...)</c> is set on this index: it inherits <c>company_name</c>'s COLUMN
    /// collation, which is EXACTLY the collation <c>ORDER BY company_name</c> sorts under — matching
    /// by construction, not by two places agreeing. That property is unchanged by #884, and in fact
    /// strengthened: the column now carries an explicit ICU <c>sv-SE</c> collation, so the sort order
    /// no longer depends on how the target cluster happened to be <c>initdb</c>'d.
    /// </para>
    ///
    /// <para>
    /// <b>CORRECTION (#884, 2026-07-14) — the paragraph that stood here was false.</b> It said that
    /// #884 would change the column's collation and that <b>THIS INDEX MUST THEN BE REBUILT</b>, or it
    /// would silently fall out of the plan. <b>Measured, and disproved:</b> <c>ALTER TABLE ... ALTER
    /// COLUMN ... TYPE text COLLATE "swedish"</c> rebuilds every dependent index automatically and
    /// atomically (1,17M rows: the table's <c>relfilenode</c> is unchanged — no rewrite — while this
    /// index's changed, and it returns <c>indisvalid = t</c> under the new collation). There was never
    /// a manual step to forget. The warning was aimed at the road #884 did NOT take: it describes what
    /// happens if the collation is put in the QUERY, or set explicitly and divergently on the index,
    /// instead of on the column. It is left corrected rather than deleted because a false warning is
    /// the mirror image of a guard that claims a protection it never had — both are untrue statements
    /// living in the codebase, and the false one teaches the next reader to discount the true ones.
    /// </para>
    ///
    /// <para>
    /// <b>The live danger, stated correctly:</b> do not write <c>COLLATE</c> into
    /// <c>CompanyWatchBrowseQuery.ItemsSql</c> — the column carries it. A sort requested under a
    /// collation this index was not built under does not error; Postgres silently Sorts the whole
    /// match set (back to 7 066 ms). The guard is not this comment: it is
    /// <c>CompanyWatchBrowseQueryPlanTests.BroadCriterion_WalksTheNameIndexInOrder_AndStopsEarly</c>,
    /// which goes red the moment the two diverge.
    /// </para>
    ///
    /// <para>
    /// <b>Plain, not partial, not covering</b> — see the index's doc-comment in
    /// <c>ScbCompanyRegisterEntryConfiguration</c> for the full reasoning (no WHERE clause this
    /// table's query shape can usefully carry; INCLUDE-ing the remaining SELECT columns was not
    /// part of what the campaign measured).
    /// </para>
    ///
    /// <para>
    /// <b>Down:</b> drops the index. Non-destructive (no data loss) — a plain <c>DROP INDEX</c>
    /// re-admits the pre-#875 Sort-over-match-set cost, nothing more.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddCompanyRegisterBrowseOrderByIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_company_register_company_name_organization_number",
                table: "company_register",
                columns: new[] { "company_name", "organization_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_company_register_company_name_organization_number",
                table: "company_register");
        }
    }
}
