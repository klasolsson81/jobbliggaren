using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0067 Beslut 6 — Fas B2 Klass 2-kolumner på recent_job_searches.
    ///
    /// <para>Adderar <c>employment_type_list</c> och <c>worktime_extent_list</c>
    /// (text[], NOT NULL DEFAULT '{}') på <c>recent_job_searches</c>.
    /// Speglar samma shadow-backing-field-mönster som befintliga
    /// <c>occupation_group_list</c>, <c>municipality_list</c> och <c>region_list</c>
    /// (C2-migrationen 20260609214512).</para>
    ///
    /// <para><b>Additivt — inga befintliga rader bryts.</b> Kolumnerna är NOT NULL
    /// med default-värde tom lista (<c>'{}'</c>). Befintliga cache-rader får tom
    /// lista; nya captures fyller kolumnerna direkt. FilterHash-formatet bumpas
    /// för rader med employment_type/worktime_extent-filter → benign dubblett,
    /// cap-20-eviction självläker (CTO-dom B2 2026-06-12).</para>
    ///
    /// <para><b>GDPR:</b> kolumnerna lagrar Platsbanken concept-ids (taxonomi-koder,
    /// ej PII). Inga ytterligare GDPR-kontroller krävs.</para>
    ///
    /// <para><b>Down() är icke-destruktiv</b> för data — kolumnerna droppas,
    /// cache-data i de två kolumnerna förloras men <c>recent_job_searches</c>
    /// är efemär, självåterbyggande cache (max 20 rader/seeker).</para>
    /// </summary>
    public partial class B2RecentJobSearchKlass2Columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "employment_type_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<List<string>>(
                name: "worktime_extent_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employment_type_list",
                table: "recent_job_searches");

            migrationBuilder.DropColumn(
                name: "worktime_extent_list",
                table: "recent_job_searches");
        }
    }
}
