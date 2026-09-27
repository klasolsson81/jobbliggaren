using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0146 — adds the <c>xmin</c> shadow concurrency token to <c>job_seekers</c>
    /// (<see cref="Jobbliggaren.Infrastructure.Persistence.Configurations.JobSeekerConfiguration"/>),
    /// closing the model/snapshot diff. Parity with <c>ResumeConfiguration</c>,
    /// <c>ApplicationConfiguration</c> and <c>ParsedResumeConfiguration</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero DDL.</b> <c>xmin</c> is a PostgreSQL system column that always exists on every row;
    /// Npgsql's migrations SQL generator special-cases system columns (<c>tableoid</c>,
    /// <c>xmin</c>, <c>cmin</c>, <c>xmax</c>, <c>cmax</c>, <c>ctid</c>) and emits nothing for them in
    /// Create/Add/Alter/Drop. The scaffolded <c>AddColumn&lt;uint&gt;("xmin", ...)</c> /
    /// <c>DropColumn</c> pair is removed by hand below, as
    /// <c>20260507193020_RemoveRowVersionUseXmin</c> did for the same reason on <c>applications</c>:
    /// left in, it reads as a real <c>ALTER TABLE ... ADD COLUMN</c> against a live, populated table,
    /// which it is not. <c>dotnet ef migrations script</c> confirms only the
    /// <c>__EFMigrationsHistory</c> row is written.
    /// </para>
    /// <para>No backfill, no lock, no data at risk. <c>Down()</c> is symmetric: nothing to drop.</para>
    /// </remarks>
    public partial class AddJobSeekerXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column — no ADD COLUMN needed (see class remarks).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column — no DROP COLUMN needed (see class remarks).
        }
    }
}
