using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fas 4 STEG F4-12 (ADR 0076) — adds the <c>match_preferences</c> jsonb column to
    /// <c>job_seekers</c>. Stores the job-seeker's stated job-search preferences
    /// (<see cref="Jobbliggaren.Domain.JobSeekers.MatchPreferences"/>): preferred
    /// occupation-groups (ssyk-level-4), regions, and employment-types as a single jsonb
    /// document. These feed the deterministic match score (F4-13+).
    ///
    /// <para>
    /// <b>Mapping rationale (CTO ADR 0076):</b> <c>OwnsOne(...).ToJson()</c> does not
    /// map <c>IReadOnlyList&lt;string&gt;</c> stably in Npgsql (#3129); a property-level
    /// <c>ValueConverter</c> (parity with <c>SearchCriteria</c> / ADR 0042 Beslut B) is
    /// used instead. The comparer carries the VO's structural record-equality.
    /// </para>
    ///
    /// <para>
    /// <b>Column default <c>'{}'::jsonb</c>:</b> backfills existing rows at migration
    /// time. The converter's <c>Read</c> path handles a bare <c>{}</c> document (all
    /// keys missing → <see cref="Jobbliggaren.Domain.JobSeekers.MatchPreferences.Empty"/>).
    /// On application INSERT, EF includes the app-serialized JSON in every statement
    /// (the CLR value is never null); the DB default is only exercised by PostgreSQL's
    /// <c>ADD COLUMN … DEFAULT</c> catalogue rewrite for pre-existing rows.
    /// </para>
    ///
    /// <para>
    /// <b>No data backfill step:</b> the column default is the canonical "no preferences
    /// stated" sentinel (<c>MatchPreferences.Empty</c>) — no additional UPDATE is required.
    /// </para>
    ///
    /// <para>
    /// <b>GDPR:</b> <c>match_preferences</c> holds taxonomy concept-ids (opaque strings
    /// such as occupation-group or region ids), NOT free-text PII. Existing soft-delete
    /// (<c>deleted_at</c>) and query filter on <c>job_seekers</c> cover this column.
    /// No new encryption surface is introduced.
    /// </para>
    ///
    /// <para>
    /// <b>Down:</b> destructive — <c>DROP COLUMN match_preferences</c>. Requires explicit
    /// user approval if ever applied in a non-test environment (repo convention).
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddJobSeekerMatchPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "match_preferences",
                table: "job_seekers",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "match_preferences",
                table: "job_seekers");
        }
    }
}
