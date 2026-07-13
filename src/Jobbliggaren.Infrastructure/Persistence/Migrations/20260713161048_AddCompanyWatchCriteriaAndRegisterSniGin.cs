using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #560 kriterie-vågen PR-1 (senior-cto-advisor Fork A1/B1 2026-07-12; architect bind
    /// 2026-07-13) — the schema foundation for criteria-based company watches. ONE migration,
    /// carrying the whole schema change-reason (CLAUDE.md §6.5: the migration hotspot is strictly
    /// single-owner and serial, so the wave touches it exactly once).
    ///
    /// <para>
    /// <b>1. <c>company_watch_criteria</c> (new table).</b> A user's discovery predicate: SNI
    /// industry codes ∧ SCB seat-municipality codes, evaluated as a QUERY against
    /// <c>company_register</c> — never expanded into per-company rows (the epic's binding
    /// constraint: the scan-set explodes). Two real <c>text[]</c> columns rather than a jsonb value
    /// object (Fork A1): the future notification scan wants to INVERT the predicate
    /// (<c>@company_sni &amp;&amp; sni_codes</c> across all users' criteria), which is GIN-indexable
    /// on <c>text[]</c> and impossible on jsonb. <c>kommun_codes</c> holds SCB 4-digit SEAT codes
    /// (matching <c>company_register.sate_kommun_code</c>) — a DIFFERENT namespace from the JobTech
    /// <c>municipality_concept_id</c> an ad carries (RF-4, ADR 0105); the two are kept apart in code
    /// and in copy ("säteskommun" vs "annonsens ort").
    /// </para>
    ///
    /// <para>
    /// <b>2. <c>ix_company_register_sni_codes_gin</c> (new index).</b> The index-backed half of the
    /// browse predicate (<c>sni_codes &amp;&amp; @user_sni</c>, array overlap). ADR 0091 deliberately
    /// deferred this ("no GIN index in v1 — no consumer until smart-bevakning"); the criteria wave IS
    /// that consumer. EF-modelled via <c>HasMethod("gin")</c> → the <c>Npgsql:IndexMethod</c>
    /// annotation below; the default GIN operator class for <c>text[]</c> is the built-in
    /// <c>array_ops</c>, which supports <c>&amp;&amp;</c>/<c>@&gt;</c>/<c>&lt;@</c>/<c>=</c>.
    /// <b>Ops note:</b> a plain (non-CONCURRENTLY) <c>CREATE INDEX</c> takes a SHARE lock on the
    /// table for the duration of the build — it blocks WRITES (i.e. exactly the SCB sync job) but
    /// NOT reads. Over the ~1.17M-row register that build is not instant, so do not apply this
    /// migration while the sync job is running. Reads are unaffected, so no read path needs a
    /// deploy window. <c>CREATE INDEX CONCURRENTLY</c> (which would need
    /// <c>suppressTransaction: true</c>) is deliberately not used: the register has no live
    /// consumer yet and the deploy is manually approved, so the added complexity buys nothing.
    /// </para>
    ///
    /// <para>
    /// <b>Indexes on the new table.</b> Exactly one: <c>user_id</c>, and deliberately NOT partial
    /// (parity <c>ix_company_watches_user_id</c>). A <c>WHERE deleted_at IS NULL</c> filter would
    /// exclude the Art. 17 cascade sweep — which runs <c>IgnoreQueryFilters()</c>, i.e. without the
    /// <c>deleted_at</c> predicate — from using the index, turning the ERASURE path into a seq scan.
    /// No <c>UNIQUE(user_id, sni_codes, kommun_codes)</c>: a btree index tuple is capped at ~2704
    /// bytes, and a legitimate whole-industry selection (up to 1000 SNI codes ≈ 9 kB) would make
    /// INSERT throw "index row size exceeds btree version 4 maximum". A duplicate criterion is a
    /// cosmetic, user-deletable nuisance; a write-path landmine is not.
    /// </para>
    ///
    /// <para>
    /// <b>GDPR (DPIA Part D, security-auditor CONDITIONAL GREEN 2026-07-12).</b> The criterion is
    /// personal data ABOUT THE USER (which industries/towns they job-hunt in — profiling-adjacent),
    /// stored under Art. 6(1)(b); the proactive notification that would need Art. 6(1)(a) consent is
    /// deferred (RF-9=9C). No contact PII, no special categories, no personnummer (Art. 9 N/A).
    /// <b>C-D1 (binding):</b> the table is FK-less by <c>user_id</c> (ADR 0011 soft-reference), so it
    /// is wired into the Art. 17 cascade (<c>AccountHardDeleter.HardDeleteAccountAsync</c>) and
    /// classified in <c>AccountHardDeleteCascadeFitnessTests.CascadeMap</c> — the fail-closed
    /// partition fails the BUILD until both are done.
    /// </para>
    ///
    /// <para>
    /// <b>Down:</b> destructive — drops the table and the index. Requires explicit approval if ever
    /// applied outside a test environment (repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddCompanyWatchCriteriaAndRegisterSniGin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_watch_criteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kommun_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    sni_codes = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_watch_criteria", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_register_sni_codes_gin",
                table: "company_register",
                column: "sni_codes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_company_watch_criteria_user_id",
                table: "company_watch_criteria",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_watch_criteria");

            migrationBuilder.DropIndex(
                name: "ix_company_register_sni_codes_gin",
                table: "company_register");
        }
    }
}
