using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NullResumeVersionLegacyContent : Migration
    {
        // SPOT: the precondition guard + null-out SQL are exposed as constants so
        // the fail-loud guard test and the UPDATE-effect test exercise the EXACT
        // strings this migration applies (no drifting test copy; repo precedent
        // 20260609214512.BuildReverseLookupSql + InternalsVisibleTo).

        // Fail-loud precondition (Saltzer/Schroeder fail-safe default; CTO bind
        // 2026-07-04). Aborts + rolls back (EF runs migrations in a transaction) if
        // any legacy-only row remains, so the cutover can never silently orphan CV
        // content in an environment where the backfill has not converged.
        internal const string PreconditionGuardSql =
            """
            DO $$
            DECLARE
                orphan_count bigint;
            BEGIN
                SELECT count(*) INTO orphan_count
                FROM resume_versions
                WHERE content_enc IS NULL AND content IS NOT NULL;

                IF orphan_count > 0 THEN
                    RAISE EXCEPTION
                        'ABORT: resume_versions plaintext cutover precondition failed: % row(s) have content_enc IS NULL AND content IS NOT NULL (legacy-only; backfill not converged). Removing the plaintext fallback would render their CV content inaccessible. Drive the backfill to 0 legacy-only rows before applying; do not null the plaintext silently.',
                        orphan_count;
                END IF;
            END $$;
            """;

        // Contract null-out: content_enc (AES-256-GCM) is now the sole source. Rows
        // that never had plaintext written (content_enc-only inserts) are already
        // NULL; this clears the dual-state rows the backfill migrated to content_enc.
        internal const string NullOutSql =
            """
            UPDATE resume_versions
            SET content = NULL
            WHERE content_enc IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GDPR Art.17 crypto-erasure cutover (ADR 0049 Beslut 5 steg 3; #507a /
            // #482). resume_versions dual-stored CV content in `content_enc`
            // (AES-256-GCM ciphertext, go-forward source) and legacy `content` (jsonb
            // plaintext). The FieldEncryptionBackfiller drove content_enc to 100%
            // coverage; this nulls the legacy plaintext so DEK-erasure at account
            // deletion renders backup-resident CV-PII unreadable. The interceptor
            // plaintext fallback is removed in the same PR (EncryptedFieldRegistry
            // LegacyShadowProperty = null); the embedded guard makes applying this to
            // a not-yet-converged environment fail loud rather than orphan CVs.
            migrationBuilder.Sql(PreconditionGuardSql);
            migrationBuilder.Sql(NullOutSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: DOWN IS NON-RESTORING BY DESIGN (crypto-erasure semantics,
            // ADR 0049 Beslut 2 + 5; CTO bind 2026-07-04). The legacy plaintext
            // `content` was irrecoverably nulled. The sole source is now `content_enc`
            // (AES-256-GCM); reconstructing plaintext would require per-user DEKs to
            // decrypt, which a schema migration has no access to, and restoring
            // readable CV-PII would defeat the Art.17 erasure promise. Intentional
            // no-op. The physical DROP COLUMN content is DEFERRED to a separate
            // verified follow-up (Beslut 5 steg 4).
        }
    }
}
