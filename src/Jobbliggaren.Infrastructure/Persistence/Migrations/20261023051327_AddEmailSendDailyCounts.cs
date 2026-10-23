using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #1981 PR 3 (ADR 0181) — our own send counts per mail kind: two new tables and no change to any existing one.
    /// <c>email_send_daily_counts</c> holds attempts per Swedish civil date, mail kind and outcome (primary key
    /// <c>(day, kind, outcome)</c>, one positivity check, no other index). <c>email_send_counting</c> holds one row, the
    /// instant counting began, written here with the database clock and by no code afterwards. Klas's migration GO of
    /// 2026-10-23 names exactly this shape; any other shape needs a new GO.
    ///
    /// <para>
    /// <b>Down is destructive</b> by this repository's convention: it drops both tables and every count with them. A code
    /// rollback makes <c>Migrate</c> refuse (schema ahead, exit 3) until <c>MIGRATE_ALLOW_SCHEMA_AHEAD</c> names this id;
    /// older code never touches the tables.
    /// </para>
    /// </summary>
    public partial class AddEmailSendDailyCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_send_counting",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_send_counting", x => x.id);
                    table.CheckConstraint("ck_email_send_counting_singleton", "id = 1");
                });

            migrationBuilder.Sql("INSERT INTO email_send_counting (id, started_at) VALUES (1, now());");

            migrationBuilder.CreateTable(
                name: "email_send_daily_counts",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    outcome = table.Column<short>(type: "smallint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_send_daily_counts", x => new { x.day, x.kind, x.outcome });
                    table.CheckConstraint("ck_email_send_daily_counts_positive", "attempts > 0 AND kind > 0 AND outcome > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_send_counting");

            migrationBuilder.DropTable(
                name: "email_send_daily_counts");
        }
    }
}
