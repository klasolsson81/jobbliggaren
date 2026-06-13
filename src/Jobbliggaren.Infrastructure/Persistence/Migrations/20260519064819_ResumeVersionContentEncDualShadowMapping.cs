using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResumeVersionContentEncDualShadowMapping : Migration
    {
        // TD-13 C4.2 #1c dual-shadow mapping delta (ADR 0049 Mekanik-not 6,
        // architect-låst 2026-05-19 agentId a96b9f1a77e6b4ba7). Detta är
        // expand-phase-steget (ADR 0049 Beslut 5 steg 2) för dual-property-
        // mappningen på `resume_versions`:
        //
        //   - `content_enc` (krypterad text-shadow, ContentEnc) ADDADES REDAN
        //     FYSISKT i C4.1-migrationen 20260519060041_AddResumeVersionContent-
        //     Enc (`content_enc text NULL`). Denna migration lägger INTE till
        //     den igen — ett andra ADD COLUMN failar 42701 (duplicate_column).
        //     Den auto-genererade AddColumn/DropColumn för `content_enc` är
        //     handborttagen (se "Flaggad auto-destruktiv op" i leveransrapport).
        //
        //   - Denna migrations enda DB-touch är att VIDGA nullbarheten på
        //     `resume_versions.content` (jsonb NOT NULL -> jsonb NULL). Skälet:
        //     Content är EF-Ignore:ad (ResumeVersionConfiguration #1c) och
        //     ContentEnc äger transformen via FieldEncryptionSaveChanges-
        //     Interceptor; nya content_enc-only-INSERTs skriver aldrig `content`
        //     och skulle annars NOT-NULL-violera (23502) den kvarvarande
        //     `content jsonb NOT NULL` (källa: 20260508014955_AddResumeAggregate
        //     .cs:37). `ALTER COLUMN content DROP NOT NULL` är icke-destruktiv,
        //     metadata-only — INGEN content-drop, INGEN ALTER TYPE (de stegen är
        //     ADR 0049 Beslut 5 steg 3-4 och hör till en separat senare
        //     Klas-STOPP-migration).
        //
        // SNAPSHOT-KOHERENS: C4.1 sköt MEDVETET upp shadow-property-metadatan
        // (`content_enc`/`ContentEnc` + read-only `ContentLegacyJson`->`content`
        // jsonb) till C4.2 (se C4.1-migrationens XML-kommentar). Denna migrations
        // Designer + AppDbContextModelSnapshot bär nu den synkade modell-deltan
        // — det är hela poängen med C4.2. has-pending-model-changes verifierar
        // snapshot↔modell-koherensen.
        //
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "resume_versions",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: false);
        }

        // VARNING — DOWN ÄR ENDAST SÄKER FÖRE BACKFILL/KRYPTERING.
        // SET NOT NULL scannar tabellen och FELAR (23502) om någon rad har
        // content IS NULL — vilket inträffar så snart första content_enc-only-
        // raden skrivits (Content builder.Ignore'd, ContentEnc satt av
        // FieldEncryptionSaveChangesInterceptor). Down får därför köras ENDAST
        // innan content_enc-only-rader existerar. Samma tidsfönster-bundna
        // rollback-klass som ADR 0049 Beslut 5 + DropMaxLengthOnEncryptedText-
        // Columns-precedensen.
        //
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "resume_versions",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
