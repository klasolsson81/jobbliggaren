using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1742 (epic #1732 part 4b) — the model stops mapping <c>job_seekers.display_name</c>. The column
    /// stays, and the raw-SQL recruiter-erasure search still reads it; a later migration under the
    /// same issue drops it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Up() and Down() are empty on purpose.</b> The scaffold emitted a DropColumn/AddColumn pair,
    /// which was discarded by hand; the regenerated snapshot is kept, so it no longer matches the
    /// physical table until the drop migration. This is the unmap step of a Parallel Change: each
    /// image is published in its own matrix cell and nothing checks that the box runs one commit's
    /// images (#1238), so a migrate that dropped the column could run beside an api or worker that
    /// still selects it. With the mapping gone one deploy earlier, both skews are safe. ADR 0142
    /// records the decision.
    /// </para>
    /// <para>
    /// <b>Rolling back.</b> Pinning an image tag older than this migration makes <c>migrate</c> refuse
    /// with exit 3 unless <c>MIGRATE_ALLOW_SCHEMA_AHEAD</c> names the exact set of ids it refuses
    /// (<c>docs/runbooks/vps-deploy-stack.md</c> §3a).
    /// </para>
    /// </remarks>
    public partial class UnmapJobSeekerDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: see the class remarks.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: Up() applies no DDL.
        }
    }
}
