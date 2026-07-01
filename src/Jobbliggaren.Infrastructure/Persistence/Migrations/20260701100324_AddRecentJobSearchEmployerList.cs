using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #311 PR-2b C1 (ADR 0087 D6) — employer_list (org.nr) på recent_job_searches.
    ///
    /// <para>Adderar <c>employer_list</c> (text[], NOT NULL DEFAULT '{}') på
    /// <c>recent_job_searches</c>. Speglar samma shadow-backing-field-mönster som de
    /// befintliga list-kolumnerna (occupation_group_list/municipality_list/region_list +
    /// Klass 2 employment_type_list/worktime_extent_list, B2-migrationen 20260612120000).</para>
    ///
    /// <para><b>Additivt — inga befintliga rader bryts.</b> Kolumnen är NOT NULL med
    /// default-värde tom lista (<c>'{}'</c>) → befintliga cache-rader får tom lista utan
    /// bakåtfyllning. FilterHash-formatet bumpas för rader med arbetsgivar-filter → benign
    /// dubblett, cap-20-eviction självläker (paritet Klass 2). PR-2 shippade filter-dimensionen
    /// CONTAINED; denna PR trådar in den i sök-identiteten (senior-cto-advisor 2026-07-01).</para>
    ///
    /// <para><b>GDPR:</b> kolumnen lagrar org.nr (arbetsgivar-nyckel). En enskild firmas org.nr
    /// KAN vara ett personnummer (ADR 0087 D8) — men detta är en USER-OWNED cache-rad över den
    /// egna sökningen (samma at-rest-posture som company_watches: plaintext pga queryability,
    /// skyddad av owner-scope + Art.17-cascade, EJ kryptering). Recent-searches saknar en egen
    /// surfnings-yta som skulle behöva pnr-guarden (guarden bor på disambiguerings-/logg-gränsen,
    /// C2). Ingen ytterligare GDPR-kontroll i denna migration.</para>
    ///
    /// <para><b>Down() är icke-destruktiv</b> för värdefull data — kolumnen droppas; cache-data
    /// förloras men <c>recent_job_searches</c> är efemär, självåterbyggande cache (max 20/seeker).</para>
    /// </summary>
    public partial class AddRecentJobSearchEmployerList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "employer_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employer_list",
                table: "recent_job_searches");
        }
    }
}
