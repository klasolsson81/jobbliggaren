using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1742 (epic #1732 part 4b) — drops <c>job_seekers.display_name</c>, the contract half after
    /// <c>20260924182548_UnmapJobSeekerDisplayName</c> unmapped it from the model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No dependent object.</b> <c>DROP COLUMN</c> takes dependent indexes, constraints and
    /// defaults with it silently, so they were measured first: read-only on the box
    /// 2026-09-23T22:23Z (<c>pg_indexes</c>, <c>pg_constraint</c>, <c>pg_trigger</c>,
    /// <c>pg_depend</c>→<c>pg_rewrite</c>) and in the migration history, nothing depends on it.
    /// </para>
    /// <para>
    /// <b>Down() restores the shape only.</b> Every restored row is NULL, so once it has run on a
    /// populated table, <c>DisplayNameNullable</c>'s own Down refuses: its guard rejects a nameless
    /// row.
    /// </para>
    /// </remarks>
    public partial class DropJobSeekerDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "job_seekers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "job_seekers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
