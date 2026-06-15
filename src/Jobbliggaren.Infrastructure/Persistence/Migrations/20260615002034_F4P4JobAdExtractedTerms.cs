using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C — no AI/LLM) — deterministic
    /// per-job-ad keyword/skill extraction persistence.
    /// <para>
    /// <b><c>extracted_terms</c> (jsonb, nullable):</b> the canonical extracted-term
    /// list (a jsonb array of term objects). UNLIKE every other derived column on
    /// <c>job_ads</c> (ssyk_concept_id, search_vector, … all
    /// <c>GENERATED … STORED</c> from raw_payload/title/description) this column is
    /// <b>ACTIVELY WRITTEN in C#</b> — NLP + taxonomy lookup is not expressible as a
    /// Postgres generated expression. Therefore <c>ADD COLUMN</c> backfills NOTHING:
    /// every existing row is NULL until <c>BackfillJobAdExtractedTermsJob</c>
    /// (a LOCAL re-projection — NO JobTech re-fetch) runs; new/updated ads are
    /// populated at the ingest hook (UpsertExternalJobAd, both Add + Update paths).
    /// NULL = never extracted; non-null (incl. the empty array) = extracted.
    /// </para>
    /// <para>
    /// <b><c>extracted_lexemes</c> (jsonb, STORED generated):</b> the F4-6 overlap
    /// pre-filter surface, derived deterministically from <c>extracted_terms</c> by
    /// Postgres (<c>jsonb_path_query_array(extracted_terms, '$[*].Lexeme')</c> — a
    /// constant jsonpath ⇒ IMMUTABLE ⇒ valid for a STORED generated column,
    /// verified PG 18.3). Drift is impossible (re-evaluated on every write of
    /// extracted_terms). The <c>text[]</c> form would require a subquery (illegal in
    /// generated columns), so the jsonb form is the correct companion.
    /// <c>extracted_lexemes IS NULL ⟺ extracted_terms IS NULL</c> → the backfill
    /// idempotency predicate.
    /// </para>
    /// <para>
    /// <b>GIN index</b> (default <c>jsonb_ops</c> opclass — supports <c>?|</c>, unlike
    /// jsonb_path_ops) backs the matching engine's any-of-CV-lexemes overlap
    /// (<c>extracted_lexemes ?| array[…]</c>, F4-6). Created via raw SQL — the fluent
    /// API cannot GIN a shadow property (same as <c>ix_job_ads_search_vector</c>).
    /// Partial <c>WHERE deleted_at IS NULL</c> mirrors the FTS index (only active ads
    /// are matched; un-extracted rows carry no GIN keys anyway).
    /// </para>
    /// <para>
    /// <b>GDPR:</b> derived solely from already-public ad title/description — no new
    /// PII surface. <b>Lock:</b> the nullable <c>extracted_terms</c> ADD is cheap
    /// (catalog-only, no default); the STORED generated <c>extracted_lexemes</c> is a
    /// full table rewrite under ACCESS EXCLUSIVE (~54k rows → seconds locally).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class F4P4JobAdExtractedTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extracted_terms",
                table: "job_ads",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extracted_lexemes",
                table: "job_ads",
                type: "jsonb",
                nullable: true,
                computedColumnSql: "jsonb_path_query_array(extracted_terms, '$[*].Lexeme')",
                stored: true);

            // GIN via raw SQL — fluent API can't GIN a shadow property (parity
            // ix_job_ads_search_vector). Default jsonb_ops opclass supports the `?|`
            // (exists-any) operator the matching engine (F4-6) uses; jsonb_path_ops
            // would not. Partial WHERE deleted_at IS NULL mirrors the FTS index.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_extracted_lexemes " +
                "ON job_ads USING gin (extracted_lexemes) " +
                "WHERE deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_extracted_lexemes;");

            migrationBuilder.DropColumn(
                name: "extracted_lexemes",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "extracted_terms",
                table: "job_ads");
        }
    }
}
