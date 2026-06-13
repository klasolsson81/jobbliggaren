using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationStaleDetectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // STEG 9 — Stale-application detection (Fas 9.2).
            //
            // Lägger till två kolumner på applications-tabellen som driver
            // DetectGhostedApplicationsJob (Fas 9.4):
            //   * last_status_change_at  — när status senast flippades
            //   * ghosted_threshold_days — per-application timeout (default 21)
            //
            // BACKFILL-VAL: last_status_change_at = NOW() (NOT updated_at).
            //
            // Motivering: om vi backfillade från updated_at skulle alla befintliga
            // ansökningar med updated_at äldre än 21 dagar omedelbart klassas som
            // Ghosted vid första cron-körning av DetectGhostedApplicationsJob.
            // Det är fel beteende — fältet representerar "när status senast ändrades",
            // inte "när raden senast uppdaterades" (CoverLetter-edit, AddNote etc.
            // bumpar också updated_at). Genom att sätta NOW() på alla befintliga
            // rader får de ett rättvist 21-dagars-fönster räknat från deploy-tillfället
            // innan de kan klassas stale.
            //
            // Referens:
            //   * docs/reviews/2026-05-08-steg9-dotnet-architect.md
            //   * docs/decisions/0023-* (skrivs i Fas 9.8)
            //
            // Kolumnen läggs till nullable först, backfillas med NOW(), och flippas
            // sedan till NOT NULL. Standardmönster för NOT NULL-kolumn utan
            // konstant default på befintlig tabell.

            migrationBuilder.AddColumn<int>(
                name: "ghosted_threshold_days",
                table: "applications",
                type: "integer",
                nullable: false,
                defaultValue: 21);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_status_change_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE applications SET last_status_change_at = NOW() WHERE last_status_change_at IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_status_change_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            // Partial index för DetectGhostedApplicationsJob:
            //   WHERE status IN ('Submitted', 'Acknowledged')
            //     AND deleted_at IS NULL
            //     AND last_status_change_at + (ghosted_threshold_days * INTERVAL '1 day') < NOW()
            //
            // Indexet är filtrerat till de två statusar som kan bli stale, vilket
            // håller indexet litet (de flesta rader är Draft/Accepted/Rejected/etc.
            // och hamnar utanför). Daglig batch över växande tabell motiverar
            // indexet redan i Fas 1-volym — kostnaden är försumbar (kompakt
            // bytea-träd) och vinsten skalar med användarbas.
            migrationBuilder.Sql(
                @"CREATE INDEX ix_applications_stale_detection
                  ON applications (last_status_change_at)
                  WHERE status IN ('Submitted', 'Acknowledged') AND deleted_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_applications_stale_detection;");

            migrationBuilder.DropColumn(
                name: "ghosted_threshold_days",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "last_status_change_at",
                table: "applications");
        }
    }
}
