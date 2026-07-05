using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Fas 4b PR-3 (ADR 0096, CTO-bind D1/D5d/D9; issue #652). Adds non-PII source
    /// metadata + template options as plain columns on <c>resumes</c> (ADR 0059
    /// parity — every new column is an enum Name-string, a bool, or a timestamp;
    /// zero free text, pinned by <c>ResumeRootPlainColumnGuardTests</c>).
    ///
    /// <para>
    /// Hand-edit required (<c>AddResumeLanguageDenormProjAndPrimaryResume</c>
    /// precedent): EF scaffolds <c>defaultValue: ""</c> for every NOT NULL string
    /// column here because it only derives the CLR default (<c>null</c>/empty),
    /// never the SmartEnum's actual default member. An empty string is not a valid
    /// <c>SmartEnum.FromName</c> value for any of these six enums and would break
    /// on first read of a backfilled row, so every string default below was
    /// corrected by hand to the true domain default:
    /// <list type="bullet">
    /// <item><c>origin</c> → <c>"Legacy"</c> (<see cref="Jobbliggaren.Domain.Resumes.ResumeSourceOrigin.Legacy"/> —
    /// honest-unknown provenance for pre-PR-3 rows, ADR 0074 parity; never
    /// fabricated as Import/Template).</item>
    /// <item><c>template</c> → <c>"Klar"</c>, <c>template_accent</c> →
    /// <c>"NavyBlue"</c>, <c>template_font</c> → <c>"Modern"</c>,
    /// <c>template_density</c> → <c>"Normal"</c>, <c>template_photo_shape</c> →
    /// <c>"Circle"</c> — the handoff-bound display defaults
    /// (<see cref="Jobbliggaren.Domain.Resumes.CvTemplateOptions.Default"/>). These
    /// are rendering defaults, not provenance — backfilling them is honest; it is
    /// exactly what the renderer would use for a CV with no options set.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <c>template_photo_enabled</c> → EF's scaffolded <c>defaultValue: false</c>
    /// already matches the domain default (photo OFF, Swedish norm) — no edit
    /// needed. <c>adopted_at</c> is nullable with no default (no existing resume
    /// is adopted) — no edit needed.
    /// </para>
    ///
    /// <para>
    /// ADR 0059 migration policy: DB defaults only, no data-backfill script, no
    /// downtime concern — <c>ALTER TABLE ADD COLUMN ... NOT NULL DEFAULT</c> is
    /// O(1) metadata-only in PostgreSQL 11+ for non-volatile defaults (no table
    /// rewrite), <c>ExtendWaitlistEntryWithAcceptance</c> precedent. Table
    /// <c>resumes</c>' xmin concurrency token and soft-delete query filter are
    /// untouched by this migration.
    /// </para>
    /// </remarks>
    public partial class AddResumeSourceMetadataAndTemplateOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "adopted_at",
                table: "resumes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "resumes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<string>(
                name: "template",
                table: "resumes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Klar");

            migrationBuilder.AddColumn<string>(
                name: "template_accent",
                table: "resumes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NavyBlue");

            migrationBuilder.AddColumn<string>(
                name: "template_density",
                table: "resumes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<string>(
                name: "template_font",
                table: "resumes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Modern");

            migrationBuilder.AddColumn<bool>(
                name: "template_photo_enabled",
                table: "resumes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "template_photo_shape",
                table: "resumes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Circle");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adopted_at",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "template",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "template_accent",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "template_density",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "template_font",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "template_photo_enabled",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "template_photo_shape",
                table: "resumes");
        }
    }
}
