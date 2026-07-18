using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #544 (ADR 0090 D5) — widen company_watches.organization_number varchar(10) → varchar(64) so a
    /// personnummer-shaped (enskild-firma) org.nr can be stored as a 64-char HMAC-SHA256 hex token at
    /// rest (never plaintext), while a legal-entity (AB) org.nr stays a 10-digit plaintext value in the
    /// SAME column. Schema-only + non-destructive (a widen — no value change). The plaintext→token
    /// rewrite of existing pnr-shaped rows is a SEPARATE app-side, guarded, KLAS-gated backfill: the
    /// pepper is a runtime secret and must NEVER appear in migration SQL / __EFMigrationsHistory. Down
    /// narrows back to varchar(10) — safe only before any token is written (R1: tokens are permanent,
    /// non-rotatable, non-reversible).
    /// </summary>
    public partial class WidenCompanyWatchOrganizationNumberForToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "organization_number",
                table: "company_watches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "organization_number",
                table: "company_watches",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
