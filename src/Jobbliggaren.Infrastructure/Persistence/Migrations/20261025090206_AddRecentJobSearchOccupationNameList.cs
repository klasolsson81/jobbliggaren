using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2199 PR 2 (ADR 0067 Amendment 2026-10-25) — occupation_name_list on recent_job_searches.
    ///
    /// <para>Adds <c>occupation_name_list</c> (text[], NOT NULL DEFAULT '{}') to
    /// <c>recent_job_searches</c>: the occupation-names (yrkesbenämningar) a /jobb search picked.
    /// Same shadow-backing-field pattern as the list columns beside it
    /// (<c>AddRecentJobSearchEmployerList</c>, 20260701100324).</para>
    ///
    /// <para><b>Additive — no row is deleted or rewritten.</b> Existing rows get an empty list
    /// from the default, and <c>FilterHashCalculator</c> leaves an empty occupation-name list out
    /// of the canonical form, so every existing row keeps the hash it has; no row is orphaned or
    /// duplicated. The scaffold emitted no default, which a NOT NULL column on a populated table
    /// cannot take, so the empty-list default is written here as in the employer migration.</para>
    ///
    /// <para><b>GDPR:</b> the column holds JobTech concept ids, validated on shape only
    /// (<c>^[A-Za-z0-9_-]{1,32}\z</c>) like the other concept-id lists. A user-owned cache row over
    /// the user's own search: the capture guard refuses a personnummer on this axis as
    /// on the other five, and the recruiter-erasure match searches it as the sixth concept-id
    /// axis.</para>
    ///
    /// <para><b>Down()</b> drops the column. A row captured with an occupation-name then reads as
    /// the same search without it while its hash still carries the names — the cost of a schema
    /// rollback on an ephemeral cache capped at 20 per seeker (ADR 0060), never of valuable data.</para>
    ///
    /// <para>The model snapshot also carries #2199 PR 1's rename of the C# property
    /// <c>SsykConceptId</c> → <c>OccupationNameConceptId</c> on <c>job_ads</c>. The column
    /// <c>ssyk_concept_id</c> is unchanged, so it has no operation here.</para>
    /// </summary>
    public partial class AddRecentJobSearchOccupationNameList : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<List<string>>(
                name: "occupation_name_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.DropColumn(
                name: "occupation_name_list",
                table: "recent_job_searches");
        }
    }
}
