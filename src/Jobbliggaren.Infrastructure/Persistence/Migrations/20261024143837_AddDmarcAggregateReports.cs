using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1981 PR 4a (ADR 0185) — stored DMARC aggregate reports: four new tables and no change to any existing one.
    /// <c>dmarc_reports</c> holds one row per accepted report, unique on reporter, reporter's report id and content
    /// digest; <c>dmarc_report_rows</c> its records, keyed <c>(report_id, ordinal)</c> and deleted with the report by
    /// cascade; <c>dmarc_mailbox_messages</c> the per-message ledger by epoch and position; <c>dmarc_mailbox_state</c>
    /// the one state row with the cursor. Four indexes, no seed: a missing state row is "never attempted". Klas's
    /// migration GO of 2026-10-24 names exactly this shape; any other shape needs a new GO.
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops all four tables and their contents. The
    /// ledger history and the cursor are lost for good. The reports can be rebuilt by a re-Up and a rescan, but only
    /// while the mail is still in the STRATO mailbox. A code rollback makes <c>Migrate</c> refuse (schema ahead, exit
    /// 3) until <c>MIGRATE_ALLOW_SCHEMA_AHEAD</c> names this id. While rolled back there is no ingestion and no
    /// 12-month purge, so rows past the bound persist until roll-forward.
    /// </para>
    /// </summary>
    public partial class AddDmarcAggregateReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dmarc_mailbox_messages",
                columns: table => new
                {
                    uid_validity = table.Column<long>(type: "bigint", nullable: false),
                    uid = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    retries = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dmarc_mailbox_messages", x => new { x.uid_validity, x.uid });
                    table.CheckConstraint("ck_dmarc_mailbox_messages_positive", "uid_validity > 0 AND uid > 0 AND outcome > 0 AND attempts BETWEEN 1 AND 2 AND retries >= 0");
                });

            migrationBuilder.CreateTable(
                name: "dmarc_mailbox_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_outcome = table.Column<short>(type: "smallint", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uid_validity = table.Column<long>(type: "bigint", nullable: true),
                    last_uid = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dmarc_mailbox_state", x => x.id);
                    table.CheckConstraint("ck_dmarc_mailbox_state_positive", "last_outcome > 0 AND (uid_validity IS NULL OR uid_validity > 0) AND (last_uid IS NULL OR last_uid >= 0) AND ((uid_validity IS NULL) = (last_uid IS NULL))");
                    table.CheckConstraint("ck_dmarc_mailbox_state_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "dmarc_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    reporter_report_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    begins_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dmarc_reports", x => x.id);
                    table.CheckConstraint("ck_dmarc_reports_content_sha256", "octet_length(content_sha256) = 32");
                    table.CheckConstraint("ck_dmarc_reports_period", "ends_at > begins_at");
                });

            migrationBuilder.CreateTable(
                name: "dmarc_report_rows",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    source_ip = table.Column<IPAddress>(type: "inet", nullable: false),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    header_from = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    dkim_aligned = table.Column<short>(type: "smallint", nullable: false),
                    spf_aligned = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dmarc_report_rows", x => new { x.report_id, x.ordinal });
                    table.CheckConstraint("ck_dmarc_report_rows_positive", "ordinal >= 0 AND message_count > 0 AND dkim_aligned > 0 AND spf_aligned > 0");
                    table.ForeignKey(
                        name: "fk_dmarc_report_rows_dmarc_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "dmarc_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dmarc_mailbox_messages_received_at",
                table: "dmarc_mailbox_messages",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_dmarc_reports_ends_at",
                table: "dmarc_reports",
                column: "ends_at");

            migrationBuilder.CreateIndex(
                name: "ix_dmarc_reports_received_at",
                table: "dmarc_reports",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ux_dmarc_reports_reporter_report_id_content_sha256",
                table: "dmarc_reports",
                columns: new[] { "reporter", "reporter_report_id", "content_sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dmarc_mailbox_messages");

            migrationBuilder.DropTable(
                name: "dmarc_mailbox_state");

            migrationBuilder.DropTable(
                name: "dmarc_report_rows");

            migrationBuilder.DropTable(
                name: "dmarc_reports");
        }
    }
}
