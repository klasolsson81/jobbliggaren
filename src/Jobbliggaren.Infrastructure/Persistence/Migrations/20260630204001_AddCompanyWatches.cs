using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    // ADR 0087 D3 / D8(b) — introduces the company_watches table for the CompanyWatch aggregate.
    //
    // org.nr is stored PLAINTEXT (not DEK-encrypted). Protection is owner-scoped access and Art. 17
    // cascade erasure (AccountHardDeleter.HardDeleteAccountAsync sweeps by UserId). The PR-4 scan job
    // needs equality/IN matching on org.nr across all users — DEK encryption would break SQL IN
    // (ef_strongly_typed_vo_contains trap). A sole-prop (enskild firma) org.nr can equal a
    // personnummer; the guard lives at the surfacing/log boundary, not here.
    //
    // FORK B1 — the active-partial UNIQUE (user_id, organization_number) WHERE deleted_at IS NULL
    // guarantees at most one ACTIVE follow per (user, org.nr), guarding the concurrent-fresh-follow
    // race. The resurrect handler (Refollow) reuses the same physical row instead of inserting a
    // second one (soft-delete + resurrect pattern, mirrors SavedSearch).

    /// <inheritdoc />
    public partial class AddCompanyWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_watches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_watches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_watches_user_id",
                table: "company_watches",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_company_watches_user_orgnr_active",
                table: "company_watches",
                columns: new[] { "user_id", "organization_number" },
                unique: true,
                filter: "\"deleted_at\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_watches");
        }
    }
}
