using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeVersionContentEnc : Migration
    {
        // TD-13 C4.1 (ADR 0049 Beslut 5, Steg A). Rent additivt: lägger en
        // ny nullable TEXT-kolumn `content_enc` på `resume_versions`. Ingen
        // ändring av befintlig `content jsonb`-kolumn, ingen ALTER TYPE, ingen
        // data-touch, ingen EF-mappnings-ändring (dual-property-mappning hör
        // till C4.2). `ADD COLUMN ... text NULL` är metadata-only i PostgreSQL
        // (ingen default → ingen tabell-omskrivning, ingen ACCESS EXCLUSIVE-
        // lock av betydelse, ingen radvis touch).
        //
        // SNAPSHOT-KOHERENS: EF-modellen har ÄNNU INGEN `content_enc`-property
        // i C4.1 (dual-property-mappningen införs i C4.2, beroende av C4.0-
        // gaten). Denna migrations Designer.BuildTargetModel är därför byte-
        // identisk med `AppDbContextModelSnapshot.cs` (ingen modell-delta).
        // `dotnet ef migrations` diffar enbart modell↔snapshot, aldrig
        // DB↔snapshot — en rå DB-kolumn före motsvarande modell-property är
        // benignt och bryter inte EF-konsistensen. Snapshot synkas i C4.2 när
        // dual-property-mappningen införs.
        //
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_enc",
                table: "resume_versions",
                type: "text",
                nullable: true);
        }

        // VARNING — DOWN ÄR ENDAST SÄKER FÖRE BACKFILL/KRYPTERING.
        // C4.1 är pre-backfill: `content_enc` är garanterat NULL på alla rader
        // (ingen läsare/skrivare flippad ännu — det sker i senare C4-steg).
        // `DROP COLUMN content_enc` är då en ovillkorligt säker, icke-
        // destruktiv rollback (inga data att tappa). Efter att backfill/
        // krypterings-vägen aktiverats bär kolumnen ciphertext och samma
        // tidsfönster-bundna rollback-resonemang som ADR 0049 Beslut 5 +
        // DropMaxLengthOnEncryptedTextColumns-precedensen gäller: Down får
        // INTE köras efter att krypterad data existerar i `content_enc`.
        //
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_enc",
                table: "resume_versions");
        }
    }
}
