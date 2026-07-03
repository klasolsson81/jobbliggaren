using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationUserCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lock note (parity with the #506 CREATE INDEX lock-documentation standard):
            // ADD COLUMN with a volatile DEFAULT now() rewrites the table and holds an
            // ACCESS EXCLUSIVE lock on identity."AspNetUsers" for the whole migration
            // transaction (the table-wide backfill UPDATE below runs under the same lock),
            // so auth (login/registration/token reads) is blocked for its duration. Blast
            // radius is nil in Fas 1 — first prod deploy runs against a greenfield (empty)
            // AspNetUsers per the ADR 0024 D2 empty-table-at-deploy pattern.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "identity",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // #508 (ADR 0024 D6) — backfill rows that predate this column to a fixed past
            // timestamp so the orphan-sweep grace window does not shield them (they are not
            // mid-registration; the grace window models an in-flight registration only).
            // New rows get now() via the store default above.
            migrationBuilder.Sql(
                "UPDATE identity.\"AspNetUsers\" SET created_at = timestamptz '1970-01-01 00:00:00+00';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "identity",
                table: "AspNetUsers");
        }
    }
}
