using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #311 PR-5 (ADR 0087 D4) — extend <c>company_watches</c> for BRAND_GROUP follows. Three purely
    /// additive/widening operations, so no RAISE vacuity guard (guards are reserved for destructive ops;
    /// parity the <c>WidenCompanyWatchOrganizationNumberForToken</c> widen):
    /// <list type="number">
    /// <item>widen <c>organization_number</c> to NULLABLE — a BRAND_GROUP row carries no org.nr;</item>
    /// <item>add nullable <c>brand_group_id varchar(40)</c> — the XOR counterpart (set for group rows);</item>
    /// <item>add the mirror active-partial UNIQUE <c>(user_id, brand_group_id) WHERE deleted_at IS NULL
    /// AND brand_group_id IS NOT NULL</c>. The <c>AND brand_group_id IS NOT NULL</c> keeps EMPLOYER rows
    /// (NULL group id) out of this index entirely, so it is disjoint from the existing
    /// <c>ux_company_watches_user_orgnr_active</c> — the two partial uniques coexist, each owning its own
    /// target axis (PG NULLS DISTINCT).</item>
    /// </list>
    /// The TargetType-discriminated XOR between the two columns is a DOMAIN invariant (the factories),
    /// not a DB CHECK (house style: aggregate invariant + partial unique indexes, not CHECK constraints).
    /// </summary>
    public partial class AddCompanyWatchBrandGroupTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "organization_number",
                table: "company_watches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "brand_group_id",
                table: "company_watches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_company_watches_user_brand_group_active",
                table: "company_watches",
                columns: new[] { "user_id", "brand_group_id" },
                unique: true,
                filter: "\"deleted_at\" IS NULL AND \"brand_group_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_company_watches_user_brand_group_active",
                table: "company_watches");

            migrationBuilder.DropColumn(
                name: "brand_group_id",
                table: "company_watches");

            migrationBuilder.AlterColumn<string>(
                name: "organization_number",
                table: "company_watches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
