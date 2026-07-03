using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class DropRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Executes the ADR 0017 Fas 1 cleanup (overdue): the refresh-token subsystem
            // was retired at Turn 4 (/auth/refresh -> 410, no live writer) but the table
            // survived, so user_id + created_by_ip (PII, varchar(45)) outlived Art. 17 hard
            // deletion (audit #482 / #504a). Dropping the table closes the gap at the source.
            //
            // DROP TABLE takes ACCESS EXCLUSIVE on identity.refresh_tokens. Blast radius is nil:
            // the table is a dead leaf (no live writer since Turn 4; user_id is a scalar, not a
            // FK, so no dependent constraints) and pre-prod holds no rows worth preserving.
            //
            // Down() recreates the empty structure (schema-reversible) - there is no data to
            // restore, so this is an ordinary reversible DROP, NOT crypto-erasure semantics.
            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "identity",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                schema: "identity",
                table: "refresh_tokens",
                column: "user_id");
        }
    }
}
