using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class NullPasswordHashes : Migration
    {
        // SPOT: the exact statement this migration applies, exposed so the journey test runs it directly.
        internal const string NullPasswordHashesSql =
            """
            UPDATE identity."AspNetUsers"
            SET password_hash = NULL,
                security_stamp = gen_random_uuid()::text,
                concurrency_stamp = gen_random_uuid()::text
            WHERE password_hash IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NullPasswordHashesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "This migration is irreversible by decision (ADR 0142, part 5b, #1857).");
    }
}
