using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Partitionering per dag (aktiveras i Fas 4 av retention-jobbet, ADR 0022).
            // Kräver att tabellen återskapas som PARTITION BY RANGE (occurred_at)
            // och att daily partitions skapas via cron/Hangfire-jobb.
            // Skeleton (ej körd nu):
            //   ALTER TABLE audit_log RENAME TO audit_log_legacy;
            //   CREATE TABLE audit_log (LIKE audit_log_legacy INCLUDING ALL) PARTITION BY RANGE (occurred_at);
            //   CREATE TABLE audit_log_YYYYMMDD PARTITION OF audit_log
            //     FOR VALUES FROM ('YYYY-MM-DD') TO ('YYYY-MM-DD'+1);
            //   INSERT INTO audit_log SELECT * FROM audit_log_legacy;
            //   DROP TABLE audit_log_legacy;
            // Inga FK-constraints mot users/job_seekers — audit är write-only och får
            // inte hindras av FK-cascades vid soft-delete (ADR 0022).

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    impersonated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                table: "audit_log",
                column: "occurred_at",
                descending: Array.Empty<bool>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");
        }
    }
}
