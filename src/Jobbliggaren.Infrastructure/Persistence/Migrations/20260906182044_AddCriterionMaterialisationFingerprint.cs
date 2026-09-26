using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1681 part 2 (ADR 0139) — the staleness guard's discriminator: the digest of the PREDICATE each
    /// materialisation was computed from, so a read can tell numbers that belong to the criterion on
    /// screen from numbers that belong to a predicate its owner has since edited.
    ///
    /// <para>
    /// <b>The backfill is an empty string, and that is the SAFE direction rather than a shortcut.</b>
    /// A pre-existing row was written before this column existed, so what predicate produced its
    /// members is genuinely unknown. An empty string is a value <c>CriteriaFingerprint.Of</c> can
    /// never produce (it always returns 64 hex chars), so every pre-existing row fails the read's
    /// equality check and degrades to "not materialised" — the surfaces say they do not know instead
    /// of asserting a number nobody can vouch for. The next materialisation run rewrites the row with
    /// a real fingerprint and it self-heals.
    /// </para>
    ///
    /// <para>
    /// <b>The DEFAULT is then DROPPED, and that is the point of the second statement.</b> EF's
    /// <c>AddColumn</c> needs a default to add a NOT NULL column to a table that already has rows, but
    /// leaving it behind would mean a future INSERT that forgets the fingerprint silently gets <c>''</c>
    /// — a row that reads as permanently stale, with nothing to indicate the writer was at fault. The
    /// column is written on every path by <c>CompanyWatchCriterionMemberStore.ReplaceAsync</c>, so
    /// after the backfill nothing should ever rely on a default; dropping it turns a silent wrong value
    /// into a loud <c>NOT NULL</c> violation.
    /// </para>
    /// </summary>
    public partial class AddCriterionMaterialisationFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "criteria_fingerprint",
                table: "company_watch_criterion_materialisations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // See the class docblock: the default exists only to backfill existing rows, and a
            // lingering one would let a future writer omit the fingerprint without failing.
            migrationBuilder.Sql(
                "ALTER TABLE company_watch_criterion_materialisations "
                + "ALTER COLUMN criteria_fingerprint DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "criteria_fingerprint",
                table: "company_watch_criterion_materialisations");
        }
    }
}
