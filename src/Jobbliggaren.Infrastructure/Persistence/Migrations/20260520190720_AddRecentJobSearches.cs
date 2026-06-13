using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // Avoid constant arrays as arguments — EF Core scaffolds new[] in CreateIndex; called once per migration.

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// ADR 0060 — RecentJobSearches auto-capture-domän.
    /// - Skapar <c>recent_job_searches</c> med UNIQUE-invariant
    ///   <c>ux_recent_job_searches_seeker_hash</c> på
    ///   (job_seeker_id, filter_hash). Capturer-impl tappar alltid till
    ///   ON CONFLICT-fall via try/catch DbUpdateException +
    ///   IDbExceptionInspector.IsUniqueConstraintViolation.
    /// - Sekundär-index <c>ix_recent_job_searches_seeker_viewed_at</c>
    ///   (job_seeker_id ASC, last_viewed_at DESC) för list-query ordering.
    /// - text[]-kolumner ssyk_list/region_list mappade via shadow-backing-
    ///   fields i RecentJobSearchConfiguration (paritet med Resume).
    /// </remarks>
    public partial class AddRecentJobSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recent_job_searches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    filter_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    q = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_by = table.Column<int>(type: "integer", nullable: false),
                    last_viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    region_list = table.Column<List<string>>(type: "text[]", nullable: false),
                    ssyk_list = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recent_job_searches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recent_job_searches_seeker_viewed_at",
                table: "recent_job_searches",
                columns: new[] { "job_seeker_id", "last_viewed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_recent_job_searches_seeker_hash",
                table: "recent_job_searches",
                columns: new[] { "job_seeker_id", "filter_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recent_job_searches");
        }
    }
}

#pragma warning restore CA1861
