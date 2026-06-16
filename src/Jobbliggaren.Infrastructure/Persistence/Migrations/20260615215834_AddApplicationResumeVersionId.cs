using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fas 4 STEG 11 (F4-11, BUILD §5.3) — records the exact CV version used when
    /// an application is submitted (<see cref="Jobbliggaren.Domain.Applications.Application.ResumeVersionId"/>).
    /// <para>
    /// <b>Design (CTO D-Q4=B):</b> plain nullable converted column, parity with
    /// <c>job_ad_id</c> on the same table. No FK constraint to <c>resume_versions</c>
    /// is emitted — referential integrity is enforced in the domain (
    /// <see cref="Jobbliggaren.Domain.Applications.Application.AttachResumeVersion"/>)
    /// and in handlers (cross-user ownership guard). A real FK would couple the
    /// Applications and Resumes aggregates at the DB level against the
    /// reference-by-id stance (CLAUDE.md §2.2) and would add no anti-dangling value
    /// because <c>ResumeVersion</c> is soft-delete-only (a deleted-at row always
    /// exists).
    /// </para>
    /// <para>
    /// <b>No index added:</b> the delete-guard query
    /// (<c>WHERE resume_version_id = @p AND status NOT IN (…)</c>) runs only on
    /// the <c>AttachResumeVersion</c> path — low frequency; full-table scan is
    /// acceptable on a bounded-by-user sub-set of rows. The column is not used in
    /// listing/sorting queries.
    /// </para>
    /// <para>
    /// <b>Nullable / no default:</b> backward-compatible; existing applications have
    /// no linked CV version. <c>ADD COLUMN uuid NULL</c> is a catalog-only change in
    /// PostgreSQL 18.3 — no table rewrite, no ACCESS EXCLUSIVE lock beyond catalog.
    /// </para>
    /// <para>
    /// <b>GDPR:</b> <c>resume_version_id</c> is a PII cross-reference (links an
    /// application to an encrypted CV); it is covered by the <c>applications</c>
    /// aggregate's existing soft-delete (<c>deleted_at</c>) and query filter.
    /// No new PII surface is introduced — the actual CV content is stored (encrypted)
    /// in <c>resume_versions</c> and is unaffected by this migration.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddApplicationResumeVersionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "resume_version_id",
                table: "applications",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resume_version_id",
                table: "applications");
        }
    }
}
