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
    /// <b>Collation — inherited, not pinned, and that is deliberate.</b> No
    /// <c>UseCollation(...)</c> is set on <c>company_name</c> or on this index: the index therefore
    /// inherits the column's default collation, which is EXACTLY the collation
    /// <c>ORDER BY company_name</c> sorts under — matching by construction, not by two places
    /// agreeing. The database currently collates under <c>en_US.utf8</c>, which sorts Swedish
    /// Å/Ä/Ö among A/O instead of after Z (tracked separately, #884). <b>If/when #884 changes
    /// company_name's collation (e.g. to <c>sv-SE-x-icu</c>), THIS INDEX MUST BE REBUILT</b> — an
    /// index built under one collation does not serve an ORDER BY requested under another, and
    /// Postgres does not error when that happens, it just silently stops using the index (repo
    /// precedent for vacuous-by-mismatch: #805-3, #842). This paragraph is the tripwire for whoever
    /// ships #884.
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
