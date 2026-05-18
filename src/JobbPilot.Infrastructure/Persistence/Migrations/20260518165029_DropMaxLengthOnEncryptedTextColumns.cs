using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropMaxLengthOnEncryptedTextColumns : Migration
    {
        // TD-13 C3 (ADR 0049). HasMaxLength dras från tre TEXT-PII-kolumner —
        // ciphertext (sentinel "v1:"+base64 via FieldEncryptionSaveChanges-
        // Interceptor) överskrider klartext-cap; TEXT är obegränsad i Postgres.
        //
        // Up är ICKE-DESTRUKTIV: character varying(N) -> text är en ren typ-
        // relax i PostgreSQL (samma underliggande varlena-lagring, ingen
        // omskrivning som trunkerar eller tappar data). EF:s "may result in
        // the loss of data"-varning vid scaffold är generisk för AlterColumn
        // och gäller INTE varchar->text-riktningen här.
        //
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "note",
                table: "follow_ups",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cover_letter",
                table: "applications",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "application_notes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(5000)",
                oldMaxLength: 5000);
        }

        // VARNING — DOWN ÄR ENDAST SÄKER FÖRE BACKFILL/KRYPTERING.
        // text -> character varying(N) påför en längd-cap (note 2000,
        // cover_letter 10000, content 5000). Postgres TRUNKERAR INTE tyst —
        // en rad vars värde överskrider N får ALTER att FELA (22001
        // string_data_right_truncation), vilket avbryter rollbacken. Efter
        // att FieldEncryptionSaveChangesInterceptor börjat skriva ciphertext
        // ("v1:"+base64, längre än klartext) kommer denna Down att antingen
        // faila eller — om manuellt forcerad — korrumpera odekrypterbar
        // ciphertext. Down får därför köras ENDAST innan krypterad data
        // existerar i dessa kolumner. Samma risk-klass som ADR 0049 Beslut 5
        // (drop-stegs-resonemang: rollback-säkerhet är tidsfönster-bunden,
        // ej ovillkorlig).
        //
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "note",
                table: "follow_ups",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cover_letter",
                table: "applications",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "application_notes",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
