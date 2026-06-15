using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fas 4 STEG 8 (F4-8, ADR 0071/0074) — greenfield <c>parsed_resumes</c> staging
    /// aggregate for the deterministic CV import/parse pipeline.
    /// <para>
    /// <b>Encrypted columns (ADR 0074 Invariant 2/3):</b>
    /// <c>raw_text</c> (text NOT NULL) is Form A in-place encrypted by the interceptor
    /// (normalized plain text → ciphertext, stored in-column).
    /// <c>parsed_content_enc</c> (text NULL) is the Form B shadow for the
    /// EF-Ignored <c>ParsedResume.Content</c> value object; the interceptor pair owns
    /// the <see cref="ParsedResumeContent"/> ↔ JSON ↔ ciphertext transform.
    /// Both columns are plain TEXT at the DDL level — crypto is handled in the
    /// application layer by the interceptor pair (no pg_crypto involvement).
    /// </para>
    /// <para>
    /// <b>JSONB metadata (non-PII):</b> <c>parse_confidence</c>, <c>personnummer_scan</c>,
    /// and <c>occupation_proposals</c> are stored as jsonb. They contain derived
    /// metadata only — no raw PII. No GIN index is added here; the matching engine
    /// (F4-9+) may add one if needed.
    /// </para>
    /// <para>
    /// <b>Concurrency:</b> xmin PostgreSQL system column — no DDL column emitted;
    /// Npgsql maps it automatically as a concurrency token (parity with
    /// <c>resume_versions</c>).
    /// </para>
    /// <para>
    /// <b>GDPR:</b> soft-delete via <c>deleted_at</c> (nullable timestamptz) +
    /// global query filter (<c>deleted_at IS NULL</c> on <see cref="ParsedResume"/>).
    /// Audit trail via <c>created_at</c> / <c>updated_at</c> (both NOT NULL). PII
    /// encrypted via interceptor (DEK envelope, ADR 0049/0066). No plaintext CV
    /// content at rest. Retention sweep operates on <c>deleted_at</c> via
    /// <c>ExecuteDeleteAsync</c> (out of scope this migration).
    /// </para>
    /// <para>
    /// <b>No FK constraint to job_seekers:</b> by convention (parity
    /// <c>ResumesConfiguration</c> — <c>HasIndex</c> only, no <c>HasForeignKey</c>);
    /// referential integrity enforced at the application layer.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddParsedResumeAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parsed_resumes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    source_content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    detected_language = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    parse_confidence = table.Column<string>(type: "jsonb", nullable: false),
                    personnummer_scan = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    parsed_content_enc = table.Column<string>(type: "text", nullable: true),
                    occupation_proposals = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parsed_resumes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parsed_resumes_job_seeker_id",
                table: "parsed_resumes",
                column: "job_seeker_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parsed_resumes");
        }
    }
}
