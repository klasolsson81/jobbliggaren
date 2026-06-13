using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogPayload : Migration
    {
        // ADR 0035 — audit_log.payload jsonb aktiveras för Fas 2 system-events
        // (counts + tidsstämplar, ingen PII). ADR 0022 reserverade kolumnen för
        // Fas 4 command-audit (CV-text med PII-saner-krav); system-event-payload
        // har ingen PII och kan aktiveras tidigare.
        //
        // audit_log är partitionerad sedan 20260508152351_AddAuditLogPartitioning.
        // ALTER TABLE på parent propagerar automatiskt till alla partitions per
        // PostgreSQL native partitioning-semantik (samtliga befintliga +
        // framtida range-partitions + default-partition ärver kolumnen).
        // Inga FK-cascades (audit_log är write-only per ADR 0022).
        //
        // Kolumnen är nullable — befintliga rader (command-audit) får null;
        // SystemEventAuditor.RecordAsync (ADR 0035 impl) sätter värdet vid nya
        // rader. Inga back-fill-jobb behövs.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payload",
                table: "audit_log",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payload",
                table: "audit_log");
        }
    }
}
