using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2265 (ADR 0188) — the quick links in the /jobb hero. <c>recent_job_searches.quick_link_slot smallint NULL</c>:
    /// the slot a search holds as a quick link, or null. A CHECK admits 1 to 3 (<c>QuickLinkCap.MaxTotal</c>) and a
    /// partial unique index admits each slot once per job seeker, so the store bounds the marked searches on its own and
    /// the capture's eviction always finds an unmarked row (security-auditor's #2265 plan-read Major 1).
    ///
    /// <para>
    /// <b>The <c>Preferences</c> key moves the snapshot only.</b> <c>JuniorQuickLinkOffAt</c> lives in the
    /// <c>job_seekers.preferences</c> jsonb, null meaning the junior link is shown, so it carries no DDL (the form of
    /// <c>AddNotificationMailPauses</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Locks.</b> <c>ADD COLUMN</c> without a default changes the catalog only, but it takes ACCESS EXCLUSIVE, and
    /// every committed search writes this table: <c>SET LOCAL lock_timeout</c> turns a queue behind a long transaction into
    /// a loud deploy failure that the next reconcile retries. The CHECK validates a column that is all null, and the index
    /// covers no row yet, so neither needs <c>NOT VALID</c> or <c>CONCURRENTLY</c>.
    /// </para>
    ///
    /// <para>
    /// <b>No data step.</b> Null means "not a quick link", which is every existing row's meaning.
    /// </para>
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops the column, so every quick link to a search is
    /// lost; the junior stamp stays in the jsonb, unread.
    /// </para>
    /// </summary>
    public partial class AddRecentJobSearchQuickLinkSlot : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<short>(
                name: "quick_link_slot",
                table: "recent_job_searches",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_recent_job_searches_seeker_quick_link_slot",
                table: "recent_job_searches",
                columns: new[] { "job_seeker_id", "quick_link_slot" },
                unique: true,
                filter: "\"quick_link_slot\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recent_job_searches_quick_link_slot",
                table: "recent_job_searches",
                sql: "\"quick_link_slot\" BETWEEN 1 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_recent_job_searches_seeker_quick_link_slot",
                table: "recent_job_searches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recent_job_searches_quick_link_slot",
                table: "recent_job_searches");

            migrationBuilder.DropColumn(
                name: "quick_link_slot",
                table: "recent_job_searches");
        }
    }
}
