using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationAppliedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "applied_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill the apply date for already-submitted applications (issue
            // #316, senior-cto-advisor 2026-06-28 D5). Going forward the value is
            // stamped exactly on the first Submitted transition; for pre-existing
            // rows the only stored proxy is last_status_change_at — best-effort
            // and exact only for rows still in Submitted (later transitions
            // overwrite it). Draft rows are never-applied → left NULL. Pre-prod
            // test data only; precision is low-stakes. Down() drops the column,
            // naturally undoing the backfill.
            migrationBuilder.Sql(
                """
                UPDATE applications
                SET applied_at = last_status_change_at
                WHERE status <> 'Draft' AND deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "applied_at",
                table: "applications");
        }
    }
}
