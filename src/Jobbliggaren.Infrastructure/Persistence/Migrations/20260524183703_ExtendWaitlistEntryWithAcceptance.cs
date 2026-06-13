using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Steg 5 — Closed beta-disciplin (2026-05-24). Utvidgar
    /// <c>waitlist_entries</c> med Name + Motivation + AcceptanceSnapshot
    /// (marketing-consent + acceptance-timestamp + privacy-policy-version).
    /// CTO-dom 2026-05-24 Fynd 1 Approach B: användarvillkor + nödvändiga
    /// cookies levereras under GDPR Art. 6(1)(b) "performance of contract"
    /// (submit = acceptance), ingen separat consent-checkbox. Endast
    /// marketing-email är genuint Art. 7-samtycke.
    ///
    /// <para>
    /// Sentinel-defaults för legacy-rader: <c>name='(legacy)'</c>,
    /// <c>motivation='(legacy entry — backfilled)'</c>,
    /// <c>marketing_email_accepted=false</c>,
    /// <c>accepted_at='-infinity'</c> (Npgsql-idiomatisk
    /// MinValue-timestamp), <c>privacy_policy_version='legacy'</c>. Identifierbara
    /// via SQL-predikat för audit/cleanup. Nya rader får riktiga värden via
    /// domain-validering.
    /// </para>
    ///
    /// <para>
    /// Strategi: ALTER TABLE ADD COLUMN ... NOT NULL DEFAULT är O(1)
    /// metadata-only i PostgreSQL 11+ för icke-volatila defaults — ingen
    /// table rewrite.
    /// </para>
    /// </summary>
    public partial class ExtendWaitlistEntryWithAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "waitlist_entries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "(legacy)");

            migrationBuilder.AddColumn<string>(
                name: "motivation",
                table: "waitlist_entries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "(legacy entry — backfilled)");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "accepted_at",
                table: "waitlist_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero));

            migrationBuilder.AddColumn<bool>(
                name: "marketing_email_accepted",
                table: "waitlist_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "privacy_policy_version",
                table: "waitlist_entries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "legacy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "privacy_policy_version", table: "waitlist_entries");
            migrationBuilder.DropColumn(name: "marketing_email_accepted", table: "waitlist_entries");
            migrationBuilder.DropColumn(name: "accepted_at", table: "waitlist_entries");
            migrationBuilder.DropColumn(name: "motivation", table: "waitlist_entries");
            migrationBuilder.DropColumn(name: "name", table: "waitlist_entries");
        }
    }
}
