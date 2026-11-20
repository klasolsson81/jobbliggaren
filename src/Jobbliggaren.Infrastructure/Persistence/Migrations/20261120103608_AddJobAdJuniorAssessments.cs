using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2264 (ADR 0187) — the live junior classification's storage: two nullable columns on <c>job_ads</c>
    /// (<c>experience_required</c> boolean, <c>junior_input_fingerprint</c> varchar(64)) and one new table,
    /// <c>job_ad_junior_assessments</c> (one row per classified ad; primary key = foreign key <c>job_ad_id</c> to
    /// <c>job_ads(id)</c> ON DELETE CASCADE; the decision pair held whole by a CHECK). No index beyond the primary key.
    ///
    /// <para>
    /// <b>No table rewrite.</b> Both columns are nullable with no default, so each <c>ADD COLUMN</c> is a catalog-only
    /// change on PostgreSQL 18; <c>job_ads</c> is not scanned or rewritten. The new table is empty at creation, so the
    /// foreign key has no rows to validate. Existing ads read <c>NULL</c> in both columns until their next source write
    /// (<c>JobAd.UpdateFromSource</c>); no data step.
    /// </para>
    ///
    /// <para>
    /// <b>Locks.</b> The two <c>ADD COLUMN</c>s take ACCESS EXCLUSIVE on <c>job_ads</c> and the foreign key takes SHARE
    /// ROW EXCLUSIVE on it, both until commit and both held only for the catalog changes. <c>SET LOCAL lock_timeout</c>
    /// turns a queue behind a long transaction on <c>job_ads</c> (the sync's write batch) into a loud deploy failure that
    /// the next reconcile retries, instead of a stall that blocks every read of <c>job_ads</c> behind it.
    /// </para>
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops the assessments table, and every verdict
    /// and admin decision with it, then the two columns. It drops the table first, so the foreign key is gone before
    /// the principal's columns change.
    /// </para>
    /// </summary>
    public partial class AddJobAdJuniorAssessments : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<bool>(
                name: "experience_required",
                table: "job_ads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "junior_input_fingerprint",
                table: "job_ads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_ad_junior_assessments",
                columns: table => new
                {
                    job_ad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    classified_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    classified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_ad_junior_assessments", x => x.job_ad_id);
                    table.CheckConstraint("ck_job_ad_junior_assessments_decision_whole", "(decision IS NULL) = (decided_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_job_ad_junior_assessments_job_ads_job_ad_id",
                        column: x => x.job_ad_id,
                        principalTable: "job_ads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.DropTable(
                name: "job_ad_junior_assessments");

            migrationBuilder.DropColumn(
                name: "experience_required",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "junior_input_fingerprint",
                table: "job_ads");
        }
    }
}
