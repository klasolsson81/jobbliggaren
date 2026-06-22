using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0079 STEG 3 PR-B — adds the <c>skill_proposals</c> jsonb column to
    /// <c>parsed_resumes</c>. Stores CV-resolved skill proposals
    /// (<see cref="Jobbliggaren.Domain.Resumes.Parsing.ProposedSkill"/>: taxonomy concept-id
    /// + canonical label) surfaced by the deterministic NLP tier and awaiting user confirmation.
    /// Symmetric with <c>occupation_proposals</c> on the same table.
    ///
    /// <para>
    /// <b>Column default <c>'[]'::jsonb</c>:</b> backfills existing <c>parsed_resumes</c>
    /// rows at migration time with an empty proposal list. The converter's read path
    /// deserialises a bare <c>[]</c> document to an empty
    /// <c>IReadOnlyList&lt;ProposedSkill&gt;</c>. On application INSERT, EF includes the
    /// app-serialised JSON in every statement; the DB default is only exercised by
    /// PostgreSQL's <c>ADD COLUMN … DEFAULT</c> catalogue rewrite for pre-existing rows.
    /// </para>
    ///
    /// <para>
    /// <b>GDPR:</b> <c>skill_proposals</c> holds only taxonomy concept-ids and canonical
    /// labels — non-PII (identical sensitivity to <c>occupation_proposals</c>). No
    /// encryption surface is introduced. Existing soft-delete (<c>deleted_at</c>) and
    /// global query filter on <c>parsed_resumes</c> cover this column without change.
    /// </para>
    ///
    /// <para>
    /// <b>Down:</b> destructive — <c>DROP COLUMN skill_proposals</c>. Requires explicit
    /// user approval if ever applied in a non-test environment (repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddParsedResumeSkillProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "skill_proposals",
                table: "parsed_resumes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "skill_proposals",
                table: "parsed_resumes");
        }
    }
}
