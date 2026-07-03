using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationOwnerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #506 (persistence audit #482, ADR 0045 query-hygiene). The applications
            // table — the hottest per-user table — carried no index on its owner key
            // job_seeker_id (the pre-existing ix_applications_stale_detection is a
            // partial index on last_status_change_at for the ghosting job, orthogonal),
            // so every owner-scoped read and both account-delete cascades
            // sequential-scanned it. One composite index closes it (net 1:
            // senior-cto-advisor re-triage
            // superseded the 2026-07-02 net-2 bind; a second bare (job_ad_id) index
            // served no read path this prefix does not already cover — every real
            // job_ad_id predicate is owner-scoped and leads with job_seeker_id).
            //
            // ix_applications_job_seeker_id_status: the btree leftmost-prefix serves
            // the owner-scoped equality every read applies (Where JobSeekerId == x),
            // both cascades (soft: DeleteAccountCommandHandler; hard: AccountHardDeleter
            // via IgnoreQueryFilters — hence NOT partial on deleted_at, which would
            // miss the soft-deleted rows the hard-delete cascade must resolve), and
            // the owner-scoped two-column (job_seeker_id + job_ad_id) lookups over the
            // tiny per-seeker partition (HasApplied / GetJobAdStatusBatch / the /jobb
            // EXISTS in PerUserJobAdSearchQuery). The second column serves the #383
            // status filter (AND Status == s).
            //
            // LOCK BEHAVIOUR: a non-concurrent CREATE INDEX (the EF default emitted
            // here) takes a SHARE lock on `applications` — it blocks writes
            // (INSERT/UPDATE/DELETE) but ALLOWS concurrent reads for the duration of
            // the build (PostgreSQL 18, sql-createindex / index-locking docs). This is
            // strictly weaker than the ACCESS EXCLUSIVE full-table-rewrite of the
            // sibling `ADD COLUMN ... GENERATED ... STORED` migrations
            // (AddJobAdOrganizationNumber / F6P7) — no table rewrite here, index build
            // only. EF wraps the migration in a transaction, so the SHARE lock is held
            // until it commits. Deploy consequence (Hetzner): a short write pause on
            // applications while the index builds; applications is a small per-user
            // table, so the build is sub-second locally. CREATE INDEX CONCURRENTLY is
            // deliberately NOT used — it cannot run inside EF's migration transaction,
            // and pre-prod table sizes make it unnecessary.
            migrationBuilder.CreateIndex(
                name: "ix_applications_job_seeker_id_status",
                table: "applications",
                columns: new[] { "job_seeker_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applications_job_seeker_id_status",
                table: "applications");
        }
    }
}
