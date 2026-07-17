using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #560 (C-D8 / senior-cto-advisor Fork G1, 2026-07-16) — drops <c>company_watch_criteria.deleted_at</c>.
    ///
    /// <para>
    /// <b>What this column was.</b> A soft-delete stamp under a HARD-delete verdict: the delete
    /// handler removes the row via tracked <c>Remove</c>, so nothing ever wrote it, the EF query
    /// filter over it never excluded a row, and <c>SoftDelete()</c> never had a production caller.
    /// PR-3 shipped it documented-to-demolition because its removal is a migration and PR-3 carried a
    /// no-migration mandate. This is that migration; the method, the property and the filter go with
    /// it in the same change-reason.
    /// </para>
    ///
    /// <para>
    /// <b>Why a plain DROP COLUMN is safe here — measured, not assumed.</b> <c>DROP COLUMN</c>
    /// silently drops every index, constraint and default that depends on the column, and the EF
    /// model snapshot cannot see that happen (it models the C# model, not the physical catalog), so
    /// the house rule is to write the drop by hand and prove the dependencies first. Verified against
    /// the tree:
    /// <list type="bullet">
    ///   <item><b>No data can be lost.</b> No writer ever set the stamp (that is the whole finding),
    ///     so every row holds NULL. EF's scaffolder flags this operation as possible data loss on
    ///     shape alone — the flag is correct in general and vacuous here.</item>
    ///   <item><b>No dependent objects.</b> <c>company_watch_criteria</c> has exactly one prior
    ///     migration (<c>20260713161048</c>), which creates <c>deleted_at</c> as a plain nullable
    ///     <c>timestamp with time zone</c> — no default, no constraint, no expression, and no index
    ///     over it. Nothing else in the repo names it.</item>
    ///   <item><b>The one index survives by construction.</b>
    ///     <c>ix_company_watch_criteria_user_id</c> is on <c>user_id</c> ALONE with no
    ///     <c>WHERE</c> clause — structurally independent of this column, so the drop cannot take it.
    ///     "By construction" is not the evidence, though: <c>CompanyWatchCriterionPersistenceTests</c>
    ///     .<c>UserIdIndex_OnCompanyWatchCriteria_Exists_AndIsNotPartial</c> reads <c>pg_indexes</c>
    ///     after this migration has run and fails if it went missing. It matters because the index
    ///     serves the Art. 17 erasure sweep: losing it would degrade account deletion to a seq scan
    ///     with nothing red anywhere.</item>
    ///   <item><b>Ordinary column, not computed.</b> A generated column would need
    ///     <c>DROP EXPRESSION</c> — dropping and re-adding one destroys data. That trap does not
    ///     apply: this is a plain stored column.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Down() is genuinely runnable</b> (the #884 lesson: a scaffolded Down that cannot execute is
    /// not a rollback). It restores the column with its ORIGINAL definition — nullable, no default —
    /// so re-applying it yields the exact pre-drop shape. It restores no data, and that is faithful
    /// rather than lossy: there was never any. Rolling back gives you the empty column again; it does
    /// not give you a soft-delete mechanism, because that lived in C#.
    /// </para>
    ///
    /// <para>
    /// <b>Physical proof of the drop.</b> EF's <c>PendingModelChangesWarning</c> only pins
    /// model ≟ snapshot; it says nothing about whether <c>Up()</c> did what it claims. The pin that
    /// closes snapshot ≟ real table is
    /// <c>CompanyWatchCriterionPersistenceTests.DeletedAtColumn_IsPhysicallyGone_FromTheCriteriaTable</c>,
    /// which queries <c>information_schema.columns</c> after migration with a self-proving positive
    /// control.
    /// </para>
    /// </summary>
    public partial class DropCompanyWatchCriterionDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "company_watch_criteria");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The original definition, verbatim from 20260713161048: nullable, no default. Restores
            // the shape; there is no data to restore.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "company_watch_criteria",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
