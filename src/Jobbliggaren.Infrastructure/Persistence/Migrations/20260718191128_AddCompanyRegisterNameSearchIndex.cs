using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #560 company-search wave (CTO F2) — the case-insensitive name-PREFIX index for the
    /// register search: a functional btree over <c>lower(company_name)</c> under
    /// <c>text_pattern_ops</c>.
    ///
    /// <para>
    /// <b>Why this exact shape.</b> The search predicate is
    /// <c>lower(company_name) LIKE lower(@name_prefix) ESCAPE '\'</c>. The existing composite
    /// btree (<c>company_name, organization_number</c>) can NEVER serve it: it indexes the raw
    /// column under the ICU <c>swedish</c> collation (#884), and (a) an expression
    /// (<c>lower(...)</c>) only matches an index built over the SAME expression, (b) a plain
    /// collated btree does not support <c>LIKE</c>-prefix range derivation at all — that is what
    /// <c>text_pattern_ops</c> exists for. Without this index the name search is a sequential
    /// scan over ~1,07M rows. The plan is EXPLAIN-pinned BY INDEX NAME in
    /// <c>CompanyRegisterSearchQueryPlanTests</c> (the #805-3/#842 vacuous-guarantee discipline).
    /// </para>
    ///
    /// <para>
    /// <b>Why raw SQL and not <c>CreateIndex</c>:</b> EF cannot model an expression index —
    /// which also means the MODEL SNAPSHOT does not know it exists. That is deliberate and
    /// documented on <c>ScbCompanyRegisterEntryConfiguration</c>: any future table-rebuild
    /// migration must recreate it BY HAND (the DROP-COLUMN-drops-indexes trap the repo has
    /// already met — EF's snapshot is blind to this index, so no scaffolded migration will ever
    /// restore it).
    /// </para>
    ///
    /// <para>
    /// <b>Why CONCURRENTLY (+ <c>suppressTransaction</c>, which it requires):</b> a plain
    /// <c>CREATE INDEX</c> takes SHARE lock and blocks writes for the whole build — on the
    /// ~1,07M-row register that collides with the Saturday bulk sync's upsert batches (CTO F7:
    /// never block the sync). The converse direction is the one to schedule around: the build
    /// itself WAITS (is delayed, never fails) on any already-open long transaction against the
    /// table — the sync's vanish-sweep UPDATE can hold one for up to its 600 s ceiling — so
    /// don't apply this against the dev DB mid-sync. Fresh databases (CI/Testcontainers) build
    /// it instantly either way. First operative use of <c>suppressTransaction</c> in this repo
    /// (the four earlier expression-index migrations each DECLINED CONCURRENTLY for their own
    /// smaller/brief builds — a still-valid call for them, not a verdict on the mechanism).
    /// Failure semantics: an aborted CONCURRENTLY build leaves an INVALID index behind, and
    /// <c>IF NOT EXISTS</c> sees an INVALID index as EXISTING — a bare re-run would silently
    /// skip the build and stamp the migration applied. Mechanical recovery, in this order:
    /// <c>DROP INDEX IF EXISTS ix_company_register_company_name_lower;</c> then re-run the
    /// migration. (Plain DROP, not CONCURRENTLY, is deliberate there: dropping an invalid
    /// index's catalog entry is near-instant — there is no completed index for readers to be
    /// using — so the stronger lock protects nothing.)
    /// </para>
    /// </summary>
    public partial class AddCompanyRegisterNameSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_company_register_company_name_lower
                ON company_register (lower(company_name) text_pattern_ops);
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS ix_company_register_company_name_lower;
                """,
                suppressTransaction: true);
        }
    }
}
