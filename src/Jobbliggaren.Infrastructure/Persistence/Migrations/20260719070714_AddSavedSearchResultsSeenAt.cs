using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedSearchResultsSeenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "results_seen_at",
                table: "saved_searches",
                type: "timestamp with time zone",
                nullable: true);

            // #312 (ADR 0115) — backfill befintliga rader till now() så ingen historisk
            // backlogg tänds som "N nya träffar" vid feature-lansering (en ny sökning
            // init:ar samma watermark i ctor:n; parity). Kolumnen förblir nullable/null-
            // tolerant (räknings-handlern coalescar null → sökningens CreatedAt defensivt),
            // men efter denna backfill är alla existerande rader satta.
            migrationBuilder.Sql(
                "UPDATE saved_searches SET results_seen_at = now() WHERE results_seen_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "results_seen_at",
                table: "saved_searches");
        }
    }
}
