using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #551 PR-D (ADR 0087 D6-paritet) — remote (distans, bool) på recent_job_searches.
    ///
    /// <para>Adderar <c>remote</c> (boolean, NOT NULL DEFAULT false) på
    /// <c>recent_job_searches</c>. SKALÄR kolumn — INTE ett shadow-backing-field/text[]
    /// som list-dimensionerna (occupation_group_list/…/employer_list); en binär axel
    /// mappas nativt (RecentJobSearchConfiguration).</para>
    ///
    /// <para><b>Additivt — inga befintliga rader bryts.</b> Kolumnen är NOT NULL med
    /// default-värde <c>false</c> → befintliga cache-rader backfyllas <c>false</c> utan
    /// separat jobb. FilterHash-formatet bumpas ovillkorligt (WriteBoolean skrivs för alla
    /// rader, paritet employer/Klass 2) → benign dubblett, cap-20-eviction självläker.
    /// PR-B shippade filter-dimensionen CONTAINED (Remote: false i persistens); denna PR
    /// trådar in den i sök-identiteten (architect + senior-cto-advisor 2026-07-18).</para>
    ///
    /// <para><b>GDPR:</b> kolumnen lagrar en boolean (distans ja/nej) — INGEN PII, inget
    /// org.nr/personnummer (till skillnad mot employer_list, ADR 0087 D8). En user-owned
    /// cache-rad över den egna sökningen. Ingen ytterligare GDPR-kontroll i denna migration.</para>
    ///
    /// <para><b>Down() är icke-destruktiv</b> för värdefull data — kolumnen droppas; cache-data
    /// förloras men <c>recent_job_searches</c> är efemär, självåterbyggande cache (max 20/seeker,
    /// ADR 0060 Beslut 6).</para>
    /// </summary>
    public partial class AddRecentJobSearchRemote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "remote",
                table: "recent_job_searches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "remote",
                table: "recent_job_searches");
        }
    }
}
